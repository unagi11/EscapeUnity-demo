using System;
using Escape.Audio;
using Escape.Runtime;
using Escape.SceneFlow;
using Escape.Localization;
using Escape.Progress;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Escape.UI
{
    public sealed class SettingPopupUI : PopupUIBase
    {
        private const string KoreanLanguage = "ko";
        private const string EnglishLanguage = "en";
        private const string JapaneseLanguage = "ja";
        private const string SelectedOptionPrefix = "\u25B6 ";
        private const string UnselectedOptionPrefix = "  ";

        [Header("Popup")]
        [SerializeField] private GameObject root;
        [SerializeField] private Button openButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private bool hideOnAwake = true;
        [SerializeField, Min(0f)] private float fadeDuration = 0.18f;
        [SerializeField] private CanvasGroup canvasGroup;

        [Header("Language")]
        [SerializeField] private TMP_Dropdown languageDropdown;

        [Header("Sound")]
        [SerializeField] private Slider masterSlider;
        [SerializeField] private Slider bgmSlider;
        [SerializeField] private Slider sfxSlider;

        [Header("Display")]
        // 창 모드는 데스크톱 전용이라 모바일에서는 이 섹션을 통째로 숨긴다.
        [SerializeField] private GameObject displaySection;
        [SerializeField] private TMP_Dropdown windowModeDropdown;

        [Header("Navigation")]
        // 인게임에서 타이틀로 돌아가는 버튼. 타이틀 씬에서는 자동으로 숨긴다.
        [SerializeField] private Button titleButton;
        [SerializeField] private Button resetAllButton;
        [SerializeField] private string titleSceneName = EscapeSceneLoader.TitleSceneName;

        private bool refreshing;
        private CancellationTokenSource deferredCaptionRefreshCts;

        protected override GameObject PopupRoot { get => root; set => root = value; }
        protected override Button PopupOpenButton { get => openButton; set => openButton = value; }
        protected override Button PopupCloseButton { get => closeButton; set => closeButton = value; }
        protected override bool PopupHideOnAwake => hideOnAwake;
        protected override float PopupFadeDuration => fadeDuration;
        protected override CanvasGroup PopupCanvasGroup { get => canvasGroup; set => canvasGroup = value; }

        private void Awake()
        {
            LocalizationService.Ensure();
            ResolveReferences();
            Refresh();
            InitializePopupChrome();
            BindControls();
        }

        private void OnEnable()
        {
            LocalizationService.Ensure().LanguageChanged += Refresh;

            Refresh();
        }

        private void OnDisable()
        {
            if (LocalizationService.Instance != null)
            {
                LocalizationService.Instance.LanguageChanged -= Refresh;
            }

            CancelDeferredCaptionRefresh();
        }

        public void SetKorean()
        {
            SetLanguage(KoreanLanguage);
        }

        public void SetEnglish()
        {
            SetLanguage(EnglishLanguage);
        }

        public void SetJapanese()
        {
            SetLanguage(JapaneseLanguage);
        }

        public void SetLanguage(string language)
        {
            LocalizationService.Ensure().CurrentLanguage = language;

            Refresh();
        }

        private void Refresh()
        {
            refreshing = true;

            RefreshLanguageDropdown();
            RefreshSoundSliders();
            RefreshDisplayControls();

            refreshing = false;
            RefreshDropdownCaptions();
            QueueDeferredDropdownCaptionRefresh();
        }

        private void RefreshLanguageDropdown()
        {
            if (languageDropdown == null)
            {
                return;
            }

            if (languageDropdown.options.Count != 3)
            {
                languageDropdown.ClearOptions();
                languageDropdown.AddOptions(new System.Collections.Generic.List<string>
                {
                    "\uD55C\uAD6D\uC5B4",
                    "English",
                    "\u65E5\u672C\u8A9E",
                });
            }
            else
            {
                languageDropdown.options[0].text = "\uD55C\uAD6D\uC5B4";
                languageDropdown.options[1].text = "English";
                languageDropdown.options[2].text = "\u65E5\u672C\u8A9E";
            }

            string language = LocalizationService.Ensure().CurrentLanguage;
            int selectedIndex = LanguageToDropdownIndex(language);

            ApplySelectedOptionMarker(languageDropdown.options, selectedIndex);
            languageDropdown.SetValueWithoutNotify(selectedIndex);
            languageDropdown.RefreshShownValue();
            HideDropdownCheckmark(languageDropdown);
        }

        private void RefreshSoundSliders()
        {
            SetSliderWithoutNotify(masterSlider, SoundPlayer.MasterVolume);
            SetSliderWithoutNotify(bgmSlider, SoundPlayer.BgmVolume);
            SetSliderWithoutNotify(sfxSlider, SoundPlayer.SfxVolume);
        }

        private void RefreshDisplayControls()
        {
            bool supportsDisplay = ScreenSettings.SupportsDisplayControls;
            if (displaySection != null)
            {
                displaySection.SetActive(supportsDisplay);
            }

            if (!supportsDisplay)
            {
                return;
            }

            RefreshWindowModeDropdown();
        }

        private void RefreshWindowModeDropdown()
        {
            if (windowModeDropdown == null)
            {
                return;
            }

            var options = new System.Collections.Generic.List<string>
            {
                LocalizationService.Text("window_mode_fullscreen", "Fullscreen"),
                LocalizationService.Text("window_mode_windowed", "Windowed"),
            };
            int selectedIndex = ScreenSettings.IsFullScreen ? 0 : 1;
            ApplySelectedOptionMarker(options, selectedIndex);

            windowModeDropdown.ClearOptions();
            windowModeDropdown.AddOptions(options);
            windowModeDropdown.SetValueWithoutNotify(selectedIndex);
            windowModeDropdown.RefreshShownValue();
            HideDropdownCheckmark(windowModeDropdown);
        }

        // TMP 드롭다운 캡션은 선택 마커 없이 현재 값만 보이게 유지한다.
        private void RefreshDropdownCaptions()
        {
            RefreshDropdownCaption(languageDropdown);
            RefreshDropdownCaption(windowModeDropdown);
        }

        // TMP_Dropdown이 같은 프레임 말미에 캡션을 다시 쓰는 경우를 한 번 더 정리한다.
        private void QueueDeferredDropdownCaptionRefresh()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            CancelDeferredCaptionRefresh();

            deferredCaptionRefreshCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            RefreshDropdownCaptionsNextFrameAsync(deferredCaptionRefreshCts).Forget();
        }

        // 다음 프레임에 캡션 선택 마커를 제거한다.
        private async UniTaskVoid RefreshDropdownCaptionsNextFrameAsync(CancellationTokenSource cts)
        {
            CancellationToken ct = cts.Token;
            try
            {
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                RefreshDropdownCaptions();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // 팝업 비활성화나 새 갱신 요청으로 취소된 예약 갱신은 정상 종료로 본다.
            }
            finally
            {
                if (ReferenceEquals(deferredCaptionRefreshCts, cts))
                {
                    deferredCaptionRefreshCts.Dispose();
                    deferredCaptionRefreshCts = null;
                }
            }
        }

        private void CancelDeferredCaptionRefresh()
        {
            if (deferredCaptionRefreshCts == null)
            {
                return;
            }

            deferredCaptionRefreshCts.Cancel();
            deferredCaptionRefreshCts.Dispose();
            deferredCaptionRefreshCts = null;
        }

        // 선택된 드롭다운 항목에 체크 이미지 대신 텍스트 마커를 붙인다.
        private static void ApplySelectedOptionMarker(System.Collections.Generic.IList<TMP_Dropdown.OptionData> options, int selectedIndex)
        {
            for (int i = 0; i < options.Count; i++)
            {
                options[i].text = BuildMarkedOptionText(options[i].text, i == selectedIndex);
            }
        }

        // 선택된 드롭다운 항목에 체크 이미지 대신 텍스트 마커를 붙인다.
        private static void ApplySelectedOptionMarker(System.Collections.Generic.List<string> options, int selectedIndex)
        {
            for (int i = 0; i < options.Count; i++)
            {
                options[i] = BuildMarkedOptionText(options[i], i == selectedIndex);
            }
        }

        // 이미 붙은 마커를 정리한 뒤 현재 선택 상태에 맞는 마커를 붙인다.
        private static string BuildMarkedOptionText(string text, bool selected)
        {
            text ??= string.Empty;
            string optionText = StripSelectedOptionMarker(text);
            string prefix = selected ? SelectedOptionPrefix : UnselectedOptionPrefix;
            return prefix + optionText;
        }

        // 드롭다운 캡션은 선택 표시 없이 현재 값만 보여준다.
        private static void RefreshDropdownCaption(TMP_Dropdown dropdown)
        {
            if (dropdown == null || dropdown.captionText == null)
            {
                return;
            }

            LocalizedTextUI[] localizedTexts = dropdown.captionText.GetComponents<LocalizedTextUI>();
            foreach (LocalizedTextUI localizedText in localizedTexts)
            {
                localizedText.enabled = false;
            }

            int value = Mathf.Clamp(dropdown.value, 0, dropdown.options.Count - 1);
            if (value < 0)
            {
                dropdown.captionText.text = string.Empty;
                return;
            }

            dropdown.captionText.text = StripSelectedOptionMarker(dropdown.options[value].text);
        }

        // 옵션 앞에 붙인 선택 마커를 제거한다.
        private static string StripSelectedOptionMarker(string text)
        {
            text ??= string.Empty;
            if (text.StartsWith(SelectedOptionPrefix, StringComparison.Ordinal) ||
                text.StartsWith(UnselectedOptionPrefix, StringComparison.Ordinal))
            {
                text = text[SelectedOptionPrefix.Length..];
            }

            return text;
        }

        // TMP 드롭다운 템플릿의 기본 체크 이미지를 숨겨 텍스트 마커만 보이게 한다.
        private static void HideDropdownCheckmark(TMP_Dropdown dropdown)
        {
            if (dropdown == null || dropdown.template == null)
            {
                return;
            }

            Toggle[] toggles = dropdown.template.GetComponentsInChildren<Toggle>(true);
            foreach (Toggle toggle in toggles)
            {
                Graphic graphic = toggle.graphic;
                if (graphic == null ||
                    graphic.gameObject.name.IndexOf("Checkmark", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                graphic.gameObject.SetActive(false);
                toggle.graphic = null;
            }
        }

        private static void SetSliderWithoutNotify(Slider slider, float value)
        {
            if (slider == null)
            {
                return;
            }

            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.SetValueWithoutNotify(Mathf.Clamp01(value));
        }

        private void BindControls()
        {
            languageDropdown?.onValueChanged.AddListener(OnLanguageDropdownChanged);
            masterSlider?.onValueChanged.AddListener(OnMasterVolumeChanged);
            bgmSlider?.onValueChanged.AddListener(OnBgmVolumeChanged);
            sfxSlider?.onValueChanged.AddListener(OnSfxVolumeChanged);
            windowModeDropdown?.onValueChanged.AddListener(OnWindowModeDropdownChanged);
            titleButton?.onClick.AddListener(OnTitleButtonClicked);
            resetAllButton?.onClick.AddListener(OnResetAllButtonClicked);

            // 타이틀 씬에서는 "타이틀로" 버튼이 의미 없으므로 숨긴다.
            if (titleButton != null)
            {
                titleButton.gameObject.SetActive(
                    !string.Equals(SceneManager.GetActiveScene().name, titleSceneName, StringComparison.Ordinal));
            }
        }

        private void OnLanguageDropdownChanged(int index)
        {
            if (refreshing)
            {
                return;
            }

            SetLanguage(DropdownIndexToLanguage(index));
        }

        private void OnMasterVolumeChanged(float value)
        {
            if (!refreshing)
            {
                SoundPlayer.SetMasterVolume(value);
            }
        }

        private void OnWindowModeDropdownChanged(int index)
        {
            if (refreshing)
            {
                return;
            }

            // 드롭다운 0 = 전체화면, 1 = 창모드.
            ScreenSettings.SetFullScreen(index == 0);
            RefreshDisplayControls();
            RefreshDropdownCaptions();
            QueueDeferredDropdownCaptionRefresh();
        }

        private void OnBgmVolumeChanged(float value)
        {
            if (!refreshing)
            {
                SoundPlayer.SetBgmVolume(value);
            }
        }

        private void OnSfxVolumeChanged(float value)
        {
            if (!refreshing)
            {
                SoundPlayer.SetSfxVolume(value);
            }
        }

        private void OnTitleButtonClicked()
        {
            string message = LocalizationService.Text("return_to_title_confirm", "타이틀로 돌아가시겠습니까?");
            YesNoUI.Show(message, GoToTitle);
        }

        // 전체 데이터 삭제 전 되돌릴 수 없음을 알리고 사용자 확인을 받는다.
        private void OnResetAllButtonClicked()
        {
            string message = LocalizationService.Text(
                "reset_all_confirm",
                "세이브 데이터와 설정을 모두 초기화하고 타이틀로 돌아가시겠습니까?");
            YesNoUI.Show(message, ResetAllData);
        }

        // 로컬 데이터와 플랫폼 세이브 슬롯을 지운 뒤 초기 상태의 타이틀로 돌아간다.
        private void ResetAllData()
        {
            PlayerPrefs.DeleteAll();
            SaveDataPopupUI.DeleteAllSaveData();
            SoundPlayer.ResetVolumeSettings();
            GameSession.Instance?.SetInputLocked(false);
            GameSession.Instance?.ResetState();
            Close();
            EscapeSceneLoader.LoadTitle(titleSceneName);
        }

        // 진행 상태를 초기화하고 타이틀 씬으로 돌아간다.
        private void GoToTitle()
        {
            Close();
            GameSession.Instance?.SetInputLocked(false);
            GameSession.Instance?.ResetState();
            SaveDataPopupUI.ClearPendingLoad();
            EscapeSceneLoader.LoadTitle(titleSceneName);
        }

        private void ResolveReferences()
        {
            root ??= gameObject;
            canvasGroup ??= root.GetComponent<CanvasGroup>();
            openButton ??= FindScenePopupChild("OptionButton")?.GetComponent<Button>();
            openButton ??= FindScenePopupChild("OpenButton")?.GetComponent<Button>();
            closeButton ??= FindPopupChild("CloseButton")?.GetComponent<Button>();
            languageDropdown ??= FindPopupChild("Dropdown")?.GetComponent<TMP_Dropdown>();
            masterSlider ??= FindPopupChild("MasterSlider")?.GetComponent<Slider>();
            bgmSlider ??= FindPopupChild("BGMSlider")?.GetComponent<Slider>();
            sfxSlider ??= FindPopupChild("SFXSlider")?.GetComponent<Slider>();
            displaySection ??= FindPopupChild("DisplaySection")?.gameObject;
            windowModeDropdown ??= FindPopupChild("WindowModeDropdown")?.GetComponent<TMP_Dropdown>();
            titleButton ??= FindPopupChild("TitleButton")?.GetComponent<Button>();
            resetAllButton ??= FindPopupChild("ResetAllButton")?.GetComponent<Button>();
        }

        private static int LanguageToDropdownIndex(string language)
        {
            return language switch
            {
                EnglishLanguage => 1,
                JapaneseLanguage => 2,
                _ => 0,
            };
        }

        private static string DropdownIndexToLanguage(int index)
        {
            return index switch
            {
                1 => EnglishLanguage,
                2 => JapaneseLanguage,
                _ => KoreanLanguage,
            };
        }
    }
}
