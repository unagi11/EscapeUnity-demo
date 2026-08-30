using System;
using System.Collections.Generic;

namespace Escape.Platform
{
    // 게임 코드가 개별 플랫폼 SDK에 직접 의존하지 않게 하는 공통 경계다.
    public interface IPlatformService
    {
        event Action<string> AchievementUnlocked;
        event Action<IReadOnlyCollection<string>, IReadOnlyCollection<string>> AchievementStateLoaded;

        string PlatformName { get; }
        string UserName { get; }
        bool IsInitialized { get; }

        bool Initialize();
        bool UnlockAchievement(string achievementApiName);
        bool SaveData(string fileName, string json);
        bool TryLoadData(string fileName, out string json);
        bool DeleteData(string fileName);
        void RunCallbacks();
        void Shutdown();
    }
}
