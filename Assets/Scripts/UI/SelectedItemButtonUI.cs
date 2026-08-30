using System;
using Escape.Localization;
using Escape.Progress;
using System.Collections.Generic;
using Escape.Data;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Escape.UI
{
    [RequireComponent(typeof(Button))]
    public sealed class SelectedItemButtonUI : MonoBehaviour
    {
        [SerializeField] private Button button;
        [SerializeField, FormerlySerializedAs("itemManager")] private PlayerInventory inventory;
        [SerializeField] private TMP_Text itemText;
        [SerializeField] private Image itemIcon;
        [SerializeField] private Image nextItemIcon;
        [SerializeField] private string iconResourcePath = "Sprites/icon";

        private readonly Dictionary<int, Sprite> iconsByNo = new();
        private TsvTable<Item> itemTable;

        private void Awake()
        {
            ResolveButton();
            ResolveViews();
            LoadItemData();
            SetNextVisible(false);
        }

        private void OnEnable()
        {
            ResolveButton();
            ResolveViews();
            LoadItemData();
            SetNextVisible(false);
            button.onClick.RemoveListener(SelectNextItem);
            button.onClick.AddListener(SelectNextItem);

            PlayerInventory currentInventory = ResolvePlayerInventory();
            if (currentInventory != null)
            {
                currentInventory.Changed += Refresh;
            }

            LocalizationService.Ensure().LanguageChanged += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            if (button != null)
            {
                button.onClick.RemoveListener(SelectNextItem);
            }

            if (inventory != null)
            {
                inventory.Changed -= Refresh;
            }

            if (LocalizationService.Instance != null)
            {
                LocalizationService.Instance.LanguageChanged -= Refresh;
            }
        }

        // 외부 선택 버튼은 현재 보유 중인 모든 아이템을 순서대로 선택한다.
        private void SelectNextItem()
        {
            PlayerInventory currentInventory = ResolvePlayerInventory();
            IReadOnlyList<string> itemIds = GetSelectableItemIds(currentInventory);
            if (currentInventory == null || itemIds.Count == 0)
            {
                return;
            }

            string selectedItemId = currentInventory.SelectedItemId;
            int selectedIndex = GetItemIndex(itemIds, selectedItemId);
            currentInventory.SelectItem(itemIds[(selectedIndex + 1) % itemIds.Count]);
        }

        // 현재 선택 아이템과 다음 순회 대상을 표시한다.
        private void Refresh()
        {
            PlayerInventory currentInventory = ResolvePlayerInventory();
            string selectedItemId = currentInventory != null ? currentInventory.SelectedItemId : string.Empty;
            IReadOnlyList<string> itemIds = GetSelectableItemIds(currentInventory);
            string displayItemId = ResolveDisplayItemId(itemIds, selectedItemId);
            Item item = GetItem(displayItemId);
            bool canSelectNext = itemIds.Count > 1;

            if (button != null)
            {
                button.interactable = itemIds.Count > 0 && (currentInventory == null || !currentInventory.IsInputLocked);
            }

            if (itemText != null)
            {
                itemText.text = item != null
                    ? LocalizationService.Localized(item, nameof(Item.name), item.name)
                    : string.Empty;
            }

            if (itemIcon != null)
            {
                Sprite icon = GetItemIcon(item);
                itemIcon.sprite = icon;
                itemIcon.enabled = icon != null;
            }

            if (nextItemIcon != null)
            {
                Sprite nextIcon = canSelectNext ? GetItemIcon(GetNextItem(itemIds, displayItemId)) : null;
                nextItemIcon.sprite = nextIcon;
                nextItemIcon.enabled = nextIcon != null;
            }

            SetNextVisible(canSelectNext);
        }

        private void ResolveButton()
        {
            button ??= GetComponent<Button>();
        }

        private void ResolveViews()
        {
            if (itemText == null)
            {
                itemText = GetComponentInChildren<TMP_Text>(true);
            }

            if (itemIcon == null)
            {
                Image[] images = GetComponentsInChildren<Image>(true);
                for (int i = 0; i < images.Length; i++)
                {
                    Image image = images[i];
                    if (image != null &&
                        image != button.targetGraphic &&
                        !string.Equals(image.name, "Key Image", StringComparison.Ordinal) &&
                        !string.Equals(image.name, "NextImage", StringComparison.Ordinal) &&
                        !string.Equals(image.name, "NextItemImage", StringComparison.Ordinal))
                    {
                        itemIcon = image;
                        break;
                    }
                }
            }

            if (nextItemIcon == null)
            {
                Transform nextItemImage = FindChildByName(transform, "NextItemImage");
                if (nextItemImage != null)
                {
                    nextItemIcon = nextItemImage.GetComponent<Image>();
                }
            }
        }

        private void LoadItemData()
        {
            itemTable ??= new TsvDataLoader<Item>().LoadTable();
            if (iconsByNo.Count > 0)
            {
                return;
            }

            Sprite[] sprites = Resources.LoadAll<Sprite>(iconResourcePath);
            for (int i = 0; i < sprites.Length; i++)
            {
                Sprite sprite = sprites[i];
                if (sprite == null || !TryGetIconIdx(sprite.name, out int iconIdx))
                {
                    continue;
                }

                iconsByNo[iconIdx] = sprite;
            }
        }

        private Item GetItem(string itemId)
        {
            if (string.IsNullOrWhiteSpace(itemId) ||
                itemTable == null ||
                !itemTable.TryGet(itemId, out Item item))
            {
                return null;
            }

            return item;
        }

        // 버튼 오른쪽에 표시할 다음 순회 대상 아이템을 구한다.
        private Item GetNextItem(IReadOnlyList<string> itemIds, string selectedItemId)
        {
            if (itemIds == null || itemIds.Count == 0)
            {
                return null;
            }

            int selectedIndex = GetItemIndex(itemIds, selectedItemId);
            return GetItem(itemIds[(selectedIndex + 1) % itemIds.Count]);
        }

        private static int GetItemIndex(IReadOnlyList<string> itemIds, string selectedItemId)
        {
            for (int i = 0; i < itemIds.Count; i++)
            {
                if (string.Equals(itemIds[i], selectedItemId, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private static string ResolveDisplayItemId(IReadOnlyList<string> itemIds, string selectedItemId)
        {
            if (itemIds == null || itemIds.Count == 0)
            {
                return string.Empty;
            }

            return GetItemIndex(itemIds, selectedItemId) >= 0 ? selectedItemId : itemIds[0];
        }

        private static IReadOnlyList<string> GetSelectableItemIds(PlayerInventory currentInventory)
        {
            if (currentInventory == null)
            {
                return Array.Empty<string>();
            }

            IReadOnlyList<string> orderedItems = currentInventory.GetOrderedItems();
            var itemIds = new List<string>();
            for (int i = 0; i < orderedItems.Count; i++)
            {
                string itemId = orderedItems[i];
                if (currentInventory.IsEquipableItem(itemId))
                {
                    itemIds.Add(itemId);
                }
            }

            return itemIds;
        }

        private Sprite GetItemIcon(Item item)
        {
            if (item == null || !int.TryParse(item.icon_idx, out int iconIdx))
            {
                return null;
            }

            iconsByNo.TryGetValue(iconIdx, out Sprite icon);
            return icon;
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

            return inventory;
        }

        private static Transform FindChildByName(Transform root, string targetName)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == targetName)
                {
                    return child;
                }
            }

            return null;
        }

        private void SetChildVisible(string childName, bool visible)
        {
            Transform child = FindChildByName(transform, childName);
            if (child != null)
            {
                child.gameObject.SetActive(visible);
            }
        }

        private void SetNextVisible(bool visible)
        {
            SetChildVisible("NextImage", visible);

            if (nextItemIcon != null)
            {
                nextItemIcon.gameObject.SetActive(visible);
            }
        }

        private static bool TryGetIconIdx(string spriteName, out int iconIdx)
        {
            const string Prefix = "icon_";
            iconIdx = 0;
            if (string.IsNullOrWhiteSpace(spriteName) ||
                !spriteName.StartsWith(Prefix, StringComparison.Ordinal) ||
                !int.TryParse(spriteName[Prefix.Length..], out iconIdx))
            {
                return false;
            }

            return true;
        }
    }
}
