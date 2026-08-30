using System;
using Escape.Localization;
using Escape.Progress;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Escape.Rooms;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Escape.UI
{
    // 게임 시작 문구를 y축 슬라이스로 나눠 좌우에서 모이게 한다.
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class StartSplashUI : MonoBehaviour
    {
        private const string InputLockReason = "start_splash";

        [SerializeField] private TMP_Text splashText;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private Image touchBlockImage;
        [SerializeField] private Button testButton;
        [SerializeField, TsvId("Assets/Resources/Data/text.tsv")] private string tid = "start_splash";
        [SerializeField, TextArea] private string fallbackText = "ESCAPE START!";
        [SerializeField] private bool hideOnAwake = true;
        [SerializeField, Min(0f)] private float entryDuration = 0.62f;
        [SerializeField, Min(0f)] private float holdDuration = 0.16f;
        [SerializeField, Min(0f)] private float exitDuration = 0.24f;
        [SerializeField, Min(0f)] private float startOffsetX = 620f;
        [SerializeField, Range(1, 16)] private int verticalSliceCount = 6;
        [SerializeField, Min(0f)] private float sliceStaggerSeconds = 0.025f;
        [SerializeField, Range(0f, 1f)] private float hiddenAlpha = 0f;
        [SerializeField, Range(0f, 1f)] private float visibleAlpha = 1f;

        private readonly List<SliceVisual> sliceVisuals = new();
        private bool isPlaying;
        private bool templateTextEnabled = true;

        private readonly struct SliceVisual
        {
            public readonly RectTransform RectTransform;
            public readonly Vector2 CenterPosition;
            public readonly float Direction;
            public readonly int Index;

            public SliceVisual(RectTransform rectTransform, Vector2 centerPosition, float direction, int index)
            {
                RectTransform = rectTransform;
                CenterPosition = centerPosition;
                Direction = direction;
                Index = index;
            }
        }

        private void Awake()
        {
            ResolveReferences();
            if (hideOnAwake)
            {
                SetVisible(false);
            }
        }

        private void OnEnable()
        {
            testButton?.onClick.AddListener(PlayFromTestButton);
        }

        private void OnDisable()
        {
            testButton?.onClick.RemoveListener(PlayFromTestButton);
        }

        private void OnDestroy()
        {
            ClearSlices();
        }

        // 시작 스플래시를 한 번 재생하고 끝나면 다시 투명하게 숨긴다.
        public async UniTask PlayAsync(CancellationToken ct)
        {
            await PlayMessageAsync(ResolveMessage(), ct);
        }

        // 미니게임처럼 씬별 문구가 필요한 곳에서 같은 스플래시 연출을 재사용한다.
        public async UniTask PlayMessageAsync(string message, CancellationToken ct)
        {
            ResolveReferences();
            if (splashText == null || isPlaying)
            {
                return;
            }

            isPlaying = true;
            bool lockedBySplash = TryLockInput();
            try
            {
                message = string.IsNullOrWhiteSpace(message) ? fallbackText : message;
                gameObject.SetActive(true);
                SetVisible(true);
                splashText.text = message;
                splashText.ForceMeshUpdate();
                BuildSliceVisuals(message);

                await AnimateEntry(ct);
                ShowOriginalText();

                if (holdDuration > 0f)
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(holdDuration), ignoreTimeScale: false, cancellationToken: ct);
                }

                await FadeOut(ct);
            }
            finally
            {
                ClearSlices();
                splashText.enabled = templateTextEnabled;
                SetVisible(false);
                TryUnlockInput(lockedBySplash);
                isPlaying = false;
            }
        }

        // 현재 활성 씬의 TopUI에 있는 시작 스플래시를 우선 찾아 재생한다.
        public static async UniTask PlayOnActiveCanvasAsync(string message, CancellationToken ct)
        {
            StartSplashUI splashUI = FindPreferredSplashUI();
            if (splashUI == null)
            {
                return;
            }

            await splashUI.PlayMessageAsync(message, ct);
        }

        private static StartSplashUI FindPreferredSplashUI()
        {
            StartSplashUI[] candidates = FindObjectsByType<StartSplashUI>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            if (candidates.Length == 0)
            {
                return null;
            }

            UnityEngine.SceneManagement.Scene activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            StartSplashUI activeHierarchyCandidate = null;
            for (int i = 0; i < candidates.Length; i++)
            {
                StartSplashUI candidate = candidates[i];
                if (candidate == null)
                {
                    continue;
                }

                if (candidate.gameObject.scene == activeScene && candidate.gameObject.activeInHierarchy)
                {
                    return candidate;
                }

                if (activeHierarchyCandidate == null && candidate.gameObject.activeInHierarchy)
                {
                    activeHierarchyCandidate = candidate;
                }
            }

            return activeHierarchyCandidate != null ? activeHierarchyCandidate : candidates[0];
        }

        // 시작 스플래시가 떠 있는 동안 방/아이템 입력을 잠근다.
        private static bool TryLockInput()
        {
            GameSession state = GameSession.Instance;
            if (state == null || state.IsInputLocked)
            {
                return false;
            }

            state.SetInputLocked(true, InputLockReason);
            return true;
        }

        // 스플래시가 직접 건 입력 잠금만 해제한다.
        private static void TryUnlockInput(bool lockedBySplash)
        {
            GameSession state = GameSession.Instance;
            if (!lockedBySplash ||
                state == null ||
                !state.IsInputLocked ||
                !string.Equals(state.InputLockReason, InputLockReason, StringComparison.Ordinal))
            {
                return;
            }

            state.SetInputLocked(false);
        }

        // 테스트 버튼에서 스플래시만 단독 재생한다.
        private void PlayFromTestButton()
        {
            PlayFromTestButtonAsync(destroyCancellationToken).Forget();
        }

        private async UniTaskVoid PlayFromTestButtonAsync(CancellationToken ct)
        {
            try
            {
                await PlayAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, this);
            }
        }

        // 원본 TMP를 가로 슬라이스별 마스크 레이어로 복제한다.
        private void BuildSliceVisuals(string message)
        {
            ClearSlices();
            RectTransform sourceRect = splashText.rectTransform;
            Rect rect = sourceRect.rect;
            int sliceCount = Mathf.Max(1, verticalSliceCount);
            float sliceHeight = rect.height / sliceCount;
            templateTextEnabled = splashText.enabled;
            splashText.enabled = false;
            TMP_Text[] cloneTexts = new TMP_Text[sliceCount];
            for (int i = 0; i < sliceCount; i++)
            {
                cloneTexts[i] = Instantiate(splashText);
            }

            for (int i = 0; i < sliceCount; i++)
            {
                float centerY = rect.yMax - (sliceHeight * (i + 0.5f));
                float direction = i % 2 == 0 ? -1f : 1f;
                RectTransform maskRect = CreateSliceMask(sourceRect, i, sliceHeight, centerY);
                TMP_Text cloneText = cloneTexts[i];
                cloneText.transform.SetParent(maskRect, false);
                cloneText.name = $"Text_{i + 1:00}";
                cloneText.enabled = true;
                cloneText.text = message;
                cloneText.raycastTarget = false;
                cloneText.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
                cloneText.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                cloneText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                cloneText.rectTransform.sizeDelta = rect.size;
                cloneText.rectTransform.anchoredPosition = new Vector2(0f, -centerY);
                DisableCloneCanvasGroups(cloneText);
                cloneText.ForceMeshUpdate();

                sliceVisuals.Add(new SliceVisual(maskRect, new Vector2(0f, centerY), direction, i));
            }
        }

        // 슬라이스가 합쳐진 직후부터는 끊김 없는 원본 TMP만 보여준다.
        private void ShowOriginalText()
        {
            splashText.enabled = true;
            splashText.ForceMeshUpdate();
            ClearSlices();
        }

        // 한 슬라이스를 보여줄 RectMask2D 영역을 만든다.
        private RectTransform CreateSliceMask(RectTransform sourceRect, int index, float sliceHeight, float centerY)
        {
            GameObject maskObject = new($"SplashSlice_{index + 1:00}", typeof(RectTransform), typeof(RectMask2D));
            maskObject.layer = splashText.gameObject.layer;
            RectTransform maskRect = maskObject.GetComponent<RectTransform>();
            maskRect.SetParent(sourceRect, false);
            maskRect.anchorMin = new Vector2(0.5f, 0.5f);
            maskRect.anchorMax = new Vector2(0.5f, 0.5f);
            maskRect.pivot = new Vector2(0.5f, 0.5f);
            maskRect.sizeDelta = new Vector2(sourceRect.rect.width, sliceHeight);
            maskRect.anchoredPosition = new Vector2(0f, centerY);
            return maskRect;
        }

        // 복제된 TMP에 따라온 CanvasGroup은 슬라이스 표시를 막지 않게 만든다.
        private static void DisableCloneCanvasGroups(TMP_Text cloneText)
        {
            CanvasGroup[] groups = cloneText.GetComponents<CanvasGroup>();
            for (int i = 0; i < groups.Length; i++)
            {
                groups[i].alpha = 1f;
                groups[i].interactable = false;
                groups[i].blocksRaycasts = false;
            }
        }

        // y축 슬라이스가 홀수는 좌측, 짝수는 우측에서 중앙으로 모이게 한다.
        private async UniTask AnimateEntry(CancellationToken ct)
        {
            if (entryDuration <= 0f)
            {
                SetSliceProgress(1f);
                return;
            }

            float elapsed = 0f;
            while (elapsed < entryDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / entryDuration);
                SetSliceProgress(t);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            SetSliceProgress(1f);
        }

        // 각 슬라이스의 현재 위치를 진행률에 맞춰 갱신한다.
        private void SetSliceProgress(float rawProgress)
        {
            int lastIndex = Mathf.Max(1, sliceVisuals.Count - 1);
            for (int i = 0; i < sliceVisuals.Count; i++)
            {
                SliceVisual slice = sliceVisuals[i];
                if (slice.RectTransform == null)
                {
                    continue;
                }

                float delay = sliceStaggerSeconds * slice.Index;
                float duration = Mathf.Max(0.001f, entryDuration - (sliceStaggerSeconds * lastIndex));
                float t = Mathf.Clamp01((rawProgress * entryDuration - delay) / duration);
                float entryAmount = 1f - Smooth(t);
                slice.RectTransform.anchoredPosition = slice.CenterPosition + new Vector2(
                    slice.Direction * startOffsetX * entryAmount,
                    0f);
            }
        }

        // 스플래시 종료 시 전체 문구를 짧게 페이드아웃한다.
        private async UniTask FadeOut(CancellationToken ct)
        {
            if (canvasGroup == null || exitDuration <= 0f)
            {
                SetVisible(false);
                return;
            }

            float elapsed = 0f;
            float startAlpha = canvasGroup.alpha;
            while (elapsed < exitDuration)
            {
                elapsed += Time.deltaTime;
                float t = Smooth(Mathf.Clamp01(elapsed / exitDuration));
                canvasGroup.alpha = Mathf.Lerp(startAlpha, hiddenAlpha, t);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }

        private void SetVisible(bool visible)
        {
            if (canvasGroup == null)
            {
                return;
            }

            canvasGroup.alpha = visible ? visibleAlpha : hiddenAlpha;
            canvasGroup.interactable = visible;
            canvasGroup.blocksRaycasts = visible;

            if (touchBlockImage != null)
            {
                touchBlockImage.raycastTarget = visible;
            }
        }

        private void ResolveReferences()
        {
            canvasGroup ??= GetComponent<CanvasGroup>();
            EnsureTouchBlockImage();
        }

        // 스플래시 표시 중 하위 방/버튼 입력을 막는 투명 전체화면 Graphic을 보장한다.
        private void EnsureTouchBlockImage()
        {
            if (touchBlockImage == null)
            {
                touchBlockImage = GetComponent<Image>();
            }

            if (touchBlockImage == null)
            {
                touchBlockImage = gameObject.AddComponent<Image>();
            }

            Color color = touchBlockImage.color;
            color.a = 0f;
            touchBlockImage.color = color;
            touchBlockImage.raycastTarget = false;
        }

        // 입력된 tid로 시작 문구를 가져오고, 없으면 fallback을 사용한다.
        private string ResolveMessage()
        {
            return LocalizationService.Text(tid, fallbackText);
        }

        // 재생 중 만든 슬라이스 오브젝트를 정리한다.
        private void ClearSlices()
        {
            for (int i = 0; i < sliceVisuals.Count; i++)
            {
                RectTransform rectTransform = sliceVisuals[i].RectTransform;
                if (rectTransform == null)
                {
                    continue;
                }

                rectTransform.gameObject.SetActive(false);
                if (Application.isPlaying)
                {
                    Destroy(rectTransform.gameObject);
                }
                else
                {
                    DestroyImmediate(rectTransform.gameObject);
                }
            }

            sliceVisuals.Clear();
        }

        private static float Smooth(float t)
        {
            return t * t * (3f - 2f * t);
        }
    }
}
