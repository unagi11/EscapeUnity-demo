using UnityEngine;
using Escape.SceneFlow;
using Escape.Localization;
using Escape.Progress;
using UnityEngine.UI;

namespace Escape.UI
{
    // 인게임 일시정지 메뉴의 열기와 닫기를 공용 팝업 흐름에 연결한다.
    public sealed class PausePopupUI : PopupUIBase
    {
        [Header("Popup")]
        [SerializeField] private GameObject root;
        [SerializeField] private Button openButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private Button backPanelButton;
        [SerializeField] private bool hideOnAwake = true;
        [SerializeField, Min(0f)] private float fadeDuration = 0.12f;
        [SerializeField] private CanvasGroup canvasGroup;

        [Header("Navigation")]
        // 진행 상태를 정리하고 타이틀 씬으로 나가는 버튼.
        [SerializeField] private Button titleButton;
        [SerializeField] private string titleSceneName = EscapeSceneLoader.TitleSceneName;

        private bool isTimeScalePaused;
        private float previousTimeScale = 1f;

        protected override GameObject PopupRoot { get => root; set => root = value; }
        protected override Button PopupOpenButton { get => openButton; set => openButton = value; }
        protected override Button PopupCloseButton { get => closeButton; set => closeButton = value; }
        protected override bool PopupHideOnAwake => hideOnAwake;
        protected override float PopupFadeDuration => fadeDuration;
        protected override CanvasGroup PopupCanvasGroup { get => canvasGroup; set => canvasGroup = value; }

        // 직렬화된 버튼과 팝업 루트를 초기 상태로 연결한다.
        private void Awake()
        {
            ResolveReferences();
            InitializePopupChrome();
            BindControls();
        }

        protected override void OnAfterOpen()
        {
            PauseGameTime();
        }

        protected override void OnAfterClose()
        {
            ResumeGameTime();
        }

        private void OnDisable()
        {
            ResumeGameTime();
        }

        // 인스펙터 연결이 비어 있으면 팝업 내부 자식에서 필요한 참조를 채운다.
        private void ResolveReferences()
        {
            backPanelButton ??= FindPopupChild("BackPanel")?.GetComponent<Button>();
            titleButton ??= FindPopupChild("TitleButton")?.GetComponent<Button>();
        }

        // 일시정지 메뉴가 열린 동안 게임 시간 기반 동작을 멈춘다.
        private void PauseGameTime()
        {
            if (isTimeScalePaused)
            {
                return;
            }

            previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            isTimeScalePaused = true;
        }

        // 팝업을 닫을 때 열기 전 게임 시간 배율로 되돌린다.
        private void ResumeGameTime()
        {
            if (!isTimeScalePaused)
            {
                return;
            }

            Time.timeScale = previousTimeScale;
            isTimeScalePaused = false;
        }

        // 타이틀 버튼 클릭을 핸들러에 연결한다.
        private void BindControls()
        {
            backPanelButton?.onClick.AddListener(Close);
            titleButton?.onClick.AddListener(OnTitleButtonClicked);
        }

        // 타이틀 복귀 전에 확인 창을 띄운다.
        private void OnTitleButtonClicked()
        {
            string message = LocalizationService.Text("return_to_title_confirm", "타이틀로 돌아가시겠습니까?");
            YesNoUI.Show(message, GoToTitle);
        }

        // 진행 상태를 초기화하고 타이틀 씬으로 돌아간다(SettingPopupUI와 동일한 절차).
        private void GoToTitle()
        {
            Close();
            GameSession.Instance?.SetInputLocked(false);
            GameSession.Instance?.ResetState();
            SaveDataPopupUI.ClearPendingLoad();
            EscapeSceneLoader.LoadTitle(titleSceneName);
        }
    }
}
