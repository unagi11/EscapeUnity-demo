using System;
using System.Collections.Generic;
using TMPro;
using System.Text;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Escape.UI
{
    // 대사 팝업의 텍스트, 초상화, 입력 대기 커서를 관리한다.
    public sealed class DialoguePopupUI : PopupUIBase
    {
        private const string PortraitSortingLayerName = "Unlit";

        [Header("Popup")]
        [SerializeField] private GameObject root;
        [SerializeField] private Button openButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private bool hideOnAwake = true;
        [SerializeField, Min(0f)] private float fadeDuration;
        [SerializeField] private CanvasGroup canvasGroup;

        [Header("Dialogue")]
        [SerializeField] private GameObject touchPanel;
        [SerializeField] private GameObject speakerPanel;
        [SerializeField] private TMP_Text speakerText;
        [SerializeField] private GameObject bodyPanel;
        [SerializeField] private TMP_Text bodyText;
        [SerializeField] private GameObject centerBodyPanel;
        [SerializeField] private TMP_Text centerBodyText;
        [SerializeField] private Transform cursorText;
        [SerializeField] private Transform portraitRoot;
        [SerializeField] private SpriteRenderer portraitRenderer;
        [SerializeField] private SpriteRenderer defaultPortraitRenderer;
        [SerializeField] private SpriteRenderer closeupPortraitRenderer;
        [SerializeField, Min(0.01f)] private float portraitScale = 2.5f;
        [SerializeField] private Color portraitTint = new(0.30f, 0.46f, 0.34f, 1f);
        [SerializeField, Min(0f)] private float portraitTintDuration = 0.18f;
#pragma warning disable CS0414
        [FormerlySerializedAs("cursorRotationSpeed")]
        [SerializeField, HideInInspector] private float legacyCursorRotationSpeed = 180f;
        [FormerlySerializedAs("cursorBounceHeight")]
        [SerializeField, HideInInspector] private float legacyCursorBounceHeight = 4f;
#pragma warning restore CS0414
        [FormerlySerializedAs("cursorBounceDuration")]
        [SerializeField, Min(0.05f)] private float cursorBlinkDuration = 0.75f;
        [SerializeField, HideInInspector, Min(0.01f)] private float bodyShakeDuration = 0.18f;
        [SerializeField, HideInInspector, Min(0f)] private float bodyShakeAmount = 4f;
        [SerializeField, HideInInspector, Min(1f)] private float bodyShakeFrequency = 80f;

        [Header("Select Panel")]
        [SerializeField] private GameObject selectPanel;
        [SerializeField] private Button[] selectButtons = Array.Empty<Button>();
        [SerializeField] private CanvasGroup selectCanvasGroup;

        private bool isWaitingForInput;
        private string bodyRawText = string.Empty;
        private Vector3 cursorRestLocalPosition;
        private Vector2 cursorRestAnchoredPosition;
        private float cursorRestAlpha = 1f;
        private bool hasCursorRestPosition;
        private Vector3 portraitRootBaseScale = Vector3.one;
        private bool hasPortraitRootBaseScale;
        private Color portraitTintStart = Color.white;
        private Color portraitTintTarget = Color.white;
        private float portraitTintElapsed;
        private bool isPortraitTintTransitioning;
        private RectTransform bodyShakeTarget;
        private Vector2 bodyShakeBaseAnchoredPosition;
        private float bodyShakeElapsed;
        private bool isBodyShaking;
        private readonly List<Action> choiceActions = new();

        // Default/Closeup 자식 렌더러 중 실제 표시에 쓰는 기본 렌더러.
        private SpriteRenderer PrimaryPortraitRenderer => defaultPortraitRenderer != null ? defaultPortraitRenderer : portraitRenderer;

        public Transform CurrentPortraitTransform
        {
            get
            {
                SpriteRenderer primary = PrimaryPortraitRenderer;
                return primary != null && primary.sprite != null ? portraitRoot : null;
            }
        }
        public Color DefaultPortraitTint => portraitTint;
        public float DefaultPortraitScale => portraitScale;
        public int ChoiceCapacity
        {
            get
            {
                ResolveSelectReferences();
                return selectButtons.Length;
            }
        }

        protected override GameObject PopupRoot { get => root; set => root = value; }
        protected override Button PopupOpenButton { get => openButton; set => openButton = value; }
        protected override Button PopupCloseButton { get => closeButton; set => closeButton = value; }
        protected override bool PopupHideOnAwake => hideOnAwake;
        protected override float PopupFadeDuration => fadeDuration;
        protected override CanvasGroup PopupCanvasGroup { get => canvasGroup; set => canvasGroup = value; }
        // 선택지는 반드시 버튼으로 결정해야 하므로 ESC 공통 닫기를 막는다.
        protected override bool CanCloseTopmost => selectPanel == null || !selectPanel.activeSelf;

        private void Awake()
        {
            ResolveReferences();
            InitializePopupChrome();
            InitializeSelectHidden();
            SetBodyType("DEFAULT");
            SetTouchBlocking(false);
            SetWaitingForInput(false);
            ClearPortraits();
        }

        private void ResolveReferences()
        {
            root ??= gameObject;
            canvasGroup ??= root.GetComponent<CanvasGroup>();
            if (bodyText == null)
            {
                bodyText = GetComponentInChildren<TMP_Text>(true);
            }

            if (cursorText == null)
            {
                ResolveCursorText();
            }

            ResolveTouchPanel();
            ResolveSpeakerPanel();
            ResolvePortraitRoot();
            ResolvePortraitRenderers();
            CapturePortraitRootBaseScale();
            ResolveSelectReferences();
        }

        private void Update()
        {
            UpdateBodyShake(Time.deltaTime);
            UpdatePortraitTintTransition(Time.deltaTime);

            if (!isWaitingForInput || ResolveCursorText() == null)
            {
                return;
            }

            ApplyCursorBlink();
        }

        public void Show(string message)
        {
            Show(string.Empty, message);
        }

        public void Show(string speaker, string message)
        {
            Show(speaker, message, "DEFAULT");
        }

        // dialogue.type에 따라 일반 본문과 중앙 본문 중 출력 대상을 선택한다.
        public void Show(string speaker, string message, string dialogueType)
        {
            Open();
            SetTouchBlocking(true);
            SetSpeaker(speaker);
            SetBody(message);
            SetBodyType(dialogueType);
            SetWaitingForInput(false);
        }

        public void SetBody(string message)
        {
            StopBodyShake();
            bodyRawText = message ?? string.Empty;
            SetTextBody(bodyText, bodyRawText);
            SetTextBody(centerBodyText, bodyRawText);
        }

        private static void SetTextBody(TMP_Text target, string message)
        {
            if (target == null)
            {
                return;
            }

            target.text = message;
            target.maxVisibleCharacters = int.MaxValue;
        }

        // 빈 본문 줄에서는 일반/중앙 본문 패널을 모두 숨긴다.
        private static bool HasVisibleBodyText(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            bool inTag = false;
            for (int i = 0; i < message.Length; i++)
            {
                char character = message[i];
                if (character == '<')
                {
                    inTag = true;
                    continue;
                }

                if (character == '>' && inTag)
                {
                    inTag = false;
                    continue;
                }

                if (!inTag && !char.IsWhiteSpace(character))
                {
                    return true;
                }
            }

            return false;
        }

        // 현재 본문 내용을 지우고 본문 패널을 숨김 상태로 돌린다.
        private void ClearBody()
        {
            SetBody(string.Empty);
            SetBodyType("DEFAULT");
        }

        public void SetBodyVisibleCharacters(int visibleCharacters)
        {
            TMP_Text activeBodyText = GetActiveBodyText();
            if (activeBodyText != null)
            {
                activeBodyText.maxVisibleCharacters = int.MaxValue;
                activeBodyText.text = BuildRevealedText(bodyRawText, visibleCharacters);
            }
        }

        // 본문이 있을 때만 DEFAULT는 BodyPanel, CENTER는 CenterBodyPanel을 활성화한다.
        public void SetBodyType(string dialogueType)
        {
            bool hasBody = HasVisibleBodyText(bodyRawText);
            bool useCenter = string.Equals(dialogueType?.Trim(), "CENTER", StringComparison.OrdinalIgnoreCase);
            bodyPanel?.SetActive(hasBody && !useCenter);
            centerBodyPanel?.SetActive(hasBody && useCenter);
        }

        // 현재 활성화된 패널의 본문 텍스트를 반환한다.
        private TMP_Text GetActiveBodyText()
        {
            return centerBodyPanel != null && centerBodyPanel.activeSelf
                ? centerBodyText
                : bodyText;
        }

        // <shake> 인라인 큐가 호출되면 현재 활성 대사창을 짧게 흔든다.
        public void PlayBodyShake()
        {
            RectTransform target = GetActiveBodyPanelTransform();
            if (target == null)
            {
                return;
            }

            StopBodyShake();
            bodyShakeTarget = target;
            bodyShakeBaseAnchoredPosition = target.anchoredPosition;
            bodyShakeElapsed = 0f;
            isBodyShaking = true;
        }

        // 줄 전환이나 팝업 종료 시 흔들린 대사창을 반드시 원위치로 복구한다.
        public void StopBodyShake()
        {
            if (bodyShakeTarget != null)
            {
                bodyShakeTarget.anchoredPosition = bodyShakeBaseAnchoredPosition;
            }

            bodyShakeTarget = null;
            bodyShakeElapsed = 0f;
            isBodyShaking = false;
        }

        private RectTransform GetActiveBodyPanelTransform()
        {
            GameObject activePanel = centerBodyPanel != null && centerBodyPanel.activeSelf
                ? centerBodyPanel
                : bodyPanel;
            return activePanel != null && activePanel.activeInHierarchy
                ? activePanel.transform as RectTransform
                : null;
        }

        private void UpdateBodyShake(float deltaTime)
        {
            if (!isBodyShaking || bodyShakeTarget == null)
            {
                return;
            }

            bodyShakeElapsed += Mathf.Max(0f, deltaTime);
            float progress = Mathf.Clamp01(bodyShakeElapsed / Mathf.Max(0.01f, bodyShakeDuration));
            float damping = 1f - (progress * progress * (3f - (2f * progress)));
            float x = Mathf.Sin(bodyShakeElapsed * bodyShakeFrequency) * bodyShakeAmount * damping;
            float y = Mathf.Sin(bodyShakeElapsed * bodyShakeFrequency * 1.61f) * bodyShakeAmount * 0.35f * damping;
            bodyShakeTarget.anchoredPosition = bodyShakeBaseAnchoredPosition + new Vector2(x, y);

            if (progress >= 1f)
            {
                StopBodyShake();
            }
        }

        public void SetSpeaker(string speaker)
        {
            bool hasSpeaker = !string.IsNullOrWhiteSpace(speaker);

            if (speakerText != null)
            {
                speakerText.text = speaker ?? string.Empty;
                speakerText.gameObject.SetActive(hasSpeaker);
            }

            if (ResolveSpeakerPanel() != null)
            {
                speakerPanel.SetActive(hasSpeaker);
            }
        }

        public void Hide()
        {
            HideChoices();
            SetWaitingForInput(false);
            SetTouchBlocking(false);
            ClearBody();
            SetSpeaker(string.Empty);
            ClearPortraits();
            Close();
        }

        public void ShowPortrait(Sprite portraitSprite, Color tint, float scale)
        {
            ResolvePortraitRoot();
            ResolvePortraitRenderers();

            // 프레이밍은 Default/Closeup 자식에 미리 설정돼 있으므로 scale은 사용하지 않는다.
            _ = scale;

            if (portraitSprite == null || portraitRoot == null || PrimaryPortraitRenderer == null)
            {
                return;
            }

            ClearPortraits();
            ApplyPortraitSprite(defaultPortraitRenderer, portraitSprite, tint);
            ApplyPortraitSprite(closeupPortraitRenderer, portraitSprite, tint);
            SetPortraitCloseup(false);
        }

        // 현재 표시 중인 포트레이트의 RGB를 보간하고 페이드에서 사용하는 알파는 보존한다.
        public void SetPortraitTint(Color tint)
        {
            ResolvePortraitRenderers();
            SpriteRenderer primary = PrimaryPortraitRenderer;
            if (primary == null || primary.sprite == null)
            {
                return;
            }

            tint.a = 1f;
            if (isPortraitTintTransitioning && ApproximatelySameRgb(portraitTintTarget, tint))
            {
                return;
            }

            portraitTintStart = primary.color;
            portraitTintStart.a = 1f;
            portraitTintTarget = tint;
            portraitTintElapsed = 0f;

            if (portraitTintDuration <= 0f || ApproximatelySameRgb(portraitTintStart, portraitTintTarget))
            {
                ApplyPortraitTintToAll(portraitTintTarget);
                isPortraitTintTransitioning = false;
                return;
            }

            isPortraitTintTransitioning = true;
        }

        private void UpdatePortraitTintTransition(float deltaTime)
        {
            if (!isPortraitTintTransitioning)
            {
                return;
            }

            portraitTintElapsed += Mathf.Max(0f, deltaTime);
            float progress = Mathf.Clamp01(portraitTintElapsed / portraitTintDuration);
            float easedProgress = progress * progress * (3f - (2f * progress));
            ApplyPortraitTintToAll(Color.Lerp(portraitTintStart, portraitTintTarget, easedProgress));

            if (progress >= 1f)
            {
                isPortraitTintTransitioning = false;
            }
        }

        private void ApplyPortraitTintToAll(Color tint)
        {
            ApplyPortraitTint(defaultPortraitRenderer, tint);
            ApplyPortraitTint(closeupPortraitRenderer, tint);
            ApplyPortraitTint(portraitRenderer, tint);
        }

        private static bool ApproximatelySameRgb(Color left, Color right)
        {
            return Mathf.Approximately(left.r, right.r)
                && Mathf.Approximately(left.g, right.g)
                && Mathf.Approximately(left.b, right.b);
        }

        // 클로즈업 여부에 따라 Default/Closeup 렌더러의 표시를 전환한다.
        public void SetPortraitCloseup(bool closeup)
        {
            ResolvePortraitRenderers();
            SetRendererVisible(defaultPortraitRenderer, !closeup);
            SetRendererVisible(closeupPortraitRenderer, closeup);
        }

        // 지정 렌더러에 스프라이트와 색상을 설정한다(표시 여부는 별도 제어).
        private void ApplyPortraitSprite(SpriteRenderer renderer, Sprite portraitSprite, Color tint)
        {
            if (renderer == null)
            {
                return;
            }

            renderer.sprite = portraitSprite;
            renderer.color = tint;
            renderer.sortingLayerName = PortraitSortingLayerName;
        }

        private static void ApplyPortraitTint(SpriteRenderer renderer, Color tint)
        {
            if (renderer == null || renderer.sprite == null)
            {
                return;
            }

            tint.a = renderer.color.a;
            renderer.color = tint;
        }

        // 스프라이트가 있을 때만 렌더러를 켠다.
        private static void SetRendererVisible(SpriteRenderer renderer, bool visible)
        {
            if (renderer != null)
            {
                renderer.enabled = visible && renderer.sprite != null;
            }
        }

        public void ClearPortraits()
        {
            isPortraitTintTransitioning = false;
            ResolvePortraitRenderers();
            ClearRenderer(defaultPortraitRenderer);
            ClearRenderer(closeupPortraitRenderer);
            ClearRenderer(portraitRenderer);

            if (portraitRoot != null && hasPortraitRootBaseScale)
            {
                portraitRoot.localScale = portraitRootBaseScale;
            }
        }

        private static void ClearRenderer(SpriteRenderer renderer)
        {
            if (renderer != null)
            {
                renderer.sprite = null;
                renderer.enabled = false;
            }
        }

        public void SetTouchBlocking(bool blocking)
        {
            if (ResolveTouchPanel() == null)
            {
                return;
            }

            touchPanel.SetActive(blocking);
            Graphic graphic = touchPanel.GetComponent<Graphic>();
            if (graphic != null)
            {
                graphic.raycastTarget = blocking;
            }
        }

        public void SetWaitingForInput(bool waiting)
        {
            isWaitingForInput = waiting;

            if (ResolveCursorText() == null)
            {
                return;
            }

            cursorText.gameObject.SetActive(waiting);
            if (waiting)
            {
                CaptureCursorRestPosition();
                ApplyCursorBlink();
            }
            else
            {
                ResetCursorVisual();
            }
        }

        public void SetVisible(bool visible)
        {
            SetPopupVisibleImmediate(visible);
        }

        // 대화 선택지 컷에서 하위 SelectPanel 버튼들을 표시한다.
        public void ShowChoices(IReadOnlyList<string> labels, Action<int> onSelected)
        {
            Open();
            SetTouchBlocking(false);
            SetWaitingForInput(false);
            ResolveSelectReferences();
            ClearChoiceCallbacks();

            labels ??= Array.Empty<string>();
            int visibleCount = Mathf.Min(labels.Count, selectButtons.Length);
            for (int i = 0; i < selectButtons.Length; i++)
            {
                Button button = selectButtons[i];
                if (button == null)
                {
                    continue;
                }

                bool visible = i < visibleCount;
                button.gameObject.SetActive(visible);
                button.onClick.RemoveAllListeners();

                if (!visible)
                {
                    continue;
                }

                SetButtonText(button, labels[i]);
                int choiceIndex = i;
                Action action = () =>
                {
                    HideChoices();
                    onSelected?.Invoke(choiceIndex);
                };
                choiceActions.Add(action);
                button.onClick.AddListener(() => action());
            }

            if (selectPanel != null)
            {
                selectPanel.SetActive(true);
                selectPanel.transform.SetAsLastSibling();
            }

            RefreshSelectLayout();
            SetSelectVisible(visibleCount > 0);
        }

        // 선택 콜백과 버튼 상태는 유지한 채 현재 선택지 문구만 새 언어로 바꾼다.
        public void RefreshChoiceLabels(IReadOnlyList<string> labels)
        {
            ResolveSelectReferences();
            labels ??= Array.Empty<string>();
            int visibleCount = Mathf.Min(labels.Count, selectButtons.Length);
            for (int i = 0; i < visibleCount; i++)
            {
                Button button = selectButtons[i];
                if (button != null && button.gameObject.activeSelf)
                {
                    SetButtonText(button, labels[i]);
                }
            }

            RefreshSelectLayout();
        }

        // 선택지 패널을 닫고 임시 콜백을 정리한다.
        public void HideChoices()
        {
            ClearChoiceCallbacks();
            InitializeSelectHidden();
        }

        public void Configure(TMP_Text text)
        {
            bodyText = text;
        }

        public void Configure(TMP_Text body, TMP_Text speaker)
        {
            bodyText = body;
            speakerText = speaker;
        }

        private Transform ResolveCursorText()
        {
            if (cursorText == null)
            {
                cursorText = FindChildByName(transform, "CursorText");
            }

            return cursorText;
        }

        // 입력 대기 커서를 제자리에서 깜빡이게 한다.
        private void ApplyCursorBlink()
        {
            CaptureCursorRestPosition();

            float duration = Mathf.Max(0.05f, cursorBlinkDuration);
            float phase = Mathf.Repeat(Time.time, duration) / duration;
            float alpha = phase < 0.5f ? cursorRestAlpha : 0f;

            SetCursorAlpha(alpha);

            if (cursorText is RectTransform rectTransform)
            {
                rectTransform.anchoredPosition = cursorRestAnchoredPosition;
                rectTransform.localRotation = Quaternion.identity;
                return;
            }

            cursorText.localPosition = cursorRestLocalPosition;
            cursorText.localRotation = Quaternion.identity;
        }

        // 커서를 숨길 때 다음 표시를 위해 원래 위치, 회전, 투명도를 복원한다.
        private void ResetCursorVisual()
        {
            if (!hasCursorRestPosition)
            {
                SetCursorAlpha(1f);
                return;
            }

            if (cursorText is RectTransform rectTransform)
            {
                rectTransform.anchoredPosition = cursorRestAnchoredPosition;
            }
            else
            {
                cursorText.localPosition = cursorRestLocalPosition;
            }

            cursorText.localRotation = Quaternion.identity;
            SetCursorAlpha(cursorRestAlpha);
        }

        // 커서가 배치된 원래 위치를 애니메이션 기준점으로 저장한다.
        private void CaptureCursorRestPosition()
        {
            if (hasCursorRestPosition || cursorText == null)
            {
                return;
            }

            cursorRestLocalPosition = cursorText.localPosition;
            if (cursorText is RectTransform rectTransform)
            {
                cursorRestAnchoredPosition = rectTransform.anchoredPosition;
            }

            cursorRestAlpha = GetCursorAlpha();
            hasCursorRestPosition = true;
        }

        // 커서 텍스트의 현재 투명도를 애니메이션 기준값으로 읽는다.
        private float GetCursorAlpha()
        {
            if (cursorText == null)
            {
                return 1f;
            }

            Graphic graphic = cursorText.GetComponent<Graphic>();
            if (graphic != null)
            {
                return graphic.color.a;
            }

            CanvasRenderer canvasRenderer = cursorText.GetComponent<CanvasRenderer>();
            return canvasRenderer != null ? canvasRenderer.GetAlpha() : 1f;
        }

        // 커서 텍스트 렌더러의 투명도만 바꿔 레이아웃 변화 없이 깜빡인다.
        private void SetCursorAlpha(float alpha)
        {
            if (cursorText == null)
            {
                return;
            }

            Graphic graphic = cursorText.GetComponent<Graphic>();
            if (graphic != null)
            {
                Color color = graphic.color;
                color.a = alpha;
                graphic.color = color;
                return;
            }

            CanvasRenderer canvasRenderer = cursorText.GetComponent<CanvasRenderer>();
            if (canvasRenderer != null)
            {
                canvasRenderer.SetAlpha(alpha);
            }
        }

        private GameObject ResolveTouchPanel()
        {
            if (touchPanel == null)
            {
                Transform touchPanelTransform = FindChildByName(transform, "TouchPanel");
                if (touchPanelTransform != null)
                {
                    touchPanel = touchPanelTransform.gameObject;
                }
            }

            return touchPanel;
        }

        private GameObject ResolveSpeakerPanel()
        {
            if (speakerPanel == null)
            {
                Transform speakerPanelTransform = FindChildByName(transform, "SpeakerPanel");
                if (speakerPanelTransform != null)
                {
                    speakerPanel = speakerPanelTransform.gameObject;
                }
            }

            if (speakerPanel == null && speakerText != null && speakerText.transform.parent != null)
            {
                speakerPanel = speakerText.transform.parent.gameObject;
            }

            return speakerPanel;
        }

        private Transform ResolvePortraitRoot()
        {
            if (portraitRoot == null && portraitRenderer != null)
            {
                portraitRoot = portraitRenderer.transform;
            }

            if (portraitRoot == null)
            {
                Transform foundRoot = FindChildByName(transform, "PortraitRoot");
                portraitRoot = foundRoot != null ? foundRoot : transform;
            }

            return portraitRoot;
        }

        // PortraitRoot 하위의 Default/Closeup SpriteRenderer 참조를 확인한다.
        private void ResolvePortraitRenderers()
        {
            if (ResolvePortraitRoot() == null)
            {
                return;
            }

            if (defaultPortraitRenderer == null)
            {
                Transform defaultChild = FindChildByName(portraitRoot, "Default");
                if (defaultChild != null)
                {
                    defaultPortraitRenderer = defaultChild.GetComponent<SpriteRenderer>();
                }
            }

            if (closeupPortraitRenderer == null)
            {
                Transform closeupChild = FindChildByName(portraitRoot, "Closeup");
                if (closeupChild != null)
                {
                    closeupPortraitRenderer = closeupChild.GetComponent<SpriteRenderer>();
                }
            }

            // 구조 변경 이전 씬과의 호환을 위해 단일 렌더러도 확인한다.
            if (defaultPortraitRenderer == null && portraitRenderer == null)
            {
                portraitRenderer = portraitRoot.GetComponent<SpriteRenderer>();
            }
        }

        // SpriteRenderer 전환 전 RectTransform 기준 크기를 보존할 배율을 저장한다.
        private void CapturePortraitRootBaseScale()
        {
            if (hasPortraitRootBaseScale || portraitRoot == null)
            {
                return;
            }

            portraitRootBaseScale = portraitRoot.localScale;
            hasPortraitRootBaseScale = true;
        }

        private void ResolveSelectReferences()
        {
            if (selectPanel == null)
            {
                Transform selectPanelTransform = FindChildByName(transform, "SelectPanel");
                if (selectPanelTransform != null)
                {
                    selectPanel = selectPanelTransform.gameObject;
                }
            }

            if (selectPanel == null)
            {
                selectButtons = Array.Empty<Button>();
                return;
            }

            selectCanvasGroup ??= selectPanel.GetComponent<CanvasGroup>();
            selectCanvasGroup ??= selectPanel.AddComponent<CanvasGroup>();

            if (selectButtons == null || selectButtons.Length == 0)
            {
                selectButtons = selectPanel.GetComponentsInChildren<Button>(true);
                Array.Sort(selectButtons, CompareButtons);
            }
        }

        private void InitializeSelectHidden()
        {
            ResolveSelectReferences();
            SetSelectVisible(false);
            for (int i = 0; i < selectButtons.Length; i++)
            {
                if (selectButtons[i] != null)
                {
                    selectButtons[i].gameObject.SetActive(false);
                }
            }
        }

        private void SetSelectVisible(bool visible)
        {
            ResolveSelectReferences();
            if (selectPanel != null)
            {
                selectPanel.SetActive(visible);
            }

            if (selectCanvasGroup == null)
            {
                return;
            }

            selectCanvasGroup.alpha = visible ? 1f : 0f;
            selectCanvasGroup.interactable = visible;
            selectCanvasGroup.blocksRaycasts = visible;
        }

        private void ClearChoiceCallbacks()
        {
            for (int i = 0; i < selectButtons.Length; i++)
            {
                selectButtons[i]?.onClick.RemoveAllListeners();
            }

            choiceActions.Clear();
        }

        private static int CompareButtons(Button left, Button right)
        {
            if (left == right)
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            return string.CompareOrdinal(GetSiblingPath(left.transform), GetSiblingPath(right.transform));
        }

        private static string GetSiblingPath(Transform target)
        {
            var parts = new Stack<string>();
            Transform cursor = target;
            while (cursor != null)
            {
                parts.Push(cursor.GetSiblingIndex().ToString("D4"));
                cursor = cursor.parent;
            }

            return string.Join("/", parts);
        }

        private static void SetButtonText(Button button, string label)
        {
            TMP_Text text = button != null ? button.GetComponentInChildren<TMP_Text>(true) : null;
            if (text != null)
            {
                text.text = label ?? string.Empty;
            }
        }

        private void RefreshSelectLayout()
        {
            Canvas.ForceUpdateCanvases();
            if (selectPanel != null && selectPanel.transform is RectTransform rectTransform)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);
            }

            Canvas.ForceUpdateCanvases();
        }

        private static Transform FindChildByName(Transform rootTransform, string childName)
        {
            if (rootTransform == null)
            {
                return null;
            }

            foreach (var child in rootTransform.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == childName)
                {
                    return child;
                }
            }

            return null;
        }

        private static string BuildRevealedText(string text, int visibleCharacters)
        {
            text ??= string.Empty;
            if (visibleCharacters == int.MaxValue)
            {
                return text;
            }

            var builder = new StringBuilder(text.Length + 24);
            int revealedCharacters = 0;
            bool isInsideTag = false;
            bool startedHiddenText = false;

            for (int i = 0; i < text.Length; i++)
            {
                char character = text[i];
                if (character == '<')
                {
                    isInsideTag = true;
                }

                if (!isInsideTag && !startedHiddenText && revealedCharacters >= visibleCharacters)
                {
                    builder.Append("<alpha=#00>");
                    startedHiddenText = true;
                }

                builder.Append(character);

                if (character == '>' && isInsideTag)
                {
                    isInsideTag = false;
                    if (startedHiddenText)
                    {
                        builder.Append("<alpha=#00>");
                    }
                    continue;
                }

                if (!isInsideTag)
                {
                    revealedCharacters++;
                }
            }

            if (startedHiddenText)
            {
                builder.Append("<alpha=#FF>");
            }

            return builder.ToString();
        }

    }
}
