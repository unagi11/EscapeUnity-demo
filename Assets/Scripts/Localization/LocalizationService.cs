using System;
using Escape.Progress;
using System.Reflection;
using Escape.Data;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Escape.Localization
{
    [MovedFrom(true, "Escape.Managers", null, "LocalizationManager")]
    public sealed class LocalizationService : MonoBehaviour
    {
        private const string UiTextResourcePath = "Data/text";
        private const string PlayerPrefsLanguageKey = "escape.language";
        private const string KoreanLanguage = "ko";
        private const string EnglishLanguage = "en";
        private const string JapaneseLanguage = "ja";

        public static LocalizationService Instance { get; private set; }

        [SerializeField] private string currentLanguage = "ko";
        [SerializeField] private string fallbackLanguage = "ko";

        private TsvTable<UiText> uiTextTable;

        public event Action LanguageChanged;

        public string CurrentLanguage
        {
            get => currentLanguage;
            set
            {
                string next = NormalizeLanguage(value, fallbackLanguage);
                if (string.Equals(currentLanguage, next, StringComparison.Ordinal))
                {
                    return;
                }

                currentLanguage = next;
                SaveLanguage(currentLanguage);
                LanguageChanged?.Invoke();
            }
        }

        public string FallbackLanguage
        {
            get => fallbackLanguage;
            set => fallbackLanguage = NormalizeLanguage(value, "ko");
        }

        public static LocalizationService Ensure()
        {
            if (Instance != null)
            {
                return Instance;
            }

            LocalizationService found = FindFirstObjectByType<LocalizationService>(FindObjectsInactive.Include);
            if (found != null)
            {
                Instance = found;
                found.InitializeLanguageState();
                return found;
            }

            var serviceObject = new GameObject(nameof(LocalizationService));
            return serviceObject.AddComponent<LocalizationService>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            InitializeLanguageState();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public static string Localized(object row, string fieldName, string fallback = "")
        {
            LocalizationService service = Ensure();
            string language = service.currentLanguage;
            string fallbackLanguage = service.fallbackLanguage;
            return GetLocalized(row, fieldName, language, fallbackLanguage, fallback);
        }

        // TID 텍스트를 현재 언어로 반환하고 필요하면 런타임 토큰을 치환한다.
        public static string Text(string tid, string fallback = "", bool applyRuntimeTokens = true)
        {
            return Ensure().GetText(tid, fallback, applyRuntimeTokens);
        }

        // 기본 이름처럼 토큰 치환에 사용되는 값은 치환 없이 조회할 수 있다.
        public string GetText(string tid, string fallback = "", bool applyRuntimeTokens = true)
        {
            if (string.IsNullOrWhiteSpace(tid))
            {
                return fallback ?? string.Empty;
            }

            uiTextTable ??= new TsvDataLoader<UiText>().LoadTable(UiTextResourcePath);
            if (uiTextTable != null && uiTextTable.TryGet(tid, out UiText row))
            {
                return GetLocalized(
                    row,
                    nameof(UiText.text),
                    currentLanguage,
                    fallbackLanguage,
                    fallback,
                    applyRuntimeTokens);
            }

            return fallback ?? tid;
        }

        private static string GetLocalized(
            object row,
            string fieldName,
            string language,
            string fallbackLanguage,
            string fallback,
            bool applyRuntimeTokens = true)
        {
            if (row == null || string.IsNullOrWhiteSpace(fieldName))
            {
                return fallback ?? string.Empty;
            }

            Type type = row.GetType();
            string text = GetFieldValue(type, row, $"{fieldName}_{NormalizeLanguage(language, "ko")}");
            if (!string.IsNullOrWhiteSpace(text))
            {
                return applyRuntimeTokens ? ApplyRuntimeTokens(text) : text;
            }

            text = GetFieldValue(type, row, $"{fieldName}_{NormalizeLanguage(fallbackLanguage, "ko")}");
            if (!string.IsNullOrWhiteSpace(text))
            {
                return applyRuntimeTokens ? ApplyRuntimeTokens(text) : text;
            }

            text = GetFieldValue(type, row, fieldName);
            text = !string.IsNullOrWhiteSpace(text) ? text : fallback ?? string.Empty;
            return applyRuntimeTokens ? ApplyRuntimeTokens(text) : text;
        }

        // 회차별 퍼즐 번호와 주인공 이름처럼 런타임 상태에서 결정되는 값을 텍스트에 반영한다.
        public static string ApplyRuntimeTokens(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text ?? string.Empty;
            }

            GameSession state = GameSession.Instance;
            string playerName = GameSession.GetPlayerNameOrDefault();
            if (state == null)
            {
                return text
                    .Replace("{주인공}", playerName, StringComparison.Ordinal)
                    .Replace("{PLAYER_NAME}", playerName, StringComparison.Ordinal)
                    .Replace("{HERO_NAME}", playerName, StringComparison.Ordinal);
            }

            return text
                .Replace("{CHAINLOCK_CODE}", state.ChainLockCode, StringComparison.Ordinal)
                .Replace("{체인락번호}", state.ChainLockCode, StringComparison.Ordinal)
                .Replace("{DOORLOCK_CODE}", state.DoorLockCode, StringComparison.Ordinal)
                .Replace("{도어락번호}", state.DoorLockCode, StringComparison.Ordinal)
                .Replace("{DOORLOCK_RESET_CODE}", state.DoorLockResetCode, StringComparison.Ordinal)
                .Replace("{YEON_BIRTHDAY_CODE}", state.YeonBirthdayCode, StringComparison.Ordinal)
                .Replace("{YEON_BIRTHDAY_MMDD}", state.YeonBirthdayMonthDay, StringComparison.Ordinal)
                .Replace("{주인공}", playerName, StringComparison.Ordinal)
                .Replace("{PLAYER_NAME}", playerName, StringComparison.Ordinal)
                .Replace("{HERO_NAME}", playerName, StringComparison.Ordinal);
        }

        private static string GetFieldValue(Type type, object row, string fieldName)
        {
            FieldInfo field = type.GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);

            return field != null && field.FieldType == typeof(string)
                ? field.GetValue(row) as string
                : string.Empty;
        }

        private static string NormalizeLanguage(string language, string fallback)
        {
            return string.IsNullOrWhiteSpace(language)
                ? fallback
                : language.Trim().ToLowerInvariant();
        }

        // 저장된 언어가 있으면 우선 적용하고, 첫 실행이면 운영체제 언어를 감지해 저장한다.
        private void InitializeLanguageState()
        {
            fallbackLanguage = NormalizeLanguage(fallbackLanguage, KoreanLanguage);
            string savedLanguage = PlayerPrefs.GetString(PlayerPrefsLanguageKey, string.Empty);
            if (TryGetSupportedLanguage(savedLanguage, out string supportedLanguage))
            {
                currentLanguage = supportedLanguage;
                return;
            }

            currentLanguage = DetectSystemLanguage(Application.systemLanguage);
            SaveLanguage(currentLanguage);
        }

        // 게임에서 지원하는 언어 코드만 저장값으로 인정한다.
        private static bool TryGetSupportedLanguage(string language, out string supportedLanguage)
        {
            supportedLanguage = NormalizeLanguage(language, string.Empty);
            return supportedLanguage is KoreanLanguage or EnglishLanguage or JapaneseLanguage;
        }

        // 지원하지 않는 운영체제 언어는 공용 기본 언어인 영어로 처리한다.
        private static string DetectSystemLanguage(SystemLanguage systemLanguage)
        {
            return systemLanguage switch
            {
                SystemLanguage.Korean => KoreanLanguage,
                SystemLanguage.Japanese => JapaneseLanguage,
                _ => EnglishLanguage,
            };
        }

        // 사용자가 바꾼 언어를 다음 실행에서도 유지한다.
        private static void SaveLanguage(string language)
        {
            PlayerPrefs.SetString(PlayerPrefsLanguageKey, language);
            PlayerPrefs.Save();
        }
    }
}
