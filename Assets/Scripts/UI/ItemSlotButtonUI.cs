using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Escape.UI
{
    // 아이템 슬롯 버튼 하나의 표시 상태와 클릭을 관리한다.
    [RequireComponent(typeof(Button))]
    public sealed class ItemSlotButtonUI : MonoBehaviour
    {
        public enum SlotState
        {
            Empty,
            ItemIdle,
            ItemSelected
        }

        [SerializeField] private Button button;
        [SerializeField] private Image background;
        [SerializeField] private Image itemIcon;
        [SerializeField] private TMP_Text labelText;

        private System.Action<ItemSlotButtonUI> clicked;
        private bool canClick = true;

        public SlotState State { get; private set; } = SlotState.Empty;
        public string ItemId { get; private set; } = string.Empty;

        // 필요한 UI 참조를 준비하고 클릭 이벤트를 연결한다.
        private void Awake()
        {
            ResolveReferences();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(HandleClick);
            SetEmpty();
        }

        // 파괴될 때 버튼 이벤트 연결을 해제한다.
        private void OnDestroy()
        {
            button?.onClick.RemoveListener(HandleClick);
        }

        // 패널에서 슬롯 클릭 콜백을 연결한다.
        public void Configure(System.Action<ItemSlotButtonUI> onClicked)
        {
            clicked = onClicked;
            ApplyState();
        }

        // 아이템이 없는 슬롯 상태로 바꾼다.
        public void SetEmpty()
        {
            SetItem(string.Empty, null, false, false);
        }

        // 아이템 유무와 선택 여부를 받아 알맞은 슬롯 상태로 바꾼다.
        public void SetItem(string itemId, Sprite icon, bool hasItem, bool selected, bool interactable = true, string label = "")
        {
            ItemId = hasItem ? itemId ?? string.Empty : string.Empty;
            canClick = interactable;
            State = !hasItem
                ? SlotState.Empty
                : selected ? SlotState.ItemSelected : SlotState.ItemIdle;

            if (itemIcon != null)
            {
                itemIcon.sprite = hasItem ? icon : null;
            }

            if (labelText != null)
            {
                labelText.text = hasItem ? label ?? string.Empty : string.Empty;
            }

            ApplyState();
        }

        // 슬롯 버튼 클릭을 패널 쪽으로 전달한다.
        private void HandleClick()
        {
            if (State == SlotState.Empty)
            {
                return;
            }

            clicked?.Invoke(this);
        }

        // 현재 슬롯 상태를 실제 버튼/이미지/텍스트에 반영한다.
        private void ApplyState()
        {
            bool hasItem = State != SlotState.Empty;
            button.interactable = hasItem && canClick;

            if (background != null)
            {
                background.color = State switch
                {
                    SlotState.ItemSelected => UIDarkTheme.ItemSlotSelected,
                    SlotState.ItemIdle => UIDarkTheme.ItemSlotIdle,
                    _ => UIDarkTheme.ItemSlotEmpty
                };
            }

            if (itemIcon != null)
            {
                itemIcon.enabled = hasItem && itemIcon.sprite != null;
                itemIcon.color = State switch
                {
                    SlotState.ItemSelected => UIDarkTheme.ItemSlotSelectedContent,
                    SlotState.ItemIdle => UIDarkTheme.ItemSlotIdleContent,
                    _ => Color.clear
                };
                itemIcon.preserveAspect = true;
                itemIcon.raycastTarget = false;
            }

            ApplyTextColor();
        }

        // 직렬화된 참조가 없을 때 기존 자식 UI에서 참조를 보완한다.
        private void ResolveReferences()
        {
            button ??= GetComponent<Button>();
            background ??= GetComponent<Image>();
            itemIcon ??= FindItemIcon();
            labelText ??= GetComponentInChildren<TMP_Text>(true);
        }

        // 슬롯 배경이 아닌 자식 Image를 아이콘 표시용으로 찾는다.
        private Image FindItemIcon()
        {
            Image[] images = GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                Image image = images[i];
                if (image != null && image != background)
                {
                    return image;
                }
            }

            return null;
        }

        // 내부 텍스트의 선택/비선택 색을 슬롯 상태에 맞춘다.
        private void ApplyTextColor()
        {
            if (labelText == null)
            {
                return;
            }

            labelText.color = State switch
            {
                SlotState.ItemSelected => UIDarkTheme.ItemSlotSelectedContent,
                SlotState.ItemIdle => UIDarkTheme.ItemSlotIdleContent,
                _ => UIDarkTheme.ItemSlotEmptyContent
            };

            labelText.raycastTarget = false;
        }
    }
}
