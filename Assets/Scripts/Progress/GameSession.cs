using System;
using Escape.Localization;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Escape.Progress
{
    // 게임 전체 진행 상태를 보관하고 변경 이벤트를 알린다.
    [MovedFrom(true, "Escape.Managers", null, "GameManager")]
    public sealed class GameSession : MonoBehaviour
    {
        public const string DefaultPlayerName = "나";
        public const string DefaultPlayerNameTid = "player_name_default";
        public const string HeroSpeakerId = "hero";

        public static GameSession Instance { get; private set; }

        public event Action Changed;

        [SerializeField, Min(1)] private int maxHealth = 3;

        public string SelectedItemId { get; private set; } = string.Empty;
        public bool IsInputLocked { get; private set; }
        public string InputLockReason { get; private set; } = string.Empty;
        public int MaxHealth => maxHealth;
        public int CurrentHealth { get; private set; }
        public float HealthNormalized => maxHealth > 0 ? CurrentHealth / (float)maxHealth : 0f;
        public int RunSeed { get; private set; }
        public int TutorialSeenMask { get; private set; }
        public string PlayerName { get; private set; } = string.Empty;
        public string ChainLockCode => GetPuzzleCode("chainlock", false);
        public string ChainLockDialOrder => GetPuzzleOrder("chainlock_order", 4);
        public string DoorLockCode => GetPuzzleCode("doorlock", false);
        public string DoorLockResetCode => GetDistinctDoorLockResetCode();
        public string YeonBirthdayCode => GetPuzzleBirthday().code;
        public string YeonBirthdayMonthDay => GetPuzzleBirthday().monthDay;
        public readonly HashSet<string> Items = new(StringComparer.Ordinal);
        public readonly HashSet<string> Infos = new(StringComparer.Ordinal);
        public readonly HashSet<string> Inspected = new(StringComparer.Ordinal);
        public readonly HashSet<string> DialogueFlags = new(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> roomObjectVisibility = new(StringComparer.Ordinal);
        private readonly List<string> itemsInAcquiredOrder = new();

        public IReadOnlyDictionary<string, bool> RoomObjectVisibility => roomObjectVisibility;
        public IReadOnlyList<string> ItemsInAcquiredOrder
        {
            get
            {
                SyncItemOrder();
                return itemsInAcquiredOrder;
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            maxHealth = Mathf.Max(1, maxHealth);
            CurrentHealth = maxHealth;
            EnsureRunSeed();
            DontDestroyOnLoad(gameObject);
        }

        // 현재 회차에 저장된 주인공 표시명을 반환한다.
        public static string GetPlayerNameOrDefault()
        {
            return NormalizePlayerName(Instance != null
                ? Instance.PlayerName
                : string.Empty);
        }

        // 현재 언어의 기본 주인공 이름을 TID에서 반환한다.
        public static string GetDefaultPlayerName()
        {
            return LocalizationService.Text(
                DefaultPlayerNameTid,
                DefaultPlayerName,
                applyRuntimeTokens: false);
        }

        // 새 게임/세이브 로드에서 주인공 표시명을 갱신한다.
        public void SetPlayerName(string playerName)
        {
            string nextName = NormalizePlayerName(playerName);
            if (string.Equals(PlayerName, nextName, StringComparison.Ordinal))
            {
                return;
            }

            PlayerName = nextName;
            Changed?.Invoke();
        }

        // 피해를 적용하고 체력이 모두 소진됐는지 반환한다.
        public bool ApplyDamage(int amount)
        {
            if (amount <= 0 || CurrentHealth <= 0)
            {
                return CurrentHealth <= 0;
            }

            CurrentHealth = Mathf.Max(0, CurrentHealth - amount);
            Changed?.Invoke();
            return CurrentHealth <= 0;
        }

        // 체력을 최대치 이내에서 회복하고 실제 변경 여부를 반환한다.
        public bool Heal(int amount)
        {
            if (amount <= 0 || CurrentHealth >= maxHealth)
            {
                return false;
            }

            CurrentHealth = Mathf.Min(maxHealth, CurrentHealth + amount);
            Changed?.Invoke();
            return true;
        }

        // 저장 데이터 복원 시 체력을 유효 범위 안에서 설정한다.
        public void SetHealth(int health)
        {
            int nextHealth = Mathf.Clamp(health, 0, maxHealth);
            if (CurrentHealth == nextHealth)
            {
                return;
            }

            CurrentHealth = nextHealth;
            Changed?.Invoke();
        }

        // 오브젝트를 조사 완료 상태로 기록한다.
        public bool MarkInspected(string objectId)
        {
            if (string.IsNullOrWhiteSpace(objectId))
            {
                return false;
            }

            var added = Inspected.Add(objectId);
            if (added)
            {
                Changed?.Invoke();
            }

            return added;
        }

        // 아이템을 보유 목록에 추가한다.
        public bool AddItem(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return false;
            }

            string normalizedItemId = itemId.Trim();
            var added = Items.Add(normalizedItemId);
            if (added)
            {
                itemsInAcquiredOrder.Add(normalizedItemId);
                Changed?.Invoke();
            }

            return added;
        }

        // 아이템을 보유 목록에서 제거한다.
        public bool RemoveItem(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                return false;
            }

            string normalizedItemId = itemId.Trim();
            var removed = Items.Remove(normalizedItemId);
            if (removed)
            {
                itemsInAcquiredOrder.RemoveAll(ownedItemId =>
                    string.Equals(ownedItemId, normalizedItemId, StringComparison.Ordinal));

                if (string.Equals(SelectedItemId, normalizedItemId, StringComparison.Ordinal))
                {
                    SelectedItemId = string.Empty;
                }

                Changed?.Invoke();
            }

            return removed;
        }

        // 정보를 보유 목록에 추가한다.
        public bool AddInfo(string infoId)
        {
            if (string.IsNullOrWhiteSpace(infoId))
            {
                return false;
            }

            var added = Infos.Add(infoId);
            if (added)
            {
                Changed?.Invoke();
            }

            return added;
        }

        // 정보를 보유 목록에서 제거한다.
        public bool RemoveInfo(string infoId)
        {
            if (string.IsNullOrWhiteSpace(infoId))
            {
                return false;
            }

            var removed = Infos.Remove(infoId);
            if (removed)
            {
                Changed?.Invoke();
            }

            return removed;
        }

        // 현재 커서처럼 사용할 아이템을 선택한다.
        public void SelectItem(string itemId)
        {
            if (!string.IsNullOrWhiteSpace(itemId) && !Items.Contains(itemId))
            {
                return;
            }

            string nextItemId = itemId ?? string.Empty;
            if (string.Equals(SelectedItemId, nextItemId, StringComparison.Ordinal))
            {
                return;
            }

            SelectedItemId = nextItemId;
            Changed?.Invoke();
        }

        // 선택 아이템을 비운다.
        public void ClearSelectedItem()
        {
            SelectItem(string.Empty);
        }

        public void SetInputLocked(bool locked, string reason = "")
        {
            reason ??= string.Empty;
            if (IsInputLocked == locked && string.Equals(InputLockReason, reason, StringComparison.Ordinal))
            {
                return;
            }

            IsInputLocked = locked;
            InputLockReason = locked ? reason : string.Empty;
            Changed?.Invoke();
        }

        // 대화 선택 결과처럼 TSV 문자열로 관리하는 플래그를 추가한다.
        public bool AddDialogueFlag(string dialogueId, string flag)
        {
            string key = BuildDialogueFlagKey(dialogueId, flag);
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            var added = DialogueFlags.Add(key);
            if (added)
            {
                Changed?.Invoke();
            }

            return added;
        }

        // 특정 대화 그룹에서 획득한 문자열 플래그인지 확인한다.
        public bool HasDialogueFlag(string dialogueId, string flag)
        {
            string key = BuildDialogueFlagKey(dialogueId, flag);
            return !string.IsNullOrWhiteSpace(key) && DialogueFlags.Contains(key);
        }

        // 특정 대화 그룹에서 획득한 문자열 플래그 하나를 지운다.
        public bool RemoveDialogueFlag(string dialogueId, string flag)
        {
            string key = BuildDialogueFlagKey(dialogueId, flag);
            if (string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            bool removed = DialogueFlags.Remove(key);
            if (removed)
            {
                Changed?.Invoke();
            }

            return removed;
        }

        // 특정 대화 그룹에서 획득한 문자열 플래그를 모두 지운다.
        public bool ClearDialogueFlags(string dialogueId)
        {
            dialogueId = (dialogueId ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(dialogueId) || DialogueFlags.Count == 0)
            {
                return false;
            }

            string keyPrefix = $"{dialogueId}:";
            var targets = new List<string>();
            foreach (string key in DialogueFlags)
            {
                if (!string.IsNullOrWhiteSpace(key) &&
                    key.StartsWith(keyPrefix, StringComparison.Ordinal))
                {
                    targets.Add(key);
                }
            }

            for (int i = 0; i < targets.Count; i++)
            {
                DialogueFlags.Remove(targets[i]);
            }

            if (targets.Count > 0)
            {
                Changed?.Invoke();
                return true;
            }

            return false;
        }

        public static string BuildDialogueFlagKey(string dialogueId, string flag)
        {
            dialogueId = (dialogueId ?? string.Empty).Trim();
            flag = (flag ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(dialogueId) || string.IsNullOrWhiteSpace(flag)
                ? string.Empty
                : $"{dialogueId}:{flag}";
        }

        // 저장 복원 시 회차 seed를 되살리고 퍼즐 번호를 같은 값으로 유지한다.
        public void SetRunSeed(int runSeed)
        {
            int nextSeed = runSeed != 0 ? runSeed : GenerateRunSeed();
            if (RunSeed == nextSeed)
            {
                return;
            }

            RunSeed = nextSeed;
            Changed?.Invoke();
        }

        // Room 하위 GameObject 하나의 activeSelf 상태를 경로별로 기록한다.
        public bool SetRoomObjectVisible(string objectPath, bool visible)
        {
            objectPath = (objectPath ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(objectPath) ||
                (roomObjectVisibility.TryGetValue(objectPath, out bool current) && current == visible))
            {
                return false;
            }

            roomObjectVisibility[objectPath] = visible;
            Changed?.Invoke();
            return true;
        }

        // 저장 복원이나 씬 초기화 시 Room visible 상태 전체를 한 번에 교체한다.
        public bool SetRoomObjectVisibility(IReadOnlyDictionary<string, bool> visibility)
        {
            bool changed = roomObjectVisibility.Count != (visibility?.Count ?? 0);
            if (!changed && visibility != null)
            {
                foreach (KeyValuePair<string, bool> pair in visibility)
                {
                    if (!roomObjectVisibility.TryGetValue(pair.Key, out bool current) || current != pair.Value)
                    {
                        changed = true;
                        break;
                    }
                }
            }

            if (!changed)
            {
                return false;
            }

            roomObjectVisibility.Clear();
            if (visibility != null)
            {
                foreach (KeyValuePair<string, bool> pair in visibility)
                {
                    if (!string.IsNullOrWhiteSpace(pair.Key))
                    {
                        roomObjectVisibility[pair.Key] = pair.Value;
                    }
                }
            }

            Changed?.Invoke();
            return true;
        }

        // 게임 진행 상태를 처음 값으로 되돌린다.
        public void ResetState()
        {
            SelectedItemId = string.Empty;
            PlayerName = string.Empty;
            IsInputLocked = false;
            InputLockReason = string.Empty;
            CurrentHealth = maxHealth;
            Items.Clear();
            itemsInAcquiredOrder.Clear();
            Infos.Clear();
            Inspected.Clear();
            DialogueFlags.Clear();
            roomObjectVisibility.Clear();
            TutorialSeenMask = 0;
            RunSeed = GenerateRunSeed();
            Changed?.Invoke();
        }

        // 현재 플레이 진행에서 지정 튜토리얼을 이미 봤는지 확인한다.
        public bool HasSeenTutorial(int tutorialIndex)
        {
            return tutorialIndex is >= 0 and < 31 &&
                (TutorialSeenMask & (1 << tutorialIndex)) != 0;
        }

        // 현재 플레이 진행에 지정 튜토리얼의 열람 상태를 기록한다.
        public void MarkTutorialSeen(int tutorialIndex)
        {
            if (tutorialIndex is < 0 or >= 31)
            {
                return;
            }

            TutorialSeenMask |= 1 << tutorialIndex;
        }

        // 저장 데이터에서 튜토리얼 열람 상태를 복원한다.
        public void RestoreTutorialSeenMask(int seenMask)
        {
            TutorialSeenMask = Mathf.Max(0, seenMask);
        }

        // 표시명에 들어가면 안 되는 제어 문자를 제거하고 빈 값은 기본 이름으로 바꾼다.
        public static string NormalizePlayerName(string playerName)
        {
            playerName = (playerName ?? string.Empty)
                .Replace('\n', ' ')
                .Replace('\r', ' ')
                .Replace('\t', ' ')
                .Trim();

            return string.IsNullOrWhiteSpace(playerName) ? GetDefaultPlayerName() : playerName;
        }

        // seed가 없는 직접 씬 실행이나 구버전 세이브를 위한 안전장치.
        private void EnsureRunSeed()
        {
            if (RunSeed == 0)
            {
                RunSeed = GenerateRunSeed();
            }
        }

        // UnityEngine.Random 전역 상태를 건드리지 않고 회차 seed를 만든다.
        private static int GenerateRunSeed()
        {
            int seed = Guid.NewGuid().GetHashCode();
            return seed != 0 ? seed : 1;
        }

        // 같은 회차 seed와 salt에서 항상 같은 네 자리 퍼즐 번호를 만든다.
        private string GetPuzzleCode(string salt, bool allowLeadingZero)
        {
            EnsureRunSeed();
            int mixedSeed = MixSeed(RunSeed, salt);
            var random = new System.Random(mixedSeed);
            int min = allowLeadingZero ? 0 : 1000;
            return random.Next(min, 10000).ToString("D4");
        }

        // 같은 회차 seed와 salt에서 1부터 count까지의 순서를 항상 같은 방식으로 섞는다.
        private string GetPuzzleOrder(string salt, int count)
        {
            EnsureRunSeed();
            count = Mathf.Clamp(count, 1, 9);
            var order = new char[count];
            for (int i = 0; i < order.Length; i++)
            {
                order[i] = (char)('1' + i);
            }

            var random = new System.Random(MixSeed(RunSeed, salt));
            for (int i = order.Length - 1; i > 0; i--)
            {
                int swapIndex = random.Next(i + 1);
                (order[i], order[swapIndex]) = (order[swapIndex], order[i]);
            }

            return new string(order);
        }

        // 같은 회차 seed에서 MDD 또는 MMD가 세 자리가 되는 유효한 연이 생일을 만든다.
        private (string code, string monthDay) GetPuzzleBirthday()
        {
            EnsureRunSeed();
            var random = new System.Random(MixSeed(RunSeed, "yeon_birthday"));
            int eligibleDateCount = 0;
            for (int month = 1; month <= 12; month++)
            {
                eligibleDateCount += month < 10
                    ? DateTime.DaysInMonth(1990, month)
                    : 9;
            }

            int dateIndex = random.Next(eligibleDateCount);
            for (int month = 1; month <= 12; month++)
            {
                int eligibleDayCount = month < 10
                    ? DateTime.DaysInMonth(1990, month)
                    : 9;
                if (dateIndex >= eligibleDayCount)
                {
                    dateIndex -= eligibleDayCount;
                    continue;
                }

                int day = dateIndex + 1;
                string code = month < 10
                    ? $"{month}{day:D2}"
                    : $"{month}{day}";
                return (code, $"{month:D2}{day:D2}");
            }

            return ("901", "0901");
        }

        private string GetDistinctDoorLockResetCode()
        {
            string code = GetPuzzleCode("doorlock_reset", false);
            if (!string.Equals(code, DoorLockCode, StringComparison.Ordinal))
            {
                return code;
            }

            int nextCode = (int.Parse(code) - 1000 + 1) % 9000 + 1000;
            return nextCode.ToString("D4");
        }

        private void SyncItemOrder()
        {
            itemsInAcquiredOrder.RemoveAll(itemId =>
                string.IsNullOrWhiteSpace(itemId) || !Items.Contains(itemId));

            foreach (string itemId in Items)
            {
                if (!string.IsNullOrWhiteSpace(itemId) && !itemsInAcquiredOrder.Contains(itemId))
                {
                    itemsInAcquiredOrder.Add(itemId);
                }
            }
        }

        private static int MixSeed(int seed, string salt)
        {
            unchecked
            {
                int hash = seed;
                string value = salt ?? string.Empty;
                for (int i = 0; i < value.Length; i++)
                {
                    hash = (hash * 397) ^ value[i];
                }

                return hash == 0 ? 1 : hash;
            }
        }
    }
}
