using System;
using Escape.Audio;
using Escape.Localization;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Escape.Rooms;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Escape.UI
{
    // 클릭 순간의 슬라이더 위치로 조용한 행동의 성공 여부를 돌려주는 타이밍 체크 팝업이다.
    public sealed class TimingCheckPopupUI : PopupUIBase
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ESCAPE_TESTFLIGHT
        public static bool QaForceNextSuccess { get; set; }
#endif
        private const string TextAssetPath = "Assets/Resources/Data/text.tsv";
        private const string DefaultActiveTid = "timing_check_active";
        private const string DefaultSuccessTid = "timing_check_success";
        private const string DefaultFailureTid = "timing_check_failure";
        private const string DefaultActiveFallbackText = "타이밍에 맞춰서 멈춰라!";
        private const string DefaultSuccessFallbackText = "성공!";
        private const string DefaultFailureFallbackText = "실패..";

        [Header("Popup")]
        [SerializeField] private GameObject root;
        [SerializeField] private Button inputButton;
        [SerializeField] private bool hideOnAwake = true;
        [SerializeField, Min(0f)] private float fadeDuration;
        [SerializeField] private CanvasGroup canvasGroup;

        [Header("Timing Check")]
        [SerializeField] private Slider timingSlider;
        [SerializeField] private RectTransform safeWindowVisual;
        [SerializeField] private TMP_Text promptText;
        [SerializeField, Min(0.1f)] private float sweepDuration = 1.8f;
        [SerializeField, Range(0f, 1f)] private float safeWindowStart = 0.4f;
        [SerializeField, Range(0f, 1f)] private float safeWindowEnd = 0.6f;
        [SerializeField, Range(0f, 1f)] private float safeWindowMinimumStart = 0.58f;
        [SerializeField, Range(0f, 1f)] private float safeWindowMaximumStart = 0.82f;
        [SerializeField, Range(0.01f, 1f)] private float safeWindowWidth = 0.125f;
        [SerializeField, Min(0f)] private float startDelay;
        [SerializeField, Min(0f)] private float resultDisplayDuration = 0.35f;
        [SerializeField, TsvId(TextAssetPath)] private string activeTid = DefaultActiveTid;
        [SerializeField, TsvId(TextAssetPath)] private string successTid = DefaultSuccessTid;
        [SerializeField, TsvId(TextAssetPath)] private string failureTid = DefaultFailureTid;
        [SerializeField, FormerlySerializedAs("activeText"), HideInInspector] private string activeFallbackText = DefaultActiveFallbackText;
        [SerializeField, FormerlySerializedAs("successText"), HideInInspector] private string successFallbackText = DefaultSuccessFallbackText;
        [SerializeField, FormerlySerializedAs("failureText"), HideInInspector] private string failureFallbackText = DefaultFailureFallbackText;

        [Header("Result Feedback")]
        [SerializeField, Min(0f)] private float resultFeedbackDuration = 0.46f;
        [SerializeField, Min(1f)] private float successTextScale = 1.42f;
        [SerializeField, Min(0f)] private float failureShakePixels = 20f;
        [SerializeField, FormerlySerializedAs("resultFlashAlpha"), Range(0f, 1f)] private float failureFlashAlpha = 0.28f;
        [SerializeField] private Color failureFlashColor = new(1f, 0.1f, 0.16f, 1f);
        [SerializeField, Min(0)] private int successFireworkParticleCount = 42;
        [SerializeField, Min(0f)] private float successFireworkDuration = 0.72f;
        [SerializeField, Min(1f)] private float successFireworkParticleSize = 8f;
        [SerializeField, Min(0f)] private float successFireworkMinSpeed = 170f;
        [SerializeField, Min(0f)] private float successFireworkMaxSpeed = 390f;
        [SerializeField] private Color[] successFireworkColors =
        {
            new(1f, 0.86f, 0.25f, 1f),
            new(0.25f, 0.95f, 1f, 1f),
            new(1f, 0.36f, 0.68f, 1f),
            new(0.48f, 1f, 0.42f, 1f)
        };

        private UniTaskCompletionSource<bool> resultSource;
        private float elapsed;
        private bool isResolving;
        private bool isTutorialBlocking;

        protected override GameObject PopupRoot { get => root; set => root = value; }
        protected override Button PopupOpenButton { get => null; set { } }
        protected override Button PopupCloseButton { get => null; set { } }
        protected override bool PopupHideOnAwake => hideOnAwake;
        protected override float PopupFadeDuration => fadeDuration;
        protected override CanvasGroup PopupCanvasGroup { get => canvasGroup; set => canvasGroup = value; }

        private void Awake()
        {
            InitializePopupChrome();
            ConfigureTimingSlider();
            inputButton?.onClick.AddListener(Submit);
        }

        private void OnDestroy()
        {
            inputButton?.onClick.RemoveListener(Submit);
            resultSource?.TrySetCanceled(destroyCancellationToken);
        }

        private void Update()
        {
            if (!IsPopupVisible || resultSource == null || isResolving ||
                isTutorialBlocking || timingSlider == null)
            {
                return;
            }

            elapsed += Time.deltaTime;
            if (elapsed < startDelay)
            {
                timingSlider.SetValueWithoutNotify(0f);
                return;
            }

            if (inputButton != null && !inputButton.interactable)
            {
                inputButton.interactable = true;
            }

            float progress = (elapsed - startDelay) / Mathf.Max(0.1f, sweepDuration);
            timingSlider.SetValueWithoutNotify(Mathf.PingPong(progress, 1f));
        }

        // 타이밍 체크를 열고 클릭 결과를 비동기로 돌려준다.
        public async UniTask<bool> ShowAsync(CancellationToken ct = default)
        {
            if (resultSource != null)
            {
                Debug.LogWarning("Timing check is already active.", this);
                return false;
            }

            elapsed = 0f;
            isResolving = false;
            isTutorialBlocking = true;
            ConfigureTimingSlider();
            RandomizeSafeWindow();
            timingSlider?.SetValueWithoutNotify(0f);
            if (promptText != null)
            {
                promptText.text = GetLocalizedText(activeTid, activeFallbackText);
            }

            resultSource = new UniTaskCompletionSource<bool>();
            Open();
            if (inputButton != null)
            {
                inputButton.interactable = false;
            }

            try
            {
                await TutorialPanelUI.ShowOnceAsync(TutorialPanelUI.TutorialId.TimingCheck, ct);
                ct.ThrowIfCancellationRequested();

                elapsed = 0f;
                isTutorialBlocking = false;
                if (inputButton != null)
                {
                    inputButton.interactable = true;
                }

                return await resultSource.Task.AttachExternalCancellation(ct);
            }
            finally
            {
                isTutorialBlocking = false;
                resultSource = null;
                if (this != null && root != null)
                {
                    Close();
                }
            }
        }

        protected override void OnAfterClose()
        {
            if (resultSource != null && !isResolving)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ESCAPE_TESTFLIGHT
                if (QaForceNextSuccess)
                {
                    QaForceNextSuccess = false;
                    resultSource.TrySetResult(true);
                    return;
                }
#endif
                resultSource.TrySetResult(false);
            }
        }

        // 전체 오버레이 클릭을 현재 슬라이더 위치의 성공 또는 실패로 확정한다.
        private void Submit()
        {
            if (resultSource == null || isResolving || timingSlider == null)
            {
                return;
            }

            if (elapsed < startDelay)
            {
                return;
            }

            bool succeeded = IsHandleInsideSafeWindow();
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ESCAPE_TESTFLIGHT
            if (QaForceNextSuccess)
            {
                succeeded = true;
                QaForceNextSuccess = false;
            }
#endif
            ResolveResult(succeeded).Forget();
        }

        // 슬라이더의 정규화 값으로 현재 핸들이 표시된 성공 구간 안에 있는지 판정한다.
        private bool IsHandleInsideSafeWindow()
        {
            return timingSlider != null && TimingCheckEvaluator.IsSuccess(
                timingSlider.value,
                safeWindowStart,
                safeWindowEnd);
        }

        // 결과 문구를 짧게 보여준 뒤 대기 중인 호출자에게 성공 여부를 전달한다.
        private async UniTaskVoid ResolveResult(bool succeeded)
        {
            isResolving = true;
            if (promptText != null)
            {
                promptText.text = succeeded
                    ? GetLocalizedText(successTid, successFallbackText)
                    : GetLocalizedText(failureTid, failureFallbackText);
            }

            if (inputButton != null)
            {
                inputButton.interactable = false;
            }

            try
            {
                PlayResultSfx(succeeded);
                float feedbackDuration = GetResultFeedbackDuration(succeeded);
                await PlayResultFeedback(succeeded, destroyCancellationToken);
                float remainingDuration = resultDisplayDuration - feedbackDuration;
                if (remainingDuration > 0f)
                {
                    await UniTask.Delay(
                        TimeSpan.FromSeconds(remainingDuration),
                        DelayType.DeltaTime,
                        PlayerLoopTiming.Update,
                        destroyCancellationToken);
                }

                resultSource?.TrySetResult(succeeded);
            }
            catch (OperationCanceledException)
            {
                resultSource?.TrySetCanceled(destroyCancellationToken);
            }
        }

        // 성공은 폭죽 파티클과 강한 팝, 실패는 경고 플래시와 흔들림으로 피드백한다.
        private async UniTask PlayResultFeedback(bool succeeded, CancellationToken ct)
        {
            float duration = GetResultFeedbackDuration(succeeded);
            float feedbackDuration = Mathf.Max(0.01f, resultFeedbackDuration);
            float elapsedFeedback = 0f;
            RectTransform promptRect = promptText != null ? promptText.rectTransform : null;
            RectTransform shakeRect = timingSlider != null ? timingSlider.transform as RectTransform : null;
            Graphic safeWindowGraphic = safeWindowVisual != null
                ? safeWindowVisual.GetComponent<Graphic>()
                : null;
            Graphic handleGraphic = timingSlider != null && timingSlider.handleRect != null
                ? timingSlider.handleRect.GetComponent<Graphic>()
                : null;
            Image screenFlash = succeeded ? null : CreateResultFlash();
            List<UiParticle> fireworkParticles = succeeded ? CreateSuccessFireworkParticles() : null;
            Color originalSafeWindowColor = safeWindowGraphic != null ? safeWindowGraphic.color : Color.white;
            Color originalHandleColor = handleGraphic != null ? handleGraphic.color : Color.white;
            Color originalPromptColor = promptText != null ? promptText.color : Color.white;
            Vector3 originalPromptScale = promptRect != null ? promptRect.localScale : Vector3.one;
            Vector2 originalPromptPosition = promptRect != null ? promptRect.anchoredPosition : Vector2.zero;
            Vector2 originalShakePosition = shakeRect != null ? shakeRect.anchoredPosition : Vector2.zero;

            try
            {
                while (elapsedFeedback < duration)
                {
                    ct.ThrowIfCancellationRequested();
                    float t = Mathf.Clamp01(elapsedFeedback / feedbackDuration);
                    float pulse = Mathf.Sin(t * Mathf.PI);

                    if (!succeeded && safeWindowGraphic != null)
                    {
                        safeWindowGraphic.color = Color.Lerp(originalSafeWindowColor, failureFlashColor, Mathf.Clamp01(pulse * 1.25f));
                    }

                    if (!succeeded && handleGraphic != null)
                    {
                        handleGraphic.color = Color.Lerp(originalHandleColor, failureFlashColor, Mathf.Clamp01(pulse * 1.1f));
                    }

                    if (screenFlash != null)
                    {
                        Color flashColor = failureFlashColor;
                        flashColor.a = failureFlashAlpha * pulse;
                        screenFlash.color = flashColor;
                    }

                    if (!succeeded && promptText != null)
                    {
                        promptText.color = Color.Lerp(originalPromptColor, failureFlashColor, Mathf.Clamp01(pulse * 0.7f));
                    }

                    if (promptRect != null)
                    {
                        if (succeeded)
                        {
                            float settle = 1f - Mathf.Pow(1f - t, 3f);
                            promptRect.localScale = originalPromptScale * Mathf.Lerp(successTextScale, 1.08f, settle);
                        }
                        else
                        {
                            promptRect.localScale = originalPromptScale * Mathf.Lerp(1f, 1.12f, pulse);
                            promptRect.anchoredPosition = originalPromptPosition + new Vector2(0f, -10f * pulse);
                        }
                    }

                    if (!succeeded && shakeRect != null)
                    {
                        float damping = 1f - t;
                        float shakeX = Mathf.Sin(t * Mathf.PI * 14f) * failureShakePixels * damping;
                        float shakeY = Mathf.Sin(t * Mathf.PI * 9f) * failureShakePixels * 0.25f * damping;
                        shakeRect.anchoredPosition = originalShakePosition + new Vector2(shakeX, shakeY);
                    }

                    if (fireworkParticles != null)
                    {
                        UpdateFireworkParticles(fireworkParticles, elapsedFeedback, duration);
                    }

                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                    elapsedFeedback += Time.deltaTime;
                }
            }
            finally
            {
                if (safeWindowGraphic != null)
                {
                    safeWindowGraphic.color = originalSafeWindowColor;
                }

                if (handleGraphic != null)
                {
                    handleGraphic.color = originalHandleColor;
                }

                if (promptText != null)
                {
                    promptText.color = originalPromptColor;
                }

                if (promptRect != null)
                {
                    promptRect.localScale = originalPromptScale;
                    promptRect.anchoredPosition = originalPromptPosition;
                }

                if (shakeRect != null)
                {
                    shakeRect.anchoredPosition = originalShakePosition;
                }

                if (screenFlash != null)
                {
                    Destroy(screenFlash.gameObject);
                }

                if (fireworkParticles != null)
                {
                    DestroyFireworkParticles(fireworkParticles);
                }
            }
        }

        private float GetResultFeedbackDuration(bool succeeded)
        {
            return Mathf.Max(
                0.01f,
                succeeded ? Mathf.Max(resultFeedbackDuration, successFireworkDuration) : resultFeedbackDuration);
        }

        private static string GetLocalizedText(string tid, string fallback)
        {
            return LocalizationService.Text(tid, fallback);
        }

        private Image CreateResultFlash()
        {
            RectTransform parent = root != null ? root.transform as RectTransform : null;
            if (parent == null)
            {
                return null;
            }

            var flashObject = new GameObject("TimingCheckResultFlash", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            flashObject.transform.SetParent(parent, false);
            flashObject.transform.SetAsLastSibling();

            var flashRect = flashObject.transform as RectTransform;
            flashRect.anchorMin = Vector2.zero;
            flashRect.anchorMax = Vector2.one;
            flashRect.offsetMin = Vector2.zero;
            flashRect.offsetMax = Vector2.zero;

            var image = flashObject.GetComponent<Image>();
            image.raycastTarget = false;
            image.color = Color.clear;
            return image;
        }

        private List<UiParticle> CreateSuccessFireworkParticles()
        {
            RectTransform parent = root != null ? root.transform as RectTransform : null;
            if (parent == null || successFireworkParticleCount <= 0)
            {
                return null;
            }

            var layerObject = new GameObject("TimingCheckSuccessFirework", typeof(RectTransform));
            layerObject.transform.SetParent(parent, false);
            layerObject.transform.SetAsLastSibling();

            var layerRect = layerObject.transform as RectTransform;
            layerRect.anchorMin = Vector2.zero;
            layerRect.anchorMax = Vector2.one;
            layerRect.offsetMin = Vector2.zero;
            layerRect.offsetMax = Vector2.zero;

            Vector2 origin = Vector2.zero;
            RectTransform promptRect = promptText != null ? promptText.rectTransform : null;
            if (promptRect != null)
            {
                origin = layerRect.InverseTransformPoint(promptRect.TransformPoint(promptRect.rect.center));
            }

            var particles = new List<UiParticle>(successFireworkParticleCount + 1)
            {
                new UiParticle(layerRect)
            };

            for (int i = 0; i < successFireworkParticleCount; i++)
            {
                var particleObject = new GameObject("FireworkParticle", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                particleObject.transform.SetParent(layerRect, false);

                var particleRect = particleObject.transform as RectTransform;
                float size = successFireworkParticleSize * UnityEngine.Random.Range(0.75f, 1.45f);
                particleRect.sizeDelta = new Vector2(size, size);

                var image = particleObject.GetComponent<Image>();
                image.raycastTarget = false;
                Color color = GetFireworkColor(i);
                image.color = color;

                float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                float maxSpeed = Mathf.Max(successFireworkMinSpeed, successFireworkMaxSpeed);
                float speed = UnityEngine.Random.Range(successFireworkMinSpeed, maxSpeed);
                Vector2 burstOffset = new Vector2(UnityEngine.Random.Range(-72f, 72f), UnityEngine.Random.Range(-6f, 38f));
                Vector2 velocity = new Vector2(Mathf.Cos(angle) * speed, Mathf.Sin(angle) * speed + UnityEngine.Random.Range(45f, 120f));
                particles.Add(new UiParticle(
                    particleRect,
                    image,
                    origin + burstOffset,
                    velocity,
                    UnityEngine.Random.Range(-360f, 360f),
                    color));
            }

            return particles;
        }

        private Color GetFireworkColor(int index)
        {
            if (successFireworkColors == null || successFireworkColors.Length == 0)
            {
                return Color.white;
            }

            return successFireworkColors[index % successFireworkColors.Length];
        }

        private static void UpdateFireworkParticles(List<UiParticle> particles, float elapsedSeconds, float duration)
        {
            if (particles == null || particles.Count <= 1)
            {
                return;
            }

            float t = Mathf.Clamp01(elapsedSeconds / Mathf.Max(0.01f, duration));
            float gravity = 460f;
            float fade = Mathf.Pow(1f - t, 1.65f);
            for (int i = 1; i < particles.Count; i++)
            {
                UiParticle particle = particles[i];
                if (particle.Rect == null || particle.Graphic == null)
                {
                    continue;
                }

                Vector2 position = particle.StartPosition +
                    (particle.Velocity * elapsedSeconds) +
                    (Vector2.down * (0.5f * gravity * elapsedSeconds * elapsedSeconds));
                particle.Rect.anchoredPosition = position;
                particle.Rect.localEulerAngles = new Vector3(0f, 0f, particle.AngularSpeed * elapsedSeconds);
                particle.Rect.localScale = Vector3.one * Mathf.Lerp(1.15f, 0.35f, t);

                Color color = particle.Color;
                color.a *= fade;
                particle.Graphic.color = color;
            }
        }

        private static void DestroyFireworkParticles(List<UiParticle> particles)
        {
            if (particles == null || particles.Count == 0)
            {
                return;
            }

            RectTransform layer = particles[0].Rect;
            if (layer != null)
            {
                Destroy(layer.gameObject);
            }
        }

        private static void PlayResultSfx(bool succeeded)
        {
            if (succeeded)
            {
                SoundPlayer.PlayKeypadSuccessSfx();
                return;
            }

            SoundPlayer.PlayKeypadFailSfx();
        }

        protected override void OnAfterOpen()
        {
            if (inputButton != null)
            {
                inputButton.interactable = !isTutorialBlocking;
            }
        }

        // 슬라이더 핸들 중심이 트랙 시작과 끝까지 닿도록 런타임 설정을 보정한다.
        private void ConfigureTimingSlider()
        {
            if (timingSlider == null)
            {
                return;
            }

            timingSlider.minValue = 0f;
            timingSlider.maxValue = 1f;
            timingSlider.wholeNumbers = false;
            timingSlider.direction = Slider.Direction.LeftToRight;

            RectTransform handleRect = timingSlider.handleRect;
            if (handleRect == null)
            {
                return;
            }

            Vector2 pivot = handleRect.pivot;
            pivot.x = 0.5f;
            handleRect.pivot = pivot;

            RectTransform handleSlideArea = handleRect.parent as RectTransform;
            if (handleSlideArea == null)
            {
                return;
            }

            handleSlideArea.anchorMin = new Vector2(0f, handleSlideArea.anchorMin.y);
            handleSlideArea.anchorMax = new Vector2(1f, handleSlideArea.anchorMax.y);

            Vector2 anchoredPosition = handleSlideArea.anchoredPosition;
            anchoredPosition.x = 0f;
            handleSlideArea.anchoredPosition = anchoredPosition;

            Vector2 sizeDelta = handleSlideArea.sizeDelta;
            sizeDelta.x = 0f;
            handleSlideArea.sizeDelta = sizeDelta;
        }

        // 매 시도마다 허용 구간을 옮기고 씬의 금색 목표 영역도 같은 위치로 맞춘다.
        private void RandomizeSafeWindow()
        {
            float width = Mathf.Clamp01(safeWindowWidth);
            safeWindowStart = TimingCheckEvaluator.GetRandomizedSafeWindowStart(
                UnityEngine.Random.value,
                safeWindowMinimumStart,
                safeWindowMaximumStart,
                width);
            safeWindowEnd = safeWindowStart + width;

            if (safeWindowVisual == null)
            {
                return;
            }

            Vector2 anchorMin = safeWindowVisual.anchorMin;
            Vector2 anchorMax = safeWindowVisual.anchorMax;
            anchorMin.x = safeWindowStart;
            anchorMax.x = safeWindowEnd;
            safeWindowVisual.anchorMin = anchorMin;
            safeWindowVisual.anchorMax = anchorMax;

            Vector2 anchoredPosition = safeWindowVisual.anchoredPosition;
            anchoredPosition.x = 0f;
            safeWindowVisual.anchoredPosition = anchoredPosition;

            Vector2 sizeDelta = safeWindowVisual.sizeDelta;
            sizeDelta.x = 0f;
            safeWindowVisual.sizeDelta = sizeDelta;
        }

        private readonly struct UiParticle
        {
            public UiParticle(RectTransform layer)
                : this(layer, null, Vector2.zero, Vector2.zero, 0f, Color.white)
            {
            }

            public UiParticle(
                RectTransform rect,
                Graphic graphic,
                Vector2 startPosition,
                Vector2 velocity,
                float angularSpeed,
                Color color)
            {
                Rect = rect;
                Graphic = graphic;
                StartPosition = startPosition;
                Velocity = velocity;
                AngularSpeed = angularSpeed;
                Color = color;
            }

            public RectTransform Rect { get; }
            public Graphic Graphic { get; }
            public Vector2 StartPosition { get; }
            public Vector2 Velocity { get; }
            public float AngularSpeed { get; }
            public Color Color { get; }
        }
    }
}
