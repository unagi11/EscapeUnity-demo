using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Escape.UI
{
    // 앱에 포함된 제3자 소프트웨어와 폰트의 고지문을 스크롤 팝업으로 표시한다.
    public sealed class LicensePopupUI : PopupUIBase, IPointerClickHandler
    {
        [Header("Popup")]
        [SerializeField] private GameObject root;
        [SerializeField] private Button openButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private bool hideOnAwake = true;
        [SerializeField, Min(0f)] private float fadeDuration = 0.18f;
        [SerializeField] private CanvasGroup canvasGroup;

        [Header("Notice")]
        [SerializeField] private TextAsset noticeAsset;
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private RectTransform contentRoot;
        [SerializeField] private TMP_Text noticeText;
        [SerializeField, Min(0f)] private float horizontalPadding = 8f;
        [SerializeField, Min(0f)] private float verticalPadding = 8f;
        [SerializeField, Min(6f)] private float bodyFontSize = 8f;
        [SerializeField, Min(0f)] private float lineSpacing = 2f;

        protected override GameObject PopupRoot { get => root; set => root = value; }
        protected override Button PopupOpenButton { get => openButton; set => openButton = value; }
        protected override Button PopupCloseButton { get => closeButton; set => closeButton = value; }
        protected override bool PopupHideOnAwake => hideOnAwake;
        protected override float PopupFadeDuration => fadeDuration;
        protected override CanvasGroup PopupCanvasGroup { get => canvasGroup; set => canvasGroup = value; }

        private void Awake()
        {
            ValidateReferences();
            InitializePopupChrome();
            RefreshNotice();
        }

        protected override void OnAfterOpen()
        {
            RefreshNotice();
            ResetScrollPosition();
        }

        // 번들된 고지문을 표시하고 현재 뷰포트 폭에 맞춰 스크롤 높이를 계산한다.
        private void RefreshNotice()
        {
            if (noticeAsset == null || noticeText == null || contentRoot == null || scrollRect == null)
            {
                return;
            }

            noticeText.text = BuildDisplayText(noticeAsset.text);
            noticeText.richText = true;
            noticeText.fontSize = bodyFontSize;
            noticeText.lineSpacing = lineSpacing;
            noticeText.textWrappingMode = TextWrappingModes.Normal;
            noticeText.raycastTarget = true;

            Canvas.ForceUpdateCanvases();
            float viewportWidth = scrollRect.viewport != null
                ? scrollRect.viewport.rect.width
                : contentRoot.rect.width;
            float textWidth = Mathf.Max(1f, viewportWidth - horizontalPadding * 2f);
            Vector2 preferredSize = noticeText.GetPreferredValues(noticeText.text, textWidth, 100000f);
            float textHeight = Mathf.Ceil(preferredSize.y);
            float contentHeight = textHeight + verticalPadding * 2f;

            contentRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, viewportWidth);
            contentRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, contentHeight);

            RectTransform textRect = noticeText.rectTransform;
            textRect.anchorMin = new Vector2(0f, 1f);
            textRect.anchorMax = new Vector2(0f, 1f);
            textRect.pivot = new Vector2(0f, 1f);
            textRect.anchoredPosition = new Vector2(horizontalPadding, -verticalPadding);
            textRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, textWidth);
            textRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, textHeight);
            noticeText.ForceMeshUpdate();
        }

        // 배포용 원문은 유지하면서 강제 줄바꿈과 장식선을 화면용 문단 및 제목으로 정리한다.
        private static string BuildDisplayText(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return string.Empty;
            }

            string[] lines = source.Replace("\r\n", "\n").Split('\n');
            StringBuilder result = new();
            StringBuilder paragraph = new();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (i <= 1)
                {
                    continue;
                }

                if (IsSeparator(line))
                {
                    FlushParagraph(result, paragraph);
                    continue;
                }

                if (line.Length == 0)
                {
                    FlushParagraph(result, paragraph);
                    EnsureBlankLine(result);
                    continue;
                }

                if (IsSectionHeading(lines, i))
                {
                    FlushParagraph(result, paragraph);
                    AppendBlock(result, $"<size=10><b><color=#79CEC4>{line}</color></b></size>");
                    continue;
                }

                if (IsMinorHeading(line))
                {
                    FlushParagraph(result, paragraph);
                    AppendBlock(result, $"<size=9><b><color=#F1D98A>{line}</color></b></size>");
                    continue;
                }

                if (IsUrl(line))
                {
                    FlushParagraph(result, paragraph);
                    AppendBlock(result, $"<link=\"{line}\"><u><color=#72BFD0>{line}</color></u></link>");
                    continue;
                }

                if (line.StartsWith("- ", StringComparison.Ordinal))
                {
                    FlushParagraph(result, paragraph);
                    AppendBlock(result, $"• {line[2..]}");
                    continue;
                }

                if (paragraph.Length > 0)
                {
                    paragraph.Append(' ');
                }

                paragraph.Append(line);
            }

            FlushParagraph(result, paragraph);
            return result.ToString().TrimEnd();
        }

        private static bool IsSectionHeading(string[] lines, int index)
        {
            return index > 0 && index + 1 < lines.Length &&
                IsSeparator(lines[index - 1].Trim()) &&
                IsSeparator(lines[index + 1].Trim());
        }

        private static bool IsSeparator(string line)
        {
            if (line.Length < 8)
            {
                return false;
            }

            char marker = line[0];
            if (marker != '=' && marker != '-')
            {
                return false;
            }

            for (int i = 1; i < line.Length; i++)
            {
                if (line[i] != marker)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsMinorHeading(string line)
        {
            return line.Equals("The MIT License (MIT)", StringComparison.OrdinalIgnoreCase) ||
                line is "PREAMBLE" or "DEFINITIONS" or "PERMISSION & CONDITIONS" or
                    "TERMINATION" or "DISCLAIMER";
        }

        private static bool IsUrl(string line)
        {
            return line.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("http://", StringComparison.OrdinalIgnoreCase);
        }

        // 고지문 링크를 클릭하거나 터치하면 안전한 웹 주소만 기기 브라우저로 연다.
        public void OnPointerClick(PointerEventData eventData)
        {
            if (noticeText == null || eventData == null)
            {
                return;
            }

            int linkIndex = TMP_TextUtilities.FindIntersectingLink(
                noticeText,
                eventData.position,
                eventData.pressEventCamera);
            if (linkIndex < 0)
            {
                return;
            }

            string url = noticeText.textInfo.linkInfo[linkIndex].GetLinkID();
            if (IsUrl(url))
            {
                Application.OpenURL(url);
            }
        }

        private static void FlushParagraph(StringBuilder result, StringBuilder paragraph)
        {
            if (paragraph.Length == 0)
            {
                return;
            }

            AppendBlock(result, paragraph.ToString());
            paragraph.Clear();
        }

        private static void AppendBlock(StringBuilder result, string text)
        {
            EnsureBlankLine(result);
            result.Append(text);
        }

        private static void EnsureBlankLine(StringBuilder result)
        {
            if (result.Length == 0)
            {
                return;
            }

            int trailingNewLines = 0;
            for (int i = result.Length - 1; i >= 0 && result[i] == '\n'; i--)
            {
                trailingNewLines++;
            }

            while (trailingNewLines < 2)
            {
                result.Append('\n');
                trailingNewLines++;
            }
        }

        // 팝업을 열 때마다 고지문의 첫 줄부터 보이게 한다.
        private void ResetScrollPosition()
        {
            if (scrollRect == null)
            {
                return;
            }

            scrollRect.StopMovement();
            scrollRect.horizontalNormalizedPosition = 0f;
            scrollRect.verticalNormalizedPosition = 1f;
        }

        private void ValidateReferences()
        {
            root ??= gameObject;
            if (root == null || canvasGroup == null || closeButton == null ||
                noticeAsset == null || scrollRect == null || contentRoot == null || noticeText == null)
            {
                Debug.LogError("LicensePopupUI scene references are incomplete.", this);
            }
        }
    }
}
