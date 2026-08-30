using Cysharp.Threading.Tasks;
using Escape.Dialogues;
using Escape.Localization;
using Escape.Progress;
using Escape.Audio;
using Escape.Data;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace Escape.Rooms
{
    // 도어락 숫자 입력, 표시등, 정답 플래그 처리를 담당한다.
    public sealed class DoorLockKeypadController : MonoBehaviour
    {
        private const string DoorLockObjectName = "obj_doorlock";
        private const string DoorLockUnlockedObjectName = "obj_doorlock_unlock";
        private const string DoorlockResetInfoId = "doorlock_reset_info";
        private const string ResetModeMissingManualDialogueId = "doorlock_reset_mode_missing_manual";
        private const string DoorlockResetCodeToken = "{DOORLOCK_RESET_CODE}";
        private const int ResetModePressCount = 5;
        private const string InitializedPasswordFlagObjectName = "FLAG:INIT_PASSWORD";
        private const string FailedOnceFlagObjectName = "FLAG:DOORLOCK_FAIL_ONE";
        private const string FailedTwiceFlagObjectName = "FLAG:DOORLOCK_FAIL_TWO";
        private const string InitializedDoorLockCode = "0000";
        private const string FingerprintObjectPrefix = "fingerprint_";
        private const string DirectUnlockAchievementId = "doorlock_direct_unlock";
        private const string FactoryResetAchievementId = "doorlock_factory_reset";

        [SerializeField] private GameObject[] digitDisplays = System.Array.Empty<GameObject>();
        [SerializeField] private string unlockCode = "1234";
        [SerializeField] private string resetUnlockCode = "0000";
        [TsvId("Assets/Resources/Data/dialogue.tsv")]
        [SerializeField] private string successDialogueId = "doorlock_reset_success";
        [TsvId("Assets/Resources/Data/dialogue.tsv")]
        [SerializeField] private string passwordSuccessDialogueId = "doorlock_password_success";
        [TsvId("Assets/Resources/Data/dialogue.tsv")]
        [SerializeField] private string passwordSuccessPostDialogueId = "doorlock_password_opened";
        [TsvId("Assets/Resources/Data/dialogue.tsv")]
        [SerializeField] private string failDialogueId = "doorlock_fail";
        [TsvId("Assets/Resources/Data/dialogue.tsv")]
        [SerializeField] private string failPenaltyDialogueId = "doorlock_fail_penalty";
        [TsvId("Assets/Resources/Data/dialogue.tsv")]
        [SerializeField] private string resetModeDialogueId = "doorlock_reset_mode";
        [SerializeField] private GameObject lockedDoorLockObject;
        [SerializeField] private GameObject unlockedDoorLockObject;
        [SerializeField] private bool resetInputOnEnable = true;

        private TsvTable<Dialogue> dialogueTable;
        private TsvTable<Speaker> speakerTable;
        private string input = string.Empty;
        private bool resetModeActive;
        private bool unlockSequencePlaying;
        private int resetModePresses;

        private void OnEnable()
        {
            unlockSequencePlaying = false;
            resetModePresses = 0;

            if (resetInputOnEnable)
            {
                ClearInput(false);
            }
        }

        private void OnDisable()
        {
            resetModePresses = 0;
        }

        public UniTask Press0(CancellationToken ct) => PressDigit('0', ct);
        public UniTask Press1(CancellationToken ct) => PressDigit('1', ct);
        public UniTask Press2(CancellationToken ct) => PressDigit('2', ct);
        public UniTask Press3(CancellationToken ct) => PressDigit('3', ct);
        public UniTask Press4(CancellationToken ct) => PressDigit('4', ct);
        public UniTask Press5(CancellationToken ct) => PressDigit('5', ct);
        public UniTask Press6(CancellationToken ct) => PressDigit('6', ct);
        public UniTask Press7(CancellationToken ct) => PressDigit('7', ct);
        public UniTask Press8(CancellationToken ct) => PressDigit('8', ct);
        public UniTask Press9(CancellationToken ct) => PressDigit('9', ct);

        // 밀가루를 뿌리면 현재 비밀번호에 포함된 숫자 키의 지문만 드러낸다.
        public void RevealPasswordFingerprints()
        {
            string code = ResolveUnlockCode();
            RoomController roomController = FindFirstObjectByType<RoomController>(FindObjectsInactive.Include);
            for (int digit = 0; digit <= 9; digit++)
            {
                string objectName = FingerprintObjectPrefix + digit;
                bool visible = code.IndexOf((char)('0' + digit)) >= 0;
                if (roomController != null && roomController.SetSceneObjectActive(objectName, visible))
                {
                    continue;
                }

                SetObjectActive(FindSceneObject(objectName), visible);
            }
        }

        // 별표 버튼으로 현재 입력한 비밀번호를 확인하고 결과 대사까지 기다린다.
        public async UniTask Confirm(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (unlockSequencePlaying)
            {
                return;
            }

            SoundPlayer.PlayKeypadSfx('*');
            resetModePresses = 0;

            if (resetModeActive)
            {
                if (string.Equals(input, ResolveResetUnlockCode(), System.StringComparison.Ordinal))
                {
                    ResetDoorLockCodeToDefault();
                    AchievementProgress.Unlock(FactoryResetAchievementId);
                    ResetFailedConfirmCount();
                    ClearInput(false);
                    await PlayDialogueAndWait(successDialogueId, ct);
                    return;
                }

                ClearInput(false);
                await HandleFailedConfirm(ct);
                return;
            }

            if (string.IsNullOrEmpty(input) || input.Length < digitDisplays.Length)
            {
                ClearInput(false);
                await HandleFailedConfirm(ct);
                return;
            }

            if (string.Equals(input, ResolveUnlockCode(), System.StringComparison.Ordinal))
            {
                ResetFailedConfirmCount();
                await PlayPasswordUnlockSequence(ct);
                return;
            }

            ClearInput(false);
            await HandleFailedConfirm(ct);
        }

        // 샵 버튼을 다섯 번 누르면 초기화 모드에 들어가고 설명서 보유 여부에 맞는 안내 대사를 재생한다.
        public async UniTask ResetInput(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (unlockSequencePlaying)
            {
                return;
            }

            SoundPlayer.PlayKeypadSfx('#');
            resetModePresses++;
            ClearInput(false);

            if (resetModePresses < ResetModePressCount)
            {
                return;
            }

            resetModePresses = 0;
            ClearInput(true);
            string dialogueId = HasInfo(DoorlockResetInfoId)
                ? resetModeDialogueId
                : ResetModeMissingManualDialogueId;
            await PlayDialogueAndWait(dialogueId, ct);
        }

        // 숫자 입력도 특수 액션의 비동기 완료 계약에 맞춰 처리한다.
        private UniTask PressDigit(char digit, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (unlockSequencePlaying)
            {
                return UniTask.CompletedTask;
            }

            SoundPlayer.PlayKeypadSfx(digit);
            resetModePresses = 0;

            if (resetModeActive && TryConsumeResetModeDigit(digit))
            {
                return UniTask.CompletedTask;
            }

            if (input.Length >= digitDisplays.Length)
            {
                return UniTask.CompletedTask;
            }

            input += digit;
            RefreshDisplays();
            return UniTask.CompletedTask;
        }

        private void ClearInput(bool resetMode)
        {
            input = string.Empty;
            resetModeActive = resetMode;
            RefreshDisplays();
        }

        // 확인 버튼 오답을 오브젝트 플래그로 누적하고 세 번째에는 초기화와 경보 대사를 적용한다.
        private async UniTask HandleFailedConfirm(CancellationToken ct)
        {
            bool failedOnce = IsObjectFlagActive(FailedOnceFlagObjectName);
            bool failedTwice = IsObjectFlagActive(FailedTwiceFlagObjectName);
            if (!failedOnce && !failedTwice)
            {
                SetFailedConfirmFlags(true, false);
                await PlayDialogueAndWait(failDialogueId, ct);
                return;
            }

            if (failedOnce && !failedTwice)
            {
                SetFailedConfirmFlags(false, true);
                await PlayDialogueAndWait(failDialogueId, ct);
                return;
            }

            ResetFailedConfirmCount();
            await PlayDialogueAndWait(failPenaltyDialogueId, ct);
        }

        // 도어락 실패 단계 플래그를 모두 초기화한다.
        private void ResetFailedConfirmCount()
        {
            SetFailedConfirmFlags(false, false);
        }

        // 성공 대사를 열림 전/후로 나누어 도어락 상태 전환 타이밍을 맞춘다.
        private async UniTask PlayPasswordUnlockSequence(CancellationToken ct)
        {
            unlockSequencePlaying = true;
            try
            {
                bool unlockedWithoutFactoryReset = !IsInitializedPasswordFlagActive();
                await PlayDialogueAndWait(passwordSuccessDialogueId, ct);
                UnlockDoorLock();
                if (unlockedWithoutFactoryReset)
                {
                    AchievementProgress.Unlock(DirectUnlockAchievementId);
                }

                await PlayDialogueAndWait(passwordSuccessPostDialogueId, ct);
            }
            finally
            {
                unlockSequencePlaying = false;
            }
        }

        // 초기화 모드에서는 설명서의 초기화번호와 맞는 prefix만 계속 입력받는다.
        private bool TryConsumeResetModeDigit(char digit)
        {
            string expectedCode = ResolveResetUnlockCode();
            string nextInput = input + digit;
            if (!expectedCode.StartsWith(nextInput, System.StringComparison.Ordinal))
            {
                ClearInput(false);
                return false;
            }

            input = nextInput;
            RefreshDisplays();
            return true;
        }

        // 초기화번호 확인이 끝나면 씬 플래그 오브젝트를 켜서 objectStates에 남긴다.
        private void ResetDoorLockCodeToDefault()
        {
            RoomController roomController = FindFirstObjectByType<RoomController>(FindObjectsInactive.Include);
            if (roomController != null && roomController.SetSceneObjectActive(InitializedPasswordFlagObjectName, true))
            {
                return;
            }

            SetObjectActive(FindSceneObject(InitializedPasswordFlagObjectName), true);
        }

        // 도어락이 풀리면 잠긴 오브젝트를 끄고 해제 표시를 켠다.
        private void UnlockDoorLock()
        {
            SetObjectActive(ResolveLockedDoorLockObject(), false);
            SetObjectActive(ResolveUnlockedDoorLockObject(), true);
            gameObject.SetActive(false);
        }

        // 현재 회차 seed에서 파생된 도어락 초기화 번호를 우선 사용한다.
        private string ResolveResetUnlockCode()
        {
            GameSession state = GameSession.Instance;
            return state != null && !string.IsNullOrWhiteSpace(state.DoorLockResetCode)
                ? state.DoorLockResetCode
                : resetUnlockCode;
        }

        // 현재 회차 seed에서 파생된 실제 도어락 비밀번호를 우선 사용한다.
        private string ResolveUnlockCode()
        {
            if (IsInitializedPasswordFlagActive())
            {
                return InitializedDoorLockCode;
            }

            GameSession state = GameSession.Instance;
            return state != null && !string.IsNullOrWhiteSpace(state.DoorLockCode)
                ? state.DoorLockCode
                : unlockCode;
        }

        private static bool IsInitializedPasswordFlagActive()
        {
            return IsObjectFlagActive(InitializedPasswordFlagObjectName);
        }

        private static bool IsObjectFlagActive(string objectName)
        {
            GameObject flagObject = FindSceneObject(objectName);
            return flagObject != null && flagObject.activeSelf;
        }

        // 실패 횟수 오브젝트를 상호 배타적으로 전환하고 objectStates에도 기록한다.
        private static void SetFailedConfirmFlags(bool failedOnce, bool failedTwice)
        {
            SetObjectFlagActive(FailedOnceFlagObjectName, failedOnce);
            SetObjectFlagActive(FailedTwiceFlagObjectName, failedTwice);
        }

        private static void SetObjectFlagActive(string objectName, bool active)
        {
            RoomController roomController = FindFirstObjectByType<RoomController>(FindObjectsInactive.Include);
            if (roomController != null && roomController.SetSceneObjectActive(objectName, active))
            {
                return;
            }

            SetObjectActive(FindSceneObject(objectName), active);
        }

        private GameObject ResolveLockedDoorLockObject()
        {
            if (lockedDoorLockObject == null)
            {
                lockedDoorLockObject = FindSceneObject(DoorLockObjectName);
            }

            return lockedDoorLockObject;
        }

        private GameObject ResolveUnlockedDoorLockObject()
        {
            if (unlockedDoorLockObject == null)
            {
                unlockedDoorLockObject = FindSceneObject(DoorLockUnlockedObjectName);
            }

            return unlockedDoorLockObject;
        }

        private static void SetObjectActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active)
            {
                target.SetActive(active);
            }
        }

        private static GameObject FindSceneObject(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
            {
                return null;
            }

            var transforms = FindObjectsByType<Transform>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform candidate = transforms[i];
                if (candidate != null &&
                    string.Equals(candidate.name, objectName, System.StringComparison.Ordinal))
                {
                    return candidate.gameObject;
                }
            }

            return null;
        }

        private void RefreshDisplays()
        {
            for (int i = 0; i < digitDisplays.Length; i++)
            {
                if (digitDisplays[i] != null)
                {
                    digitDisplays[i].SetActive(i < input.Length);
                }
            }
        }

        private bool PlayDialogue(string dialogueId)
        {
            if (DialoguePlayer.Instance == null || string.IsNullOrWhiteSpace(dialogueId))
            {
                return false;
            }

            ResolveDialogueTables();
            if (dialogueTable == null || !dialogueTable.TryGetRows(dialogueId, out IReadOnlyList<Dialogue> dialogues))
            {
                return false;
            }

            var lines = new List<DialogueLine>();
            for (int i = 0; i < dialogues.Count; i++)
            {
                Dialogue dialogue = dialogues[i];
                if (dialogue == null || IsDialogueChoice(dialogue) || !ShouldShowDialogueLine(dialogueId, dialogue))
                {
                    continue;
                }

                Speaker speaker = null;
                if (speakerTable != null && !string.IsNullOrWhiteSpace(dialogue.speaker_id))
                {
                    speakerTable.TryGet(dialogue.speaker_id, out speaker);
                }

                string speakerName = speaker != null
                    ? LocalizationService.Localized(speaker, nameof(Speaker.name), speaker.name)
                    : string.Empty;
                string text = LocalizationService.Localized(dialogue, nameof(Dialogue.text), dialogue.text);
                lines.Add(new DialogueLine(
                    dialogue.speaker_id,
                    speakerName,
                    UnescapeDialogueText(text),
                    DialoguePortraitResolver.Load(dialogue, speakerTable),
                    DialoguePortraitResolver.ResolveTint(speaker, Color.white),
                    DialoguePortraitResolver.ResolveScale(speaker, 1f),
                    dialogue.effect,
                    speaker != null ? speaker.typing_sfx : string.Empty,
                    dialogue.type,
                    dialogue.bg_path,
                    dialogue.bgm,
                    shader: dialogue.shader,
                    sourceDialogue: dialogue,
                    sourceSpeaker: speaker));
            }

            DialoguePlayer.Instance.Play(lines.ToArray());
            return lines.Count > 0;
        }

        // 대사 재생이 끝날 때까지 기다려 상태 변경과 다음 대사의 순서를 보장한다.
        private async UniTask PlayDialogueAndWait(string dialogueId, CancellationToken ct)
        {
            RoomController roomController = FindFirstObjectByType<RoomController>(FindObjectsInactive.Include);
            if (roomController != null)
            {
                // 공용 결과 대사와 탈출의지 증감 연출이 붙는 RoomController 경로를 사용한다.
                await roomController.PlayDialogueStory(dialogueId, ct);
                return;
            }

            if (!PlayDialogue(dialogueId))
            {
                return;
            }

            await UniTask.Yield(PlayerLoopTiming.Update, ct);
            await UniTask.WaitWhile(
                () => DialoguePlayer.Instance != null && DialoguePlayer.Instance.IsPlaying,
                PlayerLoopTiming.Update,
                ct);
        }

        private static bool ShouldShowDialogueLine(string dialogueId, Dialogue dialogue)
        {
            if (IsDoorlockResetCodeHintLine(dialogueId, dialogue) &&
                !HasInfo(DoorlockResetInfoId))
            {
                return false;
            }

            string flag = dialogue != null ? (dialogue.flag ?? string.Empty).Trim() : string.Empty;
            if (string.IsNullOrWhiteSpace(flag))
            {
                return true;
            }

            GameSession state = GameSession.Instance;
            if (state == null)
            {
                return false;
            }

            return state.HasDialogueFlag(dialogueId, flag);
        }

        private static bool IsDoorlockResetCodeHintLine(string dialogueId, Dialogue dialogue)
        {
            return string.Equals(dialogueId, "doorlock_reset_mode", System.StringComparison.Ordinal) &&
                dialogue != null &&
                (dialogue.text ?? string.Empty).Contains(DoorlockResetCodeToken);
        }

        private static bool HasInfo(string infoId)
        {
            GameSession state = GameSession.Instance;
            return state != null && state.Infos.Contains(infoId);
        }

        private static bool IsDialogueChoice(Dialogue dialogue)
        {
            return dialogue != null &&
                   !string.IsNullOrWhiteSpace(dialogue.type) &&
                   dialogue.type.Trim().StartsWith("SELECT_", System.StringComparison.OrdinalIgnoreCase);
        }

        private void ResolveDialogueTables()
        {
            dialogueTable ??= new TsvDataLoader<Dialogue>().LoadTable();
            speakerTable ??= new TsvDataLoader<Speaker>().LoadTable();
        }

        private static string UnescapeDialogueText(string text)
        {
            return (text ?? string.Empty)
                .Replace("\\t", "\t")
                .Replace("\\n", "\n");
        }
    }
}
