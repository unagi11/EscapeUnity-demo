using System;
using Escape.Audio;
using Escape.SceneFlow;
using Escape.Localization;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Escape.UI
{
    // 씬 로드 동안 화면을 덮는 로딩 오버레이를 담당한다. (검정 페이드 인/아웃 없이 즉시 표시/숨김)
    public sealed class SceneTransitionFadeUI : MonoBehaviour
    {
        private const string TopCanvasPrefabResourcePath = "Prefabs/TopUICanvas";
        private const string TransitionMaskShaderName = "Hidden/Escape/RoomTransitionMask";
        private const string TransitionMaskShaderResourcePath = "Shaders/RoomTransitionMask";
        private const string TransitionSpiralPixelResourcePath = "Effect/transition_spiral_pixel";
        private const float TransitionMaskClearProgress = -0.01f;
        private const int ArrivalCoveredFrameCount = 2;
        private const int LoadingDotStateCount = 4;
        private const uint FullscreenNoiseDefaultSeed = 0x6D2B79F5u;
        private static readonly int TransitionMaskTextureId = Shader.PropertyToID("_MaskTex");
        private static readonly int TransitionColorId = Shader.PropertyToID("_Color");
        private static readonly int TransitionProgressId = Shader.PropertyToID("_Progress");
        private static readonly int TransitionModeId = Shader.PropertyToID("_Mode");
        private static readonly int TransitionUseTextureColorId = Shader.PropertyToID("_UseTextureColor");

        private static SceneTransitionFadeUI instance;
        private static bool hasPendingSpiralReveal;
        private static float pendingSpiralRevealDuration;

        [SerializeField] private Image overlay;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private TMP_Text loadingText;
        [SerializeField] private TutorialPanelUI tutorialPanel;
        [SerializeField, Min(0f)] private float minimumPreLoadSeconds;
        [SerializeField, Min(0.05f)] private float dotInterval = 0.35f;
        [SerializeField, Min(0.05f)] private float fullscreenNoiseDuration = 0.55f;
        [SerializeField, Range(0f, 1f)] private float fullscreenNoiseAlpha = 0.82f;
        [SerializeField, Min(0f)] private float fullscreenNoiseFadeInDuration = 3f;
        [SerializeField, Min(8)] private int fullscreenNoiseWidth = 128;
        [SerializeField, Min(8)] private int fullscreenNoiseHeight = 96;
        [SerializeField, Min(1f)] private float fullscreenNoiseFramesPerSecond = 24f;

        private RawImage spiralOverlay;
        private Material spiralMaterial;
        private Texture2D spiralMaskTexture;
        private RawImage fullscreenNoiseOverlay;
        private Texture2D fullscreenNoiseTexture;
        private Color32[] fullscreenNoisePixels;
        private uint fullscreenNoiseState = FullscreenNoiseDefaultSeed;
        private bool isTransitioning;
        private bool isSpiralTransitioning;
        private bool isFullscreenNoisePlaying;
        private bool isFullscreenNoiseHeld;
        private float fullscreenNoiseHeldStartedAt;
        private float nextFullscreenNoiseFrameAt;
        private float loadingAnimationStartedAt;
        private int displayedDotCount;

        public bool IsTransitioning => isTransitioning;
        public TutorialPanelUI TutorialPanel => tutorialPanel;

        // 일반 진입은 오버레이를 숨기고, 스파이럴 전환 도착이면 덮인 화면의 역재생을 이어받는다.
        private void Awake()
        {
            instance = this;
            SetOverlay(false);
            if (TryConsumePendingSpiralReveal(out float durationSeconds))
            {
                PlaySpiralRevealOnArrival(durationSeconds, destroyCancellationToken).Forget();
            }
        }

        private void OnEnable()
        {
            instance = this;
        }

        private void OnDisable()
        {
            if (instance == this)
            {
                instance = null;
            }
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                instance = null;
            }

            if (spiralMaterial != null)
            {
                Destroy(spiralMaterial);
            }

            if (fullscreenNoiseTexture != null)
            {
                Destroy(fullscreenNoiseTexture);
            }
        }

        // 표시 중에는 Loading부터 Loading...까지 점 개수를 반복한다.
        private void Update()
        {
            UpdateLoadingText();
            UpdateFullscreenNoiseFrame();
        }

        // 로딩 오버레이의 문구 점 개수를 갱신한다.
        private void UpdateLoadingText()
        {
            if (overlay == null || !overlay.gameObject.activeSelf || loadingText == null)
            {
                return;
            }

            int dotCount = Mathf.FloorToInt((Time.time - loadingAnimationStartedAt) / GetSafeDotInterval()) % LoadingDotStateCount;
            if (dotCount == displayedDotCount)
            {
                return;
            }

            displayedDotCount = dotCount;
            loadingText.text = LocalizationService.Text("loading", "Loading") + new string('.', dotCount);
        }

        // 지속형 노이즈의 텍스처는 지정 FPS로 갱신하고 알파는 매 프레임 부드럽게 올린다.
        private void UpdateFullscreenNoiseFrame()
        {
            if (!isFullscreenNoiseHeld ||
                fullscreenNoiseOverlay == null ||
                !fullscreenNoiseOverlay.gameObject.activeSelf)
            {
                return;
            }

            if (Time.time >= nextFullscreenNoiseFrameAt)
            {
                Texture2D texture = GetFullscreenNoiseTexture();
                if (texture != null)
                {
                    WriteFullscreenNoiseFrame(texture);
                }

                nextFullscreenNoiseFrameAt = Time.time + GetFullscreenNoiseFrameInterval();
            }

            float fadeDuration = Mathf.Max(0f, fullscreenNoiseFadeInDuration);
            float fadeProgress = fadeDuration <= 0f
                ? 1f
                : Mathf.Clamp01((Time.time - fullscreenNoiseHeldStartedAt) / fadeDuration);
            float alpha = fullscreenNoiseAlpha * Mathf.SmoothStep(0f, 1f, fadeProgress);
            fullscreenNoiseOverlay.color = new Color(1f, 1f, 1f, alpha);
        }

        // 로딩 오버레이를 즉시 표시한 뒤 준비된 씬 로드를 실행한다.
        public bool TryShowLoadingThenLoad(string sceneName)
        {
            if (isTransitioning || overlay == null)
            {
                return false;
            }

            ShowLoadingThenLoad(sceneName, destroyCancellationToken).Forget();
            return true;
        }

        private async UniTaskVoid ShowLoadingThenLoad(string sceneName, CancellationToken ct)
        {
            isTransitioning = true;
            try
            {
                await ShowLoadingForMinimumDuration(ct);
                await EscapeSceneLoader.LoadPreparedSceneAsync(sceneName, ct);
            }
            finally
            {
                isTransitioning = false;
            }
        }

        private async UniTask ShowLoadingForMinimumDuration(CancellationToken ct)
        {
            overlay.transform.SetAsLastSibling();
            ShowLoading();

            float startedAt = Time.realtimeSinceStartup;
            // 로딩 패널이 실제로 한 프레임 렌더링되도록 넘긴다.
            await UniTask.Yield(PlayerLoopTiming.Update, ct);

            float minimumVisibleSeconds = Mathf.Max(minimumPreLoadSeconds, GetMinimumLoadingLoopSeconds());
            float remaining = minimumVisibleSeconds - (Time.realtimeSinceStartup - startedAt);
            if (remaining > 0f)
            {
                await UniTask.Delay(
                    System.TimeSpan.FromSeconds(remaining),
                    ignoreTimeScale: false,
                    cancellationToken: ct);
            }
        }

        // TOPUI 캔버스에서 스파이럴로 화면을 덮고 액션을 실행한 뒤 다시 걷어낸다.
        public static bool TryPlaySpiralTransition(Action onCovered, float durationSeconds)
        {
            SceneTransitionFadeUI transitionUI = Ensure();
            if (transitionUI == null || transitionUI.isSpiralTransitioning)
            {
                return false;
            }

            transitionUI.PlaySpiralTransitionInternal(onCovered, durationSeconds, transitionUI.destroyCancellationToken).Forget();
            return true;
        }

        public static async UniTask PlaySpiralTransitionAsync(
            Action onCovered,
            float durationSeconds,
            CancellationToken cancellationToken)
        {
            SceneTransitionFadeUI transitionUI = Ensure();
            if (transitionUI == null)
            {
                onCovered?.Invoke();
                return;
            }

            while (transitionUI.isSpiralTransitioning)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }

            await transitionUI.PlaySpiralTransitionInternal(onCovered, durationSeconds, cancellationToken);
        }

        public static async UniTask WaitForSpiralIdleAsync(CancellationToken cancellationToken)
        {
            SceneTransitionFadeUI transitionUI = Ensure();
            while (transitionUI != null && transitionUI.isSpiralTransitioning)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }
        }

        // TOPUI 전체를 짧은 흑백 노이즈로 덮는다.
        public static async UniTask PlayFullscreenNoiseAsync(CancellationToken cancellationToken)
        {
            SceneTransitionFadeUI transitionUI = Ensure();
            if (transitionUI == null)
            {
                return;
            }

            while (transitionUI.isFullscreenNoisePlaying)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }

            await transitionUI.PlayFullscreenNoiseInternal(cancellationToken);
        }

        // TOPUI 전체 노이즈를 켜진 상태로 유지한다.
        public static void ShowFullscreenNoise()
        {
            SceneTransitionFadeUI transitionUI = Ensure();
            transitionUI?.ShowFullscreenNoiseInternal();
        }

        // 켜져 있는 TOPUI 전체 노이즈를 끈다.
        public static void HideFullscreenNoise()
        {
            SceneTransitionFadeUI transitionUI = instance;
            if (transitionUI == null)
            {
                transitionUI = FindFirstObjectByType<SceneTransitionFadeUI>(FindObjectsInactive.Include);
            }

            transitionUI?.HideFullscreenNoiseInternal();
        }

        // 필요하면 Resources의 TopUICanvas 프리팹을 만들어 전환 캔버스를 보장한다.
        public static SceneTransitionFadeUI Ensure()
        {
            if (instance != null && instance.isActiveAndEnabled)
            {
                return instance;
            }

            SceneTransitionFadeUI found = FindPreferredInstance();
            if (found != null)
            {
                instance = found;
                return found;
            }

            GameObject prefab = Resources.Load<GameObject>(TopCanvasPrefabResourcePath);
            if (prefab == null)
            {
                Debug.LogWarning($"Top UI canvas prefab not found: Resources/{TopCanvasPrefabResourcePath}.prefab");
                return null;
            }

            GameObject canvasObject = Instantiate(prefab);
            canvasObject.name = "TopUICanvas";
            canvasObject.transform.localScale = Vector3.one;
            if (canvasObject.transform is RectTransform rectTransform)
            {
                rectTransform.anchorMin = Vector2.zero;
                rectTransform.anchorMax = Vector2.one;
                rectTransform.offsetMin = Vector2.zero;
                rectTransform.offsetMax = Vector2.zero;
            }

            return canvasObject.GetComponent<SceneTransitionFadeUI>();
        }

        private static SceneTransitionFadeUI FindPreferredInstance()
        {
            SceneTransitionFadeUI[] candidates = FindObjectsByType<SceneTransitionFadeUI>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            if (candidates.Length == 0)
            {
                return null;
            }

            Scene activeScene = SceneManager.GetActiveScene();
            SceneTransitionFadeUI activeHierarchyCandidate = null;
            for (int i = 0; i < candidates.Length; i++)
            {
                SceneTransitionFadeUI candidate = candidates[i];
                if (candidate == null)
                {
                    continue;
                }

                if (candidate.gameObject.scene == activeScene && candidate.isActiveAndEnabled)
                {
                    return candidate;
                }

                if (activeHierarchyCandidate == null && candidate.isActiveAndEnabled)
                {
                    activeHierarchyCandidate = candidate;
                }
            }

            return activeHierarchyCandidate != null ? activeHierarchyCandidate : candidates[0];
        }

        private async UniTask PlaySpiralTransitionInternal(Action onCovered, float durationSeconds, CancellationToken ct)
        {
            isSpiralTransitioning = true;
            Scene sourceScene = gameObject.scene;
            bool preservePendingRevealForSceneLoad = false;
            try
            {
                await PlaySpiralCover(TransitionMaskClearProgress, 1f, durationSeconds, ct);
                QueuePendingSpiralReveal(durationSeconds);
                try
                {
                    onCovered?.Invoke();
                }
                catch
                {
                    ClearPendingSpiralReveal();
                    throw;
                }

                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                if (this == null)
                {
                    return;
                }

                if (SceneManager.GetActiveScene() != sourceScene)
                {
                    return;
                }

                if (isTransitioning)
                {
                    preservePendingRevealForSceneLoad = true;
                    HideSpiralOverlay();
                    return;
                }

                ClearPendingSpiralReveal();
                await PlaySpiralCover(1f, TransitionMaskClearProgress, durationSeconds, ct);
                HideSpiralOverlay();
            }
            finally
            {
                if (this != null && ct.IsCancellationRequested)
                {
                    HideSpiralOverlay();
                }

                if (this != null)
                {
                    if (!preservePendingRevealForSceneLoad &&
                        SceneManager.GetActiveScene() == sourceScene)
                    {
                        ClearPendingSpiralReveal();
                    }

                    isSpiralTransitioning = false;
                }
            }
        }

        // 새 씬의 TOPUI가 완전히 덮인 상태를 이어받아 스파이럴을 역순으로 걷어낸다.
        private async UniTaskVoid PlaySpiralRevealOnArrival(float durationSeconds, CancellationToken ct)
        {
            isSpiralTransitioning = true;
            try
            {
                if (!TryShowSpiralOverlayAtProgress(1f))
                {
                    return;
                }

                // 씬 로드 직후 프레임이 지연돼도 역재생 중간 화면이 첫 장면으로 노출되지 않게 한다.
                for (int i = 0; i < ArrivalCoveredFrameCount; i++)
                {
                    await UniTask.NextFrame(ct);
                }

                await PlaySpiralCover(1f, TransitionMaskClearProgress, durationSeconds, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
            finally
            {
                if (this != null)
                {
                    HideSpiralOverlay();
                    isSpiralTransitioning = false;
                }
            }
        }

        // 씬 로드로 기존 TOPUI가 파괴되기 전에 도착 씬이 이어받을 역재생 정보를 남긴다.
        private static void QueuePendingSpiralReveal(float durationSeconds)
        {
            pendingSpiralRevealDuration = Mathf.Max(0.01f, durationSeconds);
            hasPendingSpiralReveal = true;
        }

        // 도착 씬의 TOPUI 한 곳만 예약된 역재생을 소비한다.
        private static bool TryConsumePendingSpiralReveal(out float durationSeconds)
        {
            durationSeconds = pendingSpiralRevealDuration;
            if (!hasPendingSpiralReveal)
            {
                return false;
            }

            hasPendingSpiralReveal = false;
            pendingSpiralRevealDuration = 0f;
            return true;
        }

        // 같은 씬에서 전환이 끝나거나 실패하면 남은 역재생 예약을 지운다.
        private static void ClearPendingSpiralReveal()
        {
            hasPendingSpiralReveal = false;
            pendingSpiralRevealDuration = 0f;
        }

        // Spiral Pixel 마스크로 TOPUI 전체를 검정 전환시킨다.
        private async UniTask PlaySpiralCover(float from, float to, float durationSeconds, CancellationToken ct)
        {
            if (!TryShowSpiralOverlayAtProgress(from))
            {
                return;
            }

            Material material = spiralMaterial;

            float duration = Mathf.Max(0.01f, durationSeconds);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = Mathf.SmoothStep(0f, 1f, t);
                material.SetFloat(TransitionProgressId, Mathf.Lerp(from, to, eased));
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            material.SetFloat(TransitionProgressId, to);
        }

        // 스파이럴 오버레이를 지정 진행도에서 즉시 표시한다.
        private bool TryShowSpiralOverlayAtProgress(float progress)
        {
            RawImage image = GetSpiralOverlay();
            Material material = GetSpiralMaterial();
            Texture2D maskTexture = GetSpiralMaskTexture();
            if (image == null || material == null || maskTexture == null)
            {
                return false;
            }

            material.SetTexture(TransitionMaskTextureId, maskTexture);
            material.SetColor(TransitionColorId, Color.black);
            material.SetFloat(TransitionModeId, 0f);
            material.SetFloat(TransitionUseTextureColorId, 0f);
            material.SetFloat(TransitionProgressId, progress);
            image.texture = Texture2D.whiteTexture;
            image.material = material;
            image.color = Color.white;
            image.raycastTarget = true;
            image.gameObject.SetActive(true);
            image.transform.SetAsLastSibling();
            return true;
        }

        // 전환용 RawImage를 TOPUI 캔버스 가장 위에 준비한다.
        private RawImage GetSpiralOverlay()
        {
            if (spiralOverlay != null)
            {
                return spiralOverlay;
            }

            GameObject imageObject = new("TopUISpiralTransition", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            imageObject.transform.SetParent(transform, false);
            RectTransform rectTransform = imageObject.GetComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
            rectTransform.localScale = Vector3.one;

            spiralOverlay = imageObject.GetComponent<RawImage>();
            spiralOverlay.gameObject.SetActive(false);
            return spiralOverlay;
        }

        // 방 전환과 같은 마스크 셰이더 Material을 TOPUI 전환용으로 만든다.
        private Material GetSpiralMaterial()
        {
            if (spiralMaterial != null)
            {
                return spiralMaterial;
            }

            Shader shader = Resources.Load<Shader>(TransitionMaskShaderResourcePath);
            if (shader == null)
            {
                shader = Shader.Find(TransitionMaskShaderName);
            }

            if (shader == null)
            {
                Debug.LogWarning($"Top UI transition shader not found: Resources/{TransitionMaskShaderResourcePath}.shader", this);
                return null;
            }

            spiralMaterial = new Material(shader)
            {
                name = "TopUISpiralTransitionMaterial",
                hideFlags = HideFlags.DontSave
            };
            return spiralMaterial;
        }

        // Spiral Pixel 마스크 텍스처를 Resources에서 불러온다.
        private Texture2D GetSpiralMaskTexture()
        {
            if (spiralMaskTexture != null)
            {
                return spiralMaskTexture;
            }

            spiralMaskTexture = Resources.Load<Texture2D>(TransitionSpiralPixelResourcePath);
            if (spiralMaskTexture == null)
            {
                Debug.LogWarning($"Top UI transition mask not found: Resources/{TransitionSpiralPixelResourcePath}.png", this);
                return null;
            }

            spiralMaskTexture.filterMode = FilterMode.Point;
            spiralMaskTexture.wrapMode = TextureWrapMode.Clamp;
            return spiralMaskTexture;
        }

        private void HideSpiralOverlay()
        {
            if (spiralOverlay == null)
            {
                return;
            }

            spiralOverlay.raycastTarget = false;
            spiralOverlay.gameObject.SetActive(false);
        }

        // 절차적으로 생성한 저해상도 텍스처를 확대해 픽셀 노이즈를 재생한다.
        private async UniTask PlayFullscreenNoiseInternal(CancellationToken ct)
        {
            isFullscreenNoisePlaying = true;
            try
            {
                RawImage image = GetFullscreenNoiseOverlay();
                Texture2D texture = GetFullscreenNoiseTexture();
                if (image == null || texture == null)
                {
                    return;
                }

                ShowFullscreenNoiseOverlay(image, texture, true);

                float duration = Mathf.Max(0.05f, fullscreenNoiseDuration);
                float frameInterval = GetFullscreenNoiseFrameInterval();
                float frameElapsed = frameInterval;
                float elapsed = 0f;

                while (elapsed < duration)
                {
                    if (frameElapsed >= frameInterval)
                    {
                        WriteFullscreenNoiseFrame(texture);
                        frameElapsed = 0f;
                    }

                    float alpha = fullscreenNoiseAlpha * GetFullscreenNoiseEnvelope(elapsed, duration);
                    image.color = new Color(1f, 1f, 1f, alpha);

                    elapsed += Time.deltaTime;
                    frameElapsed += Time.deltaTime;
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
            }
            finally
            {
                HideFullscreenNoiseOverlay();
                isFullscreenNoisePlaying = false;
            }
        }

        // 지속형 전체화면 노이즈를 켜고 첫 프레임을 즉시 표시한다.
        private void ShowFullscreenNoiseInternal()
        {
            RawImage image = GetFullscreenNoiseOverlay();
            Texture2D texture = GetFullscreenNoiseTexture();
            if (image == null || texture == null)
            {
                return;
            }

            isFullscreenNoiseHeld = true;
            fullscreenNoiseHeldStartedAt = Time.time;
            ShowFullscreenNoiseOverlay(image, texture, false);
            WriteFullscreenNoiseFrame(texture);
            float initialAlpha = fullscreenNoiseFadeInDuration > 0f ? 0f : fullscreenNoiseAlpha;
            image.color = new Color(1f, 1f, 1f, initialAlpha);
            nextFullscreenNoiseFrameAt = Time.time + GetFullscreenNoiseFrameInterval();
        }

        // 지속형 전체화면 노이즈 상태를 해제하고 화면에서 숨긴다.
        private void HideFullscreenNoiseInternal()
        {
            isFullscreenNoiseHeld = false;
            fullscreenNoiseHeldStartedAt = 0f;
            HideFullscreenNoiseOverlay();
        }

        // 노이즈 RawImage의 공통 표시 상태를 맞춘다.
        private static void ShowFullscreenNoiseOverlay(RawImage image, Texture2D texture, bool blockInput)
        {
            image.texture = texture;
            image.material = null;
            image.raycastTarget = blockInput;
            image.gameObject.SetActive(true);
            image.transform.SetAsLastSibling();
        }

        // TOPUI 캔버스 가장 위에 전체화면 노이즈 이미지를 준비한다.
        private RawImage GetFullscreenNoiseOverlay()
        {
            if (fullscreenNoiseOverlay != null)
            {
                return fullscreenNoiseOverlay;
            }

            GameObject imageObject = new("TopUIFullscreenNoise", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            imageObject.transform.SetParent(transform, false);
            RectTransform rectTransform = imageObject.GetComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
            rectTransform.localScale = Vector3.one;

            fullscreenNoiseOverlay = imageObject.GetComponent<RawImage>();
            fullscreenNoiseOverlay.raycastTarget = false;
            fullscreenNoiseOverlay.gameObject.SetActive(false);
            return fullscreenNoiseOverlay;
        }

        // 노이즈 텍스처 크기가 바뀌면 런타임 텍스처를 다시 만든다.
        private Texture2D GetFullscreenNoiseTexture()
        {
            int width = Mathf.Max(8, fullscreenNoiseWidth);
            int height = Mathf.Max(8, fullscreenNoiseHeight);
            if (fullscreenNoiseTexture != null &&
                fullscreenNoiseTexture.width == width &&
                fullscreenNoiseTexture.height == height)
            {
                return fullscreenNoiseTexture;
            }

            if (fullscreenNoiseTexture != null)
            {
                Destroy(fullscreenNoiseTexture);
            }

            fullscreenNoiseTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "TopUIFullscreenNoiseTexture",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Repeat,
                hideFlags = HideFlags.DontSave
            };
            fullscreenNoisePixels = new Color32[width * height];
            return fullscreenNoiseTexture;
        }

        // xorshift 난수로 흑백 픽셀을 채운 뒤 UI 텍스처에 반영한다.
        private void WriteFullscreenNoiseFrame(Texture2D texture)
        {
            if (fullscreenNoisePixels == null || fullscreenNoisePixels.Length != texture.width * texture.height)
            {
                fullscreenNoisePixels = new Color32[texture.width * texture.height];
            }

            if (fullscreenNoiseState == 0)
            {
                fullscreenNoiseState = FullscreenNoiseDefaultSeed;
            }

            for (int i = 0; i < fullscreenNoisePixels.Length; i++)
            {
                fullscreenNoiseState ^= fullscreenNoiseState << 13;
                fullscreenNoiseState ^= fullscreenNoiseState >> 17;
                fullscreenNoiseState ^= fullscreenNoiseState << 5;
                byte value = (fullscreenNoiseState & 1u) == 0u ? (byte)24 : (byte)245;
                fullscreenNoisePixels[i] = new Color32(value, value, value, 255);
            }

            texture.SetPixels32(fullscreenNoisePixels);
            texture.Apply(false);
        }

        // 시작과 끝을 살짝 눌러 노이즈가 너무 딱딱하게 튀지 않게 한다.
        private static float GetFullscreenNoiseEnvelope(float elapsed, float duration)
        {
            float fadeIn = Mathf.Clamp01(elapsed / 0.04f);
            float fadeOut = Mathf.Clamp01((duration - elapsed) / 0.08f);
            return Mathf.SmoothStep(0f, 1f, Mathf.Min(fadeIn, fadeOut));
        }

        private float GetFullscreenNoiseFrameInterval()
        {
            return 1f / Mathf.Max(1f, fullscreenNoiseFramesPerSecond);
        }

        private void HideFullscreenNoiseOverlay()
        {
            if (fullscreenNoiseOverlay == null)
            {
                return;
            }

            fullscreenNoiseOverlay.raycastTarget = false;
            fullscreenNoiseOverlay.texture = null;
            fullscreenNoiseOverlay.gameObject.SetActive(false);
        }

        // 씬 내부 연출에서도 로딩 오버레이를 즉시 표시/숨길 수 있다.
        public void ShowLoading()
        {
            SoundPlayer.StopAllBgm();
            BeginLoadingAnimation();
            SetOverlay(true);
        }

        public void HideLoading()
        {
            SetOverlay(false);
        }

        // 오버레이의 표시와 입력 차단 상태를 함께 갱신한다.
        private void SetOverlay(bool visible)
        {
            if (overlay == null)
            {
                return;
            }

            overlay.gameObject.SetActive(visible);
            if (canvasGroup != null)
            {
                canvasGroup.alpha = visible ? 1f : 0f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = visible;
            }

            overlay.raycastTarget = visible;
        }

        // 로딩 문구 반복의 기준 시각과 첫 문구를 초기화한다.
        private void BeginLoadingAnimation()
        {
            loadingAnimationStartedAt = Time.time;
            displayedDotCount = 1;
            if (loadingText != null)
            {
                loadingText.text = LocalizationService.Text("loading", "Loading");
            }
        }

        private float GetMinimumLoadingLoopSeconds()
        {
            return GetSafeDotInterval() * LoadingDotStateCount;
        }

        private float GetSafeDotInterval()
        {
            return Mathf.Max(0.05f, dotInterval);
        }
    }
}
