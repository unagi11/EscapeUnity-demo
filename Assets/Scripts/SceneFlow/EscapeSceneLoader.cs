using System.Threading;
using Escape.Progress;
using Cysharp.Threading.Tasks;
using Escape.Audio;
using Escape.Rooms;
using Escape.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Escape.SceneFlow
{
    // 프로젝트의 씬 이름 검증과 일회성 로드 인자 설정을 한곳에서 처리한다.
    public static class EscapeSceneLoader
    {
        public const string TitleSceneName = "0_TitleScene";
        public const string RoomSceneName = "1_RoomScene";
        public const string SpaceShooterSceneName = "2_SpaceShooterScene";
        public const string RythmRecycleSceneName = "3_RythmRecycleScene";
        public const string LockPickSceneName = "4_LockPickScene";

        // 지정한 씬을 단일 모드로 로드하고 다음 1_RoomScene의 인트로 재생 여부를 전달한다.
        // showLoading이 true면 로딩 오버레이가 있는 씬에서 오버레이를 띄운 뒤 로드한다.
        public static bool Load(
            string sceneName,
            bool playIntro = false,
            bool showLoading = true,
            bool playStartSplash = false)
        {
            sceneName = (sceneName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                Debug.LogError("[SceneLoader] Scene name is empty.");
                return false;
            }

            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogError($"[SceneLoader] Scene is not included in build settings: {sceneName}");
                return false;
            }

            SceneLoadArgs.PlayIntro = playIntro;
            SceneLoadArgs.PlayStartSplash = playStartSplash || playIntro;
            Debug.Log(
                $"[SceneLoader] Load scene={sceneName}, playIntro={playIntro}, " +
                $"playStartSplash={SceneLoadArgs.PlayStartSplash}, showLoading={showLoading}");
            if (showLoading)
            {
                SceneTransitionFadeUI loadingUI = UnityEngine.Object.FindFirstObjectByType<SceneTransitionFadeUI>();
                if (loadingUI != null && loadingUI.TryShowLoadingThenLoad(sceneName))
                {
                    return true;
                }
            }

            LoadPreparedScene(sceneName);
            return true;
        }

        // 오버레이가 없는 호출도 비동기 로더를 사용해 동기 씬 로드 정지를 피한다.
        internal static void LoadPreparedScene(string sceneName)
        {
            LoadPreparedSceneAsync(sceneName).Forget();
        }

        // 씬 데이터를 백그라운드에서 준비하고 한 프레임 경계에서 활성화한다.
        internal static async UniTask LoadPreparedSceneAsync(
            string sceneName,
            CancellationToken cancellationToken = default)
        {
            AsyncOperation loadOperation = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
            if (loadOperation == null)
            {
                Debug.LogError($"[SceneLoader] Failed to start async scene load: {sceneName}");
                return;
            }

            loadOperation.allowSceneActivation = false;
            try
            {
                while (loadOperation.progress < 0.9f)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                }

                // 로딩 화면의 마지막 상태가 제출된 뒤 무거운 씬 활성화를 시작한다.
                await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate, cancellationToken);
                loadOperation.allowSceneActivation = true;

                // 활성화가 시작되면 기존 씬 파괴로 토큰이 취소되므로 완료까지는 토큰 없이 기다린다.
                while (!loadOperation.isDone)
                {
                    await UniTask.Yield(PlayerLoopTiming.Update);
                }
            }
            finally
            {
                // 취소 시 90%에서 영구 정지하지 않도록 활성화 잠금을 반드시 해제한다.
                if (!loadOperation.isDone)
                {
                    loadOperation.allowSceneActivation = true;
                }
            }
        }

        // 타이틀 씬으로 이동하면서 남아 있는 인트로 요청을 제거한다.
        public static bool LoadTitle(string sceneName = TitleSceneName)
        {
            return Load(sceneName, false);
        }

        // 게임 룸 씬으로 이동하면서 신규 게임 여부에 맞춰 인트로 요청을 전달한다.
        public static bool LoadRoom(
            string sceneName = RoomSceneName,
            bool playIntro = false,
            bool showLoading = true,
            bool playStartSplash = false,
            string playerName = null)
        {
            if (playerName != null)
            {
                SceneLoadArgs.PassPlayerName(playerName);
            }

            return Load(sceneName, playIntro, showLoading, playStartSplash);
        }

        // Space Shooter 씬으로 이동한다.
        public static bool LoadSpaceShooter(string sceneName = SpaceShooterSceneName)
        {
            return Load(sceneName, false);
        }

        // Space Shooter 미니게임 씬으로 단일 전환한다.
        public static bool LoadSpaceShooterMiniGame(
            RoomType returnRoom = RoomType.None,
            string sceneName = SpaceShooterSceneName)
        {
            return LoadMiniGameScene(sceneName, returnRoom);
        }

        // 리듬 분리수거 미니게임 씬으로 단일 전환한다.
        public static bool LoadRythmRecycleMiniGame(
            RoomType returnRoom = RoomType.None,
            string sceneName = RythmRecycleSceneName)
        {
            return LoadMiniGameScene(sceneName, returnRoom);
        }

        // 잠금따기 미니게임 씬으로 단일 전환한다.
        public static bool LoadLockPickMiniGame(
            RoomType returnRoom = RoomType.None,
            string sceneName = LockPickSceneName)
        {
            return LoadLockPickMiniGame(LockPickUnlockTarget.Drawer, returnRoom, sceneName);
        }

        // 잠금따기 성공 시 해제할 대상을 예약하고 미니게임 씬으로 전환한다.
        public static bool LoadLockPickMiniGame(
            LockPickUnlockTarget unlockTarget,
            RoomType returnRoom = RoomType.None,
            string sceneName = LockPickSceneName)
        {
            sceneName = (sceneName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                Debug.LogError("[SceneLoader] Mini scene name is empty.");
                return false;
            }

            if (!Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogError($"[SceneLoader] Mini scene is not included in build settings: {sceneName}");
                return false;
            }

            SceneLoadArgs.SetLockPickUnlockTarget(unlockTarget);
            return LoadMiniGameScene(sceneName, returnRoom, validateScene: false);
        }

        // 미니게임 씬에서 룸 씬으로 단일 복귀한다.
        public static bool ReturnRoomFromMiniGame()
        {
            SoundPlayer.StopStoryBgm();
            ReleaseMiniGameInputLock();
            return LoadRoom(showLoading: true);
        }

        public static void ReleaseMiniGameInputLock()
        {
            GameSession state = GameSession.Instance;
            if (state != null &&
                state.IsInputLocked &&
                state.InputLockReason == SceneLoadArgs.MiniGameInputLockReason)
            {
                state.SetInputLocked(false);
            }
        }

        private static bool LoadMiniGameScene(
            string sceneName,
            RoomType returnRoom,
            bool validateScene = true)
        {
            sceneName = (sceneName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(sceneName))
            {
                Debug.LogError("[SceneLoader] Mini scene name is empty.");
                return false;
            }

            if (validateScene && !Application.CanStreamedLevelBeLoaded(sceneName))
            {
                Debug.LogError($"[SceneLoader] Mini scene is not included in build settings: {sceneName}");
                return false;
            }

            SceneLoadArgs.SetMiniGameReturnRoom(returnRoom);
            GameSession.Instance?.SetInputLocked(true, SceneLoadArgs.MiniGameInputLockReason);
            return Load(sceneName);
        }
    }
}
