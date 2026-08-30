using System;
using Escape.Localization;
using Escape.Progress;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Escape.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Escape.UI
{
    // Shows acquired info as a selectable list and a fixed-size script panel.
    public sealed class InfomationPopupUI : PopupUIBase
    {
        private const string MaruMinyaFontResourcePath = "Fonts/x12y12pxMaruMinyaHangul SDF";
        private const string EmptyInfoFallbackText = "\uD68D\uB4DD\uD55C \uC815\uBCF4\uAC00 \uC5C6\uB2E4.";
        private const string DiaryInfoId = "diary_info";
        private static readonly Color32 SelectedRowColor = new(121, 206, 196, 255);
        private static readonly Color32 SelectedRowTextColor = new(8, 22, 23, 255);
        private static readonly Regex DiaryDateBoundaryPattern = new(
            @"\n+(?=\d{4}년\s+\d{1,2}월\s+\d{1,2}일(?:\s|$))",
            RegexOptions.CultureInvariant);

        [Header("Popup")]
        [SerializeField] private GameObject root;
        [SerializeField] private Button openButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private bool hideOnAwake = true;
        [SerializeField, Min(0f)] private float fadeDuration = 0.18f;
        [SerializeField] private CanvasGroup canvasGroup;

        [Header("Infos")]
        [SerializeField] private GameObject listScrollView;
        [SerializeField] private GameObject scriptPanel;
        [SerializeField] private RectTransform contentRoot;
        [SerializeField] private RectTransform elementTemplate;
        [SerializeField] private TMP_Text scriptText;
        [SerializeField] private Button prevButton;
        [SerializeField] private Button nextButton;
        [SerializeField] private GameObject emptyPanel;
        [SerializeField] private TMP_Text emptyText;
        [SerializeField] private TMP_FontAsset fontAsset;
        [SerializeField, Min(8f)] private float titleFontSize = 12f;

        private readonly List<InfoElementRow> rows = new();
        private readonly List<string> currentInfoIds = new();
        private InfoCollection infoCollection;
        private TsvTable<Info> infoTable;
        private string selectedInfoId;
        private int currentScriptPage;

        protected override GameObject PopupRoot { get => root; set => root = value; }
        protected override Button PopupOpenButton { get => openButton; set => openButton = value; }
        protected override Button PopupCloseButton { get => closeButton; set => closeButton = value; }
        protected override bool PopupHideOnAwake => hideOnAwake;
        protected override float PopupFadeDuration => fadeDuration;
        protected override CanvasGroup PopupCanvasGroup { get => canvasGroup; set => canvasGroup = value; }

        public bool IsOpen => IsPopupVisible;

        private void Awake()
        {
            infoTable = new TsvDataLoader<Info>().LoadTable();
            ResolveReferences();
            ConfigurePageButtons();
            SetTemplateVisible(false);
            InitializePopupChrome();
            if (IsPopupVisible)
            {
                Refresh();
            }
        }

        private void OnEnable()
        {
            ResolveInfoCollection();
            if (infoCollection != null)
            {
                infoCollection.Changed += Refresh;
                infoCollection.EnsureDefaults();
            }

            LocalizationService.Ensure().LanguageChanged += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            if (infoCollection != null)
            {
                infoCollection.Changed -= Refresh;
            }

            if (LocalizationService.Instance != null)
            {
                LocalizationService.Instance.LanguageChanged -= Refresh;
            }
        }

        // 다시 열 때 첫 정보의 첫 페이지부터 보여준다.
        protected override void OnBeforeOpen()
        {
            selectedInfoId = null;
            currentScriptPage = 0;
        }

        protected override void OnAfterOpen()
        {
            Refresh();
            ResetScrollToTop(listScrollView != null ? listScrollView.GetComponent<ScrollRect>() : null);
        }

        public void Toggle()
        {
            if (IsOpen)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        public void Refresh()
        {
            ResolveReferences();
            ConfigurePageButtons();
            SetTemplateVisible(false);
            ResolveInfoCollection();

            IReadOnlyList<string> infoIds = infoCollection != null
                ? infoCollection.GetOrderedInfos()
                : Array.Empty<string>();

            currentInfoIds.Clear();
            for (int i = 0; i < infoIds.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(infoIds[i]))
                {
                    currentInfoIds.Add(infoIds[i]);
                }
            }

            bool hasInfo = currentInfoIds.Count > 0;
            ApplyContentState(hasInfo);

            if (!hasInfo)
            {
                selectedInfoId = null;
                SetScriptText(string.Empty, true);
                SetVisibleRows(0);
                return;
            }

            EnsureRowCount(currentInfoIds.Count);
            SetVisibleRows(currentInfoIds.Count);

            if (string.IsNullOrWhiteSpace(selectedInfoId) || !currentInfoIds.Contains(selectedInfoId))
            {
                selectedInfoId = currentInfoIds[0];
                currentScriptPage = 0;
            }

            int rowCount = Mathf.Min(currentInfoIds.Count, rows.Count);
            for (int i = 0; i < rowCount; i++)
            {
                ConfigureRow(rows[i], currentInfoIds[i]);
            }

            ShowInfo(selectedInfoId, false);
        }

        private void ApplyContentState(bool hasInfo)
        {
            if (listScrollView != null)
            {
                listScrollView.SetActive(hasInfo);
            }

            if (scriptPanel != null)
            {
                scriptPanel.SetActive(hasInfo);
            }

            SetEmptyTextVisible(!hasInfo);
        }

        private void EnsureRowCount(int count)
        {
            if (contentRoot == null)
            {
                return;
            }

            RectTransform template = ResolveElementTemplate();
            if (template == null)
            {
                Debug.LogWarning("InfomationPopupUI could not find the List Scroll View Element template.", this);
                return;
            }

            while (rows.Count < count)
            {
                RectTransform rowTransform = Instantiate(template, contentRoot, false);
                rowTransform.name = $"InfoElement ({rows.Count + 1})";
                rows.Add(new InfoElementRow(rowTransform));
            }
        }

        private void ConfigureRow(InfoElementRow row, string infoId)
        {
            if (row == null || row.Root == null)
            {
                return;
            }

            row.Root.SetActive(true);
            row.InfoId = infoId;

            if (row.Label != null)
            {
                row.Label.text = BuildInfoTitle(infoId);
                row.Label.richText = true;
                row.Label.fontSize = titleFontSize;
                if (fontAsset != null && row.Label.font != fontAsset)
                {
                    row.Label.font = fontAsset;
                }
            }

            if (row.Button != null)
            {
                row.Button.onClick.RemoveAllListeners();
                row.Button.onClick.AddListener(() => SelectInfo(infoId));
            }

            ApplyRowSelection(row);
        }

        private void SelectInfo(string infoId)
        {
            if (string.IsNullOrWhiteSpace(infoId))
            {
                return;
            }

            ShowInfo(infoId, true);
        }

        private void ShowInfo(string infoId, bool resetPage)
        {
            selectedInfoId = infoId;
            if (resetPage)
            {
                currentScriptPage = 0;
            }

            UpdateRowSelectionStates();
            SetScriptText(BuildInfoScript(infoId), false);
        }

        private string BuildInfoTitle(string infoId)
        {
            if (infoTable == null || !infoTable.TryGet(infoId, out Info info))
            {
                return infoId ?? string.Empty;
            }

            return LocalizationService.Localized(info, nameof(Info.name), info.name);
        }

        private string BuildInfoScript(string infoId)
        {
            if (infoTable == null || !infoTable.TryGet(infoId, out Info info))
            {
                return infoId ?? string.Empty;
            }

            string description = LocalizationService.Localized(info, nameof(Info.desc), info.desc);
            string script = UnescapeLineBreaks(description);
            return string.Equals(infoId, DiaryInfoId, StringComparison.Ordinal)
                ? InsertDiaryPageBreaks(script)
                : script;
        }

        // 일기의 각 날짜 기록이 반드시 새 페이지에서 시작되도록 구분 태그를 넣는다.
        private static string InsertDiaryPageBreaks(string script)
        {
            return string.IsNullOrEmpty(script)
                ? string.Empty
                : DiaryDateBoundaryPattern.Replace(script, "\n<page>\n");
        }


        private static string UnescapeLineBreaks(string value)
        {
            return string.IsNullOrEmpty(value)
                ? string.Empty
                : value.Replace("\\n", "\n", StringComparison.Ordinal);
        }

        private void SetScriptText(string value, bool resetPage)
        {
            if (scriptText == null)
            {
                UpdatePageButtons(0);
                return;
            }

            if (resetPage)
            {
                currentScriptPage = 0;
            }

            scriptText.text = value ?? string.Empty;
            scriptText.richText = true;
            scriptText.textWrappingMode = TextWrappingModes.Normal;
            scriptText.overflowMode = TextOverflowModes.Page;

            Canvas.ForceUpdateCanvases();
            scriptText.ForceMeshUpdate();

            int pageCount = Mathf.Max(1, scriptText.textInfo.pageCount);
            currentScriptPage = Mathf.Clamp(currentScriptPage, 0, pageCount - 1);
            scriptText.pageToDisplay = currentScriptPage + 1;
            UpdatePageButtons(pageCount);
        }

        private void ShowPreviousPage()
        {
            if (currentScriptPage <= 0 || scriptText == null)
            {
                return;
            }

            currentScriptPage--;
            SetScriptText(scriptText.text, false);
        }

        private void ShowNextPage()
        {
            if (scriptText == null)
            {
                return;
            }

            scriptText.ForceMeshUpdate();
            int pageCount = Mathf.Max(1, scriptText.textInfo.pageCount);
            if (currentScriptPage >= pageCount - 1)
            {
                return;
            }

            currentScriptPage++;
            SetScriptText(scriptText.text, false);
        }

        private void UpdatePageButtons(int pageCount)
        {
            bool hasMultiplePages = pageCount > 1;
            if (prevButton != null)
            {
                prevButton.gameObject.SetActive(hasMultiplePages);
                prevButton.interactable = hasMultiplePages && currentScriptPage > 0;
            }

            if (nextButton != null)
            {
                nextButton.gameObject.SetActive(hasMultiplePages);
                nextButton.interactable = hasMultiplePages && currentScriptPage < pageCount - 1;
            }
        }

        private void SetVisibleRows(int count)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i]?.Root != null)
                {
                    rows[i].Root.SetActive(i < count);
                }
            }
        }

        // 현재 선택된 정보 행에만 포인트색을 적용한다.
        private void UpdateRowSelectionStates()
        {
            for (int i = 0; i < rows.Count; i++)
            {
                ApplyRowSelection(rows[i]);
            }
        }

        private void ApplyRowSelection(InfoElementRow row)
        {
            if (row == null)
            {
                return;
            }

            bool isSelected = string.Equals(row.InfoId, selectedInfoId, StringComparison.Ordinal);
            if (row.Background != null)
            {
                row.Background.color = isSelected ? SelectedRowColor : row.NormalBackgroundColor;
            }

            if (row.Label != null)
            {
                row.Label.color = isSelected ? SelectedRowTextColor : row.NormalTextColor;
            }
        }

        private void SetEmptyTextVisible(bool visible)
        {
            if (emptyPanel != null)
            {
                emptyPanel.SetActive(visible);
            }

            if (emptyText == null)
            {
                return;
            }

            emptyText.text = LocalizationService.Text("info_empty", EmptyInfoFallbackText);

            emptyText.gameObject.SetActive(visible);
        }

        private void ResolveReferences()
        {
            root ??= gameObject;
            canvasGroup ??= root.GetComponent<CanvasGroup>();
            closeButton ??= FindPopupChild("BackButton")?.GetComponent<Button>();
            closeButton ??= FindPopupChild("CloseButton")?.GetComponent<Button>();
            openButton ??= FindScenePopupChild("InfomationButton")?.GetComponent<Button>();
            openButton ??= FindScenePopupChild("InformationButton")?.GetComponent<Button>();

            listScrollView ??= FindPopupChild("List Scroll View")?.gameObject;
            scriptPanel ??= FindPopupChild("Script Panel")?.gameObject;
            contentRoot ??= FindPopupChild("Content") as RectTransform;
            elementTemplate ??= FindPopupChild("Element") as RectTransform;
            scriptText ??= FindPopupChild("Script Text (TMP)")?.GetComponent<TMP_Text>();
            scriptText ??= FindPopupChild("Script Text")?.GetComponent<TMP_Text>();
            prevButton ??= FindPopupChild("PrevButton")?.GetComponent<Button>();
            nextButton ??= FindPopupChild("NextButton")?.GetComponent<Button>();
            emptyPanel ??= FindPopupChild("Empty Panel")?.gameObject;
            emptyText ??= FindPopupChild("Empty Text")?.GetComponent<TMP_Text>();

            TryResolveFontAsset();
        }

        private RectTransform ResolveElementTemplate()
        {
            if (elementTemplate != null)
            {
                return elementTemplate;
            }

            if (contentRoot == null)
            {
                return null;
            }

            for (int i = 0; i < contentRoot.childCount; i++)
            {
                Transform child = contentRoot.GetChild(i);
                if (child != null && string.Equals(child.name, "Element", StringComparison.Ordinal))
                {
                    elementTemplate = child as RectTransform;
                    SetTemplateVisible(false);
                    return elementTemplate;
                }
            }

            return null;
        }

        private void SetTemplateVisible(bool visible)
        {
            if (elementTemplate != null)
            {
                elementTemplate.gameObject.SetActive(visible);
            }
        }

        private void ConfigurePageButtons()
        {
            if (prevButton != null)
            {
                prevButton.onClick.RemoveListener(ShowPreviousPage);
                prevButton.onClick.AddListener(ShowPreviousPage);
            }

            if (nextButton != null)
            {
                nextButton.onClick.RemoveListener(ShowNextPage);
                nextButton.onClick.AddListener(ShowNextPage);
            }
        }

        private bool TryResolveFontAsset()
        {
            if (fontAsset != null)
            {
                return true;
            }

            TMP_Text templateText = ResolveElementTemplate()?.GetComponentInChildren<TMP_Text>(true);
            if (templateText != null && templateText.font != null)
            {
                fontAsset = templateText.font;
                return true;
            }

            if (scriptText != null && scriptText.font != null)
            {
                fontAsset = scriptText.font;
                return true;
            }

            TMP_FontAsset maruMinya = Resources.Load<TMP_FontAsset>(MaruMinyaFontResourcePath);
            if (maruMinya != null)
            {
                fontAsset = maruMinya;
                return true;
            }

            fontAsset = TMP_Settings.defaultFontAsset;
            return fontAsset != null;
        }

        private void ResolveInfoCollection()
        {
            if (infoCollection != null)
            {
                return;
            }

            infoCollection = InfoCollection.Instance;
            if (infoCollection == null)
            {
                infoCollection = FindFirstObjectByType<InfoCollection>(FindObjectsInactive.Include);
            }

            if (infoCollection == null)
            {
                var collectionObject = new GameObject(nameof(InfoCollection));
                infoCollection = collectionObject.AddComponent<InfoCollection>();
            }
        }

        private sealed class InfoElementRow
        {
            public InfoElementRow(RectTransform rootTransform)
            {
                Root = rootTransform != null ? rootTransform.gameObject : null;
                Button = rootTransform != null ? rootTransform.GetComponent<Button>() : null;
                Label = rootTransform != null ? rootTransform.GetComponentInChildren<TMP_Text>(true) : null;
                Background = Button != null ? Button.targetGraphic : rootTransform?.GetComponent<Graphic>();
                NormalBackgroundColor = Background != null ? Background.color : Color.white;
                NormalTextColor = Label != null ? Label.color : Color.white;
            }

            public GameObject Root { get; }
            public Button Button { get; }
            public TMP_Text Label { get; }
            public Graphic Background { get; }
            public Color NormalBackgroundColor { get; }
            public Color NormalTextColor { get; }
            public string InfoId { get; set; }
        }
    }
}
