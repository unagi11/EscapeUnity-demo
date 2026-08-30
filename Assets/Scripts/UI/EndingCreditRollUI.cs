using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Escape.UI
{
    // 엔딩 크레딧을 전체 화면 팝업으로 열고 아래에서 위로 재생한다.
    public sealed class EndingCreditRollUI : PopupUIBase, IPointerDownHandler, IPointerUpHandler
    {
        [Header("Popup")]
        [SerializeField] private RectTransform root;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private Image backgroundImage;
        [SerializeField] private bool hideOnAwake = true;

        [Header("Credit Roll")]
        [SerializeField] private RectTransform viewport;
        [SerializeField] private RectTransform content;
        [SerializeField] private Image titleLogoImage;
        [SerializeField] private LayoutElement titleLogoLayoutElement;
        [SerializeField] private Sprite koreanTitleLogo;
        [SerializeField] private Sprite englishTitleLogo;
        [SerializeField] private Sprite japaneseTitleLogo;
        [SerializeField] private TextMeshProUGUI creditText;
        [SerializeField] private LayoutElement creditTextLayoutElement;
        [SerializeField] private TextMeshProUGUI legalText;
        [SerializeField] private LayoutElement legalTextLayoutElement;
        [SerializeField] private Image madeWithUnityLogoImage;
        [SerializeField] private RectTransform endPromptRoot;
        [SerializeField] private CanvasGroup endPromptCanvasGroup;
        [SerializeField] private TextMeshProUGUI endPromptText;
        [SerializeField] private TMP_ColorGradient defaultFontColor;

        [Header("Playback")]
        [SerializeField, Min(0f)] private float scrollPixelsPerSecond = 18f;
        [SerializeField, Min(0f)] private float startPadding = 0f;
        [SerializeField, Min(0f)] private float endPadding = 64f;
        [SerializeField, Min(0f)] private float fadeInDuration = 0.25f;
        [SerializeField, Min(0f)] private float endPromptHoldDuration = 3f;
        [SerializeField, Min(0f)] private float fadeOutDuration = 0.5f;
        [SerializeField, Min(1f)] private float fastForwardMultiplier = 5f;
        [SerializeField] private Color textColor = new(0.92f, 0.92f, 0.88f, 1f);

        private bool openingFromInactiveState;
        private bool usingTransparentCreditStyle;
        private bool canPointerFastForward;
        private bool pointerFastForwardHeld;

        protected override GameObject PopupRoot
        {
            get => root != null ? root.gameObject : null;
            set => root = value != null ? value.transform as RectTransform : null;
        }

        protected override Button PopupOpenButton { get => null; set { } }
        protected override Button PopupCloseButton { get => null; set { } }
        protected override bool PopupHideOnAwake => hideOnAwake && !openingFromInactiveState;
        protected override float PopupFadeDuration => 0f;
        protected override CanvasGroup PopupCanvasGroup { get => canvasGroup; set => canvasGroup = value; }
        protected override bool CanCloseTopmost => false;

        private void Awake()
        {
            ValidateReferences();
            RefreshEnglishContent();
            InitializePopupChrome();
        }

        private void OnEnable()
        {
            RefreshEnglishContent();
        }

        private void OnDisable()
        {
            pointerFastForwardHeld = false;
        }

        // 씬 초기화 순서나 이전 재생 상태와 무관하게 열릴 때마다 전체 화면 루트를 중앙에 맞춘다.
        protected override void OnBeforeOpen()
        {
            ResetPopupPosition();
        }

        // 스토리 배경을 넘겨받으면 크레딧 배경으로 쓰고, 종료 후 원래 팝업 배경을 복원한다.
        public async UniTask PlayAsync(
            CancellationToken ct,
            Sprite backgroundOverride = null,
            bool fastForwardOnPointerHold = false)
        {
            if (!HasRequiredReferences())
            {
                Debug.LogError("EndingCreditRollUI has missing scene references.", this);
                return;
            }

            usingTransparentCreditStyle = backgroundOverride != null;
            canPointerFastForward = fastForwardOnPointerHold;
            pointerFastForwardHeld = false;
            root.SetAsLastSibling();
            openingFromInactiveState = true;
            try
            {
                Open();
            }
            finally
            {
                openingFromInactiveState = false;
            }

            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = true;
            canvasGroup.interactable = false;
            content.gameObject.SetActive(true);
            HideEndPromptImmediate();

            Sprite originalBackgroundSprite = backgroundImage.sprite;
            Color originalBackgroundColor = backgroundImage.color;
            if (backgroundOverride != null)
            {
                backgroundImage.sprite = backgroundOverride;
                backgroundImage.color = Color.white;
            }

            try
            {
                RefreshEnglishContent();
                await UniTask.Yield(PlayerLoopTiming.Update, ct);

                Canvas.ForceUpdateCanvases();
                RefreshCreditLayout();
                LayoutRebuilder.ForceRebuildLayoutImmediate(content);
                Canvas.ForceUpdateCanvases();

                float viewportHeight = Mathf.Max(1f, viewport.rect.height);
                float contentHeight = Mathf.Max(1f, LayoutUtility.GetPreferredHeight(content));
                content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, contentHeight);

                float startY = -contentHeight * 0.5f - startPadding;
                float endY = viewportHeight + contentHeight * 0.5f + endPadding;
                content.anchoredPosition = new Vector2(0f, startY);

                await FadeCanvasGroup(canvasGroup, 0f, 1f, fadeInDuration, ct);

                float distance = Mathf.Max(1f, endY - startY);
                float duration = distance / Mathf.Max(1f, scrollPixelsPerSecond);
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    ct.ThrowIfCancellationRequested();
                    elapsed += Time.deltaTime * GetCurrentSpeedMultiplier();
                    float y = Mathf.Lerp(startY, endY, Mathf.Clamp01(elapsed / duration));
                    content.anchoredPosition = new Vector2(0f, y);
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }

                content.anchoredPosition = new Vector2(0f, endY);
                content.gameObject.SetActive(false);
                await ShowEndPromptThenReturn(ct);
                canPointerFastForward = false;
                pointerFastForwardHeld = false;
                await FadeCanvasGroup(canvasGroup, 1f, 0f, fadeOutDuration, ct);
            }
            finally
            {
                canPointerFastForward = false;
                pointerFastForwardHeld = false;
                if (content != null)
                {
                    content.gameObject.SetActive(true);
                }

                HideEndPromptImmediate();
                backgroundImage.sprite = originalBackgroundSprite;
                backgroundImage.color = originalBackgroundColor;
                usingTransparentCreditStyle = false;
                ApplyCreditForegroundStyle();
                if (root != null)
                {
                    Close();
                }
            }
        }

        // 타이틀 크레딧 화면을 누르는 동안 빠른 재생 배율을 적용한다.
        public void OnPointerDown(PointerEventData eventData)
        {
            if (canPointerFastForward)
            {
                pointerFastForwardHeld = true;
            }
        }

        // 화면에서 손가락이나 마우스를 떼면 즉시 기본 재생 속도로 돌아간다.
        public void OnPointerUp(PointerEventData eventData)
        {
            pointerFastForwardHeld = false;
        }

        // 게임 언어와 무관하게 엔딩 크레딧의 모든 문구와 로고를 영어로 표시한다.
        private void RefreshEnglishContent()
        {
            RefreshEnglishTitleLogo();

            if (creditText != null)
            {
                creditText.text = "Presented by";
                creditText.fontSize = 12f;
                creditText.enableAutoSizing = false;
                creditText.fontStyle = FontStyles.Normal;
                creditText.alignment = TextAlignmentOptions.Center;
                creditText.lineSpacing = 6f;
                creditText.textWrappingMode = TextWrappingModes.Normal;
                creditText.raycastTarget = false;
            }

            if (legalText != null)
            {
                legalText.text =
                    "© 2026 UnyaUnya Games. All rights reserved.\n\n\n" +
                    "Licenses are available from the title menu.";
                legalText.fontSize = 10f;
                legalText.enableAutoSizing = false;
                legalText.fontStyle = FontStyles.Normal;
                legalText.alignment = TextAlignmentOptions.Center;
                legalText.lineSpacing = 4f;
                legalText.textWrappingMode = TextWrappingModes.Normal;
                legalText.raycastTarget = false;
            }

            if (endPromptText != null)
            {
                endPromptText.text = "Thank You for Playing";
                endPromptText.fontSize = 12f;
                endPromptText.enableVertexGradient = false;
                endPromptText.alignment = TextAlignmentOptions.Center;
                endPromptText.lineSpacing = 4f;
                endPromptText.raycastTarget = false;
            }

            ApplyCreditForegroundStyle();
        }

        // 배경이 비치는 엔딩 크래딧에서는 밝은 하늘 위의 글자와 Unity 로고를 검정색으로 표시한다.
        private void ApplyCreditForegroundStyle()
        {
            Color currentTextColor = usingTransparentCreditStyle ? Color.black : textColor;
            if (creditText != null)
            {
                creditText.color = currentTextColor;
                creditText.enableVertexGradient = !usingTransparentCreditStyle;
                creditText.colorGradientPreset = usingTransparentCreditStyle ? null : defaultFontColor;
            }

            if (legalText != null)
            {
                legalText.color = currentTextColor;
            }

            if (endPromptText != null)
            {
                endPromptText.color = currentTextColor;
            }

            if (madeWithUnityLogoImage != null)
            {
                madeWithUnityLogoImage.color = usingTransparentCreditStyle ? Color.black : Color.white;
            }
        }

        // 뷰포트 폭을 기준으로 크레딧 자식의 폭과 텍스트 높이를 다시 계산한다.
        private void RefreshCreditLayout()
        {
            if (viewport == null || content == null ||
                creditText == null || creditTextLayoutElement == null ||
                legalText == null || legalTextLayoutElement == null)
            {
                return;
            }

            float availableWidth = Mathf.Max(1f, viewport.rect.width - 16f);
            content.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, availableWidth);

            VerticalLayoutGroup layoutGroup = content.GetComponent<VerticalLayoutGroup>();
            if (layoutGroup != null)
            {
                layoutGroup.childControlWidth = true;
                layoutGroup.childControlHeight = true;
            }

            ResizeTextToPreferredHeight(creditText, creditTextLayoutElement, availableWidth);
            ResizeTextToPreferredHeight(legalText, legalTextLayoutElement, availableWidth);
        }

        // 지정된 폭에서 텍스트가 잘리지 않도록 레이아웃 높이를 실제 내용에 맞춘다.
        private static void ResizeTextToPreferredHeight(
            TextMeshProUGUI targetText,
            LayoutElement targetLayout,
            float availableWidth)
        {
            targetLayout.preferredWidth = availableWidth;
            targetText.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, availableWidth);
            Vector2 preferredSize = targetText.GetPreferredValues(targetText.text, availableWidth, 10000f);
            float preferredHeight = Mathf.Ceil(preferredSize.y) + 16f;
            targetLayout.preferredHeight = preferredHeight;
            targetText.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, preferredHeight);
            targetText.ForceMeshUpdate();
        }

        // 감사 문구를 중앙에 잠시 유지한 뒤 호출 측이 타이틀로 복귀하도록 재생을 끝낸다.
        private async UniTask ShowEndPromptThenReturn(CancellationToken ct)
        {
            endPromptRoot.gameObject.SetActive(true);
            endPromptCanvasGroup.blocksRaycasts = true;
            endPromptCanvasGroup.interactable = false;
            await FadeCanvasGroup(endPromptCanvasGroup, 0f, 1f, fadeInDuration, ct);

            float elapsed = 0f;
            while (elapsed < endPromptHoldDuration)
            {
                ct.ThrowIfCancellationRequested();
                elapsed += Time.deltaTime;
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }

        // 영어 타이틀 스프라이트를 선택하고 원본 비율에 맞춰 높이를 조정한다.
        private void RefreshEnglishTitleLogo()
        {
            if (titleLogoImage == null)
            {
                return;
            }

            Sprite selectedLogo = englishTitleLogo;

            titleLogoImage.sprite = selectedLogo;
            titleLogoImage.preserveAspect = true;
            titleLogoImage.raycastTarget = false;

            if (selectedLogo != null && titleLogoLayoutElement != null)
            {
                float width = Mathf.Max(1f, titleLogoLayoutElement.preferredWidth);
                float aspect = selectedLogo.rect.height / Mathf.Max(1f, selectedLogo.rect.width);
                titleLogoLayoutElement.preferredHeight = Mathf.Ceil(width * aspect);
            }
        }

        private async UniTask FadeCanvasGroup(
            CanvasGroup target,
            float from,
            float to,
            float duration,
            CancellationToken ct)
        {
            if (duration <= 0f)
            {
                target.alpha = to;
                return;
            }

            float elapsed = 0f;
            target.alpha = from;
            while (elapsed < duration)
            {
                ct.ThrowIfCancellationRequested();
                elapsed += Time.deltaTime * GetCurrentSpeedMultiplier();
                target.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            target.alpha = to;
        }

        private void HideEndPromptImmediate()
        {
            if (endPromptCanvasGroup != null)
            {
                endPromptCanvasGroup.alpha = 0f;
                endPromptCanvasGroup.blocksRaycasts = false;
                endPromptCanvasGroup.interactable = false;
            }

            if (endPromptRoot != null)
            {
                endPromptRoot.gameObject.SetActive(false);
            }
        }

        private bool HasRequiredReferences()
        {
            return root != null &&
                canvasGroup != null &&
                backgroundImage != null &&
                viewport != null &&
                content != null &&
                titleLogoImage != null &&
                titleLogoLayoutElement != null &&
                koreanTitleLogo != null &&
                englishTitleLogo != null &&
                japaneseTitleLogo != null &&
                creditText != null &&
                creditTextLayoutElement != null &&
                legalText != null &&
                legalTextLayoutElement != null &&
                madeWithUnityLogoImage != null &&
                endPromptRoot != null &&
                endPromptCanvasGroup != null &&
                endPromptText != null &&
                defaultFontColor != null;
        }

        private void ValidateReferences()
        {
            if (!HasRequiredReferences())
            {
                Debug.LogError("EndingCreditRollUI scene references are incomplete.", this);
            }
        }

        private float GetCurrentSpeedMultiplier()
        {
            return pointerFastForwardHeld || IsFastForwardHeld() ? fastForwardMultiplier : 1f;
        }

        private static bool IsFastForwardHeld()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null &&
                (Keyboard.current.leftCtrlKey.isPressed ||
                 Keyboard.current.rightCtrlKey.isPressed))
            {
                return true;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
            {
                return true;
            }
#endif
            return false;
        }
    }
}
