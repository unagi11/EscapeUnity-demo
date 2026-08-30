using Escape.Rooms;
using Escape.Progress;

namespace Escape.SceneFlow
{
    // 리듬 분리수거 미니게임 결과를 룸 씬 복귀 뒤 한 번만 처리한다.
    public enum RythmRecycleResult
    {
        None = 0,
        Failed = 1,
        Success = 2,
        Perfect = 3,
    }

    // 해정 미니게임 성공 뒤 룸 씬에서 해제할 대상.
    public enum LockPickUnlockTarget
    {
        None = 0,
        Drawer = 1,
        UtilityDoor = 2,
        EntrancePadlock = 3,
        Handcuffs = 4,
    }

    // 씬 전환 시 다음 씬으로 넘길 일회성 인자를 담는다.
    // static이라 씬 로드에도 값이 유지되고, GameSession 존재 여부와 무관하게 쓸 수 있다.
    public static class SceneLoadArgs
    {
        // 기존 세이브 호환을 위해 저장되는 object flag 이름은 이전 값을 유지한다.
        public const string SpaceShooterClearRewardSeenObjectName = "FLAG:SPACE_SHOOTER_FIRST_RANK_REWARD_SEEN";
        public const string MiniGameInputLockReason = "mini_game";

        public static bool PlayIntro { get; set; }
        public static bool PlayStartSplash { get; set; }
        public static bool SpaceShooterClearRewardPending { get; private set; }
        public static RythmRecycleResult PendingRythmRecycleResult { get; private set; }
        public static bool LockPickUnlockPending { get; private set; }
        public static LockPickUnlockTarget PendingLockPickUnlockTarget { get; private set; }
        private static RoomType pendingMiniGameReturnRoom = RoomType.None;
        private static string pendingPlayerName = string.Empty;

        // 인트로 재생 요청을 한 번만 소비한다(읽고 즉시 소거).
        public static bool ConsumePlayIntro()
        {
            bool requested = PlayIntro;
            PlayIntro = false;
            return requested;
        }

        // 시작 스플래시 표시 요청을 한 번만 소비한다.
        public static bool ConsumePlayStartSplash()
        {
            bool requested = PlayStartSplash;
            PlayStartSplash = false;
            return requested;
        }

        // 타이틀 씬에서 입력한 새 게임 이름을 다음 룸 씬으로 한 번만 넘긴다.
        public static void PassPlayerName(string playerName)
        {
            pendingPlayerName = GameSession.NormalizePlayerName(playerName);
        }

        // 룸 씬이 새 게임 이름을 소비한다. 세이브 로드는 별도 저장 데이터 적용 경로를 따른다.
        public static bool ConsumePlayerName(out string playerName)
        {
            playerName = pendingPlayerName;
            pendingPlayerName = string.Empty;
            return !string.IsNullOrWhiteSpace(playerName);
        }

        // TV 미니게임 클리어 보상은 룸 씬 복구 뒤 한 번만 소비한다.
        public static void RequestSpaceShooterClearReward()
        {
            SpaceShooterClearRewardPending = true;
        }

        public static bool ConsumeSpaceShooterClearReward()
        {
            bool requested = SpaceShooterClearRewardPending;
            SpaceShooterClearRewardPending = false;
            return requested;
        }

        // 리듬 분리수거 결과를 룸 씬 복구 뒤 처리하도록 예약한다.
        public static void RequestRythmRecycleResult(RythmRecycleResult result)
        {
            PendingRythmRecycleResult = result;
        }

        public static bool ConsumeRythmRecycleResult(out RythmRecycleResult result)
        {
            result = PendingRythmRecycleResult;
            PendingRythmRecycleResult = RythmRecycleResult.None;
            return result != RythmRecycleResult.None;
        }

        public static void RequestLockPickUnlock()
        {
            if (PendingLockPickUnlockTarget == LockPickUnlockTarget.None)
            {
                PendingLockPickUnlockTarget = LockPickUnlockTarget.Drawer;
            }

            LockPickUnlockPending = true;
        }

        public static bool ConsumeLockPickUnlock()
        {
            return ConsumeLockPickUnlock(out _);
        }

        public static bool ConsumeLockPickUnlock(out LockPickUnlockTarget target)
        {
            bool requested = LockPickUnlockPending;
            target = PendingLockPickUnlockTarget == LockPickUnlockTarget.None
                ? LockPickUnlockTarget.Drawer
                : PendingLockPickUnlockTarget;
            LockPickUnlockPending = false;
            PendingLockPickUnlockTarget = LockPickUnlockTarget.None;
            return requested;
        }

        public static void SetLockPickUnlockTarget(LockPickUnlockTarget target)
        {
            PendingLockPickUnlockTarget = target == LockPickUnlockTarget.None
                ? LockPickUnlockTarget.Drawer
                : target;
        }

        // 미니게임에서 룸으로 돌아올 때 재진입할 방을 한 번만 넘긴다.
        public static void SetMiniGameReturnRoom(RoomType roomType)
        {
            pendingMiniGameReturnRoom = roomType;
        }

        public static bool ConsumeMiniGameReturnRoom(out RoomType roomType)
        {
            roomType = pendingMiniGameReturnRoom;
            pendingMiniGameReturnRoom = RoomType.None;
            return roomType != RoomType.None;
        }
    }

}
