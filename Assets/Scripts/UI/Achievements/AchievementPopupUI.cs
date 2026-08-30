using System;
using Escape.Localization;
using Escape.Progress;
using System.Collections.Generic;
using System.Text;
using Escape.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Escape.UI
{
    // 모든 도전과제/엔딩 수집요소를 TSV 순서대로 보여주는 팝업이다.
    public sealed class AchievementPopupUI : PopupUIBase
    {
        private const string AchievementResourcePath = "Data/achievement";
        private const string AchievementIconResourcePath = "Sprites/icon_achv";
        private const string MaruMinyaFontResourcePath = "Fonts/x12y12pxMaruMinyaHangul SDF";
        private const int LockedIconIndex = 0;
        private const string IconAchvPrefix = "icon_achv_";
        private const string LegacyIconPrefix = "achievement_";
        private const string EmptyAchievementTid = "achievement_empty";
        private static readonly Color UnlockedRowColor = UIDarkTheme.SurfaceRaised;
        private static readonly Color UnlockedTitleColor = new(1f, 0.84705883f, 0.3529412f, 1f);
        private static readonly Color LockedRowColor = UIDarkTheme.WithAlpha(UIDarkTheme.Background, 0.82f);
        private static readonly Color LockedContentColor = UIDarkTheme.WithAlpha(UIDarkTheme.Accent, 0.24f);
        private static readonly Color LockedBodyColor = UIDarkTheme.WithAlpha(UIDarkTheme.TextPrimary, 0.34f);

        [Header("Popup")]
        [SerializeField] private GameObject root;
        [SerializeField] private Button openButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private bool hideOnAwake = true;
        [SerializeField, Min(0f)] private float fadeDuration = 0.18f;
        [SerializeField] private CanvasGroup canvasGroup;

        [Header("Achievements")]
        [SerializeField] private RectTransform contentRoot;
        [SerializeField] private GameObject rowTemplate;
        [SerializeField] private TMP_Text descText;
        [SerializeField] private TMP_Text emptyText;
        [SerializeField] private TMP_FontAsset fontAsset;

        private readonly List<RowView> rows = new();
        private readonly List<Achievement> sortedAchievements = new();
        private readonly Dictionary<int, Sprite> iconsByNo = new();
        private TsvTable<Achievement> achievementTable;

        protected override GameObject PopupRoot { get => root; set => root = value; }
        protected override Button PopupOpenButton { get => openButton; set => openButton = value; }
        protected override Button PopupCloseButton { get => closeButton; set => closeButton = value; }
        protected override bool PopupHideOnAwake => hideOnAwake;
        protected override float PopupFadeDuration => fadeDuration;
        protected override CanvasGroup PopupCanvasGroup { get => canvasGroup; set => canvasGroup = value; }

        public bool IsOpen => IsPopupVisible;

        private void Awake()
        {
            achievementTable = new TsvDataLoader<Achievement>().LoadTable(AchievementResourcePath);
            LoadAchievementIcons();
            ResolveReferences();
            InitializePopupChrome();

            if (IsPopupVisible)
            {
                Refresh();
            }
        }

        private void OnEnable()
        {
            AchievementProgress.Changed += Refresh;
            LocalizationService.Ensure().LanguageChanged += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            AchievementProgress.Changed -= Refresh;
            if (LocalizationService.Instance != null)
            {
                LocalizationService.Instance.LanguageChanged -= Refresh;
            }
        }

        protected override void OnAfterOpen()
        {
            Refresh();
            ResetScrollToTop(contentRoot != null ? contentRoot.GetComponentInParent<ScrollRect>() : null);
        }

        // 키보드나 버튼에서 도전과제창을 열고 닫는다.
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
            achievementTable ??= new TsvDataLoader<Achievement>().LoadTable(AchievementResourcePath);

            IReadOnlyList<Achievement> achievements = achievementTable?.Rows ?? Array.Empty<Achievement>();
            FillOrderedAchievements(achievements);
            UpdateProgressTexts(CountUnlocked(sortedAchievements), sortedAchievements.Count);
            EnsureRowCount(sortedAchievements.Count);
            int visibleCount = Mathf.Min(sortedAchievements.Count, rows.Count);
            if (visibleCount == 0)
            {
                SetVisibleRows(0);
                SetEmptyTextVisible(true);
                return;
            }

            for (int i = 0; i < visibleCount; i++)
            {
                ConfigureRow(rows[i], sortedAchievements[i]);
            }

            SetVisibleRows(visibleCount);
            SetEmptyTextVisible(false);
        }

        private void ConfigureRow(RowView rowView, Achievement achievement)
        {
            if (rowView == null || achievement == null)
            {
                return;
            }

            bool unlocked = AchievementProgress.IsUnlocked(achievement.id);
            bool concealed = !unlocked && IsHidden(achievement.hidden);
            rowView.Root.SetActive(true);
            if (rowView.Background != null)
            {
                rowView.Background.color = unlocked ? UnlockedRowColor : LockedRowColor;
            }

            if (rowView.Icon != null)
            {
                rowView.Icon.sprite = unlocked
                    ? GetIcon(achievement.icon_achv_idx)
                    : GetIcon(LockedIconIndex);
                rowView.Icon.enabled = rowView.Icon.sprite != null;
                rowView.Icon.color = unlocked ? UnlockedTitleColor : LockedContentColor;
            }

            if (rowView.NameText != null)
            {
                string localizedName = LocalizationService.Localized(
                    achievement,
                    nameof(Achievement.name),
                    achievement.id);
                rowView.NameText.text = concealed
                    ? Censor(localizedName, LockedContentColor)
                    : localizedName;
                rowView.NameText.color = unlocked ? UnlockedTitleColor : LockedContentColor;
                rowView.NameText.fontStyle = unlocked ? FontStyles.Bold : FontStyles.Normal;
            }

            if (rowView.BodyText != null)
            {
                string localizedDescription = LocalizationService.Localized(
                    achievement,
                    nameof(Achievement.desc),
                    string.Empty);
                rowView.BodyText.text = concealed
                    ? Censor(localizedDescription, LockedBodyColor)
                    : localizedDescription;
                rowView.BodyText.color = unlocked ? UIDarkTheme.TextPrimary : LockedBodyColor;
            }
        }

        // 원래 문구의 공백과 줄바꿈은 유지하고 나머지 글자를 검열 네모로 바꾼다.
        private static string Censor(string text, Color blockColor)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            const string gap = "<space=0.08em>";
            string block = $"<mark=#{ColorUtility.ToHtmlStringRGBA(blockColor)}><color=#00000000>?</color></mark>";
            var censored = new StringBuilder(text.Length * block.Length);
            bool previousWasBlock = false;
            for (int i = 0; i < text.Length; i++)
            {
                char character = text[i];
                if (char.IsWhiteSpace(character))
                {
                    censored.Append(character);
                    previousWasBlock = false;
                    continue;
                }

                if (previousWasBlock)
                {
                    censored.Append(gap);
                }

                censored.Append(block);
                previousWasBlock = true;
            }

            return censored.ToString();
        }

        // TSV의 hidden 플래그가 참이면 달성 전 제목과 설명을 감춘다.
        private static bool IsHidden(string hidden)
        {
            return string.Equals(hidden?.Trim(), "1", StringComparison.Ordinal) ||
                bool.TryParse(hidden, out bool parsed) && parsed;
        }

        // 현재 달성 수와 전체 달성률 텍스트를 갱신한다.
        private void UpdateProgressTexts(int unlockedCount, int totalCount)
        {
            int percent = totalCount <= 0
                ? 0
                : Mathf.RoundToInt(unlockedCount / (float)totalCount * 100f);

            if (descText != null)
            {
                descText.text = $"{unlockedCount}/{totalCount} ({percent}%)";
            }
        }

        // 목록에 표시되는 도전과제 중 달성한 개수를 센다.
        private static int CountUnlocked(IReadOnlyList<Achievement> achievements)
        {
            int count = 0;
            for (int i = 0; i < achievements.Count; i++)
            {
                if (achievements[i] != null && AchievementProgress.IsUnlocked(achievements[i].id))
                {
                    count++;
                }
            }

            return count;
        }

        // 달성 여부와 관계없이 진행 흐름에 맞춘 TSV 순서를 그대로 유지한다.
        private void FillOrderedAchievements(IReadOnlyList<Achievement> achievements)
        {
            sortedAchievements.Clear();
            for (int i = 0; i < achievements.Count; i++)
            {
                Achievement achievement = achievements[i];
                if (achievement != null)
                {
                    sortedAchievements.Add(achievement);
                }
            }
        }

        private Sprite GetIcon(string iconIndexText)
        {
            return int.TryParse(iconIndexText, out int iconIndex)
                ? GetIcon(iconIndex)
                : GetIcon(LockedIconIndex);
        }

        private Sprite GetIcon(int iconIndex)
        {
            if (iconsByNo.Count == 0)
            {
                LoadAchievementIcons();
            }

            if (iconsByNo.TryGetValue(iconIndex, out Sprite sprite) ||
                iconsByNo.TryGetValue(LockedIconIndex, out sprite))
            {
                return sprite;
            }

            return null;
        }

        private void LoadAchievementIcons()
        {
            iconsByNo.Clear();
            Sprite[] sprites = Resources.LoadAll<Sprite>(AchievementIconResourcePath);
            for (int i = 0; i < sprites.Length; i++)
            {
                Sprite sprite = sprites[i];
                if (sprite == null || !TryGetAchievementIconIdx(sprite.name, out int iconIdx))
                {
                    continue;
                }

                iconsByNo[iconIdx] = sprite;
            }
        }

        private static bool TryGetAchievementIconIdx(string spriteName, out int iconIdx)
        {
            if (TryGetIconIdx(spriteName, IconAchvPrefix, out iconIdx))
            {
                return true;
            }

            return TryGetIconIdx(spriteName, LegacyIconPrefix, out iconIdx);
        }

        private static bool TryGetIconIdx(string spriteName, string prefix, out int iconIdx)
        {
            iconIdx = 0;
            return !string.IsNullOrWhiteSpace(spriteName) &&
                spriteName.StartsWith(prefix, StringComparison.Ordinal) &&
                int.TryParse(spriteName[prefix.Length..], out iconIdx);
        }

        private void EnsureRowCount(int count)
        {
            if (contentRoot == null || rowTemplate == null)
            {
                return;
            }

            while (rows.Count < count)
            {
                RowView row = CreateRow(rows.Count);
                if (row == null)
                {
                    return;
                }

                rows.Add(row);
            }
        }

        // 씬에 배치된 Achievement Item을 복제해서 한 줄 뷰를 만든다.
        private RowView CreateRow(int index)
        {
            GameObject rowObject = Instantiate(rowTemplate, contentRoot);
            rowObject.name = $"AchievementRow ({index + 1})";
            rowObject.SetActive(true);
            return CreateRowView(rowObject);
        }

        private void SetVisibleRows(int count)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                rows[i]?.Root.SetActive(i < count);
            }
        }

        private void SetEmptyTextVisible(bool visible)
        {
            if (emptyText == null)
            {
                return;
            }

            emptyText.text = LocalizationService.Text(EmptyAchievementTid);
            emptyText.gameObject.SetActive(visible);
        }

        private void ResolveReferences()
        {
            root ??= gameObject;
            canvasGroup ??= root.GetComponent<CanvasGroup>();
            closeButton ??= FindPopupChild("BackButton")?.GetComponent<Button>();
            closeButton ??= FindPopupChild("CloseButton")?.GetComponent<Button>();
            openButton ??= FindScenePopupChild("AchievementButton")?.GetComponent<Button>();
            openButton ??= FindScenePopupChild("AchievementOpenButton")?.GetComponent<Button>();
            contentRoot ??= FindPopupChild("Content") as RectTransform;
            descText ??= FindPopupChild("Desc Text")?.GetComponent<TMP_Text>();
            descText ??= FindPopupChild("DescText")?.GetComponent<TMP_Text>();
            TryResolveFontAsset();
        }

        private bool TryResolveFontAsset()
        {
            if (fontAsset != null)
            {
                return true;
            }

            if (root != null)
            {
                TMP_Text[] texts = root.GetComponentsInChildren<TMP_Text>(true);
                for (int i = 0; i < texts.Length; i++)
                {
                    if (texts[i] != null && texts[i].font != null)
                    {
                        fontAsset = texts[i].font;
                        return true;
                    }
                }
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

        // 복제된 row에서 제목/설명/아이콘 슬롯을 찾는다.
        private static RowView CreateRowView(GameObject rowObject)
        {
            if (rowObject == null)
            {
                return null;
            }

            TMP_Text nameText = FindText(rowObject, "Title Text");
            TMP_Text bodyText = FindText(rowObject, "Desc Text");
            TMP_Text[] texts = rowObject.GetComponentsInChildren<TMP_Text>(true);
            nameText ??= texts.Length > 0 ? texts[0] : null;
            bodyText ??= texts.Length > 1 ? texts[1] : null;

            return new RowView(rowObject, rowObject.GetComponent<Image>(), FindIcon(rowObject), nameText, bodyText);
        }

        // 지정 이름의 TMP 텍스트를 하위에서 찾는다.
        private static TMP_Text FindText(GameObject rootObject, string objectName)
        {
            TMP_Text[] texts = rootObject.GetComponentsInChildren<TMP_Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] != null && texts[i].gameObject.name == objectName)
                {
                    return texts[i];
                }
            }

            return null;
        }

        // 템플릿의 실제 아이콘 이미지를 배경 이미지와 구분해서 찾는다.
        private static Image FindIcon(GameObject rowObject)
        {
            Image[] images = rowObject.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                if (images[i] != null && images[i].gameObject != rowObject && images[i].sprite != null)
                {
                    return images[i];
                }
            }

            for (int i = images.Length - 1; i >= 0; i--)
            {
                if (images[i] != null && images[i].gameObject != rowObject)
                {
                    return images[i];
                }
            }

            return null;
        }

        private sealed class RowView
        {
            public RowView(GameObject root, Image background, Image icon, TMP_Text nameText, TMP_Text bodyText)
            {
                Root = root;
                Background = background;
                Icon = icon;
                NameText = nameText;
                BodyText = bodyText;
            }

            public GameObject Root { get; }
            public Image Background { get; }
            public Image Icon { get; }
            public TMP_Text NameText { get; }
            public TMP_Text BodyText { get; }
        }
    }
}
