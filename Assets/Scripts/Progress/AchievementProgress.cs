using System;
using System.Collections.Generic;
using Escape.Data;
using UnityEngine;

namespace Escape.Progress
{
    // 도전과제/엔딩 수집 상태를 기기 단위 PlayerPrefs로 저장한다.
    public static class AchievementProgress
    {
        private const string PlayerPrefsKeyPrefix = "escape.achievement.";
        private const string ProgressPlayerPrefsKeyPrefix = "escape.achievement.progress.";
        private const string AchievementResourcePath = "Data/achievement";
        private const string AllEndingsAchievementId = "all_endings";
        private const string AllAchievementsAchievementId = "all_achievements";
        private const string EndingAchievementPrefix = "ending_";

        public static event Action Changed;
        public static event Action<string> Unlocked;

        private static TsvTable<Achievement> achievementTable;
        private static bool isUpdatingAggregateAchievements;

        // 지정 수집요소가 이미 달성되었는지 반환한다.
        public static bool IsUnlocked(string achievementId)
        {
            string normalizedId = NormalizeId(achievementId);
            return !string.IsNullOrEmpty(normalizedId) &&
                PlayerPrefs.GetInt(GetPlayerPrefsKey(normalizedId), 0) == 1;
        }

        // 지정 수집요소를 달성 처리하고 새로 달성했는지 반환한다.
        public static bool Unlock(string achievementId)
        {
            string normalizedId = NormalizeId(achievementId);
            if (string.IsNullOrEmpty(normalizedId))
            {
                return false;
            }

            List<string> unlockedIds = new();
            UnlockDirect(normalizedId, unlockedIds);
            TryUnlockAggregateAchievements(unlockedIds);
            if (unlockedIds.Count > 0)
            {
                PlayerPrefs.Save();
                for (int i = 0; i < unlockedIds.Count; i++)
                {
                    Unlocked?.Invoke(unlockedIds[i]);
                }

                Changed?.Invoke();
            }

            return unlockedIds.Count > 0;
        }

        // 여러 종류의 조건을 기기 단위로 누적하고 필요한 조건이 모두 모이면 달성 처리한다.
        public static bool RecordProgressFlag(string achievementId, int progressFlag, int requiredMask)
        {
            string normalizedId = NormalizeId(achievementId);
            if (string.IsNullOrEmpty(normalizedId) ||
                progressFlag <= 0 ||
                requiredMask <= 0 ||
                IsUnlocked(normalizedId))
            {
                return false;
            }

            string progressKey = GetProgressPlayerPrefsKey(normalizedId);
            int progressMask = PlayerPrefs.GetInt(progressKey, 0) | progressFlag;
            if ((progressMask & requiredMask) == requiredMask)
            {
                PlayerPrefs.DeleteKey(progressKey);
                return Unlock(normalizedId);
            }

            PlayerPrefs.SetInt(progressKey, progressMask);
            PlayerPrefs.Save();
            return false;
        }

        // 플랫폼에서 받아온 달성 상태를 로컬 수집 상태에 병합한다.
        public static int SynchronizeUnlocked(IEnumerable<string> achievementIds)
        {
            if (achievementIds == null)
            {
                return 0;
            }

            var validIds = new HashSet<string>(GetAchievementIds(), StringComparer.Ordinal);
            var synchronizedIds = new List<string>();
            foreach (string achievementId in achievementIds)
            {
                string normalizedId = NormalizeId(achievementId);
                if (validIds.Contains(normalizedId))
                {
                    UnlockDirect(normalizedId, synchronizedIds);
                }
            }

            TryUnlockAggregateAchievements(synchronizedIds);
            if (synchronizedIds.Count > 0)
            {
                PlayerPrefs.Save();
                Changed?.Invoke();
            }

            return synchronizedIds.Count;
        }

        // 플랫폼과 대조할 전체 도전 과제 API 이름을 반환한다.
        public static IReadOnlyList<string> GetAchievementIds()
        {
            IReadOnlyList<Achievement> achievements = LoadAchievements();
            var achievementIds = new List<string>(achievements.Count);
            for (int i = 0; i < achievements.Count; i++)
            {
                string achievementId = NormalizeId(achievements[i]?.id);
                if (!string.IsNullOrEmpty(achievementId))
                {
                    achievementIds.Add(achievementId);
                }
            }

            return achievementIds;
        }

