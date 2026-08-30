using System;
using Escape.Localization;
using Escape.Progress;
using System.Collections.Generic;
using Escape.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Escape.UI
{
    // 하단 ItemPanel의 상호작용/아이템 빠른 선택 상태를 관리한다.
    public sealed class ItemPanelUI : MonoBehaviour
    {
        [SerializeField] private Button interactButton;
        [SerializeField] private Button flashlightButton;
        [SerializeField] private Button itemButton;
        [SerializeField] private RectTransform selectedFrame;
        [SerializeField] private GameObject nextRoot;
        [SerializeField] private Image nextItemIcon;
        [SerializeField] private TMP_Text equipedItemText;
        [SerializeField] private string iconResourcePath = "Sprites/icon";

        private readonly Dictionary<int, Sprite> iconsByNo = new();
        private PlayerInventory inventory;
        private TsvTable<Item> itemTable;
        private string panelItemId = string.Empty;
        private string nextPanelItemId = string.Empty;
        private PanelFocus currentFocus = PanelFocus.Interact;

        private enum PanelFocus
        {
            Interact,
            Item
        }

        // 씬에 배치된 버튼과 데이터 테이블을 준비한다.
        private void Awake()
        {
            itemTable = new TsvDataLoader<Item>().LoadTable();
            LoadItemIcons();
            ResolveReferences();
            DisableLegacySelectedItemButtons();
            BindControls();
            Refresh();
        }

        // 매니저 변경 이벤트를 받아 패널을 최신 상태로 유지한다.
        private void OnEnable()
        {
            ResolveReferences();
            DisableLegacySelectedItemButtons();
            BindControls();
            ResolvePlayerInventory();
            if (inventory != null)
            {
                inventory.Changed += Refresh;
                inventory.EnsureDefaults();
            }

            LocalizationService.Ensure().LanguageChanged += Refresh;
            Refresh();
        }

        // 패널이 꺼질 때 매니저와 버튼 이벤트 구독을 해제한다.
        private void OnDisable()
        {
            if (interactButton != null)
            {
                interactButton.onClick.RemoveListener(SelectInteract);
            }

            if (flashlightButton != null)
            {
                flashlightButton.onClick.RemoveListener(SelectPanelItem);
            }

            if (itemButton != null)
            {
                itemButton.onClick.RemoveListener(SelectPanelItem);
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

        // 외부에서 사용할 아이템 매니저를 연결한다.
        public void Configure(PlayerInventory nextInventory)
        {
            if (inventory == nextInventory)
            {
                Refresh();
                return;
            }

            if (inventory != null && isActiveAndEnabled)
            {
                inventory.Changed -= Refresh;
            }

            inventory = nextInventory;
            if (inventory != null && isActiveAndEnabled)
            {
                inventory.Changed += Refresh;
                inventory.EnsureDefaults();
            }

            Refresh();
        }

        // 현재 보유 아이템과 선택 모드에 맞춰 버튼 표시와 포커스 프레임을 갱신한다.
        public void Refresh()
        {
            ResolveReferences();

            if (inventory == null)
            {
                ResolvePlayerInventory();
            }

            bool canInput = inventory == null || !inventory.IsInputLocked;
            string selectedItemId = inventory != null ? inventory.SelectedItemId : PlayerInventory.InteractItemId;
            IReadOnlyList<string> panelItemIds = GetPanelItemIds();
            panelItemId = ResolvePanelItemId(panelItemIds, selectedItemId);
            nextPanelItemId = ResolveNextPanelItemId(panelItemIds, panelItemId);
            bool hasPanelItem = !string.IsNullOrWhiteSpace(panelItemId);
            bool hasNextItem = panelItemIds.Count > 1 && !string.IsNullOrWhiteSpace(nextPanelItemId);

            SetButtonVisible(interactButton, true);
            SetButtonVisible(flashlightButton, false);
            SetButtonVisible(itemButton, hasPanelItem);
            SetNextVisible(hasNextItem);

            SetButtonInteractable(interactButton, canInput);
            SetButtonInteractable(itemButton, canInput && hasPanelItem);

            UpdateButtonContent(interactButton, PlayerInventory.InteractItemId);
            UpdateButtonContent(itemButton, panelItemId);
            UpdateNextItemContent(nextPanelItemId);

            currentFocus = ResolveFocus(selectedItemId, hasPanelItem);
            MoveSelectedFrame(GetFocusedButton(currentFocus));
            UpdateEquipedItemText(selectedItemId);
        }

        private void SelectInteract()
        {
            if (inventory == null || inventory.IsInputLocked)
            {
                return;
            }

            inventory.SelectItem(PlayerInventory.InteractItemId);
        }

        private void SelectPanelItem()
        {
            if (inventory == null ||
                inventory.IsInputLocked ||
                string.IsNullOrWhiteSpace(panelItemId))
            {
                return;
            }

            string selectedItemId = inventory.SelectedItemId;
            string targetItemId = string.Equals(selectedItemId, panelItemId, StringComparison.Ordinal) &&
                                  !string.IsNullOrWhiteSpace(nextPanelItemId)
                ? nextPanelItemId
                : panelItemId;

            inventory.SelectItem(targetItemId);
        }

        private void BindControls()
        {
            if (interactButton != null)
            {
                interactButton.onClick.RemoveListener(SelectInteract);
                interactButton.onClick.AddListener(SelectInteract);
            }

            if (flashlightButton != null)
            {
                flashlightButton.onClick.RemoveListener(SelectPanelItem);
            }

            if (itemButton != null)
            {
                itemButton.onClick.RemoveListener(SelectPanelItem);
                itemButton.onClick.AddListener(SelectPanelItem);
            }
        }

        private PanelFocus ResolveFocus(string selectedItemId, bool hasPanelItem)
        {
            if (hasPanelItem && inventory != null && inventory.IsEquipableItem(selectedItemId))
            {
                return PanelFocus.Item;
            }

            return PanelFocus.Interact;
        }

        private Button GetFocusedButton(PanelFocus focus)
        {
            return focus switch
            {
                PanelFocus.Item => itemButton,
                _ => interactButton
            };
        }

        private IReadOnlyList<string> GetPanelItemIds()
        {
            if (inventory == null)
            {
                return Array.Empty<string>();
            }

            IReadOnlyList<string> orderedItems = inventory.GetOrderedItems();
            var panelItemIds = new List<string>();
            for (int i = 0; i < orderedItems.Count; i++)
            {
                string itemId = orderedItems[i];
                if (inventory.IsEquipableItem(itemId))
                {
                    panelItemIds.Add(itemId);
                }
            }

            return panelItemIds;
        }

        private string ResolvePanelItemId(IReadOnlyList<string> panelItemIds, string selectedItemId)
        {
            if (panelItemIds == null || panelItemIds.Count == 0)
            {
                return string.Empty;
            }

            if (ContainsItem(panelItemIds, selectedItemId))
            {
                return selectedItemId;
            }

            string equippedItemId = inventory != null ? inventory.EquippedItemId : string.Empty;
            if (ContainsItem(panelItemIds, equippedItemId))
            {
                return equippedItemId;
            }

            return panelItemIds[0];
        }

        private static string ResolveNextPanelItemId(IReadOnlyList<string> panelItemIds, string currentItemId)
        {
            if (panelItemIds == null || panelItemIds.Count < 2)
            {
                return string.Empty;
            }

            int currentIndex = -1;
            for (int i = 0; i < panelItemIds.Count; i++)
            {
                if (string.Equals(panelItemIds[i], currentItemId, StringComparison.Ordinal))
                {
                    currentIndex = i;
                    break;
                }
            }

            return panelItemIds[(currentIndex + 1) % panelItemIds.Count];
        }

        private static bool ContainsItem(IReadOnlyList<string> itemIds, string itemId)
        {
            if (itemIds == null || string.IsNullOrWhiteSpace(itemId))
            {
                return false;
            }

            for (int i = 0; i < itemIds.Count; i++)
            {
                if (string.Equals(itemIds[i], itemId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private void MoveSelectedFrame(Button targetButton)
        {
            if (selectedFrame == null || targetButton == null || !targetButton.gameObject.activeInHierarchy)
            {
                if (selectedFrame != null)
                {
                    selectedFrame.gameObject.SetActive(false);
                }

                return;
            }

            selectedFrame.gameObject.SetActive(true);
            selectedFrame.SetAsLastSibling();

            RectTransform target = targetButton.transform as RectTransform;
            if (target == null)
            {
                return;
            }

            selectedFrame.anchorMin = target.anchorMin;
            selectedFrame.anchorMax = target.anchorMax;
            selectedFrame.pivot = target.pivot;
            selectedFrame.anchoredPosition = target.anchoredPosition;
            selectedFrame.sizeDelta = target.sizeDelta;
            selectedFrame.localScale = Vector3.one;
        }


        private void UpdateButtonContent(Button button, string itemId)
        {
            if (button == null)
            {
                return;
            }

            Item item = GetItem(itemId);
            TMP_Text text = button.GetComponentInChildren<TMP_Text>(true);
            if (text != null && item != null)
            {
                text.text = LocalizationService.Localized(item, nameof(Item.name), item.name);
                text.raycastTarget = false;
            }

            Image icon = FindButtonIcon(button);
            if (icon == null)
            {
                return;
            }

            Sprite sprite = GetItemIcon(item);
            icon.sprite = sprite;
            icon.enabled = sprite != null;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
        }

        private void UpdateNextItemContent(string itemId)
        {
            if (nextItemIcon == null)
            {
                return;
            }

            Sprite sprite = GetItemIcon(GetItem(itemId));
            nextItemIcon.sprite = sprite;
            nextItemIcon.enabled = sprite != null;
            nextItemIcon.preserveAspect = true;
            nextItemIcon.raycastTarget = false;
        }

        private void UpdateEquipedItemText(string selectedItemId)
        {
            ResolveEquipedItemText();
            if (equipedItemText == null)
            {
                return;
            }

            string itemName = GetItemName(selectedItemId);
            equipedItemText.gameObject.SetActive(!string.IsNullOrWhiteSpace(itemName));
            equipedItemText.text = itemName;
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

        private string GetItemName(string itemId)
        {
            Item item = GetItem(itemId);
            return item != null
                ? LocalizationService.Localized(item, nameof(Item.name), item.name)
                : string.Empty;
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

        private void LoadItemIcons()
        {
            iconsByNo.Clear();
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

        private void ResolveReferences()
        {
            interactButton ??= FindChildButton("Interact Button");
            flashlightButton ??= FindChildButton("FlashLight Button");
            itemButton ??= FindChildButton("Item Button");
            selectedFrame ??= FindChildByName(transform, "SelectedFrame") as RectTransform;
            ResolveNextReferences();
            ResolveEquipedItemText();

            if (selectedFrame != null)
            {
                Image frameImage = selectedFrame.GetComponent<Image>();
                if (frameImage != null)
                {
                    frameImage.raycastTarget = false;
                }
            }
        }

        private void ResolveNextReferences()
        {
            if (itemButton == null)
            {
                return;
            }

            Transform nextBackgroundTransform = FindChildByName(itemButton.transform, "NextImage");
            if (nextRoot == null && nextBackgroundTransform != null)
            {
                nextRoot = nextBackgroundTransform.gameObject;
            }

            if (nextItemIcon == null)
            {
                Transform nextItemTransform = FindChildByName(itemButton.transform, "NextItemImage");
                if (nextItemTransform != null)
                {
                    nextItemIcon = nextItemTransform.GetComponent<Image>();
                }
            }
        }

        private void DisableLegacySelectedItemButtons()
        {
            SelectedItemButtonUI[] legacyButtons = GetComponentsInChildren<SelectedItemButtonUI>(true);
            for (int i = 0; i < legacyButtons.Length; i++)
            {
                legacyButtons[i].enabled = false;
            }
        }

        private Button FindChildButton(string childName)
        {
            Transform child = FindChildByName(transform, childName);
            return child != null ? child.GetComponent<Button>() : null;
        }

        private void ResolveEquipedItemText()
        {
            if (equipedItemText != null)
            {
                return;
            }

            Transform root = transform.root != null ? transform.root : transform;
            Transform textTransform = FindChildByName(root, "EquipedItemText");
            if (textTransform != null)
            {
                equipedItemText = textTransform.GetComponent<TMP_Text>();
            }
        }

        private void ResolvePlayerInventory()
        {
            if (inventory != null)
            {
                return;
            }

            inventory = PlayerInventory.Instance;
            if (inventory == null)
            {
                inventory = FindFirstObjectByType<PlayerInventory>(FindObjectsInactive.Include);
            }
        }

        private static void SetButtonVisible(Button button, bool visible)
        {
            if (button != null)
            {
                button.gameObject.SetActive(visible);
            }
        }

        private static void SetButtonInteractable(Button button, bool interactable)
        {
            if (button != null)
            {
                button.interactable = interactable;
            }
        }

        private void SetNextVisible(bool visible)
        {
            SetChildVisible(itemButton != null ? itemButton.transform : transform, "Next", true);

            if (nextRoot != null && !string.Equals(nextRoot.name, "Next", StringComparison.Ordinal))
            {
                nextRoot.SetActive(visible);
            }

            SetChildVisible(itemButton != null ? itemButton.transform : transform, "NextImage", visible);

            if (nextItemIcon != null)
            {
                nextItemIcon.gameObject.SetActive(visible);
            }
        }

        private static void SetChildVisible(Transform root, string childName, bool visible)
        {
            Transform child = FindChildByName(root, childName);
            if (child != null)
            {
                child.gameObject.SetActive(visible);
            }
        }

        private static Image FindButtonIcon(Button button)
        {
            Transform imageTransform = FindChildByName(button.transform, "Image");
            if (imageTransform != null && imageTransform.TryGetComponent(out Image namedImage))
            {
                return namedImage;
            }

            Image[] images = button.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                Image image = images[i];
                if (image != null &&
                    image != button.targetGraphic &&
                    !string.Equals(image.name, "Key Image", StringComparison.Ordinal) &&
                    !string.Equals(image.name, "NextImage", StringComparison.Ordinal) &&
                    !string.Equals(image.name, "NextItemImage", StringComparison.Ordinal))
                {
                    return image;
                }
            }

            return null;
        }

        private static Transform FindChildByName(Transform root, string targetName)
        {
            if (root == null)
            {
                return null;
            }

            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (string.Equals(child.name, targetName, StringComparison.Ordinal))
                {
                    return child;
                }
            }

            return null;
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
