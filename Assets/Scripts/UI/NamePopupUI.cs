using System;
using Escape.Localization;
using Escape.Progress;
using System.Collections.Generic;
using System.Text;
using Escape.Data;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Escape.UI
{
    // 새 게임에 사용할 주인공 표시명을 입력받는다.
    public sealed class NamePopupUI : PopupUIBase
    {
        [Header("Popup")]
        [SerializeField] private GameObject root;
        [SerializeField] private Button openButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private bool hideOnAwake = true;
        [SerializeField, Min(0f)] private float fadeDuration = 0.18f;
        [SerializeField] private CanvasGroup canvasGroup;

        [Header("Name")]
        [SerializeField] private TMP_InputField inputField;
        [SerializeField] private TMP_Text placeholderText;
        [SerializeField] private Button confirmButton;
        [SerializeField] private string defaultName = GameSession.DefaultPlayerName;
        [SerializeField] private string placeholderFallbackText = "이름을 입력하세요";
        [SerializeField, Min(1)] private int maxNameLength = 12;

        private IReadOnlyList<NameBan> nameBans;
        private Action<string> confirmed;
        private Action canceled;
        private string localizedDefaultName;
        private bool waitingForConfirmation;

        protected override GameObject PopupRoot { get => root; set => root = value; }
        protected override Button PopupOpenButton { get => openButton; set => openButton = value; }
        protected override Button PopupCloseButton { get => closeButton; set => closeButton = value; }
        protected override bool PopupHideOnAwake => hideOnAwake;
        protected override float PopupFadeDuration => fadeDuration;
        protected override CanvasGroup PopupCanvasGroup { get => canvasGroup; set => canvasGroup = value; }
        protected override bool CanCloseTopmost => false;

        private void Awake()
        {
            ResolveReferences();
            InitializePopupChrome();
            RefreshPlaceholderText();
            BindControls();
        }

        private void OnEnable()
        {
            LocalizationService.Ensure().LanguageChanged += RefreshLocalization;
            RefreshLocalization();
        }

        private void OnDisable()
        {
            if (LocalizationService.Instance != null)
            {
                LocalizationService.Instance.LanguageChanged -= RefreshLocalization;
            }
        }

        // 새 게임 이름을 직접 입력할 수 있도록 빈 입력란으로 팝업을 연다.
        public void Open(Action<string> onConfirmed, Action onCanceled = null)
        {
            ResolveReferences();
            confirmed = onConfirmed;
            canceled = onCanceled;
            waitingForConfirmation = false;

            SetInputText(string.Empty);
            RefreshLocalization();
            Open();
            transform.SetAsLastSibling();
            DeselectInputField();
        }

        // 입력값을 정리해 콜백으로 넘긴 뒤 팝업을 닫는다.
        public void Confirm()
        {
            if (waitingForConfirmation)
            {
                return;
            }

            string playerName = SanitizeName(inputField != null ? inputField.text : string.Empty);
            if (IsBannedName(playerName))
            {
                ShowInvalidName();
                return;
            }

            waitingForConfirmation = true;
            string confirmFormat = LocalizationService.Text("name_confirm", "\"{0}\"가 맞습니까?");
            YesNoUI.Show(
                string.Format(confirmFormat, playerName),
                () => ConfirmAccepted(playerName),
                ConfirmRejected);
        }

        private void ConfirmAccepted(string playerName)
        {
            waitingForConfirmation = false;
            Action<string> action = confirmed;
            confirmed = null;
            canceled = null;

            Close();
            action?.Invoke(playerName);
        }

        private void ConfirmRejected()
        {
            waitingForConfirmation = false;
        }

        protected override void OnAfterClose()
        {
            if (confirmed == null)
            {
                return;
            }

            Action action = canceled;
            confirmed = null;
            canceled = null;
            waitingForConfirmation = false;
            action?.Invoke();
        }

        private void BindControls()
        {
            confirmButton?.onClick.RemoveListener(Confirm);
            confirmButton?.onClick.AddListener(Confirm);

            if (inputField != null)
            {
                inputField.onSubmit.RemoveListener(ConfirmFromSubmit);
                inputField.onSubmit.AddListener(ConfirmFromSubmit);
                inputField.characterLimit = Mathf.Max(1, maxNameLength);
            }
        }

        private void ConfirmFromSubmit(string _)
        {
            Confirm();
        }

        private void ResolveReferences()
        {
            root ??= gameObject;
            inputField ??= GetComponentInChildren<TMP_InputField>(true);
            placeholderText ??= inputField != null ? inputField.placeholder as TMP_Text : null;
            confirmButton ??= GetComponentInChildren<Button>(true);
            closeButton ??= FindPopupChild("BackButton")?.GetComponent<Button>();
            closeButton ??= FindPopupChild("CloseButton")?.GetComponent<Button>();
        }

        protected override void ResolvePopupChromeReferences()
        {
            root ??= gameObject;
            canvasGroup ??= root.GetComponent<CanvasGroup>();
            closeButton ??= FindPopupChild("BackButton")?.GetComponent<Button>();
            closeButton ??= FindPopupChild("CloseButton")?.GetComponent<Button>();
        }

        private void SetInputText(string value)
        {
            if (inputField == null)
            {
                return;
            }

            inputField.SetTextWithoutNotify(SanitizeName(value, useDefaultName: false));
            inputField.characterLimit = Mathf.Max(1, maxNameLength);
        }

        private void RefreshPlaceholderText()
        {
            if (placeholderText != null)
            {
                placeholderText.text = LocalizationService.Text(
                    "name_placeholder",
                    placeholderFallbackText ?? string.Empty);
            }
        }

        // 언어 변경 시 플레이스홀더와 자동 입력된 기본 이름을 함께 갱신한다.
        private void RefreshLocalization()
        {
            string previousDefaultName = localizedDefaultName;
            localizedDefaultName = GetLocalizedDefaultName();
            RefreshPlaceholderText();

            if (inputField != null &&
                (IsLegacyDefaultName(inputField.text) ||
                    (!string.IsNullOrEmpty(previousDefaultName) && inputField.text == previousDefaultName)))
            {
                SetInputText(localizedDefaultName);
            }
        }

        // 인스펙터 기본값을 유지하되 기존 한국어 기본값은 현재 언어에 맞춰 반환한다.
        private string GetLocalizedDefaultName()
        {
            string configuredDefault = string.IsNullOrWhiteSpace(defaultName)
                ? GameSession.DefaultPlayerName
                : defaultName.Trim();
            return IsLegacyDefaultName(configuredDefault)
                ? GameSession.GetDefaultPlayerName()
                : configuredDefault;
        }

        private static bool IsLegacyDefaultName(string value)
        {
            return string.Equals((value ?? string.Empty).Trim(), GameSession.DefaultPlayerName, StringComparison.Ordinal);
        }

        private void DeselectInputField()
        {
            if (inputField == null)
            {
                return;
            }

            inputField.DeactivateInputField();
            if (EventSystem.current != null &&
                EventSystem.current.currentSelectedGameObject == inputField.gameObject)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
        }

        private string SanitizeName(string value, bool useDefaultName = true)
        {
            value = (value ?? string.Empty)
                .Replace('\n', ' ')
                .Replace('\r', ' ')
                .Replace('\t', ' ')
                .Trim();

            if (useDefaultName && string.IsNullOrWhiteSpace(value))
            {
                value = GetLocalizedDefaultName();
            }

            return value.Length > maxNameLength ? value.Substring(0, maxNameLength) : value;
        }

        // TSV 밴리스트에 걸리는 이름인지 확인한다.
        private bool IsBannedName(string playerName)
        {
            string normalizedName = NormalizeBanToken(playerName);
            if (string.IsNullOrWhiteSpace(normalizedName))
            {
                return false;
            }

            IReadOnlyList<NameBan> bans = LoadNameBans();
            for (int i = 0; i < bans.Count; i++)
            {
                NameBan ban = bans[i];
                if (ban == null || IsDisabled(ban.enabled))
                {
                    continue;
                }

                string bannedValue = NormalizeBanToken(ban.value);
                if (string.IsNullOrWhiteSpace(bannedValue))
                {
                    continue;
                }

                string matchType = (ban.match_type ?? string.Empty).Trim();
                if (string.Equals(matchType, "exact", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.Equals(normalizedName, bannedValue, StringComparison.Ordinal))
                    {
                        return true;
                    }

                    continue;
                }

                if (normalizedName.Contains(bannedValue, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private IReadOnlyList<NameBan> LoadNameBans()
        {
            nameBans ??= new TsvDataLoader<NameBan>().Load();
            return nameBans;
        }

        private void ShowInvalidName()
        {
            ToastPresenter.Show(LocalizationService.Text("name_invalid", "사용할 수 없는 이름입니다."));
            inputField?.ActivateInputField();
            inputField?.Select();
            if (inputField != null)
            {
                EventSystem.current?.SetSelectedGameObject(inputField.gameObject);
            }
        }

        private static bool IsDisabled(string enabled)
        {
            enabled = (enabled ?? string.Empty).Trim();
            return string.Equals(enabled, "false", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(enabled, "0", StringComparison.Ordinal);
        }

        private static string NormalizeBanToken(string value)
        {
            value = (value ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                if (!char.IsWhiteSpace(value[i]))
                {
                    builder.Append(value[i]);
                }
            }

            return builder.ToString();
        }
    }
}