        // 로컬에서 달성된 도전 과제 API 이름을 반환한다.
        public static IReadOnlyList<string> GetUnlockedIds()
        {
            IReadOnlyList<string> achievementIds = GetAchievementIds();
            var unlockedIds = new List<string>();
            for (int i = 0; i < achievementIds.Count; i++)
            {
                if (IsUnlocked(achievementIds[i]))
                {
                    unlockedIds.Add(achievementIds[i]);
                }
            }

            return unlockedIds;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD || ESCAPE_TESTFLIGHT
        // 개발 중 특정 수집요소의 달성 상태를 되돌린다.
        public static bool DevLock(string achievementId)
        {
            string normalizedId = NormalizeId(achievementId);
            if (string.IsNullOrEmpty(normalizedId))
            {
                return false;
            }

            string achievementKey = GetPlayerPrefsKey(normalizedId);
            string progressKey = GetProgressPlayerPrefsKey(normalizedId);
            if (!PlayerPrefs.HasKey(achievementKey) && !PlayerPrefs.HasKey(progressKey))
            {
                return false;
            }

            PlayerPrefs.DeleteKey(achievementKey);
            PlayerPrefs.DeleteKey(progressKey);
            PlayerPrefs.Save();
            Changed?.Invoke();
            return true;
        }
#endif

        private static string NormalizeId(string achievementId)
        {
            return string.IsNullOrWhiteSpace(achievementId)
                ? string.Empty
                : achievementId.Trim();
        }

        private static bool UnlockDirect(string normalizedId, List<string> unlockedIds)
        {
            if (string.IsNullOrEmpty(normalizedId) || IsUnlocked(normalizedId))
            {
                return false;
            }

            PlayerPrefs.SetInt(GetPlayerPrefsKey(normalizedId), 1);
            unlockedIds?.Add(normalizedId);
            return true;
        }

        private static bool TryUnlockAggregateAchievements(List<string> unlockedIds)
        {
            if (isUpdatingAggregateAchievements)
            {
                return false;
            }

            isUpdatingAggregateAchievements = true;
            try
            {
                bool changed = false;
                if (ShouldUnlockAllEndings())
                {
                    changed |= UnlockDirect(AllEndingsAchievementId, unlockedIds);
                }

                if (ShouldUnlockAllAchievements())
                {
                    changed |= UnlockDirect(AllAchievementsAchievementId, unlockedIds);
                }

                return changed;
            }
            finally
            {
                isUpdatingAggregateAchievements = false;
            }
        }

        private static bool ShouldUnlockAllEndings()
        {
            if (IsUnlocked(AllEndingsAchievementId))
            {
                return false;
            }

            IReadOnlyList<Achievement> achievements = LoadAchievements();
            bool hasEnding = false;
            for (int i = 0; i < achievements.Count; i++)
            {
                string id = NormalizeId(achievements[i]?.id);
                if (!id.StartsWith(EndingAchievementPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                hasEnding = true;
                if (!IsUnlocked(id))
                {
                    return false;
                }
            }

            return hasEnding;
        }

        private static bool ShouldUnlockAllAchievements()
        {
            if (IsUnlocked(AllAchievementsAchievementId))
            {
                return false;
            }

            IReadOnlyList<Achievement> achievements = LoadAchievements();
            bool hasAchievement = false;
            for (int i = 0; i < achievements.Count; i++)
            {
                string id = NormalizeId(achievements[i]?.id);
                if (string.IsNullOrEmpty(id) || id == AllAchievementsAchievementId)
                {
                    continue;
                }

                hasAchievement = true;
                if (!IsUnlocked(id))
                {
                    return false;
                }
            }

            return hasAchievement;
        }

        private static IReadOnlyList<Achievement> LoadAchievements()
        {
            achievementTable ??= new TsvDataLoader<Achievement>().LoadTable(AchievementResourcePath);
            return achievementTable?.Rows ?? Array.Empty<Achievement>();
        }

        private static string GetPlayerPrefsKey(string achievementId)
        {
            return $"{PlayerPrefsKeyPrefix}{achievementId}";
        }

        private static string GetProgressPlayerPrefsKey(string achievementId)
        {
            return $"{ProgressPlayerPrefsKeyPrefix}{achievementId}";
        }
    }
}
