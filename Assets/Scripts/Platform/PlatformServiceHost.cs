using System;
using Escape.Progress;
using System.Collections.Generic;
using Escape.Platform;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Escape.Platform
{
    // 타이틀 씬부터 플랫폼 서비스를 유지하며 SDK 이벤트를 게임 이벤트로 전달한다.
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    [MovedFrom(true, "Escape.Managers", null, "PlatformEventManager")]
    public sealed class PlatformServiceHost : MonoBehaviour
    {
        public static PlatformServiceHost Instance { get; private set; }

        private IPlatformService platform;

        public event Action<IPlatformService> PlatformInitialized;
        public event Action PlatformInitializationFailed;
        public event Action<string> AchievementUnlocked;

        public IPlatformService Platform => platform;
        public bool IsInitialized => platform?.IsInitialized == true;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            platform = CreatePlatformService();
            if (platform == null)
            {
                PlatformInitializationFailed?.Invoke();
                return;
            }

            platform.AchievementUnlocked += HandleAchievementUnlocked;
            platform.AchievementStateLoaded += HandleAchievementStateLoaded;
            AchievementProgress.Unlocked += HandleGameAchievementUnlocked;

            if (platform.Initialize())
            {
                PlatformInitialized?.Invoke(platform);
            }
            else
            {
                PlatformInitializationFailed?.Invoke();
            }
        }

        // 포트폴리오 데모는 외부 스토어 SDK 없이 로컬 진행 데이터만 사용한다.
        private IPlatformService CreatePlatformService()
        {
            return null;
        }

        // 게임 도전 과제 이벤트를 현재 플랫폼의 동일한 API 이름으로 전달한다.
        private void HandleGameAchievementUnlocked(string achievementApiName)
        {
            if (platform?.UnlockAchievement(achievementApiName) != true)
            {
                Debug.LogWarning($"[Platform] 도전 과제 전달 실패: {achievementApiName}");
            }
        }

        // 게임 이벤트를 현재 플랫폼의 도전 과제 해금 요청으로 전달한다.
        public bool UnlockAchievement(string achievementApiName)
        {
            return platform?.UnlockAchievement(achievementApiName) == true;
        }

        // 게임 세이브 JSON을 현재 플랫폼 저장소에 기록한다.
        public bool SaveData(string fileName, string json)
        {
            return platform?.SaveData(fileName, json) == true;
        }

        // 현재 플랫폼 저장소에서 게임 세이브 JSON을 읽는다.
        public bool TryLoadData(string fileName, out string json)
        {
            if (platform != null)
            {
                return platform.TryLoadData(fileName, out json);
            }

            json = string.Empty;
            return false;
        }

        // 현재 플랫폼 저장소에서 게임 세이브 파일을 삭제한다.
        public bool DeleteData(string fileName)
        {
            return platform?.DeleteData(fileName) == true;
        }

        // 플랫폼 SDK의 비동기 콜백을 매 프레임 처리한다.
        private void Update()
        {
            platform?.RunCallbacks();
        }

        // 플랫폼에서 완료된 도전 과제 해금을 게임 이벤트로 전달한다.
        private void HandleAchievementUnlocked(string achievementApiName)
        {
            AchievementUnlocked?.Invoke(achievementApiName);
        }

        // Steam 달성 내역을 로컬에 반영하고 로컬에만 남은 해금은 Steam으로 복구한다.
        private void HandleAchievementStateLoaded(
            IReadOnlyCollection<string> registeredAchievementIds,
            IReadOnlyCollection<string> unlockedAchievementIds)
        {
            var registeredIds = new HashSet<string>(registeredAchievementIds, StringComparer.Ordinal);
            var steamUnlockedIds = new HashSet<string>(unlockedAchievementIds, StringComparer.Ordinal);
            int importedCount = AchievementProgress.SynchronizeUnlocked(unlockedAchievementIds);
            IReadOnlyList<string> localUnlockedIds = AchievementProgress.GetUnlockedIds();
            int exportedCount = 0;

            for (int i = 0; i < localUnlockedIds.Count; i++)
            {
                string achievementId = localUnlockedIds[i];
                if (!registeredIds.Contains(achievementId))
                {
                    Debug.LogWarning($"[Steam] 등록되지 않은 로컬 도전 과제: {achievementId}");
                    continue;
                }

                if (!steamUnlockedIds.Contains(achievementId) && platform.UnlockAchievement(achievementId))
                {
                    exportedCount++;
                }
            }

            Debug.Log($"[Platform] 도전 과제 상태 동기화 (Steam→로컬: {importedCount}, 로컬→Steam: {exportedCount})");
        }

        // 애플리케이션 종료 시 플랫폼 SDK 자원을 먼저 정리한다.
        private void OnApplicationQuit()
        {
            ShutdownPlatform();
        }

        // 매니저가 제거될 때 이벤트 구독과 플랫폼 연결을 정리한다.
        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            ShutdownPlatform();
            Instance = null;
        }

        // 중복 호출되어도 안전하게 플랫폼 서비스를 종료한다.
        private void ShutdownPlatform()
        {
            if (platform == null)
            {
                return;
            }

            platform.AchievementUnlocked -= HandleAchievementUnlocked;
            platform.AchievementStateLoaded -= HandleAchievementStateLoaded;
            AchievementProgress.Unlocked -= HandleGameAchievementUnlocked;
            platform.Shutdown();
            platform = null;
        }
    }
}
