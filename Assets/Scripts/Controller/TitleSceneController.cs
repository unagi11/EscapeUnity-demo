using System;
using Escape.SceneFlow;
using Escape.Progress;
using System.Threading;
using Cysharp.Threading.Tasks;
using Escape.UI;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Escape.Controller
{
    /// <summary>타이틀 메뉴와 연이 표정 상호작용을 제어한다.</summary>
    public sealed class TitleSceneController : MonoBehaviour
    {
        private const string YeonTouchAchievementId = "title_yeon_touch_10";
        private const int RequiredYeonTouchCount = 10;

        [SerializeField] private string roomSceneName = EscapeSceneLoader.RoomSceneName;
        [SerializeField] private Button continueButton;
        [SerializeField] private Button loadButton;
        [SerializeField] private Button startButton;
        [SerializeField] private Button settingButton;
        [SerializeField] private Button creditButton;
        [SerializeField] private TMP_Text versionText;
        [SerializeField] private SaveDataPopupUI saveDataPopup;
        [SerializeField] private SettingPopupUI settingPopup;
        [SerializeField] private NamePopupUI namePopup;
        [SerializeField] private EndingCreditRollUI endingCreditRollUI;
        [SerializeField] private Camera titleCamera;
        [Header("입장 연출")]
        [SerializeField] private Image screenFadeOverlay;
        [SerializeField] private SpriteRenderer titleArtwork;
        [SerializeField] private CanvasGroup menuCanvasGroup;
        [SerializeField] private RectTransform menuPanel;
        [SerializeField, Min(0f)] private float introDelay = 0.1f;
        [SerializeField, Min(0.1f)] private float screenFadeDuration = 1.2f;
        [SerializeField, Min(0.1f)] private float titleIntroDuration = 1.05f;
        [SerializeField, Min(0.1f)] private float menuIntroDuration = 0.75f;
        [SerializeField, Min(0f)] private float menuIntroDelay = 0.65f;
        [SerializeField, Min(0f)] private float titleStartOffset = 0.2f;
        [SerializeField, Min(0f)] private float menuStartOffset = 12f;
        [SerializeField] private SpriteRenderer yeonRenderer;
        [SerializeField] private ParticleSystem yeonHeartParticles;
        [SerializeField, Range(1, 16)] private int heartBurstCount = 7;
        [SerializeField] private Sprite yeonSmileSprite;
        [SerializeField] private Sprite yeonShySprite;
        [SerializeField] private Sprite yeonDarknessSprite;
        [SerializeField, Min(0f)] private float returnToSmileDelay = 0.2f;
        [SerializeField, Min(0.1f)] private float yeonBounceDuration = 0.18f;
        [SerializeField, Range(0.01f, 0.3f)] private float yeonBounceStrength = 0.02f;

        private bool waitingForName;
        private bool expressionInteractionLocked;
        private bool startButtonPressed;
        private bool playingCredits;
        private int consecutiveYeonTouchCount;
        private CancellationTokenSource returnToSmileCts;
        private CancellationTokenSource yeonBounceCts;
        private CancellationTokenSource introCts;
        private Vector3 yeonRestLocalPosition;
        private Vector3 yeonRestLocalScale;
        private Vector3 titleRestLocalPosition;
        private Vector3 titleRestLocalScale;
        private Color titleRestColor;
        private Color screenFadeColor;
        private Vector2 menuRestAnchoredPosition;
        private string pendingPlayerName = string.Empty;

        private void Awake()
        {
            RefreshVersionText();
            RefreshSaveButtons();
            BindManagedButtons();
            SetYeonExpression(yeonSmileSprite);
            CacheYeonRestTransform();
            CacheIntroTransforms();
            PrepareIntroVisuals();
        }

        /// <summary>첫 프레임부터 타이틀 로고와 메뉴의 입장 연출을 재생한다.</summary>
        private void Start()
        {
            if (screenFadeOverlay == null && titleArtwork == null && menuCanvasGroup == null)
            {
                return;
            }

            introCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            PlayIntroAsync(introCts).Forget();
        }

        // 현재 플레이어 설정에 기록된 출시 버전을 타이틀 화면에 표시한다.
        private void RefreshVersionText()
        {
            if (versionText != null)
            {
                versionText.text = $"v{Application.version}";
            }
        }

        private void OnEnable()
        {
            RefreshSaveButtons();
        }

        private void OnDisable()
        {
            CancelIntro(completeVisuals: true);
            CancelReturnToSmile();
            CancelYeonBounce(resetTransform: true);
        }

        /// <summary>입장 연출에 사용할 원래 위치, 크기, 색상을 저장한다.</summary>
        private void CacheIntroTransforms()
        {
            if (screenFadeOverlay != null)
            {
                screenFadeColor = screenFadeOverlay.color;
            }

            if (titleArtwork != null)
            {
                Transform titleTransform = titleArtwork.transform;
                titleRestLocalPosition = titleTransform.localPosition;
                titleRestLocalScale = titleTransform.localScale;
                titleRestColor = titleArtwork.color;
            }

            if (menuPanel != null)
            {
                menuRestAnchoredPosition = menuPanel.anchoredPosition;
            }
        }

        /// <summary>렌더링 전에 로고와 메뉴를 시작 자세로 옮기고 입력을 잠근다.</summary>
        private void PrepareIntroVisuals()
        {
            expressionInteractionLocked = true;

            if (screenFadeOverlay != null)
            {
                screenFadeOverlay.enabled = true;
                screenFadeOverlay.color = new Color(
                    screenFadeColor.r,
                    screenFadeColor.g,
                    screenFadeColor.b,
                    1f);
            }

            if (titleArtwork != null)
            {
                Transform titleTransform = titleArtwork.transform;
                titleTransform.localPosition = titleRestLocalPosition + Vector3.up * titleStartOffset;
                titleTransform.localScale = titleRestLocalScale * 0.78f;
                titleArtwork.color = new Color(titleRestColor.r, titleRestColor.g, titleRestColor.b, 0f);
            }

            if (menuCanvasGroup != null)
            {
                menuCanvasGroup.alpha = 0f;
                menuCanvasGroup.interactable = false;
                menuCanvasGroup.blocksRaycasts = false;
            }

            if (menuPanel != null)
            {
                menuPanel.anchoredPosition = menuRestAnchoredPosition + Vector2.left * menuStartOffset;
            }
        }

        /// <summary>로고가 탄성 있게 안착한 뒤 메뉴가 부드럽게 따라 들어오도록 재생한다.</summary>
        private async UniTaskVoid PlayIntroAsync(CancellationTokenSource cts)
        {
            CancellationToken ct = cts.Token;
            float elapsed = 0f;
            float animationDuration = Mathf.Max(
                screenFadeDuration,
                Mathf.Max(titleIntroDuration, menuIntroDelay + menuIntroDuration));

            try
            {
                if (introDelay > 0f)
                {
                    await UniTask.Delay(
                        TimeSpan.FromSeconds(introDelay),
                        ignoreTimeScale: false,
                        cancellationToken: ct);
                }

                while (elapsed < animationDuration)
                {
                    elapsed += Time.deltaTime;
                    UpdateScreenFade(Mathf.Clamp01(elapsed / screenFadeDuration));
                    UpdateTitleIntro(Mathf.Clamp01(elapsed / titleIntroDuration));
                    UpdateMenuIntro(Mathf.Clamp01((elapsed - menuIntroDelay) / menuIntroDuration));
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // 오브젝트 비활성화나 씬 종료로 취소된 입장 연출은 정상 종료로 본다.
            }
            finally
            {
                if (ReferenceEquals(introCts, cts))
                {
                    CompleteIntroVisuals();
                    introCts.Dispose();
                    introCts = null;
                }
            }
        }

        /// <summary>암전 오버레이를 걷어내며 타이틀 화면 전체를 서서히 드러낸다.</summary>
        private void UpdateScreenFade(float progress)
        {
            if (screenFadeOverlay == null)
            {
                return;
            }

            screenFadeOverlay.color = new Color(
                screenFadeColor.r,
                screenFadeColor.g,
                screenFadeColor.b,
                1f - SmoothStep(progress));
        }

        /// <summary>탄성 감속 곡선으로 로고 위치와 크기, 투명도를 갱신한다.</summary>
        private void UpdateTitleIntro(float progress)
        {
            if (titleArtwork == null)
            {
                return;
            }

            float easedProgress = EaseOutBack(progress);
            Transform titleTransform = titleArtwork.transform;
            titleTransform.localPosition = Vector3.LerpUnclamped(
                titleRestLocalPosition + Vector3.up * titleStartOffset,
                titleRestLocalPosition,
                easedProgress);
            titleTransform.localScale = Vector3.LerpUnclamped(
                titleRestLocalScale * 0.78f,
                titleRestLocalScale,
                easedProgress);
            titleArtwork.color = new Color(
                titleRestColor.r,
                titleRestColor.g,
                titleRestColor.b,
                titleRestColor.a * SmoothStep(progress));
        }

        /// <summary>메뉴 패널을 감속 이동시키면서 입력 가능한 상태로 페이드인한다.</summary>
        private void UpdateMenuIntro(float progress)
        {
            float easedProgress = 1f - Mathf.Pow(1f - progress, 3f);
            if (menuCanvasGroup != null)
            {
                menuCanvasGroup.alpha = SmoothStep(progress);
            }

            if (menuPanel != null)
            {
                menuPanel.anchoredPosition = Vector2.LerpUnclamped(
                    menuRestAnchoredPosition + Vector2.left * menuStartOffset,
                    menuRestAnchoredPosition,
                    easedProgress);
            }
        }

        /// <summary>입장 연출의 최종 표시와 입력 상태를 정확히 복구한다.</summary>
        private void CompleteIntroVisuals()
        {
            if (screenFadeOverlay != null)
            {
                screenFadeOverlay.color = new Color(
                    screenFadeColor.r,
                    screenFadeColor.g,
                    screenFadeColor.b,
                    0f);
                screenFadeOverlay.enabled = false;
            }

            if (titleArtwork != null)
            {
                Transform titleTransform = titleArtwork.transform;
                titleTransform.localPosition = titleRestLocalPosition;
                titleTransform.localScale = titleRestLocalScale;
                titleArtwork.color = titleRestColor;
            }

            if (menuCanvasGroup != null)
            {
                menuCanvasGroup.alpha = 1f;
                menuCanvasGroup.interactable = true;
                menuCanvasGroup.blocksRaycasts = true;
            }

            if (menuPanel != null)
            {
                menuPanel.anchoredPosition = menuRestAnchoredPosition;
            }

            expressionInteractionLocked = false;
        }

        /// <summary>진행 중인 입장 연출을 취소하고 필요하면 최종 상태로 맞춘다.</summary>
        private void CancelIntro(bool completeVisuals)
        {
            if (introCts != null)
            {
                CancellationTokenSource cts = introCts;
                introCts = null;
                cts.Cancel();
                cts.Dispose();
            }

            if (completeVisuals)
            {
                CompleteIntroVisuals();
            }
        }

        /// <summary>시작과 끝 속도를 완만하게 만드는 보간값을 계산한다.</summary>
        private static float SmoothStep(float progress)
        {
            return progress * progress * (3f - 2f * progress);
        }

        /// <summary>끝 지점에서 한 번 가볍게 넘어갔다 돌아오는 감속 곡선을 계산한다.</summary>
        private static float EaseOutBack(float progress)
        {
            const float Overshoot = 1.35f;
            float shifted = progress - 1f;
            return 1f + (Overshoot + 1f) * shifted * shifted * shifted +
                   Overshoot * shifted * shifted;
        }

        /// <summary>연이를 누르는 동안 부끄러운 표정을 보이고 손을 떼면 미소로 돌아간다.</summary>
        private void Update()
        {
            if (expressionInteractionLocked || PopupUIBase.IsAnyOpen)
            {
                ResetYeonTouchStreak();
                return;
            }

            if (PopupUIBase.LastClosedFrame == Time.frameCount || Pointer.current == null)
            {
                return;
            }

            if (Pointer.current.press.wasReleasedThisFrame)
            {
                if (startButtonPressed)
                {
                    SetYeonExpression(yeonDarknessSprite);
                }
                else
                {
                    ScheduleReturnToSmile();
                }

                return;
            }

            if (!Pointer.current.press.wasPressedThisFrame)
            {
                return;
            }

            if (startButtonPressed)
            {
                ResetYeonTouchStreak();
                return;
            }

            if (!IsYeonScreenPosition(Pointer.current.position.ReadValue()))
            {
                ResetYeonTouchStreak();
                return;
            }

            CancelReturnToSmile();
            PlayYeonBounce();
            PlayYeonHeartBurst(Pointer.current.position.ReadValue());
            consecutiveYeonTouchCount++;
            if (consecutiveYeonTouchCount < RequiredYeonTouchCount)
            {
                return;
            }

            ResetYeonTouchStreak();
            SetYeonExpression(yeonShySprite);
            AchievementProgress.Unlock(YeonTouchAchievementId);
        }

        /// <summary>시작 버튼의 누름 상태에 맞춰 연이의 표정을 바꾼다.</summary>
        public void SetStartButtonPressed(bool pressed)
        {
            startButtonPressed = pressed;
            if (expressionInteractionLocked || PopupUIBase.IsAnyOpen)
            {
                return;
            }

            if (pressed)
            {
                CancelReturnToSmile();
                SetYeonExpression(yeonDarknessSprite);
            }
            else
            {
                ScheduleReturnToSmile();
            }
        }

        public void StartGame()
        {
            if (waitingForName)
            {
                return;
            }

            CancelReturnToSmile();
            CancelYeonBounce(resetTransform: true);
            expressionInteractionLocked = true;
            SetYeonExpression(yeonDarknessSprite);

            if (namePopup != null)
            {
                waitingForName = true;
                namePopup.Open(ContinueStartGameWithName, OnNamePopupCanceled);
                return;
            }

            ContinueStartGameWithName(GameSession.GetDefaultPlayerName());
        }

        private void ContinueStartGameWithName(string playerName)
        {
            waitingForName = false;
            pendingPlayerName = GameSession.NormalizePlayerName(playerName);
            RefreshSaveButtons();

            StartNewGame();
        }

        private void OnNamePopupCanceled()
        {
            waitingForName = false;
            expressionInteractionLocked = false;
            ScheduleReturnToSmile();
        }

        public void ContinueGame()
        {
            RefreshSaveButtons();

            if (saveDataPopup != null && saveDataPopup.LoadMostRecent(roomSceneName))
            {
                return;
            }

            StartNewGame();
        }

        public void OpenLoadPopup()
        {
            RefreshSaveButtons();
            saveDataPopup?.OpenLoadOnly(roomSceneName);
        }

        public void StartNewGame()
        {
            StartNewGame(true);
        }

        private void StartNewGame(bool playIntro)
        {
            SaveDataPopupUI.ClearPendingLoad();
            GameSession.Instance?.ResetState();
            // 신규 게임 첫 진입에서만 요청값에 따라 인트로와 시작 스플래시를 재생한다.
            EscapeSceneLoader.LoadRoom(roomSceneName, playIntro, playStartSplash: true, playerName: pendingPlayerName);
        }

        public void OpenSettingPopup()
        {
            settingPopup?.Open();
        }

        /// <summary>타이틀 메뉴 위에서 엔딩 크레딧을 재생한다.</summary>
        public void OpenCredits()
        {
            if (playingCredits || endingCreditRollUI == null)
            {
                return;
            }

            PlayCreditsAsync(destroyCancellationToken).Forget();
        }

        /// <summary>크레딧 재생 중 타이틀 표정 상호작용을 잠그고 종료 후 복구한다.</summary>
        private async UniTaskVoid PlayCreditsAsync(CancellationToken ct)
        {
            playingCredits = true;
            expressionInteractionLocked = true;
            CancelReturnToSmile();
            CancelYeonBounce(resetTransform: true);

            try
            {
                await endingCreditRollUI.PlayAsync(ct, fastForwardOnPointerHold: true);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // 씬 종료로 취소된 크레딧은 정상 종료로 본다.
            }
            finally
            {
                // 씬 종료로 네이티브 객체가 파괴된 뒤에는 Unity 프로퍼티에 접근하지 않는다.
                if (this != null)
                {
                    playingCredits = false;
                    expressionInteractionLocked = false;
                    if (isActiveAndEnabled)
                    {
                        ScheduleReturnToSmile();
                    }
                }
            }
        }

        private void BindManagedButtons()
        {
            BindIfNoPersistentClick(startButton, StartGame);
            BindIfNoPersistentClick(continueButton, ContinueGame);
            BindIfNoPersistentClick(loadButton, OpenLoadPopup);
            BindIfNoPersistentClick(settingButton, OpenSettingPopup);
            BindIfNoPersistentClick(creditButton, OpenCredits);
        }

        private static void BindIfNoPersistentClick(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button == null || button.onClick.GetPersistentEventCount() > 0)
            {
                return;
            }

            button.onClick.AddListener(action);
        }

        private void RefreshSaveButtons()
        {
            bool hasSaveData = SaveDataPopupUI.HasAnySaveData();
            SetButtonVisible(continueButton, hasSaveData);
            SetButtonVisible(loadButton, hasSaveData);
        }

        private static void SetButtonVisible(Button button, bool visible)
        {
            if (button != null)
            {
                button.gameObject.SetActive(visible);
            }
        }

        /// <summary>화면 좌표가 연이 스프라이트의 표시 영역 안인지 확인한다.</summary>
        private bool IsYeonScreenPosition(Vector2 screenPosition)
        {
            if (titleCamera == null || yeonRenderer == null)
            {
                return false;
            }

            Vector3 worldPosition = titleCamera.ScreenToWorldPoint(screenPosition);
            worldPosition.z = yeonRenderer.bounds.center.z;
            return yeonRenderer.bounds.Contains(worldPosition);
        }

        /// <summary>클릭한 위치에서 작은 하트들이 위로 퍼지도록 방출한다.</summary>
        private void PlayYeonHeartBurst(Vector2 screenPosition)
        {
            if (titleCamera == null || yeonHeartParticles == null)
            {
                return;
            }

            Vector3 worldPosition = titleCamera.ScreenToWorldPoint(screenPosition);
            worldPosition.z = yeonHeartParticles.transform.position.z;

            if (!yeonHeartParticles.isPlaying)
            {
                yeonHeartParticles.Play();
            }

            for (int i = 0; i < heartBurstCount; i++)
            {
                var emitParams = new ParticleSystem.EmitParams
                {
                    position = worldPosition,
                    velocity = new Vector3(
                        UnityEngine.Random.Range(-0.35f, 0.35f),
                        UnityEngine.Random.Range(0.45f, 0.85f),
                        0f),
                    startLifetime = UnityEngine.Random.Range(0.65f, 0.95f),
                    startSize = UnityEngine.Random.Range(0.12f, 0.18f),
                    startColor = Color.white
                };
                yeonHeartParticles.Emit(emitParams, 1);
            }
        }

        /// <summary>유효한 스프라이트가 연결된 경우 연이의 표정을 적용한다.</summary>
        private void SetYeonExpression(Sprite expression)
        {
            if (yeonRenderer != null && expression != null)
            {
                yeonRenderer.sprite = expression;
            }
        }

        /// <summary>연이 스프라이트의 기본 위치와 크기를 애니메이션 기준으로 저장한다.</summary>
        private void CacheYeonRestTransform()
        {
            if (yeonRenderer == null)
            {
                return;
            }

            Transform yeonTransform = yeonRenderer.transform;
            yeonRestLocalPosition = yeonTransform.localPosition;
            yeonRestLocalScale = yeonTransform.localScale;
        }

        /// <summary>연이를 누를 때 한 번 납작해졌다 돌아오는 피드백을 시작한다.</summary>
        private void PlayYeonBounce()
        {
            if (yeonRenderer == null || yeonBounceDuration <= 0f)
            {
                return;
            }

            CancelYeonBounce(resetTransform: true);
            yeonBounceCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            PlayYeonBounceAsync(yeonBounceCts).Forget();
        }

        /// <summary>한 번의 부드러운 압축 곡선으로 연이의 크기를 프레임마다 갱신한다.</summary>
        private async UniTaskVoid PlayYeonBounceAsync(CancellationTokenSource cts)
        {
            CancellationToken ct = cts.Token;
            float elapsed = 0f;

            try
            {
                while (elapsed < yeonBounceDuration)
                {
                    elapsed += Time.deltaTime;
                    float progress = Mathf.Clamp01(elapsed / yeonBounceDuration);
                    float squash = Mathf.Sin(progress * Mathf.PI) * yeonBounceStrength;

                    Transform yeonTransform = yeonRenderer.transform;
                    float animatedScaleY = yeonRestLocalScale.y * (1f - squash);
                    float spriteBottom = yeonRenderer.sprite != null ? yeonRenderer.sprite.bounds.min.y : 0f;
                    yeonTransform.localScale = new Vector3(
                        yeonRestLocalScale.x * (1f + squash * 0.55f),
                        animatedScaleY,
                        yeonRestLocalScale.z);
                    yeonTransform.localPosition = yeonRestLocalPosition +
                                                  Vector3.up * (spriteBottom * (yeonRestLocalScale.y - animatedScaleY));

                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // 새 터치나 씬 종료로 취소된 피드백은 정상 종료로 본다.
            }
            finally
            {
                if (ReferenceEquals(yeonBounceCts, cts))
                {
                    ResetYeonTransform();
                    yeonBounceCts.Dispose();
                    yeonBounceCts = null;
                }
            }
        }

        /// <summary>진행 중인 연이 피드백을 취소하고 필요하면 기본 자세로 복구한다.</summary>
        private void CancelYeonBounce(bool resetTransform)
        {
            if (yeonBounceCts != null)
            {
                CancellationTokenSource cts = yeonBounceCts;
                yeonBounceCts = null;
                cts.Cancel();
                cts.Dispose();
            }

            if (resetTransform)
            {
                ResetYeonTransform();
            }
        }

        /// <summary>연이 스프라이트를 저장된 기본 위치와 크기로 되돌린다.</summary>
        private void ResetYeonTransform()
        {
            if (yeonRenderer == null)
            {
                return;
            }

            Transform yeonTransform = yeonRenderer.transform;
            yeonTransform.localPosition = yeonRestLocalPosition;
            yeonTransform.localScale = yeonRestLocalScale;
        }

        /// <summary>설정된 지연 뒤 상호작용 상태를 확인하고 미소로 돌아간다.</summary>
        private async UniTaskVoid ReturnToSmileAfterDelayAsync(CancellationTokenSource cts)
        {
            CancellationToken ct = cts.Token;
            try
            {
                await UniTask.Delay(
                    TimeSpan.FromSeconds(returnToSmileDelay),
                    ignoreTimeScale: false,
                    cancellationToken: ct);

                if (!expressionInteractionLocked && !startButtonPressed)
                {
                    SetYeonExpression(yeonSmileSprite);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // 새 표정 요청이나 오브젝트 비활성화로 취소된 복귀 예약은 정상 종료로 본다.
            }
            finally
            {
                if (ReferenceEquals(returnToSmileCts, cts))
                {
                    returnToSmileCts.Dispose();
                    returnToSmileCts = null;
                }
            }
        }

        /// <summary>기존 예약을 갱신해 미소 복귀를 지연한다.</summary>
        private void ScheduleReturnToSmile()
        {
            CancelReturnToSmile();
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (returnToSmileDelay <= 0f)
            {
                SetYeonExpression(yeonSmileSprite);
                return;
            }

            returnToSmileCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            ReturnToSmileAfterDelayAsync(returnToSmileCts).Forget();
        }

        /// <summary>진행 중인 미소 복귀 예약을 취소한다.</summary>
        private void CancelReturnToSmile()
        {
            if (returnToSmileCts == null)
            {
                return;
            }

            CancellationTokenSource cts = returnToSmileCts;
            returnToSmileCts = null;
            cts.Cancel();
            cts.Dispose();
        }

        /// <summary>연이 연속 터치 횟수를 초기화한다.</summary>
        private void ResetYeonTouchStreak()
        {
            consecutiveYeonTouchCount = 0;
        }
    }
}
