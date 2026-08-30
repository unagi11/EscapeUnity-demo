using Escape.Audio;
using Escape.SceneFlow;
using Escape.Dialogues;
using Escape.Progress;
using Escape.Data;
using Escape.UI;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Escape.Rooms
{
    [MovedFrom(true, "Escape.Rooms", null, "RoomManager")]
    public sealed class RoomController :
        MonoBehaviour,
        IDialogueRoomEffects
    {
        private const string LockDrawerFlagObjectName = "FLAG:LOCK_DRAWER";
        private const string EntranceVisitedFlagObjectName = "FLAG:ENTER_FIRST_ENTRANCE";
        private const string OpenUtilityDoorFlagObjectName = "FLAG:OPEN_UTILITY_DOOR";
        private const string UtilityDoorPopupObjectName = "popup_door_utility";
        private const string LockPickDrawerUnlockedDialogueId = "lockpick_drawer_unlocked";
        private const string LockPickUtilityDoorUnlockedDialogueId = "lockpick_utility_door_unlocked";
        private const string LockPickEntrancePadlockUnlockedDialogueId = "lockpick_entrance_padlock_unlocked";
        private const string LockPickHandcuffsUnlockedDialogueId = "lockpick_handcuffs_unlocked";
        private const string HandcuffsObjectName = "obj_handcuffs";
        private const string RestrainedHumanObjectName = "obj_human";
        private const string UnlockedHumanObjectName = "obj_human_unlock";
        private const string LockPickItemId = "lockpick_set";

        [SerializeField] private Camera targetCamera;
        [SerializeField] private DialoguePopupUI dialoguePanel;
        [SerializeField] private ItemPanelUI itemPanel;
        [SerializeField] private LocationPanelUI locationPanel;
        [SerializeField] private YesNoUI yesNoUI;
        [SerializeField] private TimingCheckPopupUI timingCheckPopup;
        [SerializeField] private DoorLockKeypadController doorLockKeypad;
        [Header("Adventure Panel")]
        [SerializeField] private CanvasGroup adventurePanelCanvasGroup;
        [SerializeField] private StartSplashUI startSplashUI;
        [SerializeField] private Room[] rooms = Array.Empty<Room>();
        [Header("Default Fallback Interaction")]
        [SerializeField] private InteractionRule[] fallbackInteractions = Array.Empty<InteractionRule>();
        [SerializeField] private RoomType initialRoom = RoomType.LivingRoom;
        [SerializeField] private Vector2 roomBasePosition = new(-1.28f, 0.96f);
        [Tooltip("Room transition capture and resolution fade source. Assign the RoomCanvas/RoomImage RawImage.")]
        [SerializeField] private RawImage roomImage;
        [FormerlySerializedAs("roomFadeDuration")]
        [FormerlySerializedAs("presentationDuration")]
        [SerializeField, Min(0f)] private float screenEffectDuration = 0.35f;
        [Tooltip("빨강ON/OFF 연출의 페이드 지속시간(초).")]
        [SerializeField, Min(0f)] private float redFadeDuration = 0.18f;
        [Tooltip("빨강ON이 화면에 덮는 색상과 최대 불투명도.")]
        [SerializeField] private Color redFadeColor = new(0.82f, 0.02f, 0.02f, 0.62f);
        [Tooltip("초록ON/OFF 연출의 페이드 지속시간(초).")]
        [SerializeField, Min(0f)] private float greenFadeDuration = 0.18f;
        [Tooltip("초록ON이 화면에 덮는 색상과 최대 불투명도.")]
        [SerializeField] private Color greenFadeColor = new(0.25f, 0.5f, 0.12f, 0.42f);
        [Tooltip("느린검정ON/OFF 연출의 페이드 지속시간(초). 엔딩 연출은 최소 5초를 보장한다.")]
        [SerializeField, Min(0f)] private float slowBlackFadeDuration = 5f;
        [SerializeField, Min(0f)] private float resolutionFadeDuration = 1f;
        [SerializeField, Range(0, 255)] private int alphaHitThreshold;
        [SerializeField] private bool debugLogs = true;

        private GameSession state;
        private DialoguePlayer dialoguePlayer;
        private PlayerInventory inventory;
        private InfoCollection infoCollection;
        private RoomSpecialActionDispatcher specialActionDispatcher;
        private RoomScreenEffectController screenEffectController;
        private AdventurePanelVisibilityController adventurePanelVisibilityController;
        private RoomDialogueLineFactory dialogueLineFactory;
        private RoomDialogueStoryPlayer dialogueStoryPlayer;
        private RoomObjectStatePersistence objectStatePersistence;
        private RoomHitTester hitTester;
        private RoomRegistry roomRegistry;
        private TsvTable<Dialogue> dialogueTable;
        private TsvTable<Speaker> speakerTable;
        private TsvTable<Item> itemTable;
        private TsvTable<Info> infoTable;
        private bool isPlayingSpaceShooterClearReward;
        private bool isApplyingLockPickUnlockResult;
        private int interactionSequenceCount;
        private bool isPlayingRythmRecycleCleanupResult;
        private Transform currentRoom;
        private bool hasLastInspectWorldPoint;
        private Vector2 lastInspectWorldPoint;
        private readonly Dictionary<Transform, Vector3> initialFlashlightLocalPositions = new();

        private const string LogPrefix = "[RoomController]";
        private const string FlashLightObjectName = "Flash Light 2D";
        private const string FlashLightTagName = "flash light";
        private const float FlashlightTurnOnDelay = 1f;
        private const float FlashlightFirstFlashDuration = 0.07f;
        private const float FlashlightRetryDelay = 0.18f;
        private const float FlashlightSecondFlashDuration = 0.045f;
        private const float FlashlightFinalClickDelay = 0.055f;
        private const string TrashMessyObjectName = "draw_messy";
        private const string TrashTidyObjectName = "draw_tidy_up";
        private const string DoorlockResetInfoId = "doorlock_reset_info";
        private const string RythmRecyclePerfectResultDialogueId = "rythm_recycle_result_perfect";
        private const string RythmRecycleSuccessResultDialogueId = "rythm_recycle_result_success";
        private const string RythmRecycleFailedResultDialogueId = "rythm_recycle_result_failed";
        private const string PoweredGameConsoleItemId = "powered_game_console";
        private const string GameConsolePromptDialogueId = "game_console_prompt";
        private const string GameConsolePlayFlag = "GAME_CONSOLE_PLAY";
        private const string RythmRecycleNoneFlagObjectName = "FLAG:RYTHM_RECYCLE_NOT_DONE";
        private const string RythmRecycleDoneFlagObjectName = "FLAG:RYTHM_RECYCLE_DONE";
        private const string RythmRecyclePerfectFlagObjectName = "FLAG:RYTHM_RECYCLE_PERFECT";
        private const string EntrancePadlockObjectName = "obj_lock";
        private const string EntrancePadlockUnlockedObjectName = "obj_lock_unlock";
        private const string WindowDialogueId = "interact_obj_window_prev";
        private const string WindowRepeatDialogueId = "interact_obj_window_repeat_prev";
        private const string WindowDoneDialogueId = "interact_obj_window_done";
        private const string WindowShoutFlag = "WINDOW_SHOUT";
        private const string WindowRepeatShoutFlag = "WINDOW_SHOUT_REPEAT";
        private const string CurryDialogueId = "interact_obj_pot_prev";
        private const string CurryDoneDialogueId = "interact_obj_pot_done";
        private const string CurryEatFlag = "CURRY_EAT";
        private const string IntroDialogueId = "intro";
        private const string DemoBedroomExitGameOverDialogueId = "health_game_over_yeon";
        private const string IntroWatchedAchievementId = "intro_watched";
        private const string GameStartedAchievementId = "game_started";
        private const string LockPickAllTypesAchievementId = "lockpick_tool_crafted";
        private const string SpaceShooterClearRewardDialogueId = "space_shooter_clear_reward";
        public RoomType CurrentRoomId => currentRoom != null && currentRoom.TryGetComponent(out Room room)
            ? room.RoomId
            : RoomType.None;

        private Room CurrentRoomComponent => currentRoom != null && currentRoom.TryGetComponent(out Room room)
            ? room
            : null;

        // 카메라 앞에 배치된 현재 방의 루트 트랜스폼.
        public Transform CurrentRoom => currentRoom;
        // 상호작용의 Pre/Action/Post 전체 단계가 실행 중인지 알린다.
        public bool IsExecutingInteractionSequence => interactionSequenceCount > 0;

        private void Awake()
        {
            inventory = ResolvePlayerInventory();
            infoCollection = ResolveInfoCollection();
            state = inventory.State;
            ApplyPendingPlayerName();
            inventory.EnsureDefaults();
            infoCollection.EnsureDefaults();
            ResolveItemPanel();

            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }

            dialoguePlayer = ResolveDialoguePlayer();

            if (dialoguePlayer == null)
            {
                Debug.LogWarning("RoomController requires a DialoguePlayer in the scene.");
            }

            if (dialoguePlayer != null && dialoguePanel != null)
            {
                dialoguePlayer.Configure(dialoguePanel, this, ScreenEffects);
            }

            dialogueTable = new TsvDataLoader<Dialogue>().LoadTable();
            speakerTable = new TsvDataLoader<Speaker>().LoadTable();
            itemTable = new TsvDataLoader<Item>().LoadTable();
            infoTable = new TsvDataLoader<Info>().LoadTable();
            InitializeSpecialActionDispatcher();
            ResolveRoomReferences();
            EnsureSpaceShooterClearRewardFlagObject();
            EnsureRythmRecycleStateFlagObjects();
            ObjectStates.CaptureInitialVisibility();
            CaptureInitialFlashlightPositions();
            ObjectStates.InitializeVisibility();
            InitializeStartingRoom();
            PushAdventurePanelHidden();
            SetAdventurePanelActive(false);
        }

        // 씬 전환 인자로 넘어온 새 게임 이름을 런타임 상태에 반영한다.
        private void ApplyPendingPlayerName()
        {
            if (state == null || !SceneLoadArgs.ConsumePlayerName(out string playerName))
            {
                return;
            }

            state.SetPlayerName(playerName);
        }

        private void OnEnable()
        {
            RefreshHitCandidates();
            inventory?.EnsureDefaults();
            infoCollection?.EnsureDefaults();
            itemPanel?.Refresh();
            PlayPendingSpaceShooterClearReward(destroyCancellationToken).Forget();
            PlayPendingRythmRecycleCleanupResult(destroyCancellationToken).Forget();
            ApplyPendingLockPickUnlockResult(destroyCancellationToken).Forget();
        }

        // 미니게임 클리어 보상은 TOPUI 전환이 끝난 뒤 룸 대사로 재생한다.
        private async UniTaskVoid PlayPendingSpaceShooterClearReward(CancellationToken ct)
        {
            if (isPlayingSpaceShooterClearReward ||
                !SceneLoadArgs.ConsumeSpaceShooterClearReward())
            {
                return;
            }

            isPlayingSpaceShooterClearReward = true;
            try
            {
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                await SceneTransitionFadeUI.WaitForSpiralIdleAsync(ct);

                GameObject rewardFlag = EnsureSpaceShooterClearRewardFlagObject();
                if (rewardFlag != null && rewardFlag.activeSelf)
                {
                    return;
                }

                state = ResolvePlayerInventory().State;
                if (state != null && state.CurrentHealth < state.MaxHealth)
                {
                    await PlayDialogueStory(SpaceShooterClearRewardDialogueId, null, ct);
                }

                SetObjectActive(rewardFlag, true);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
            finally
            {
                isPlayingSpaceShooterClearReward = false;
            }
        }

        // 리듬 분리수거 결과는 미니게임 전환이 끝난 뒤 방 상태와 후속 대사로 반영한다.
        private async UniTaskVoid PlayPendingRythmRecycleCleanupResult(CancellationToken ct)
        {
            if (isPlayingRythmRecycleCleanupResult ||
                !SceneLoadArgs.ConsumeRythmRecycleResult(out RythmRecycleResult result))
            {
                return;
            }

            isPlayingRythmRecycleCleanupResult = true;
            try
            {
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                await SceneTransitionFadeUI.WaitForSpiralIdleAsync(ct);

                bool addedInfo = false;
                if (result != RythmRecycleResult.Failed)
                {
                    addedInfo = CompleteTrashCleanup(result);
                }

                string resultDialogueId = GetRythmRecycleResultDialogueId(result);
                if (!string.IsNullOrWhiteSpace(resultDialogueId))
                {
                    await PlayDialogueStory(
                        resultDialogueId,
                        null,
                        ct,
                        DialogueLines.BuildActionResultDialogueLines(
                            Array.Empty<string>(),
                            Array.Empty<string>(),
                            addedInfo ? new[] { DoorlockResetInfoId } : Array.Empty<string>()));
                }

            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
            finally
            {
                isPlayingRythmRecycleCleanupResult = false;
            }
        }

        // 락픽 성공 결과를 방에 반영하고 두 번째 사용 뒤 임시 도구를 소모한다.
        private async UniTaskVoid ApplyPendingLockPickUnlockResult(CancellationToken ct)
        {
            if (isApplyingLockPickUnlockResult ||
                !SceneLoadArgs.ConsumeLockPickUnlock(out LockPickUnlockTarget target))
            {
                return;
            }

            isApplyingLockPickUnlockResult = true;
            try
            {
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                await SceneTransitionFadeUI.WaitForSpiralIdleAsync(ct);

                if (target == LockPickUnlockTarget.Handcuffs)
                {
                    bool changed = SetSceneObjectActive(HandcuffsObjectName, false);
                    changed |= SetSceneObjectActive(RestrainedHumanObjectName, false);
                    changed |= SetSceneObjectActive(UnlockedHumanObjectName, true);
                    if (changed)
                    {
                        RefreshHitCandidates();
                    }

                    await PlayDialogueStory(GetLockPickUnlockedDialogueId(target), null, ct);
                }
                else if (target == LockPickUnlockTarget.EntrancePadlock)
                {
                    bool changed = SetSceneObjectActive(EntrancePadlockObjectName, false);
                    changed |= SetSceneObjectActive(EntrancePadlockUnlockedObjectName, true);
                    if (changed)
                    {
                        RefreshHitCandidates();
                    }

                    await PlayDialogueStory(GetLockPickUnlockedDialogueId(target), null, ct);
                }
                else
                {
                    bool changed = false;
                    GameObject lockFlag = FindSceneObject(GetLockPickTargetFlagObjectName(target));
                    if (lockFlag != null)
                    {
                        SetObjectActive(lockFlag, ResolveObjectActiveForToggle(lockFlag, false));
                        changed = true;
                    }

                    if (target == LockPickUnlockTarget.UtilityDoor)
                    {
                        changed |= SetSceneObjectActive(UtilityDoorPopupObjectName, false);
                    }

                    if (changed)
                    {
                        RefreshHitCandidates();
                    }

                    await PlayDialogueStory(GetLockPickUnlockedDialogueId(target), null, ct);
                }

                if (target == LockPickUnlockTarget.EntrancePadlock)
                {
                    AchievementProgress.Unlock(LockPickAllTypesAchievementId);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
            finally
            {
                isApplyingLockPickUnlockResult = false;
            }
        }

        private static string GetLockPickTargetFlagObjectName(LockPickUnlockTarget target)
        {
            return target switch
            {
                LockPickUnlockTarget.Handcuffs => HandcuffsObjectName,
                LockPickUnlockTarget.UtilityDoor => OpenUtilityDoorFlagObjectName,
                _ => LockDrawerFlagObjectName,
            };
        }

        // 해정 성공 대상에 맞는 방 복귀 대사를 고른다.
        private static string GetLockPickUnlockedDialogueId(LockPickUnlockTarget target)
        {
            return target switch
            {
                LockPickUnlockTarget.UtilityDoor => LockPickUtilityDoorUnlockedDialogueId,
                LockPickUnlockTarget.EntrancePadlock => LockPickEntrancePadlockUnlockedDialogueId,
                LockPickUnlockTarget.Handcuffs => LockPickHandcuffsUnlockedDialogueId,
                _ => LockPickDrawerUnlockedDialogueId,
            };
        }

        private GameObject EnsureSpaceShooterClearRewardFlagObject()
        {
            GameObject existing = FindSceneObject(SceneLoadArgs.SpaceShooterClearRewardSeenObjectName);
            if (existing != null)
            {
                return existing;
            }

            Transform parent = GetRoomTransform(RoomType.LivingRoom);
            if (parent == null)
            {
                parent = currentRoom;
            }

            if (parent == null)
            {
                return null;
            }

            GameObject flagObject = new(SceneLoadArgs.SpaceShooterClearRewardSeenObjectName);
            flagObject.transform.SetParent(parent, false);
            flagObject.SetActive(false);
            return flagObject;
        }

        // 씬에 배치된 분리수거 상태 flag 중 초기 기본값을 보정한다.
        private void EnsureRythmRecycleStateFlagObjects()
        {
            GameObject noneFlag = FindSceneObject(RythmRecycleNoneFlagObjectName);
            GameObject doneFlag = FindSceneObject(RythmRecycleDoneFlagObjectName);
            GameObject perfectFlag = FindSceneObject(RythmRecyclePerfectFlagObjectName);

            if (noneFlag != null &&
                (doneFlag == null || !doneFlag.activeSelf) &&
                (perfectFlag == null || !perfectFlag.activeSelf) &&
                !noneFlag.activeSelf)
            {
                noneFlag.SetActive(true);
            }
        }

        private void Start()
        {
            PlayInitialStartFlow(destroyCancellationToken).Forget();
        }

        // 신규 게임 진입을 기록한 뒤 인트로와 시작 스플래시를 순서대로 재생한다.
        private async UniTaskVoid PlayInitialStartFlow(CancellationToken ct)
        {
            bool shouldPlayIntro = SceneLoadArgs.ConsumePlayIntro();
            bool shouldPlayStartSplash = SceneLoadArgs.ConsumePlayStartSplash();
            UnlockGameStartedAchievement(shouldPlayStartSplash);
            try
            {
                if (shouldPlayIntro)
                {
                    await PlayPendingIntro(ct);
                    ct.ThrowIfCancellationRequested();

                    if (shouldPlayStartSplash && startSplashUI != null)
                    {
                        await startSplashUI.PlayAsync(ct);
                    }

                    return;
                }

                if (shouldPlayStartSplash)
                {
                    SetAdventurePanelActive(true);
                    PopAdventurePanelHidden();
                    if (startSplashUI != null)
                    {
                        await startSplashUI.PlayAsync(ct);
                    }

                    return;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex, this);
            }

            SetAdventurePanelActive(true);
            PopAdventurePanelHidden();
        }

        // 룸 씬에 새 게임으로 진입하는 즉시 첫 시작 도전과제를 해금한다.
        private static void UnlockGameStartedAchievement(bool isNewGame)
        {
            if (isNewGame)
            {
                AchievementProgress.Unlock(GameStartedAchievementId);
            }
        }

        private async UniTask PlayPendingIntro(CancellationToken ct)
        {
            bool adventurePanelReleased = false;
            try
            {
                // 씬 로드 직후 지연 없이 인트로를 시작한다. (첫 줄의 즉시검정ON이 즉시 검정을 처리)
                await PlayDialogueStory(IntroDialogueId, ct);
                ct.ThrowIfCancellationRequested();

                IntroPlaybackState.MarkCompletedIntro();
                AchievementProgress.Unlock(IntroWatchedAchievementId);

                SetAdventurePanelActive(true);
                PopAdventurePanelHidden();
                adventurePanelReleased = true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // 씬 종료로 취소된 인트로는 조사 UI를 복구할 필요가 없다.
            }
            finally
            {
                if (this != null && !adventurePanelReleased)
                {
                    ScreenEffects.ApplyImmediate(
                        new DialogueScreenEffectCue(
                            DialogueScreenEffectKind.Blackout,
                            DialogueScreenEffectState.Hide,
                            DialogueScreenEffectSpeed.Instant));
                    SetAdventurePanelActive(true);
                    PopAdventurePanelHidden();
                }
            }
        }

        private void OnDestroy()
        {
            screenEffectController?.Dispose();
        }

        private void Update()
        {
            if (PopupUIBase.LastClosedFrame == Time.frameCount)
            {
                Log("Popup close consumed this frame. Room click blocked.");
                return;
            }

            if (dialoguePlayer != null)
            {
                if (dialoguePlayer.IsPlaying)
                {
                    return;
                }

                if (dialoguePlayer.ConsumedAdvanceInputThisFrame)
                {
                    Log("Dialogue input consumed this frame. Room click blocked.");
                    return;
                }
            }

            if (IsInputLocked())
            {
                if (WasPointerReleased(out var lockedScreenPosition) &&
                    Escape.Input.GameScreenInputArea.Contains(lockedScreenPosition))
                {
                    Log($"Input locked. Room click blocked at screen={lockedScreenPosition}, reason={state?.InputLockReason}");
                }

                return;
            }

        }

        // 가상 커서의 화면 좌표에서 사용/조사를 실행한다.
        public void TryInspectAtScreenPosition(Vector2 screenPosition)
        {
            if (PopupUIBase.LastClosedFrame == Time.frameCount ||
                (dialoguePlayer != null && (dialoguePlayer.IsPlaying || dialoguePlayer.ConsumedAdvanceInputThisFrame)) ||
                IsInputLocked() ||
                !Escape.Input.GameScreenInputArea.Contains(screenPosition))
            {
                return;
            }

            TryInspectAt(screenPosition);
        }

        private void TryInspectAt(Vector2 screenPosition)
        {
            if (targetCamera == null)
            {
                return;
            }

            state = ResolvePlayerInventory().State;
            if (!TryGetWorldPoint(screenPosition, out var worldPoint))
            {
                hasLastInspectWorldPoint = false;
                Log($"No world point. screen={screenPosition}, selectedItem={state.SelectedItemId}");
                return;
            }

            hasLastInspectWorldPoint = true;
            lastInspectWorldPoint = worldPoint;

            if (!TryFindAlphaHit(worldPoint, out var interactable))
            {
                Log($"No room hit. screen={screenPosition}, world={worldPoint}, selectedItem={state.SelectedItemId}, candidates={HitTester.CandidateCount}");
            }

            Inspect(interactable);
        }

        private void Inspect(RoomInteractor interactable)
        {
            state = ResolvePlayerInventory().State;
            if (string.Equals(state?.SelectedItemId, PoweredGameConsoleItemId, StringComparison.Ordinal))
            {
                PlayGameConsolePrompt(destroyCancellationToken).Forget();
                return;
            }

            string targetName = GetInteractableName(interactable);
            // Any 규칙도 함께 비교해야 오브젝트 규칙이 전역 fallback보다 우선한다.
            InteractionRule interaction = ResolveInteraction(interactable, true);
            Log($"Inspect {targetName}. selectedItem={state.SelectedItemId}");

            if (interaction == null)
            {
                Log($"No interaction matched. target={targetName}, selectedItem={state.SelectedItemId}");
                return;
            }

            TouchSfxRouter.OverrideCurrentTouch(GetTouchSfxPreset(interaction));
            ExecuteInteraction(interaction, interactable);
        }

        // 도어락 키패드는 컨트롤러에서 키별 SFX를 직접 재생하므로 기본 클릭음을 막는다.
        private static TouchSfxPreset GetTouchSfxPreset(InteractionRule interaction)
        {
            return interaction != null && IsDoorLockKeypadAction(interaction.SpecialAction)
                ? TouchSfxPreset.Silent
                : interaction != null
                ? interaction.TouchSfx
                : TouchSfxPreset.Default;
        }

        private static bool IsDoorLockKeypadAction(InteractionSpecialAction action)
        {
            return action >= InteractionSpecialAction.DoorLockPress0 &&
                action <= InteractionSpecialAction.DoorLockReset;
        }

        // 규칙에 정의된 대사, 아이템 처리, 특수 액션, 방 이동을 실행한다.
        private InteractionRule ResolveInteraction(
            RoomInteractor interactable,
            bool allowAnySelectedItem = true)
        {
            string selectedItemId = state != null ? state.SelectedItemId : string.Empty;
            InteractionRule bestInteraction = interactable != null
                ? interactable.ResolveInteraction(
                    state,
                    allowAnySelectedItem,
                    InteractionPriorityLayer.Object)
                : null;
            InteractionPriorityLayer bestDefaultLayer = InteractionPriorityLayer.Object;
            int bestOrder = 0;

            ConsiderInteraction(
                ResolveFallbackInteractionFrom(
                    CurrentRoomComponent?.FallbackInteractions,
                    selectedItemId,
                    allowAnySelectedItem,
                    InteractionPriorityLayer.Room,
                    out int roomOrder),
                InteractionPriorityLayer.Room,
                roomOrder,
                ref bestInteraction,
                ref bestDefaultLayer,
                ref bestOrder);

            ConsiderInteraction(
                ResolveFallbackInteractionFrom(
                    fallbackInteractions,
                    selectedItemId,
                    allowAnySelectedItem,
                    InteractionPriorityLayer.Fallback,
                    out int fallbackOrder),
                InteractionPriorityLayer.Fallback,
                fallbackOrder,
                ref bestInteraction,
                ref bestDefaultLayer,
                ref bestOrder);

            return bestInteraction;
        }

        // 전달받은 fallback 목록에서 현재 상태와 선택 아이템에 맞는 규칙을 찾는다.
        private InteractionRule ResolveFallbackInteractionFrom(
            InteractionRule[] interactions,
            string selectedItemId,
            bool allowAnySelectedItem,
            InteractionPriorityLayer defaultPriorityLayer,
            out int matchedOrder)
        {
            interactions ??= Array.Empty<InteractionRule>();
            matchedOrder = -1;
            InteractionRule bestRule = null;
            for (int i = 0; i < interactions.Length; i++)
            {
                InteractionRule interaction = interactions[i];
                if (interaction != null &&
                    interaction.Matches(state, selectedItemId, allowAnySelectedItem))
                {
                    if (bestRule == null ||
                        InteractionRule.ComparePriority(
                            interaction,
                            defaultPriorityLayer,
                            i,
                            bestRule,
                            defaultPriorityLayer,
                            matchedOrder) > 0)
                    {
                        bestRule = interaction;
                        matchedOrder = i;
                    }
                }
            }

            return bestRule;
        }

        private static void ConsiderInteraction(
            InteractionRule candidate,
            InteractionPriorityLayer candidateDefaultLayer,
            int candidateOrder,
            ref InteractionRule bestInteraction,
            ref InteractionPriorityLayer bestDefaultLayer,
            ref int bestOrder)
        {
            if (candidate == null)
            {
                return;
            }

            if (InteractionRule.ComparePriority(
                    candidate,
                    candidateDefaultLayer,
                    candidateOrder,
                    bestInteraction,
                    bestDefaultLayer,
                    bestOrder) <= 0)
            {
                return;
            }

            bestInteraction = candidate;
            bestDefaultLayer = candidateDefaultLayer;
            bestOrder = candidateOrder;
        }

        private void ExecuteInteraction(
            InteractionRule interaction,
            RoomInteractor interactable)
        {
            ExecuteInteractionSequence(interaction, interactable, destroyCancellationToken).Forget();
        }

        private async UniTaskVoid ExecuteInteractionSequence(
            InteractionRule interaction,
            RoomInteractor interactable,
            CancellationToken ct)
        {
            Log($"Interaction event. target={GetInteractableName(interactable)}, events=[{DescribeEvents(interaction)}]");

            // Pre 단계: 상태 변경 전 사전 대사만 먼저 끝까지 보여준다.
            interactionSequenceCount++;
            PushAdventurePanelHidden();
            Room preparedEntranceRoom = null;

            try
            {
                DialogueStoryResult preDialogueResult = await PlayInteractionDialogue(
                    interaction.PreDialogueId,
                    interactable,
                    ct);

                InitializeSpecialActionDispatcher();
                if (!specialActionDispatcher.ShouldExecute(interaction.SpecialAction, preDialogueResult))
                {
                    return;
                }

                if (ShouldPlayDemoBedroomExitGameOver(interaction.TransitionDestination))
                {
                    await PlayInteractionDialogue(
                        DemoBedroomExitGameOverDialogueId,
                        interactable,
                        ct);
                    return;
                }

                await ScreenEffects.PlayAsync(interaction.ScreenEffect, true, ct);

                // Action 단계: 선택한 화면 효과 사이에서 상태 변경과 동작 대사를 실행한다.
                var consumedItems = new List<string>();
                AddConsumedItem(consumedItems, ConsumeSelectedItem(interaction.ConsumeSelectedItem));
                string grantableItem = GetGrantableItem(interaction.GrantItem);
                List<string> grantableInfos = GetGrantableInfos(interaction.GrantInfo, interaction.GrantInfos);
                Grant(interaction.GrantItem);
                GrantInfos(grantableInfos);
                ApplyObjectToggles(interaction, interactable);
                await ExecuteSpecialAction(interaction.SpecialAction, interactable, ct);
                itemPanel?.Refresh();

                bool transitionedRoom = interaction.TransitionDestination != RoomType.None;
                if (transitionedRoom)
                {
                    // 새 방의 입장 연출 첫 프레임을 캡처해 화면 전환 동안 유지한다.
                    MoveToRoom(interaction.TransitionDestination, true, false);
                    Room destinationRoom = CurrentRoomComponent;
                    if (destinationRoom != null && destinationRoom.PrepareEntranceAnimation())
                    {
                        preparedEntranceRoom = destinationRoom;
                    }
                }

                await PlayInteractionDialogue(
                    interaction.ActionDialogueId,
                    interactable,
                    ct,
                    DialogueLines.BuildActionResultDialogueLines(
                        consumedItems,
                        string.IsNullOrWhiteSpace(grantableItem)
                            ? Array.Empty<string>()
                            : new[] { grantableItem },
                        grantableInfos));

                await ScreenEffects.PlayAsync(interaction.ScreenEffect, false, ct);
                if (transitionedRoom)
                {
                    preparedEntranceRoom?.ResumePreparedEntranceAnimation();
                    preparedEntranceRoom = null;
                }

                // Post 단계: 화면 연출이 복귀한 뒤 사후 대사를 끝까지 재생한다.
                await PlayInteractionDialogue(
                    interaction.PostDialogueId,
                    interactable,
                    ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // 씬 종료/오브젝트 파괴로 취소된 상호작용은 정상 종료로 본다.
            }
            finally
            {
                preparedEntranceRoom?.ResumePreparedEntranceAnimation();

                if (this != null)
                {
                    interactionSequenceCount = Mathf.Max(0, interactionSequenceCount - 1);
                    PopAdventurePanelHidden();
                }
            }
        }

        private static string DescribeEvents(InteractionRule interaction)
        {
            var events = new List<string>();
            AddEvent(events, "preDialogue", interaction.PreDialogueId);
            AddEvent(events, "screenEffect", interaction.ScreenEffect, ScreenEffectType.None);
            AddEvent(events, "grantItem", interaction.GrantItem);
            AddEvent(events, "grantInfo", interaction.GrantInfo);
            AddEvent(events, "grantInfos", interaction.GrantInfos);
            AddEvent(events, "consumeSelectedItem", interaction.ConsumeSelectedItem, false);
            AddEvent(events, "deactivateTouchedObject", interaction.DeactivateTouchedObject, false);
            AddEvent(events, "deactivate", interaction.DeactivateObjects);
            AddEvent(events, "activate", interaction.ActivateObjects);
            AddEvent(events, "specialAction", interaction.SpecialAction, InteractionSpecialAction.None);
            AddEvent(events, "transition", interaction.TransitionDestination, RoomType.None);
            events.Add($"touchSfx={interaction.TouchSfx}");
            AddEvent(events, "actionDialogue", interaction.ActionDialogueId);
            AddEvent(events, "postDialogue", interaction.PostDialogueId);
            return events.Count > 0 ? string.Join(", ", events) : "(none)";
        }

        // 데모에서는 침실을 벗어나려는 순간 게임오버 대화로 흐름을 끝낸다.
        private bool ShouldPlayDemoBedroomExitGameOver(RoomType destination)
        {
            return CurrentRoomId == RoomType.BedRoom &&
                   destination == RoomType.LivingRoom;
        }

        private static void AddEvent(List<string> events, string name, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                events.Add($"{name}={value}");
            }
        }

        private static void AddEvent<T>(List<string> events, string name, T value, T emptyValue)
        {
            if (!EqualityComparer<T>.Default.Equals(value, emptyValue))
            {
                events.Add($"{name}={value}");
            }
        }

        private static void AddEvent(List<string> events, string name, string[] values)
        {
            values ??= Array.Empty<string>();
            var builder = new StringBuilder();
            for (int i = 0; i < values.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(values[i]))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append('|');
                }

                builder.Append(values[i]);
            }

            if (builder.Length > 0)
            {
                events.Add($"{name}={builder}");
            }
        }

        private static void AddEvent(List<string> events, string name, GameObject[] objects)
        {
            objects ??= Array.Empty<GameObject>();
            var builder = new StringBuilder();
            for (int i = 0; i < objects.Length; i++)
            {
                if (objects[i] == null)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append('|');
                }

                builder.Append(objects[i].name);
            }

            if (builder.Length > 0)
            {
                events.Add($"{name}={builder}");
            }
        }

        private bool GrantDialogueChoiceFlag(string dialogueId, string flag)
        {
            if (string.IsNullOrWhiteSpace(flag))
            {
                return false;
            }

            state = ResolvePlayerInventory().State;
            return state != null && state.AddDialogueFlag(dialogueId, flag);
        }

        private async UniTask<DialogueStoryResult> PlayInteractionDialogue(
            string dialogueId,
            RoomInteractor interactable,
            CancellationToken ct,
            IReadOnlyList<DialogueLine> appendedLines = null)
        {
            string requestedDialogueId = dialogueId;
            bool isWindowInteraction = string.Equals(
                requestedDialogueId,
                WindowDialogueId,
                StringComparison.Ordinal);
            bool isCurryInteraction = string.Equals(
                requestedDialogueId,
                CurryDialogueId,
                StringComparison.Ordinal);
            if (isWindowInteraction && HasDialogueFlag(WindowDialogueId, WindowShoutFlag))
            {
                dialogueId = HasDialogueFlag(WindowRepeatDialogueId, WindowRepeatShoutFlag)
                    ? WindowDoneDialogueId
                    : WindowRepeatDialogueId;
            }
            else if (isCurryInteraction && HasDialogueFlag(CurryDialogueId, CurryEatFlag))
            {
                dialogueId = CurryDoneDialogueId;
            }

            DialogueStoryResult result = await PlayDialogueStory(
                dialogueId,
                interactable,
                ct,
                appendedLines);

            // 첫 외침을 저장해 이후 창문 조사에서는 이웃의 반복 반응 대사를 사용한다.
            if (isWindowInteraction && result != null && result.HasFlag(WindowShoutFlag))
            {
                GrantDialogueChoiceFlag(WindowDialogueId, WindowShoutFlag);
            }

            // 두 번째 외침 뒤에는 선택지를 닫고 반복 독백만 표시한다.
            if (isWindowInteraction &&
                string.Equals(dialogueId, WindowRepeatDialogueId, StringComparison.Ordinal) &&
                result != null &&
                result.HasFlag(WindowRepeatShoutFlag))
            {
                GrantDialogueChoiceFlag(WindowRepeatDialogueId, WindowRepeatShoutFlag);
            }

            // 카레를 먹은 선택을 저장해 냄비는 남기되 회복은 한 번만 허용한다.
            if (isCurryInteraction && result != null && result.HasFlag(CurryEatFlag))
            {
                GrantDialogueChoiceFlag(CurryDialogueId, CurryEatFlag);
            }

            return result;
        }

        // RoomController가 보유한 기능을 역할별 특수 행동 핸들러에 연결한다.
        private void InitializeSpecialActionDispatcher()
        {
            if (specialActionDispatcher != null)
            {
                return;
            }

            Func<string, RoomInteractor, CancellationToken, UniTask<DialogueStoryResult>> playDialogueStory =
                (dialogueId, interactable, cancellationToken) =>
                    PlayDialogueStory(dialogueId, interactable, cancellationToken);

            var bedHandler = new BedSpecialActionHandler(
                dialoguePanel,
                playDialogueStory);

            specialActionDispatcher = new RoomSpecialActionDispatcher(
                new MiniGameSpecialActionHandler(
                    () => CurrentRoomId,
                    inventory,
                    screenEffectDuration),
                new FlashlightSpecialActionHandler(
                    () => currentRoom,
                    () => hasLastInspectWorldPoint,
                    () => lastInspectWorldPoint,
                    IsFlashlightTransform),
                bedHandler);
        }

        // InteractionRule의 enum 액션을 상호작용 흐름 안에서 실행한다.
        private async UniTask ExecuteSpecialAction(
            InteractionSpecialAction action,
            RoomInteractor interactable,
            CancellationToken ct)
        {
            if (action == InteractionSpecialAction.None)
            {
                return;
            }

            InitializeSpecialActionDispatcher();
            if (!specialActionDispatcher.TryGetHandler(action, out IRoomSpecialActionHandler handler))
            {
                Debug.LogWarning($"Unsupported interaction special action: {action}", this);
                return;
            }

            await handler.ExecuteAsync(action, interactable, ct);
        }

        // 짧은 시스템 문구 한 줄 재생을 대사 Player에 위임한다.
        private UniTask PlaySingleLine(string textId, CancellationToken cancellationToken)
        {
            return DialogueStory.PlaySingleLineAsync(textId, cancellationToken);
        }

        private TimingCheckPopupUI ResolveTimingCheckPopup()
        {
            if (timingCheckPopup != null)
            {
                return timingCheckPopup;
            }

            timingCheckPopup = FindFirstObjectByType<TimingCheckPopupUI>(FindObjectsInactive.Include);
            return timingCheckPopup;
        }

        private bool PlayRoomAnimationState(string animationName, AsepriteSpritePlaybackMode playbackMode)
        {
            if (currentRoom == null)
            {
                return false;
            }

            var animators = currentRoom.GetComponentsInChildren<AsepriteRoomAnimator>(true);
            bool played = false;
            for (int i = 0; i < animators.Length; i++)
            {
                AsepriteRoomAnimator animator = animators[i];
                if (animator == null || !animator.isActiveAndEnabled)
                {
                    continue;
                }

                played |= animator.TryApplyAnimationState(animationName, playbackMode);
            }

            return played;
        }

        // 건전지를 넣어 작동하게 만든 게임기를 사용하면 선택을 확인한 뒤 SpaceShooter를 연다.
        private async UniTask PlayGameConsolePrompt(CancellationToken ct)
        {
            DialogueStoryResult result = await PlayDialogueStory(GameConsolePromptDialogueId, null, ct);
            if (result == null || !result.HasFlag(GameConsolePlayFlag))
            {
                return;
            }

            await SceneTransitionFadeUI.PlaySpiralTransitionAsync(
                () => EscapeSceneLoader.LoadSpaceShooterMiniGame(CurrentRoomId),
                screenEffectDuration,
                ct);
        }

        // 외부 요청 대사 묶음을 Room 대사 Player로 재생한다.
        public UniTask<DialogueStoryResult> PlayDialogueStory(
            string dialogueId,
            CancellationToken cancellationToken = default)
        {
            return DialogueStory.PlayAsync(
                dialogueId,
                GetInteractableName(null),
                cancellationToken);
        }

        // 대사 원본과 현재 UI·상태를 연결해 DialogueLine 생성을 지연 위임한다.
        private RoomDialogueLineFactory DialogueLines =>
            dialogueLineFactory ??= new RoomDialogueLineFactory(
                dialogueTable,
                speakerTable,
                itemTable,
                infoTable,
                () => dialoguePanel,
                () => inventory != null ? inventory.State : state);

        // 대사 묶음의 구성과 재생 수명주기를 전담하는 Player를 지연 생성한다.
        private RoomDialogueStoryPlayer DialogueStory =>
            dialogueStoryPlayer ??= new RoomDialogueStoryPlayer(
                dialogueTable,
                DialogueLines,
                () => dialoguePlayer,
                () => dialoguePanel,
                AdventurePanelVisibility);

        // 상호작용 출처와 추가 결과 대사를 포함해 대사 Player로 재생한다.
        private UniTask<DialogueStoryResult> PlayDialogueStory(
            string dialogueId,
            RoomInteractor interactable,
            CancellationToken cancellationToken,
            IReadOnlyList<DialogueLine> appendedLines = null)
        {
            return DialogueStory.PlayAsync(
                dialogueId,
                GetInteractableName(interactable),
                cancellationToken,
                appendedLines);
        }

        // 대화 패널과 유지 중인 초상화·배경을 정리한다.
        private void HideDialoguePanel()
        {
            DialogueStory.HidePanel();
        }

        // 쓰레기통 정리 완료 결과를 방 상태와 보유 정보에 반영한다.
        public bool CompleteTrashCleanup(RythmRecycleResult result = RythmRecycleResult.Success)
        {
            SetObjectActive(FindSceneObject(TrashMessyObjectName), false);
            SetObjectActive(FindSceneObject(TrashTidyObjectName), true);
            SetRythmRecycleCleanupState(result == RythmRecycleResult.Perfect
                ? RythmRecycleCleanupState.Perfect
                : RythmRecycleCleanupState.Done);

            InfoCollection infoCollection = ResolveInfoCollection();
            bool addedInfo = infoCollection != null && infoCollection.AddInfo(DoorlockResetInfoId);
            state = infoCollection != null ? infoCollection.State : state;

            RefreshHitCandidates();
            return addedInfo;
        }

        private static string GetRythmRecycleResultDialogueId(RythmRecycleResult result)
        {
            return result switch
            {
                RythmRecycleResult.Perfect => RythmRecyclePerfectResultDialogueId,
                RythmRecycleResult.Success => RythmRecycleSuccessResultDialogueId,
                RythmRecycleResult.Failed => RythmRecycleFailedResultDialogueId,
                _ => string.Empty,
            };
        }

        private RythmRecycleCleanupState GetRythmRecycleCleanupState()
        {
            if (FindSceneObject(RythmRecyclePerfectFlagObjectName) is { activeSelf: true })
            {
                return RythmRecycleCleanupState.Perfect;
            }

            if (FindSceneObject(RythmRecycleDoneFlagObjectName) is { activeSelf: true })
            {
                return RythmRecycleCleanupState.Done;
            }

            GameObject tidyObject = FindSceneObject(TrashTidyObjectName);
            if (tidyObject != null && tidyObject.activeSelf)
            {
                return RythmRecycleCleanupState.Done;
            }

            return RythmRecycleCleanupState.None;
        }

        private void SetRythmRecycleCleanupState(RythmRecycleCleanupState cleanupState)
        {
            SetObjectActive(FindSceneObject(RythmRecycleNoneFlagObjectName), cleanupState == RythmRecycleCleanupState.None);
            SetObjectActive(FindSceneObject(RythmRecycleDoneFlagObjectName), cleanupState == RythmRecycleCleanupState.Done);
            SetObjectActive(FindSceneObject(RythmRecyclePerfectFlagObjectName), cleanupState == RythmRecycleCleanupState.Perfect);
        }

        private enum RythmRecycleCleanupState
        {
            None = 0,
            Done = 1,
            Perfect = 2,
        }

        private bool HasDialogueFlag(string dialogueId, string flag)
        {
            if (string.IsNullOrWhiteSpace(flag))
            {
                return true;
            }

            state = ResolvePlayerInventory().State;
            return state != null && state.HasDialogueFlag(dialogueId, flag);
        }

        private static string GetInteractableName(RoomInteractor interactable)
        {
            return interactable != null ? interactable.name : "(Empty Space)";
        }

        // 상호작용 보상 아이템 하나를 지급한다.
        private void Grant(string itemId)
        {
            if (!string.IsNullOrWhiteSpace(itemId))
            {
                ResolvePlayerInventory().AddItem(itemId);
            }
        }

        // 상호작용 보상 정보를 지급한다.
        private void GrantInfos(IReadOnlyList<string> infoIds)
        {
            if (infoIds == null)
            {
                return;
            }

            for (int i = 0; i < infoIds.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(infoIds[i]))
                {
                    ResolveInfoCollection().AddInfo(infoIds[i]);
                }
            }
        }

        // 아직 보유하지 않은 단일 보상 아이템을 반환한다.
        private string GetGrantableItem(string itemId)
        {
            GameSession currentState = inventory != null ? inventory.State : state;
            if (string.IsNullOrWhiteSpace(itemId) ||
                (currentState != null && currentState.Items.Contains(itemId)))
            {
                return string.Empty;
            }

            return itemId;
        }

        // 아직 보유하지 않은 보상 정보를 중복 없이 반환한다.
        private List<string> GetGrantableInfos(string infoId, string[] infoIds)
        {
            var grantableInfos = new List<string>();
            GameSession currentState = infoCollection != null ? infoCollection.State : state;
            AddGrantableInfo(grantableInfos, currentState, infoId);

            infoIds ??= Array.Empty<string>();
            for (int i = 0; i < infoIds.Length; i++)
            {
                AddGrantableInfo(grantableInfos, currentState, infoIds[i]);
            }

            return grantableInfos;
        }

        // 보유하지 않은 정보 ID만 보상 목록에 넣는다.
        private static void AddGrantableInfo(List<string> grantableInfos, GameSession currentState, string infoId)
        {
            if (string.IsNullOrWhiteSpace(infoId) ||
                grantableInfos.Contains(infoId) ||
                (currentState != null && currentState.Infos.Contains(infoId)))
            {
                return;
            }

            grantableInfos.Add(infoId);
        }

        // 현재 선택 중인 아이템을 상호작용 비용으로 소비한다.
        private string ConsumeSelectedItem(bool shouldConsume)
        {
            if (!shouldConsume)
            {
                return string.Empty;
            }

            string selectedItemId = ResolvePlayerInventory().SelectedItemId;
            if (!string.IsNullOrWhiteSpace(selectedItemId) && ResolvePlayerInventory().ConsumeItem(selectedItemId))
            {
                return selectedItemId;
            }

            return string.Empty;
        }

        // 소비 목록에 유효한 아이템을 중복 없이 추가한다.
        private static void AddConsumedItem(ICollection<string> target, string itemId)
        {
            if (!string.IsNullOrWhiteSpace(itemId) && !target.Contains(itemId))
            {
                target.Add(itemId);
            }
        }

        private void ApplyObjectToggles(InteractionRule interaction, RoomInteractor interactable)
        {
            if (interaction.DeactivateTouchedObject && interactable != null)
            {
                SetObjectActive(interactable.gameObject, false);
            }

            SetObjectsActive(interaction.DeactivateObjects, false);
            SetObjectsActive(interaction.ActivateObjects, true);
            RefreshHitCandidates();
        }

        private void SetObjectsActive(GameObject[] objects, bool active)
        {
            objects ??= Array.Empty<GameObject>();

            for (int i = 0; i < objects.Length; i++)
            {
                GameObject target = objects[i];
                if (target == null)
                {
                    continue;
                }

                SetObjectActive(target, ResolveObjectActiveForToggle(target, active));
            }
        }

        private static bool ResolveObjectActiveForToggle(GameObject target, bool active)
        {
            return InteractionRule.UsesFalseDefaultFlagState(target) ? !active : active;
        }

        private void SetObjectActive(GameObject target, bool active)
        {
            if (target == null)
            {
                return;
            }

            target.SetActive(active);
            ObjectStates.RecordVisibility(target.transform, active);
            Log($"{target.name}.SetActive({active})");
        }

        // 이름으로 찾은 Room 하위 오브젝트를 변경하고 저장용 objectStates에도 기록한다.
        public bool SetSceneObjectActive(string objectName, bool active)
        {
            GameObject target = FindSceneObject(objectName);
            if (target == null)
            {
                return false;
            }

            SetObjectActive(target, active);
            return true;
        }

        private static GameObject FindSceneObject(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
            {
                return null;
            }

            GameObject activeObject = GameObject.Find(objectName);
            if (activeObject != null)
            {
                return activeObject;
            }

            foreach (GameObject candidate in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (candidate == null ||
                    !candidate.scene.IsValid() ||
                    !string.Equals(candidate.name, objectName, StringComparison.Ordinal))
                {
                    continue;
                }

                return candidate;
            }

            return null;
        }

        public void GoLivingRoom()
        {
            Log("Show LivingRoom");
            ShowOnlyRoom(RoomType.LivingRoom);
        }

        public void GoBedRoom()
        {
            Log("Show BedRoom");
            ShowOnlyRoom(RoomType.BedRoom);
        }

        public void GoKitchenRoom()
        {
            Log("Show KitchenRoom");
            ShowOnlyRoom(RoomType.KitchenRoom);
        }

        public void GoEntranceRoom()
        {
            Log("Show EntranceRoom");
            ShowOnlyRoom(RoomType.EntranceRoom);
        }

        public void GoUtilityRoom()
        {
            Log("Show UtilityRoom");
            ShowOnlyRoom(RoomType.UtilityRoom);
        }

        private void MoveToRoom(
            RoomType destination,
            bool playMoveSound = false,
            bool playEntranceAnimation = true)
        {
            Log($"MoveToRoom {destination}. playSound={playMoveSound}");
            if (playMoveSound)
            {
                SoundPlayer.PlayMoveSfx();
            }

            switch (destination)
            {
                case RoomType.LivingRoom:
                    GoLivingRoom();
                    break;
                case RoomType.BedRoom:
                    GoBedRoom();
                    break;
                case RoomType.KitchenRoom:
                    GoKitchenRoom();
                    break;
                case RoomType.EntranceRoom:
                    GoEntranceRoom();
                    break;
                case RoomType.UtilityRoom:
                    GoUtilityRoom();
                    break;
            }

            if (playMoveSound)
            {
                locationPanel?.Show(destination);
            }

            if (playEntranceAnimation)
            {
                CurrentRoomComponent?.PlayEntranceAnimation();
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD || ESCAPE_TESTFLIGHT
        public void DevMoveToRoom(RoomType destination)
        {
            MoveToRoom(destination, true);
        }
#endif

        // dialogue effect에서 배경 뒤의 실제 방만 바꾸도록 이동음·위치 표시·입장 애니메이션을 생략한다.
        public void MoveToRoomFromDialogue(RoomType destination)
        {
            if (destination == RoomType.None || GetRoomTransform(destination) == null)
            {
                Debug.LogWarning($"Dialogue room move destination is unavailable: {destination}.", this);
                return;
            }

            MoveToRoom(destination, false, false);
        }

        // 저장 데이터 복원 시 연출음 없이 지정 방으로 이동한다.
        public void RestoreRoom(RoomType destination)
        {
            if (destination != RoomType.None)
            {
                MoveToRoom(destination);
            }
        }

        // 오브젝트 상태 저장과 복원을 전담하는 컨트롤러를 지연 생성한다.
        private RoomObjectStatePersistence ObjectStates =>
            objectStatePersistence ??= new RoomObjectStatePersistence(
                Registry,
                () => state,
                Log);

        // 모든 Room 오브젝트의 현재 활성 상태 변경분을 저장용으로 만든다.
        public List<string> CaptureObjectActiveStates()
        {
            return ObjectStates.CaptureActiveStates();
        }

        // 저장된 Room 오브젝트 활성 상태를 씬에 복원한다.
        public void RestoreObjectActiveStates(IReadOnlyList<string> states)
        {
            ObjectStates.RestoreActiveStates(states);
            RefreshHitCandidates();
            itemPanel?.Refresh();
        }

        // Room 애니메이터의 현재 표시 상태를 저장용으로 만든다.
        public List<string> CaptureObjectAnimationStates()
        {
            return ObjectStates.CaptureAnimationStates();
        }

        // 저장된 Room 애니메이션 상태를 씬에 복원한다.
        public void RestoreObjectAnimationStates(IReadOnlyList<string> states)
        {
            ObjectStates.RestoreAnimationStates(states);
        }

        private void InitializeStartingRoom()
        {
            currentRoom = null;
            MoveToRoom(ResolveInitialRoom());
            Log($"Initialized starting room. currentRoom={currentRoom?.name}");
        }

        private RoomType ResolveInitialRoom()
        {
            if (SceneLoadArgs.ConsumeMiniGameReturnRoom(out RoomType returnRoom))
            {
                return returnRoom;
            }

            return initialRoom == RoomType.None
                ? RoomType.LivingRoom
                : initialRoom;
        }

        private bool IsInputLocked()
        {
            return IsExecutingInteractionSequence ||
                   (screenEffectController != null && screenEffectController.IsPlaying) ||
                   (dialogueStoryPlayer != null && dialogueStoryPlayer.IsPlaying) ||
                   (state != null && state.IsInputLocked);
        }

        // 직렬화된 설정으로 화면 효과 전용 컨트롤러를 지연 생성한다.
        private RoomScreenEffectController ScreenEffects =>
            screenEffectController ??= new RoomScreenEffectController(
                this,
                roomImage,
                screenEffectDuration,
                redFadeDuration,
                redFadeColor,
                greenFadeDuration,
                greenFadeColor,
                slowBlackFadeDuration,
                resolutionFadeDuration);

        // Room 등록과 조회를 전담하는 Registry를 지연 생성한다.
        private RoomRegistry Registry =>
            roomRegistry ??= new RoomRegistry();

        // 직렬화 참조와 현재 씬 계층의 Room 컴포넌트를 등록한다.
        private void ResolveRoomReferences()
        {
            Transform searchRoot = transform.root != null ? transform.root : transform;
            Registry.Resolve(rooms, searchRoot, gameObject);
        }

        // 지정 Room ID에 등록된 Transform을 반환한다.
        private Transform GetRoomTransform(RoomType roomId)
        {
            return Registry.GetTransform(roomId);
        }

        // 중복을 제거한 현재 Room 루트를 순회한다.
        private IEnumerable<Transform> EnumerateRoomRoots()
        {
            return Registry.EnumerateRoots();
        }

        // 각 방 손전등의 씬 초기 로컬 위치를 저장해 방 이동 시 원래 자리로 되돌린다.
        private void CaptureInitialFlashlightPositions()
        {
            initialFlashlightLocalPositions.Clear();
            foreach (Transform roomRoot in EnumerateRoomRoots())
            {
                Transform[] transforms = roomRoot.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < transforms.Length; i++)
                {
                    Transform target = transforms[i];
                    if (IsFlashlightTransform(target))
                    {
                        initialFlashlightLocalPositions[target] = target.localPosition;
                    }
                }
            }
        }

        private PlayerInventory ResolvePlayerInventory()
        {
            if (inventory != null)
            {
                return inventory;
            }

            inventory = PlayerInventory.Instance;
            if (inventory == null)
            {
                inventory = FindFirstObjectByType<PlayerInventory>(FindObjectsInactive.Include);
            }

            if (inventory == null)
            {
                var inventoryObject = new GameObject(nameof(PlayerInventory));
                inventory = inventoryObject.AddComponent<PlayerInventory>();
            }

            return inventory;
        }

        // 씬의 정보 매니저를 찾고 없으면 런타임에 만든다.
        private InfoCollection ResolveInfoCollection()
        {
            if (infoCollection != null)
            {
                return infoCollection;
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

            return infoCollection;
        }

        private DialoguePlayer ResolveDialoguePlayer()
        {
            if (dialoguePlayer != null)
            {
                return dialoguePlayer;
            }

            dialoguePlayer = DialoguePlayer.Instance;
            if (dialoguePlayer == null)
            {
                dialoguePlayer = FindFirstObjectByType<DialoguePlayer>(FindObjectsInactive.Include);
            }

            return dialoguePlayer;
        }

        private DoorLockKeypadController ResolveDoorLockKeypad()
        {
            if (doorLockKeypad != null)
            {
                return doorLockKeypad;
            }

            doorLockKeypad = FindFirstObjectByType<DoorLockKeypadController>(FindObjectsInactive.Include);
            return doorLockKeypad;
        }

        // 조사 패널의 중첩 표시 상태를 전담하는 컨트롤러를 지연 생성한다.
        private AdventurePanelVisibilityController AdventurePanelVisibility =>
            adventurePanelVisibilityController ??= new AdventurePanelVisibilityController(
                adventurePanelCanvasGroup,
                () => this != null,
                FindSceneChildByName);

        // 조사 패널 숨김 요청을 누적한다.
        private void PushAdventurePanelHidden()
        {
            AdventurePanelVisibility.PushHidden();
        }

        // 조사 패널 숨김 요청 하나를 해제한다.
        private void PopAdventurePanelHidden()
        {
            AdventurePanelVisibility.PopHidden();
        }

        // 인트로 진행에 맞춰 조사 패널 GameObject 활성 상태를 바꾼다.
        private void SetAdventurePanelActive(bool active)
        {
            AdventurePanelVisibility.SetActive(active);
        }

        // ItemPanel 오브젝트에 현재 선택 아이템 UI를 연결한다.
        private void ResolveItemPanel()
        {
            if (itemPanel == null)
            {
                itemPanel = FindFirstObjectByType<ItemPanelUI>(FindObjectsInactive.Include);
            }

            if (itemPanel == null)
            {
                var searchRoot = transform.root != null ? transform.root : transform;
                Transform panelTransform = FindChildByName(searchRoot, "ItemPanel");
                panelTransform ??= FindSceneChildByName("ItemPanel");
                if (panelTransform != null)
                {
                    itemPanel = panelTransform.GetComponent<ItemPanelUI>();
                    if (itemPanel == null)
                    {
                        itemPanel = panelTransform.gameObject.AddComponent<ItemPanelUI>();
                    }
                }
            }

            itemPanel?.Configure(inventory);
        }

        private static Transform FindChildByName(Transform root, string childName)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (string.Equals(child.name, childName, StringComparison.Ordinal))
                {
                    return child;
                }
            }

            return null;
        }

        private Transform FindSceneChildByName(string childName)
        {
            if (this == null)
            {
                return null;
            }

            var scene = gameObject.scene;
            if (!scene.IsValid())
            {
                return null;
            }

            foreach (var rootObject in scene.GetRootGameObjects())
            {
                Transform child = FindChildByName(rootObject.transform, childName);
                if (child != null)
                {
                    return child;
                }
            }

            return null;
        }

        // 현재 방만 GameObject를 켜고 나머지 방은 통째로 비활성화한다.
        private void ShowRoom(Transform room, bool visible)
        {
            if (room == null)
            {
                return;
            }

            SetObjectActive(room.gameObject, visible);
        }

        private void ShowOnlyRoom(RoomType roomId)
        {
            ResetFlashlightsToInitialPositions();

            Transform targetRoom = GetRoomTransform(roomId);
            foreach (var room in EnumerateRoomRoots())
            {
                ShowRoom(room, room == targetRoom);
            }

            MoveRoomsToCamera(targetRoom);
            RefreshHitCandidates();
        }

        // 방 전환 전에 사용으로 움직인 손전등들을 씬에 배치된 초기 위치로 복구한다.
        private void ResetFlashlightsToInitialPositions()
        {
            foreach (KeyValuePair<Transform, Vector3> pair in initialFlashlightLocalPositions)
            {
                if (pair.Key != null)
                {
                    pair.Key.localPosition = pair.Value;
                }
            }
        }

        // 현재 방 손전등을 불안정하게 두 번 깜빡인 뒤 계속 켜진 상태로 고정한다.
        public async UniTask PlayFlashlightTurnOnEffectAsync(CancellationToken ct)
        {
            Transform flashlight = GetCurrentRoomFlashlight();
            if (flashlight == null)
            {
                Debug.LogWarning("현재 방에서 손전등 조명을 찾지 못했습니다.", this);
                return;
            }

            SetObjectActive(flashlight.gameObject, false);
            try
            {
                await UniTask.Delay(
                    TimeSpan.FromSeconds(FlashlightTurnOnDelay),
                    ignoreTimeScale: false,
                    cancellationToken: ct);

                SoundPlayer.PlayLightSwitchSfx();
                SetObjectActive(flashlight.gameObject, true);
                await UniTask.Delay(
                    TimeSpan.FromSeconds(FlashlightFirstFlashDuration),
                    ignoreTimeScale: false,
                    cancellationToken: ct);

                SetObjectActive(flashlight.gameObject, false);
                await UniTask.Delay(
                    TimeSpan.FromSeconds(FlashlightRetryDelay),
                    ignoreTimeScale: false,
                    cancellationToken: ct);

                SetObjectActive(flashlight.gameObject, true);
                await UniTask.Delay(
                    TimeSpan.FromSeconds(FlashlightSecondFlashDuration),
                    ignoreTimeScale: false,
                    cancellationToken: ct);

                SetObjectActive(flashlight.gameObject, false);
                await UniTask.Delay(
                    TimeSpan.FromSeconds(FlashlightFinalClickDelay),
                    ignoreTimeScale: false,
                    cancellationToken: ct);

            }
            finally
            {
                if (flashlight != null)
                {
                    SetObjectActive(flashlight.gameObject, true);
                }
            }
        }

        // 미리 수집한 손전등 중 현재 방 루트에 속한 조명을 반환한다.
        private Transform GetCurrentRoomFlashlight()
        {
            if (currentRoom == null)
            {
                return null;
            }

            foreach (Transform flashlight in initialFlashlightLocalPositions.Keys)
            {
                if (flashlight != null && flashlight.IsChildOf(currentRoom))
                {
                    return flashlight;
                }
            }

            return null;
        }

        // 기존 이름과 새 태그 중 하나라도 맞으면 방 손전등으로 취급한다.
        private static bool IsFlashlightTransform(Transform target)
        {
            if (target == null)
            {
                return false;
            }

            return string.Equals(target.name, FlashLightObjectName, StringComparison.Ordinal) ||
                   string.Equals(target.gameObject.tag, FlashLightTagName, StringComparison.Ordinal);
        }

        private void MoveRoomsToCamera(Transform room)
        {
            if (targetCamera == null || room == null)
            {
                return;
            }

            Vector3 offset = new(
                roomBasePosition.x - room.position.x,
                roomBasePosition.y - room.position.y,
                0f);

            foreach (var roomRoot in EnumerateRoomRoots())
            {
                MoveRoomRoot(roomRoot, offset);
            }

            currentRoom = room;
        }

        private static void MoveRoomRoot(Transform room, Vector3 offset)
        {
            if (room == null)
            {
                return;
            }

            room.position += offset;
        }

        // 상호작용 후보 수집과 픽셀 판정을 전담하는 히트 테스터를 지연 생성한다.
        private RoomHitTester HitTester =>
            hitTester ??= new RoomHitTester(
                Registry,
                gameObject,
                () => targetCamera,
                alphaHitThreshold,
                Log);

        // 현재 Room 계층의 상호작용 후보를 다시 수집한다.
        private void RefreshHitCandidates()
        {
            HitTester.Refresh();
        }

        // 화면 좌표를 방 평면의 월드 좌표로 변환한다.
        public bool TryProjectScreenToRoom(Vector2 screenPosition, out Vector2 worldPoint)
        {
            return HitTester.TryProjectScreenToRoom(screenPosition, out worldPoint);
        }

        // 내부 조사 흐름에서 화면 좌표를 월드 좌표로 변환한다.
        private bool TryGetWorldPoint(Vector2 screenPosition, out Vector2 worldPoint)
        {
            return HitTester.TryProjectScreenToRoom(screenPosition, out worldPoint);
        }

        // QA reflection 계약을 유지하면서 실제 픽셀 판정을 히트 테스터에 위임한다.
        private bool TryFindAlphaHit(Vector2 worldPoint, out RoomInteractor interactable)
        {
            return HitTester.TryFindAlphaHit(worldPoint, out interactable);
        }

        private static bool WasPointerReleased(out Vector2 screenPosition)
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null &&
                Mouse.current.leftButton.wasReleasedThisFrame &&
                !CursorChaseController.IsMouseTouchGestureActive &&
                CursorChaseController.MouseTouchReleaseConsumedFrame != Time.frameCount)
            {
                screenPosition = Mouse.current.position.ReadValue();
                return true;
            }

            if (Touchscreen.current != null)
            {
                var touch = Touchscreen.current.primaryTouch;
                if (touch.press.wasReleasedThisFrame)
                {
                    screenPosition = touch.position.ReadValue();
                    return true;
                }
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetMouseButtonUp(0))
            {
                screenPosition = Input.mousePosition;
                return true;
            }
#endif

            screenPosition = default;
            return false;
        }

        public void Configure(Camera camera, DialoguePopupUI dialogue, ItemPanelUI panel)
        {
            targetCamera = camera;
            dialoguePanel = dialogue;
            itemPanel = panel;
            if (ResolveDialoguePlayer() != null)
            {
                dialoguePlayer.Configure(dialoguePanel, this, ScreenEffects);
            }

            itemPanel?.Configure(inventory);
            RefreshHitCandidates();
        }

        private void Log(string message)
        {
            if (debugLogs)
            {
                Debug.Log($"{LogPrefix} {message}", this);
            }
        }


    }

}
