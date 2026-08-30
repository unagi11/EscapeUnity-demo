using System;
using System.Collections.Generic;
using Escape.Data;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Escape.Progress
{
    // 아이템 소유와 선택 상태를 GameSession 위에서 관리한다.
    [MovedFrom(true, "Escape.Managers", null, "ItemManager")]
    public sealed class PlayerInventory : MonoBehaviour
    {
        public static PlayerInventory Instance { get; private set; }

        public event Action Changed;

        private const string ItemResourcePath = "Data/item";
        public const string InteractItemId = "interact";
        private static readonly string[] FallbackDefaultItemIds = { "interact" };
        private static readonly string[] LegacyDefaultItemIds = { "touch", "move" };

        [SerializeField] private bool debugLogs = true;

        private readonly List<string> defaultItemIds = new();
        private GameSession state;
        private bool applyingDefaults;
        private string defaultSelectedItemId = string.Empty;
        private string equippedItemId = string.Empty;
        private bool defaultsLoaded;
        private const string LogPrefix = "[PlayerInventory]";

        public GameSession State => ResolveState();
        public IReadOnlyCollection<string> Items => ResolveState().Items;
        public string SelectedItemId => ResolveState().SelectedItemId;
        public string EquippedItemId => GetValidEquippedItemId();
        public bool IsInputLocked => ResolveState().IsInputLocked;

        // 중복 매니저를 정리하고 기본 아이템/선택 상태를 보장한다.
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            ResolveState();
            EnsureDefaults();
        }

        // 게임 상태 변경을 감시해서 항상 하나의 아이템이 선택되도록 유지한다.
        private void OnEnable()
        {
            ResolveState().Changed += HandleStateChanged;
            EnsureDefaults();
        }

        // 비활성화될 때 상태 변경 구독을 해제한다.
        private void OnDisable()
        {
            if (state != null)
            {
                state.Changed -= HandleStateChanged;
            }
        }

        // 기본 아이템을 지급하고 선택 아이템이 비지 않게 맞춘다.
        public void EnsureDefaults()
        {
            if (applyingDefaults)
            {
                return;
            }

            EnsureDefaultItemsLoaded();
            applyingDefaults = true;
            MigrateLegacyDefaultItems();
            AddDefaultItems();
            EnsureSelectedItem();
            EnsureEquippedItem();
            applyingDefaults = false;
            Changed?.Invoke();
        }

        // 지정 아이템을 보유 목록에 추가한다.
        public bool AddItem(string itemId)
        {
            bool changed = ResolveState().AddItem(itemId);
            if (changed)
            {
                Log($"AddItem {itemId}");
            }

            EnsureDefaults();
            return changed;
        }

        // 지정 아이템을 소비하고 선택 아이템을 보정한다.
        public bool ConsumeItem(string itemId)
        {
            bool changed = ResolveState().RemoveItem(itemId);
            if (changed)
            {
                Log($"ConsumeItem {itemId}");
                if (string.Equals(equippedItemId, itemId, StringComparison.Ordinal))
                {
                    equippedItemId = string.Empty;
                }
            }

            EnsureDefaults();
            return changed;
        }

        // 현재 커서처럼 사용할 아이템을 선택한다.
#if UNITY_EDITOR || DEVELOPMENT_BUILD || ESCAPE_TESTFLIGHT
        public bool DevRemoveItem(string itemId)
        {
            applyingDefaults = true;
            bool changed = ResolveState().RemoveItem(itemId);
            applyingDefaults = false;

            if (changed)
            {
                Log($"DevRemoveItem {itemId}");
                Changed?.Invoke();
            }

            return changed;
        }
#endif

        public void SelectItem(string itemId)
        {
            if (ResolveState().IsInputLocked)
            {
                Log($"SelectItem blocked. input locked. item={itemId}, reason={ResolveState().InputLockReason}");
                return;
            }

            if (string.IsNullOrWhiteSpace(itemId))
            {
                EnsureDefaults();
                return;
            }

            string normalizedItemId = itemId.Trim();
            if (!ResolveState().Items.Contains(normalizedItemId))
            {
                return;
            }

            ResolveState().SelectItem(normalizedItemId);
            if (IsEquipableItem(normalizedItemId))
            {
                equippedItemId = normalizedItemId;
            }

            Log($"SelectItem {normalizedItemId}");
            EnsureDefaults();
        }

        // 아이템 메뉴의 장착 동작에서 현재 장착 아이템을 명시적으로 바꾼다.
        public void EquipItem(string itemId)
        {
            if (ResolveState().IsInputLocked)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(itemId) ||
                !ResolveState().Items.Contains(itemId))
            {
                equippedItemId = string.Empty;
                SelectItem(defaultSelectedItemId);
                return;
            }

            if (IsDefaultInteractionItem(itemId))
            {
                equippedItemId = string.Empty;
                SelectItem(defaultSelectedItemId);
                return;
            }

            equippedItemId = itemId;
            SelectItem(itemId);
        }

        // 외부 선택 버튼에서 기본 상호작용과 현재 장착 아이템만 오간다.
        public void ToggleInteractAndEquippedItem()
        {
            if (ResolveState().IsInputLocked)
            {
                return;
            }

            string currentEquippedItemId = GetValidEquippedItemId();
            if (string.IsNullOrWhiteSpace(currentEquippedItemId))
            {
                return;
            }

            SelectItem(string.Equals(SelectedItemId, currentEquippedItemId, StringComparison.Ordinal)
                ? defaultSelectedItemId
                : currentEquippedItemId);
        }

        // 현재 보유 순서에서 다음 아이템을 선택한다.
        public void SelectNextItem()
        {
            if (ResolveState().IsInputLocked)
            {
                return;
            }

            IReadOnlyList<string> itemIds = GetOrderedItems();
            if (itemIds.Count == 0)
            {
                return;
            }

            int selectedIndex = -1;
            for (int i = 0; i < itemIds.Count; i++)
            {
                if (string.Equals(itemIds[i], SelectedItemId, StringComparison.Ordinal))
                {
                    selectedIndex = i;
                    break;
                }
            }

            SelectItem(itemIds[(selectedIndex + 1) % itemIds.Count]);
        }

        // 기본 아이템을 앞에 두고 나머지 아이템을 정렬해 UI 표시 순서를 만든다.
        public IReadOnlyList<string> GetOrderedItems()
        {
            EnsureDefaultItemsLoaded();
            var ordered = new List<string>();
            GameSession currentState = ResolveState();

            for (int i = 0; i < defaultItemIds.Count; i++)
            {
                string itemId = defaultItemIds[i];
                if (!string.IsNullOrWhiteSpace(itemId) &&
                    currentState.Items.Contains(itemId) &&
                    !ordered.Contains(itemId))
                {
                    ordered.Add(itemId);
                }
            }

            IReadOnlyList<string> acquiredItems = currentState.ItemsInAcquiredOrder;
            for (int i = 0; i < acquiredItems.Count; i++)
            {
                string itemId = acquiredItems[i];
                if (!string.IsNullOrWhiteSpace(itemId) && !ordered.Contains(itemId))
                {
                    ordered.Add(itemId);
                }
            }

            return ordered;
        }

        // GameSession가 없으면 런타임에 하나 만들어 연결한다.
        private GameSession ResolveState()
        {
            if (state != null)
            {
                return state;
            }

            state = GameSession.Instance;
            if (state == null)
            {
                var stateObject = new GameObject("GameSession");
                state = stateObject.AddComponent<GameSession>();
            }

            return state;
        }

        // item.tsv에서 start_item=true인 아이템을 TSV 순서대로 불러온다.
        private void EnsureDefaultItemsLoaded()
        {
            if (defaultsLoaded)
            {
                return;
            }

            defaultsLoaded = true;
            defaultItemIds.Clear();
            defaultSelectedItemId = string.Empty;

            TsvTable<Item> table = new TsvDataLoader<Item>().LoadTable(ItemResourcePath);
            IReadOnlyList<Item> rows = table.Rows;

            for (int i = 0; i < rows.Count; i++)
            {
                Item row = rows[i];
                if (row == null ||
                    string.IsNullOrWhiteSpace(row.id) ||
                    !IsTruthy(row.start_item))
                {
                    continue;
                }

                string itemId = row.id.Trim();
                if (!defaultItemIds.Contains(itemId))
                {
                    defaultItemIds.Add(itemId);
                }

                if (string.IsNullOrWhiteSpace(defaultSelectedItemId))
                {
                    defaultSelectedItemId = itemId;
                }
            }

            if (defaultItemIds.Count == 0)
            {
                defaultItemIds.AddRange(FallbackDefaultItemIds);
            }

            if (string.IsNullOrWhiteSpace(defaultSelectedItemId))
            {
                defaultSelectedItemId = defaultItemIds.Count > 0 ? defaultItemIds[0] : string.Empty;
            }
        }

        // 기본 상호작용 아이템인지 확인한다.
        public bool IsDefaultInteractionItem(string itemId)
        {
            return string.Equals((itemId ?? string.Empty).Trim(), InteractItemId, StringComparison.Ordinal);
        }

        // ItemPanel의 Item 버튼에 올릴 수 있는 일반 아이템인지 확인한다.
        public bool IsEquipableItem(string itemId)
        {
            return !string.IsNullOrWhiteSpace(itemId) &&
                   !IsDefaultInteractionItem(itemId);
        }

        public bool HasItem(string itemId)
        {
            return !string.IsNullOrWhiteSpace(itemId) &&
                   ResolveState().Items.Contains(itemId.Trim());
        }

        private static bool IsTruthy(string value)
        {
            string normalized = (value ?? string.Empty).Trim();
            return string.Equals(normalized, "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "1", StringComparison.Ordinal) ||
                   string.Equals(normalized, "yes", StringComparison.OrdinalIgnoreCase);
        }

        private void AddDefaultItems()
        {
            GameSession currentState = ResolveState();
            for (int i = 0; i < defaultItemIds.Count; i++)
            {
                currentState.AddItem(defaultItemIds[i]);
            }
        }

        // 통합 전 기본 상호작용 아이템을 제거해 기존 저장 데이터도 새 ID로 정규화한다.
        private void MigrateLegacyDefaultItems()
        {
            GameSession currentState = ResolveState();
            for (int i = 0; i < LegacyDefaultItemIds.Length; i++)
            {
                currentState.RemoveItem(LegacyDefaultItemIds[i]);
            }
        }

        // 선택 아이템이 없거나 사라졌으면 기본 선택 아이템으로 돌린다.
        private void EnsureSelectedItem()
        {
            GameSession currentState = ResolveState();
            if (!string.IsNullOrWhiteSpace(currentState.SelectedItemId) &&
                currentState.Items.Contains(currentState.SelectedItemId))
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(defaultSelectedItemId) &&
                currentState.Items.Contains(defaultSelectedItemId))
            {
                currentState.SelectItem(defaultSelectedItemId);
                return;
            }

            IReadOnlyList<string> orderedItems = GetOrderedItems();
            if (orderedItems.Count > 0)
            {
                currentState.SelectItem(orderedItems[0]);
            }
        }

        // 장착 아이템이 사라졌으면 비우고, 저장 복원 선택값이 있으면 장착값으로 되살린다.
        private void EnsureEquippedItem()
        {
            GameSession currentState = ResolveState();
            if (!string.IsNullOrWhiteSpace(equippedItemId) &&
                currentState.Items.Contains(equippedItemId) &&
                IsEquipableItem(equippedItemId))
            {
                return;
            }

            equippedItemId = string.Empty;
            if (!string.IsNullOrWhiteSpace(currentState.SelectedItemId) &&
                currentState.Items.Contains(currentState.SelectedItemId) &&
                IsEquipableItem(currentState.SelectedItemId))
            {
                equippedItemId = currentState.SelectedItemId;
            }
        }

        // 현재 보유 중인 장착 아이템만 반환한다.
        private string GetValidEquippedItemId()
        {
            GameSession currentState = ResolveState();
            return !string.IsNullOrWhiteSpace(equippedItemId) &&
                   currentState.Items.Contains(equippedItemId) &&
                   IsEquipableItem(equippedItemId)
                ? equippedItemId
                : string.Empty;
        }

        // 외부 변경이 들어와도 기본 아이템과 선택 상태를 유지한다.
        private void HandleStateChanged()
        {
            if (applyingDefaults)
            {
                return;
            }

            EnsureDefaults();
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
