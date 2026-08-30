using System;
using Escape.Localization;
using Escape.Dialogues;
using Escape.Progress;
using System.Collections.Generic;
using Escape.Data;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Escape.UI
{
    public sealed class InventoryPopupUI : PopupUIBase
    {
        private const string ItemSlotPrefabPath = "Prefabs/ItemSlotButton";
        private const string ToastAssembleSelectTwoOrMoreTid = "inventory_toast_assemble_select_two_or_more";
        private const string ToastAssembleFailedTid = "inventory_toast_assemble_failed";
        private const string ToastItemAcquiredTid = "inventory_toast_item_acquired";
        private const string ToastDisassembleSelectItemTid = "inventory_toast_disassemble_select_item";
        private const string ToastDisassembleSelectOneTid = "inventory_toast_disassemble_select_one";
        private const string ToastDisassembleFailedTid = "inventory_toast_disassemble_failed";
        private const string ToastDisassembleSuccessTid = "inventory_toast_disassemble_success";
        private const string ItemLostDialogueId = "item_lost";
        private const string ItemDisassembledDialogueId = "item_disassembled";
        private const string EmptyInventoryFallbackText = "\uD68D\uB4DD\uD55C \uC544\uC774\uD15C\uC774 \uC5C6\uC2B5\uB2C8\uB2E4.";
        private const string RuntimeMenuName = "MenuPopup";
        private static readonly Vector2 MenuSlotOffset = new(0f, -4f);

        [Header("Popup")]
        [SerializeField] private GameObject root;
        [SerializeField] private Button openButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private Button cancelButton;
        [SerializeField] private bool hideOnAwake = true;
        [SerializeField, Min(0f)] private float fadeDuration = 0.18f;
        [SerializeField] private CanvasGroup canvasGroup;

        [Header("Items")]
        [SerializeField] private RectTransform contentRoot;
        [SerializeField] private Button slotPrefab;
        [SerializeField] private TMP_Text emptyText;
        [SerializeField] private TMP_Text assembleText;
        [SerializeField] private string iconResourcePath = "Sprites/icon";

        [Header("Item Menu")]
        [SerializeField] private RectTransform menuPopup;
        [SerializeField] private RectTransform menuPanel;
        [SerializeField] private Button menuDescriptionButton;
        [SerializeField] private Button menuEquipButton;
        [SerializeField] private Button menuAssembleButton;
        [SerializeField] private Button menuDisassembleButton;

        private readonly List<ItemSlotButtonUI> slotViews = new();
        private readonly List<string> selectedItemIds = new();
        private readonly Dictionary<int, Sprite> iconsByNo = new();
        private PlayerInventory inventory;
        private TsvTable<Item> itemTable;
        private TsvTable<Dialogue> dialogueTable;
        private TsvTable<Speaker> speakerTable;
        private string selectedItemId = string.Empty;
        private InventoryMode mode = InventoryMode.Default;
        private string pendingAssembleItemId = string.Empty;
        private Button menuPanelButton;

        protected override GameObject PopupRoot { get => root; set => root = value; }
        protected override Button PopupOpenButton { get => openButton; set => openButton = value; }
        protected override Button PopupCloseButton { get => null; set => closeButton = value; }
        protected override bool PopupHideOnAwake => hideOnAwake;
        protected override float PopupFadeDuration => fadeDuration;
        protected override CanvasGroup PopupCanvasGroup { get => canvasGroup; set => canvasGroup = value; }

        // 키보드 입력에서 현재 인벤토리 표시 상태를 확인한다.
        public bool IsOpen => IsPopupVisible;

        private enum InventoryMode
        {
            Default,
            AssemblePairSelection
        }

        private void Awake()
        {
            itemTable = new TsvDataLoader<Item>().LoadTable();
            dialogueTable = new TsvDataLoader<Dialogue>().LoadTable();
            speakerTable = new TsvDataLoader<Speaker>().LoadTable();
            LoadItemIcons();
            ResolveReferences();
            EnsureContentLayout();
            EnsureMenuPopup();
            InitializePopupChrome();
            BindControls();
            HideTemplateChildren();
            HideMenuPopup();
            UpdateModeControls();
            if (IsPopupVisible)
            {
                Refresh();
            }
        }

        private void OnEnable()
        {
            ResolvePlayerInventory();
            if (inventory != null)
            {
                inventory.Changed += Refresh;
                inventory.EnsureDefaults();
            }

            LocalizationService.Ensure().LanguageChanged += Refresh;
            Refresh();
        }

        private void OnDisable()
        {
            HideMenuPopup();

            if (inventory != null)
            {
                inventory.Changed -= Refresh;
            }

            if (LocalizationService.Instance != null)
            {
                LocalizationService.Instance.LanguageChanged -= Refresh;
            }
        }

        protected override void OnBeforeOpen()
        {
            ResolvePlayerInventory();
            SetMode(InventoryMode.Default);
            SyncSingleSelection(string.Empty, false);
            HideMenuPopup();
        }

        protected override void OnAfterOpen()
        {
            Refresh();
            ResetScrollToTop(contentRoot != null ? contentRoot.GetComponentInParent<ScrollRect>() : null);
            if (inventory != null &&
                GetDisplayItemIds(inventory.GetOrderedItems()).Count > 0)
            {
                TutorialPanelUI.ShowOnce(TutorialPanelUI.TutorialId.Inventory);
            }
        }

        protected override void OnBeforeClose()
        {
            SetMode(InventoryMode.Default);
            HideMenuPopup();
        }

        // 키보드 입력에서 인벤토리를 열고 닫는다.
        public void Toggle()
        {
            if (IsOpen)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        public void Refresh()
        {
            ResolveReferences();
            EnsureContentLayout();
            ResolvePlayerInventory();

            if (inventory == null)
            {
                SetEmptyTextVisible(true);
                UpdateModeControls();
                return;
            }

            IReadOnlyList<string> itemIds = GetDisplayItemIds(inventory.GetOrderedItems());
            EnsureSlotCount(itemIds.Count);
            SyncSelection(itemIds);

            for (int i = 0; i < slotViews.Count; i++)
            {
                bool hasItem = i < itemIds.Count;
                string itemId = hasItem ? itemIds[i] : string.Empty;
                bool selected = hasItem && IsItemSelected(itemId);
                bool interactable = hasItem && IsItemSlotInteractable(itemId);
                string itemName = hasItem ? GetItemName(itemId) : string.Empty;
                slotViews[i].gameObject.SetActive(hasItem);
                slotViews[i].SetItem(itemId, GetItemIcon(itemId), hasItem, selected, interactable, itemName);
            }

            UpdateModeControls();
            UpdateContentHeight(itemIds.Count);
            SetEmptyTextVisible(itemIds.Count == 0);
        }

        private void SelectSlot(ItemSlotButtonUI slot)
        {
            if (slot == null || string.IsNullOrWhiteSpace(slot.ItemId))
            {
                return;
            }

            if (mode == InventoryMode.AssemblePairSelection)
            {
                TryAssembleWithSecondItem(slot.ItemId);
                return;
            }

            SyncSingleSelection(slot.ItemId, false);
            Refresh();
            ShowMenuPopup(slot);
        }

        private void AssembleSelected()
        {
            if (selectedItemIds.Count < 2)
            {
                ShowToast(ToastAssembleSelectTwoOrMoreTid, "\uB450 \uAC1C \uC774\uC0C1\uC758 \uC544\uC774\uD15C\uC744 \uC120\uD0DD\uD558\uC138\uC694.");
                Refresh();
                return;
            }

            if (inventory == null || inventory.IsInputLocked || !TryFindAssemblyRecipe(selectedItemIds, out AssemblyRecipe recipe))
            {
                ShowToast(ToastAssembleFailedTid, "\uC870\uD569\uD560 \uC218 \uC5C6\uC2B5\uB2C8\uB2E4.");
                RestorePendingAssembleSelection();
                Refresh();
                return;
            }

            var consumedItemIds = new List<string>();
            for (int i = 0; i < recipe.IngredientItemIds.Count; i++)
            {
                string ingredientItemId = recipe.IngredientItemIds[i];
                if (inventory.ConsumeItem(ingredientItemId))
                {
                    consumedItemIds.Add(ingredientItemId);
                }
            }

            inventory.AddItem(recipe.ResultItemId);
            selectedItemIds.Clear();
            selectedItemIds.Add(recipe.ResultItemId);
            selectedItemId = recipe.ResultItemId;
            inventory.SelectItem(recipe.ResultItemId);
            pendingAssembleItemId = string.Empty;
            SetMode(InventoryMode.Default);
            HideMenuPopup();

            Refresh();
            PlayItemChangeStory(consumedItemIds, new[] { recipe.ResultItemId });
        }

        private void DisassembleSelected()
        {
            if (selectedItemIds.Count == 0)
            {
                ShowToast(ToastDisassembleSelectItemTid, "\uBD84\uD574\uD560 \uC544\uC774\uD15C\uC744 \uC120\uD0DD\uD558\uC138\uC694.");
                Refresh();
                return;
            }

            if (selectedItemIds.Count > 1)
            {
                ShowToast(ToastDisassembleSelectOneTid, "\uD558\uB098\uC758 \uC544\uC774\uD15C\uB9CC \uC120\uD0DD\uD558\uC138\uC694.");
                Refresh();
                return;
            }

            string itemId = GetSingleSelectedItemId();
            if (inventory == null || inventory.IsInputLocked || !TryGetDisassemblyResults(itemId, out List<string> results))
            {
                ShowToast(ToastDisassembleFailedTid, "\uBD84\uD574\uD560 \uC218 \uC5C6\uC2B5\uB2C8\uB2E4.");
                Refresh();
                return;
            }

            var consumedItemIds = new List<string>();
            if (inventory.ConsumeItem(itemId))
            {
                consumedItemIds.Add(itemId);
            }
            for (int i = 0; i < results.Count; i++)
            {
                inventory.AddItem(results[i]);
            }

            selectedItemId = results.Count > 0 ? results[0] : inventory.SelectedItemId;
            selectedItemIds.Clear();
            if (!string.IsNullOrWhiteSpace(selectedItemId))
            {
                selectedItemIds.Add(selectedItemId);
                inventory.SelectItem(selectedItemId);
            }

            SetMode(InventoryMode.Default);
            HideMenuPopup();
            Refresh();
            PlayItemChangeStory(
                consumedItemIds,
                results,
                ItemDisassembledDialogueId,
                "{0}{1} 분해했습니다.");
        }

        // 메뉴에서 조합을 고르면 기준 아이템 하나를 들고 두 번째 아이템 클릭을 기다린다.
        private void BeginAssembleMode()
        {
            if (string.IsNullOrWhiteSpace(selectedItemId))
            {
                ShowToast(ToastAssembleSelectTwoOrMoreTid, "\uB450 \uAC1C \uC774\uC0C1\uC758 \uC544\uC774\uD15C\uC744 \uC120\uD0DD\uD558\uC138\uC694.");
                return;
            }

            pendingAssembleItemId = selectedItemId;
            selectedItemIds.Clear();
            selectedItemIds.Add(pendingAssembleItemId);
            mode = InventoryMode.AssemblePairSelection;
            HideMenuPopup();
            Refresh();
        }

        // 조합모드에서 두 번째 아이템을 클릭하면 바로 조합을 시도한다.
        private void TryAssembleWithSecondItem(string itemId)
        {
            if (string.IsNullOrWhiteSpace(pendingAssembleItemId))
            {
                pendingAssembleItemId = selectedItemId;
            }

            if (string.IsNullOrWhiteSpace(itemId) ||
                string.Equals(itemId, pendingAssembleItemId, StringComparison.Ordinal))
            {
                ShowToast(ToastAssembleFailedTid, "\uC870\uD569\uD560 \uC218 \uC5C6\uC2B5\uB2C8\uB2E4.");
                RestorePendingAssembleSelection();
                Refresh();
                return;
            }

            selectedItemIds.Clear();
            selectedItemIds.Add(pendingAssembleItemId);
            selectedItemIds.Add(itemId);
            selectedItemId = pendingAssembleItemId;
            AssembleSelected();
        }

        private void RestorePendingAssembleSelection()
        {
            if (mode != InventoryMode.AssemblePairSelection ||
                string.IsNullOrWhiteSpace(pendingAssembleItemId))
            {
                return;
            }

            selectedItemIds.Clear();
            selectedItemIds.Add(pendingAssembleItemId);
            selectedItemId = pendingAssembleItemId;
        }

        private bool TryFindAssemblyRecipe(IReadOnlyCollection<string> itemIds, out AssemblyRecipe recipe)
        {
            recipe = default;
            if (inventory == null || itemTable == null || itemIds == null || itemIds.Count < 2)
            {
                return false;
            }

            foreach (Item row in itemTable.Rows)
            {
                if (!TryBuildRecipe(row, out recipe))
                {
                    continue;
                }

                if (itemIds.Count == recipe.IngredientItemIds.Count &&
                    ContainsSelectedItems(itemIds, recipe.IngredientItemIds) &&
                    OwnsAll(recipe.IngredientItemIds))
                {
                    return true;
                }
            }

            recipe = default;
            return false;
        }

        // 결과 아이템 행에 적힌 조합 재료 목록으로 레시피를 만든다.
        private static bool TryBuildRecipe(Item item, out AssemblyRecipe recipe)
        {
            recipe = default;
            if (item == null || !IsItemReference(item.id))
            {
                return false;
            }

            var ingredientItemIds = new List<string>(3);
            AddItemReference(ingredientItemIds, item.com_item_id_0);
            AddItemReference(ingredientItemIds, item.com_item_id_1);
            AddItemReference(ingredientItemIds, item.com_item_id_2);
            if (ingredientItemIds.Count < 2)
            {
                return false;
            }

            recipe = new AssemblyRecipe(item.id.Trim(), ingredientItemIds);
            return true;
        }

        private bool TryGetDisassemblyResults(string itemId, out List<string> results)
        {
            results = null;
            if (itemTable == null ||
                string.IsNullOrWhiteSpace(itemId) ||
                !itemTable.TryGet(itemId, out Item item))
            {
                return false;
            }

            results = new List<string>(2);
            AddItemReference(results, item.decom_item_id_0);
            AddItemReference(results, item.decom_item_id_1);

            return results.Count > 0 && Owns(itemId);
        }

        private RectTransform ResolveMenuPopup()
        {
            Transform found = FindPopupChild(RuntimeMenuName) ?? FindPopupChild("MenuPopupUI");
            return found as RectTransform;
        }

        private RectTransform ResolveMenuPanel()
        {
            if (menuPopup == null)
            {
                return null;
            }

            Transform panel = FindChildByName(menuPopup, "Panel");
            if (panel is RectTransform panelRect)
            {
                return panelRect;
            }

            return menuPopup;
        }

        private static Transform FindChildByName(Transform root, string childName)
        {
            if (root == null || string.IsNullOrWhiteSpace(childName))
            {
                return null;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name == childName)
                {
                    return child;
                }
            }

            return null;
        }

        // 아이템 슬롯 옆에 뜨는 메뉴와 네 개 버튼을 준비한다.
        private void EnsureMenuPopup()
        {
            ResolveReferences();
            if (menuPopup == null || menuPanel == null)
            {
                return;
            }

            Image blocker = menuPopup.GetComponent<Image>();
            if (blocker != null)
            {
                blocker.raycastTarget = true;
            }

            Image background = menuPanel.GetComponent<Image>();
            if (background != null)
            {
                background.raycastTarget = true;
            }

            menuPanelButton = menuPopup.GetComponent<Button>() ?? menuPopup.gameObject.AddComponent<Button>();
            menuPanelButton.transition = Selectable.Transition.None;
            menuPanelButton.targetGraphic = blocker;
            menuPanelButton.onClick.RemoveListener(HideMenuPopup);
            menuPanelButton.onClick.AddListener(HideMenuPopup);

            ResolveMenuButtons();
            menuDescriptionButton = EnsureMenuButton(menuDescriptionButton, 0, "DescriptionButton", "inventory_description", "설명");
            menuEquipButton = EnsureMenuButton(menuEquipButton, 1, "EquipButton", "inventory_equip", "장착");
            menuAssembleButton = EnsureMenuButton(menuAssembleButton, 2, "AssembleButton", "inventory_assemble", "조합");
            menuDisassembleButton = EnsureMenuButton(menuDisassembleButton, 3, "DisassembleButton", "inventory_disassemble", "분해");
        }

        private void ResolveMenuButtons()
        {
            if (menuPanel == null)
            {
                return;
            }

            menuDescriptionButton ??= FindMenuButton("DescriptionButton") ?? GetMenuButtonAt(0);
            menuEquipButton ??= FindMenuButton("EquipButton") ?? GetMenuButtonAt(1);
            menuAssembleButton ??= FindMenuButton("AssembleButton") ?? GetMenuButtonAt(2);
            menuDisassembleButton ??= FindMenuButton("DisassembleButton") ?? GetMenuButtonAt(3);
        }

        private Button EnsureMenuButton(Button button, int index, string buttonName, string tid, string fallback)
        {
            button ??= GetMenuButtonAt(index);
            if (button == null)
            {
                return null;
            }

            button.name = buttonName;

            Image image = button.GetComponent<Image>();
            if (image != null)
            {
                image.color = UIDarkTheme.SurfaceRaised;
            }

            TMP_Text text = button.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
            {
                text.text = LocalizationService.Text(tid, fallback);
                text.color = UIDarkTheme.TextPrimary;
                text.alignment = TextAlignmentOptions.Center;
                text.raycastTarget = false;
            }

            return button;
        }

        private Button FindMenuButton(string buttonName)
        {
            if (menuPanel == null)
            {
                return null;
            }

            for (int i = 0; i < menuPanel.childCount; i++)
            {
                Transform child = menuPanel.GetChild(i);
                if (child.name == buttonName)
                {
                    return child.GetComponent<Button>();
                }
            }

            return null;
        }

        private Button GetMenuButtonAt(int index)
        {
            if (menuPanel == null || index < 0)
            {
                return null;
            }

            int buttonIndex = 0;
            for (int i = 0; i < menuPanel.childCount; i++)
            {
                Button button = menuPanel.GetChild(i).GetComponent<Button>();
                if (button == null)
                {
                    continue;
                }

                if (buttonIndex == index)
                {
                    return button;
                }

                buttonIndex++;
            }

            return null;
        }

        private void ShowMenuPopup(ItemSlotButtonUI slot)
        {
            EnsureMenuPopup();
            if (menuPopup == null || menuPanel == null || slot == null || !(slot.transform is RectTransform slotRect))
            {
                return;
            }

            SetMenuPopupVisible(true);
            menuPopup.SetAsLastSibling();
            PositionMenuBelowSlot(slotRect);
            UpdateModeControls();
        }

        private void HideMenuPopup()
        {
            SetMenuPopupVisible(false);
            ClearSelectionWhenMenuCloses();
        }

        private void ClearSelectionWhenMenuCloses()
        {
            if (mode != InventoryMode.Default || selectedItemIds.Count == 0)
            {
                return;
            }

            SyncSingleSelection(string.Empty, false);
            Refresh();
        }

        private void SetMenuPopupVisible(bool visible)
        {
            if (menuPopup == null)
            {
                return;
            }

            menuPopup.gameObject.SetActive(visible);
        }

        private void PositionMenuBelowSlot(RectTransform slotRect)
        {
            if (menuPopup == null || menuPanel == null)
            {
                return;
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(menuPanel);
            RectTransform parentRect = menuPopup;
            Vector3[] corners = new Vector3[4];
            slotRect.GetWorldCorners(corners);
            Vector3 bottomCenter = (corners[0] + corners[3]) * 0.5f;
            Canvas canvas = menuPanel.GetComponentInParent<Canvas>();
            Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(eventCamera, bottomCenter);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parentRect, screenPoint, eventCamera, out Vector2 localPoint))
            {
                localPoint = parentRect.InverseTransformPoint(bottomCenter);
            }

            menuPanel.anchoredPosition = ClampMenuPosition(parentRect, localPoint + MenuSlotOffset);
        }

        private Vector2 ClampMenuPosition(RectTransform parentRect, Vector2 position)
        {
            Rect parent = parentRect.rect;
            Vector2 size = menuPanel.rect.size;
            if (size.x <= 1f || size.y <= 1f)
            {
                size = menuPanel.sizeDelta;
            }

            Vector2 pivot = menuPanel.pivot;
            float minX = parent.xMin + size.x * pivot.x;
            float maxX = parent.xMax - size.x * (1f - pivot.x);
            float minY = parent.yMin + size.y * pivot.y;
            float maxY = parent.yMax - size.y * (1f - pivot.y);
            position.x = minX <= maxX ? Mathf.Clamp(position.x, minX, maxX) : parent.center.x;
            position.y = minY <= maxY ? Mathf.Clamp(position.y, minY, maxY) : parent.center.y;
            return position;
        }

        private void EnsureSlotCount(int count)
        {
            ResolveReferences();
            if (contentRoot == null)
            {
                return;
            }

            while (slotViews.Count < count)
            {
                Button slotButton = CreateSlotButton();
                if (slotButton == null)
                {
                    return;
                }

                ItemSlotButtonUI slot = slotButton.GetComponent<ItemSlotButtonUI>() ??
                    slotButton.gameObject.AddComponent<ItemSlotButtonUI>();
                slot.Configure(SelectSlot);
                slotViews.Add(slot);
            }
        }

        private Button CreateSlotButton()
        {
            slotPrefab ??= LoadSlotPrefab();
            if (slotPrefab == null)
            {
                Debug.LogWarning($"Item slot prefab not found: Resources/{ItemSlotPrefabPath}", this);
                return null;
            }

            Button slot = Instantiate(slotPrefab, contentRoot);
            slot.name = $"{slotPrefab.name} ({slotViews.Count + 1})";
            slot.gameObject.SetActive(true);
            return slot;
        }

        private void UpdateContentHeight(int itemCount)
        {
            if (contentRoot == null)
            {
                return;
            }

            GridLayoutGroup grid = contentRoot.GetComponent<GridLayoutGroup>();
            if (grid == null)
            {
                return;
            }

            RectTransform viewport = contentRoot.parent as RectTransform;
            float contentWidth = viewport != null ? viewport.rect.width : contentRoot.rect.width;
            if (contentWidth <= 1f && viewport != null && viewport.parent is RectTransform scrollView)
            {
                contentWidth = scrollView.rect.width;
            }

            if (contentWidth <= 1f)
            {
                contentWidth = 160f;
            }

            float cellWidth = Mathf.Max(1f, grid.cellSize.x + grid.spacing.x);
            int columnCount = Mathf.Max(1, Mathf.FloorToInt((contentWidth + grid.spacing.x - grid.padding.left - grid.padding.right) / cellWidth));
            int rowCount = Mathf.Max(1, Mathf.CeilToInt(itemCount / (float)columnCount));
            float height = grid.padding.top + grid.padding.bottom + rowCount * grid.cellSize.y + Mathf.Max(0, rowCount - 1) * grid.spacing.y;

            Vector2 sizeDelta = contentRoot.sizeDelta;
            sizeDelta.y = Mathf.Max(height, sizeDelta.y);
            contentRoot.sizeDelta = sizeDelta;
        }

        private static IReadOnlyList<string> GetDisplayItemIds(IReadOnlyList<string> itemIds)
        {
            if (itemIds == null || itemIds.Count == 0)
            {
                return Array.Empty<string>();
            }

            var displayItemIds = new List<string>(itemIds.Count);
            for (int i = 0; i < itemIds.Count; i++)
            {
                string itemId = itemIds[i];
                if (!IsDefaultInteractionItem(itemId))
                {
                    displayItemIds.Add(itemId);
                }
            }

            return displayItemIds;
        }

        private void SetEmptyTextVisible(bool visible)
        {
            if (emptyText == null)
            {
                return;
            }

            emptyText.text = LocalizationService.Text("inventory_empty", EmptyInventoryFallbackText);

            emptyText.gameObject.SetActive(visible);
        }

        private Sprite GetItemIcon(string itemId)
        {
            if (itemTable == null ||
                !itemTable.TryGet(itemId, out Item item) ||
                !int.TryParse(item.icon_idx, out int iconIdx))
            {
                return null;
            }

            iconsByNo.TryGetValue(iconIdx, out Sprite icon);
            return icon;
        }

        private string GetItemName(string itemId)
        {
            if (itemTable != null && itemTable.TryGet(itemId, out Item item))
            {
                return GetItemName(item);
            }

            return itemId ?? string.Empty;
        }

        private static string GetItemName(Item item)
        {
            return LocalizationService.Localized(item, nameof(Item.name), item.name);
        }

        // 선택 중인 아이템 설명을 대사 팝업으로 보여준다.
        private void PlaySelectedItemDescription()
        {
            if (string.IsNullOrWhiteSpace(selectedItemId) ||
                itemTable == null ||
                !itemTable.TryGet(selectedItemId, out Item item))
            {
                return;
            }

            string description = LocalizationService.Localized(item, nameof(Item.desc), item.desc);
            if (string.IsNullOrWhiteSpace(description))
            {
                return;
            }

            DialoguePlayer dialoguePlayer = DialoguePlayer.Instance ??
                FindFirstObjectByType<DialoguePlayer>(FindObjectsInactive.Include);
            dialoguePlayer?.Play("", description);
            HideMenuPopup();
        }

        private void EquipSelectedItem()
        {
            if (string.IsNullOrWhiteSpace(selectedItemId))
            {
                return;
            }

            SyncSingleSelection(selectedItemId, false);
            inventory?.SelectItem(selectedItemId);
            Close();
        }

        // 조합/분해에서 소비 문구 뒤에 획득 문구를 한 흐름으로 재생한다.
        private void PlayItemChangeStory(
            IReadOnlyList<string> consumedItemIds,
            IReadOnlyList<string> acquiredItemIds,
            string consumedDialogueId = ItemLostDialogueId,
            string consumedFallbackText = "{0}{1} 조합했습니다.")
        {
            var lines = new List<DialogueLine>();
            if (consumedItemIds != null)
            {
                var consumedNames = new List<string>();
                for (int i = 0; i < consumedItemIds.Count; i++)
                {
                    string itemId = consumedItemIds[i];
                    if (!string.IsNullOrWhiteSpace(itemId))
                    {
                        consumedNames.Add(GetItemName(itemId));
                    }
                }

                if (consumedNames.Count > 0)
                {
                    // 소비된 아이템을 [a], [b], [c] 형태로 묶어 한 문장으로 보여준다.
                    lines.Add(CreateItemsLostLine(consumedNames, consumedDialogueId, consumedFallbackText));
                }
            }

            if (acquiredItemIds != null)
            {
                for (int i = 0; i < acquiredItemIds.Count; i++)
                {
                    string itemId = acquiredItemIds[i];
                    if (!string.IsNullOrWhiteSpace(itemId))
                    {
                        lines.Add(CreateItemAcquiredLine(GetItemName(itemId)));
                    }
                }
            }

            if (lines.Count == 0)
            {
                return;
            }

            DialoguePlayer dialoguePlayer = DialoguePlayer.Instance ??
                FindFirstObjectByType<DialoguePlayer>(FindObjectsInactive.Include);
            if (dialoguePlayer == null)
            {
                if (acquiredItemIds != null && acquiredItemIds.Count > 0)
                {
                    ShowToast(ToastItemAcquiredTid, "{0} \uD68D\uB4DD", GetItemName(acquiredItemIds[0]));
                }
                return;
            }

            dialoguePlayer.Play(lines.ToArray());
        }

        // 소비된 여러 아이템의 조합 또는 분해 결과를 한 문장으로 만든다.
        private DialogueLine CreateItemsLostLine(
            IReadOnlyList<string> itemNames,
            string dialogueId,
            string fallbackText)
        {
            var coloredNames = new List<string>(itemNames.Count);
            string lastName = string.Empty;
            for (int i = 0; i < itemNames.Count; i++)
            {
                string itemName = itemNames[i];
                coloredNames.Add(FormatItemReference(itemName));
                lastName = itemName;
            }

            string joined = string.Join(", ", coloredNames);

            string template = string.IsNullOrWhiteSpace(fallbackText)
                ? "{0}{1} 조합했습니다."
                : fallbackText;
            Dialogue dialogue = null;
            dialogueTable?.TryGet(dialogueId, out dialogue);

            if (dialogue != null)
            {
                template = UnescapeDialogueText(LocalizationService.Localized(dialogue, nameof(Dialogue.text), dialogue.text));
            }

            string text = string.Format(template, joined, GetObjectParticle(lastName));
            if (dialogue == null)
            {
                return new DialogueLine(string.Empty, string.Empty, text, null);
            }

            string speakerId = dialogue.speaker_id;
            Speaker speaker = null;
            speakerTable?.TryGet(speakerId, out speaker);
            return new DialogueLine(
                speakerId,
                speaker != null ? LocalizationService.Localized(speaker, nameof(Speaker.name), speaker.name) : string.Empty,
                text,
                DialoguePortraitResolver.Load(dialogue, speakerTable),
                DialoguePortraitResolver.ResolveTint(speaker, Color.white),
                DialoguePortraitResolver.ResolveScale(speaker, 1f),
                dialogue.effect,
                speaker != null ? speaker.typing_sfx : string.Empty,
                dialogue.type,
                dialogue.bg_path,
                dialogue.bgm,
                shader: dialogue.shader);
        }

        // dialogue.tsv의 item_acquired 문장을 아이템 이름으로 채운다.
        private DialogueLine CreateItemAcquiredLine(string itemName)
        {
            string template = "<sfx=item_acquire><color=#D8E88F>[{0}]</color>{1} 획득했습니다.";
            Dialogue dialogue = null;
            dialogueTable?.TryGet("item_acquired", out dialogue);

            if (dialogue != null)
            {
                template = UnescapeDialogueText(LocalizationService.Localized(dialogue, nameof(Dialogue.text), dialogue.text));
            }

            string text = string.Format(template, itemName, GetObjectParticle(itemName));
            if (dialogue == null)
            {
                return new DialogueLine(string.Empty, string.Empty, text, null);
            }

            string speakerId = dialogue.speaker_id;
            Speaker speaker = null;
            speakerTable?.TryGet(speakerId, out speaker);
            return new DialogueLine(
                speakerId,
                speaker != null ? LocalizationService.Localized(speaker, nameof(Speaker.name), speaker.name) : string.Empty,
                text,
                DialoguePortraitResolver.Load(dialogue, speakerTable),
                DialoguePortraitResolver.ResolveTint(speaker, Color.white),
                DialoguePortraitResolver.ResolveScale(speaker, 1f),
                dialogue.effect,
                speaker != null ? speaker.typing_sfx : string.Empty,
                dialogue.type,
                dialogue.bg_path,
                dialogue.bgm,
                shader: dialogue.shader);
        }

        private static string GetObjectParticle(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "\uC744";
            }

            char last = value.Trim()[^1];
            if (last < 0xAC00 || last > 0xD7A3)
            {
                return "\uC744";
            }

            return ((last - 0xAC00) % 28) == 0 ? "\uB97C" : "\uC744";
        }

        private static string UnescapeDialogueText(string text)
        {
            return (text ?? string.Empty)
                .Replace("\\t", "\t")
                .Replace("\\n", "\n");
        }

        private static string FormatItemReference(string itemName)
        {
            return $"<color=#D8E88F>[{itemName}]</color>";
        }

        private static void ShowToast(string tid, string fallback, params object[] args)
        {
            string template = LocalizationService.Text(tid, fallback);
            string message = template;
            if (args != null && args.Length > 0)
            {
                try
                {
                    message = string.Format(template, args);
                }
                catch (FormatException)
                {
                    message = string.Format(fallback, args);
                }
            }

            ToastPresenter.Show(message);
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

        private Button LoadSlotPrefab()
        {
            GameObject prefab = Resources.Load<GameObject>(ItemSlotPrefabPath);
            if (prefab == null)
            {
                return null;
            }

            return prefab.GetComponent<Button>();
        }

        private void BindControls()
        {
            closeButton?.onClick.AddListener(Close);
            cancelButton?.onClick.AddListener(CancelAssembleMode);
            menuDescriptionButton?.onClick.AddListener(PlaySelectedItemDescription);
            menuEquipButton?.onClick.AddListener(EquipSelectedItem);
            menuAssembleButton?.onClick.AddListener(BeginAssembleMode);
            menuDisassembleButton?.onClick.AddListener(DisassembleSelected);
        }

        private void ResolveReferences()
        {
            root ??= gameObject;
            canvasGroup ??= root.GetComponent<CanvasGroup>();
            openButton ??= FindScenePopupChild("InventoryButton")?.GetComponent<Button>();
            closeButton ??= FindPopupChild("CloseButton")?.GetComponent<Button>();
            cancelButton ??= FindPopupChild("CancelButton")?.GetComponent<Button>();
            contentRoot ??= FindPopupChild("Content") as RectTransform;
            slotPrefab ??= LoadSlotPrefab();
            menuPopup ??= ResolveMenuPopup();
            menuPanel ??= ResolveMenuPanel();
            menuPanelButton ??= menuPopup != null ? menuPopup.GetComponent<Button>() : null;
            ResolveMenuButtons();
        }

        private void SetMode(InventoryMode nextMode)
        {
            if (mode == nextMode)
            {
                UpdateModeControls();
                return;
            }

            mode = nextMode;
            if (mode == InventoryMode.Default)
            {
                pendingAssembleItemId = string.Empty;
                string firstSelectedItemId = selectedItemIds.Count > 0
                    ? selectedItemIds[0]
                    : selectedItemId;

                SyncSingleSelection(firstSelectedItemId, false);
            }
            else if (selectedItemIds.Count == 0 && !string.IsNullOrWhiteSpace(selectedItemId))
            {
                selectedItemIds.Add(selectedItemId);
            }

            UpdateModeControls();
        }

        private void UpdateModeControls()
        {
            bool defaultMode = mode == InventoryMode.Default;
            SetButtonVisible(closeButton, true);
            SetButtonVisible(cancelButton, !defaultMode);
            SetTextVisible(assembleText, !defaultMode);

            bool canInput = inventory == null || !inventory.IsInputLocked;
            if (menuDescriptionButton != null)
            {
                menuDescriptionButton.interactable = defaultMode && canInput && HasSelectedItem();
            }

            if (menuEquipButton != null)
            {
                menuEquipButton.interactable = defaultMode && canInput && HasSelectedItem();
            }

            if (menuAssembleButton != null)
            {
                menuAssembleButton.interactable = defaultMode && canInput && !string.IsNullOrWhiteSpace(selectedItemId);
            }

            if (menuDisassembleButton != null)
            {
                menuDisassembleButton.interactable = defaultMode && canInput && !string.IsNullOrWhiteSpace(selectedItemId);
            }
        }

        private void CancelAssembleMode()
        {
            SetMode(InventoryMode.Default);
            SyncSingleSelection(string.Empty, false);
            HideMenuPopup();
            Refresh();
        }

        private bool HasSelectedItem()
        {
            return !string.IsNullOrWhiteSpace(selectedItemId) &&
                   itemTable != null &&
                   itemTable.TryGet(selectedItemId, out _);
        }

        private static void SetButtonVisible(Button button, bool visible)
        {
            if (button != null)
            {
                button.gameObject.SetActive(visible);
            }
        }

        private static void SetTextVisible(TMP_Text text, bool visible)
        {
            if (text != null)
            {
                text.gameObject.SetActive(visible);
            }
        }

        private void HideTemplateChildren()
        {
            if (contentRoot == null)
            {
                return;
            }

            for (int i = 0; i < contentRoot.childCount; i++)
            {
                contentRoot.GetChild(i).gameObject.SetActive(false);
            }
        }

        private void EnsureContentLayout()
        {
            if (contentRoot == null)
            {
                return;
            }

            RectTransform viewport = contentRoot.parent as RectTransform;
            if (viewport != null)
            {
                viewport.anchorMin = Vector2.zero;
                viewport.anchorMax = Vector2.one;
                viewport.offsetMin = Vector2.zero;
                viewport.offsetMax = Vector2.zero;
                viewport.pivot = new Vector2(0f, 1f);

                ScrollRect scrollRect = viewport.GetComponentInParent<ScrollRect>();
                if (scrollRect != null)
                {
                    scrollRect.viewport = viewport;
                    scrollRect.content = contentRoot;
                }
            }

            // 선택 갱신 때 Refresh가 다시 호출되므로 anchoredPosition은 변경하지 않는다.
            contentRoot.anchorMin = new Vector2(0f, 1f);
            contentRoot.anchorMax = new Vector2(1f, 1f);
            contentRoot.pivot = new Vector2(0f, 1f);
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

            if (inventory == null)
            {
                var inventoryObject = new GameObject(nameof(PlayerInventory));
                inventory = inventoryObject.AddComponent<PlayerInventory>();
            }
        }

        private bool Owns(string itemId)
        {
            if (!IsItemReference(itemId) || inventory == null)
            {
                return false;
            }

            string normalizedItemId = itemId.Trim();
            foreach (string ownedItemId in inventory.Items)
            {
                if (string.Equals(ownedItemId, normalizedItemId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        // 선택된 조합 재료를 모두 가지고 있는지 확인한다.
        private bool OwnsAll(IReadOnlyList<string> itemIds)
        {
            if (itemIds == null || itemIds.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < itemIds.Count; i++)
            {
                if (!Owns(itemIds[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private void SyncSelection(IReadOnlyList<string> itemIds)
        {
            if (mode == InventoryMode.Default)
            {
                if (!ContainsItem(itemIds, selectedItemId))
                {
                    selectedItemId = string.Empty;
                }

                SyncSingleSelection(selectedItemId, false);

                return;
            }

            PruneSelectedItems(itemIds);
            if (selectedItemIds.Count > 0)
            {
                selectedItemId = selectedItemIds[0];
            }
            else
            {
                selectedItemId = string.Empty;
            }
        }

        private bool IsItemSelected(string itemId)
        {
            return mode == InventoryMode.Default
                ? string.Equals(itemId, selectedItemId, StringComparison.Ordinal)
                : selectedItemIds.Contains(itemId);
        }

        // 단일 선택 상태와 바깥에서 사용하는 현재 아이템을 같은 값으로 맞춘다.
        private void SyncSingleSelection(string itemId, bool updatePlayerInventory)
        {
            selectedItemId = itemId ?? string.Empty;
            selectedItemIds.Clear();
            if (!string.IsNullOrWhiteSpace(selectedItemId))
            {
                selectedItemIds.Add(selectedItemId);
                if (updatePlayerInventory)
                {
                    inventory?.SelectItem(selectedItemId);
                }
            }
        }

        private void PruneSelectedItems(IReadOnlyList<string> itemIds)
        {
            if (selectedItemIds.Count == 0)
            {
                return;
            }

            var removedItemIds = new List<string>();
            foreach (string itemId in selectedItemIds)
            {
                if (!ContainsItem(itemIds, itemId))
                {
                    removedItemIds.Add(itemId);
                }
            }

            for (int i = 0; i < removedItemIds.Count; i++)
            {
                selectedItemIds.Remove(removedItemIds[i]);
            }
        }

        private bool IsItemSlotInteractable(string itemId)
        {
            if (mode != InventoryMode.AssemblePairSelection)
            {
                return true;
            }

            string firstItemId = !string.IsNullOrWhiteSpace(pendingAssembleItemId)
                ? pendingAssembleItemId
                : selectedItemId;
            return !string.Equals(itemId, firstItemId, StringComparison.Ordinal);
        }

        private string GetSingleSelectedItemId()
        {
            return selectedItemIds.Count > 0 ? selectedItemIds[0] : string.Empty;
        }

        private static bool ContainsSelectedItem(IReadOnlyCollection<string> itemIds, string itemId)
        {
            if (itemIds == null || string.IsNullOrWhiteSpace(itemId))
            {
                return false;
            }

            string normalizedItemId = itemId.Trim();
            foreach (string selectedItemId in itemIds)
            {
                if (string.Equals(selectedItemId, normalizedItemId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        // 선택한 아이템 집합이 레시피의 재료 집합과 정확히 같은지 확인한다.
        private static bool ContainsSelectedItems(IReadOnlyCollection<string> selectedItemIds, IReadOnlyList<string> requiredItemIds)
        {
            if (selectedItemIds == null || requiredItemIds == null || selectedItemIds.Count != requiredItemIds.Count)
            {
                return false;
            }

            for (int i = 0; i < requiredItemIds.Count; i++)
            {
                if (!ContainsSelectedItem(selectedItemIds, requiredItemIds[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ContainsItem(IReadOnlyList<string> items, string itemId)
        {
            if (items == null || string.IsNullOrWhiteSpace(itemId))
            {
                return false;
            }

            for (int i = 0; i < items.Count; i++)
            {
                if (string.Equals(items[i], itemId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsDefaultInteractionItem(string itemId)
        {
            return string.Equals((itemId ?? string.Empty).Trim(), PlayerInventory.InteractItemId, StringComparison.Ordinal);
        }

        private static void AddItemReference(List<string> items, string itemId)
        {
            if (items != null && IsItemReference(itemId))
            {
                items.Add(itemId.Trim());
            }
        }

        private static bool IsItemReference(string itemId)
        {
            return !string.IsNullOrWhiteSpace(itemId) &&
                !string.Equals(itemId.Trim(), "none", StringComparison.OrdinalIgnoreCase);
        }

        private readonly struct AssemblyRecipe
        {
            public AssemblyRecipe(string resultItemId, IReadOnlyList<string> ingredientItemIds)
            {
                ResultItemId = resultItemId;
                IngredientItemIds = ingredientItemIds;
            }

            public string ResultItemId { get; }
            public IReadOnlyList<string> IngredientItemIds { get; }
        }
    }
}
