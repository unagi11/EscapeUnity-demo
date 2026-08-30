using System;
using Escape.Progress;
using Escape.Audio;
using UnityEngine;
using UnityEngine.Serialization;

namespace Escape.Rooms
{
    // 하나의 아이템 조건에서 실행할 대사, 보상, 소비, 이동을 정의한다.
    [Serializable]
    public sealed class InteractionRule
    {
        public const string AnySelectedItemId = "Any";
        private const string EnterFirstFlagPrefix = "FLAG:ENTER_FIRST_";
        private const string EntranceVisitedFlagName = "FLAG:ENTER_FIRST_ENTRANCE";
        private const string OpenUtilityDoorFlagName = "FLAG:OPEN_UTILITY_DOOR";

        [TsvId("Assets/Resources/Data/item.tsv", AnySelectedItemId)]
        [SerializeField] private string selectedItemId;
        [TextArea(1, 3)]
        [SerializeField] private string comment;
        [SerializeField] private InteractionPriorityLayer priorityLayer = InteractionPriorityLayer.Default;
        [SerializeField] private int priorityNumber;
        [SerializeField] private GameObject[] activeObjects = Array.Empty<GameObject>();
        [SerializeField] private GameObject[] inactiveObjects = Array.Empty<GameObject>();

        // 인벤토리에 반드시 보유해야 하는 아이템 / 보유하면 안 되는 아이템 조건.
        [TsvId("Assets/Resources/Data/item.tsv")]
        [SerializeField] private string[] requiredItems = Array.Empty<string>();
        [TsvId("Assets/Resources/Data/item.tsv")]
        [SerializeField] private string[] absentItems = Array.Empty<string>();
        // 정보창에 반드시 보유해야 하는 정보 / 보유하면 안 되는 정보 조건.
        [TsvId("Assets/Resources/Data/info.tsv")]
        [SerializeField] private string[] requiredInfos = Array.Empty<string>();
        [TsvId("Assets/Resources/Data/info.tsv")]
        [SerializeField] private string[] absentInfos = Array.Empty<string>();

        [TsvId("Assets/Resources/Data/dialogue.tsv")]
        [FormerlySerializedAs("dialogueId")]
        [SerializeField] private string preDialogueId;

        [FormerlySerializedAs("prePresentation")]
        [FormerlySerializedAs("fadeOutPresentation")]
        [FormerlySerializedAs("screenPresentation")]
        [SerializeField] private ScreenEffectType screenEffect;
        [TsvId("Assets/Resources/Data/item.tsv")]
        [SerializeField] private string grantItem;
        [TsvId("Assets/Resources/Data/info.tsv")]
        [SerializeField] private string grantInfo;
        [TsvId("Assets/Resources/Data/info.tsv")]
        [SerializeField] private string[] grantInfos = Array.Empty<string>();
        [SerializeField] private bool consumeSelectedItem;
        [SerializeField] private bool deactivateTouchedObject;
        [SerializeField] private GameObject[] activateObjects = Array.Empty<GameObject>();
        [SerializeField] private GameObject[] deactivateObjects = Array.Empty<GameObject>();
        [SerializeField] private InteractionSpecialAction specialAction;
        [SerializeField] private RoomType transitionDestination;
        [SerializeField] private TouchSfxPreset touchSfx;
        [TsvId("Assets/Resources/Data/dialogue.tsv")]
        [SerializeField] private string actionDialogueId;

        [TsvId("Assets/Resources/Data/dialogue.tsv")]
        [SerializeField] private string postDialogueId;

        public string SelectedItemId => selectedItemId;
        public string Comment => comment;
        public InteractionPriorityLayer PriorityLayer => priorityLayer;
        public int PriorityNumber => priorityNumber;
        public GameObject[] ActiveObjects => activeObjects;
        public GameObject[] InactiveObjects => inactiveObjects;
        public string[] RequiredItems => requiredItems;
        public string[] AbsentItems => absentItems;
        public string[] RequiredInfos => requiredInfos;
        public string[] AbsentInfos => absentInfos;
        public string PreDialogueId => preDialogueId;
        public ScreenEffectType ScreenEffect => screenEffect;
        public string GrantItem => grantItem;
        public string GrantInfo => grantInfo;
        public string[] GrantInfos => grantInfos;
        public bool ConsumeSelectedItem => consumeSelectedItem;
        public bool DeactivateTouchedObject => deactivateTouchedObject;
        public GameObject[] ActivateObjects => activateObjects;
        public GameObject[] DeactivateObjects => deactivateObjects;
        public InteractionSpecialAction SpecialAction => specialAction;
        public RoomType TransitionDestination => transitionDestination;
        public TouchSfxPreset TouchSfx => touchSfx;
        public string ActionDialogueId => actionDialogueId;
        public string PostDialogueId => postDialogueId;

        // 현재 상태와 선택 아이템이 이 규칙의 조건을 만족하는지 확인한다.
        public InteractionPriorityLayer GetEffectivePriorityLayer(InteractionPriorityLayer defaultPriorityLayer)
        {
            return priorityLayer == InteractionPriorityLayer.Default
                ? defaultPriorityLayer
                : priorityLayer;
        }

        public static int ComparePriority(
            InteractionRule a,
            InteractionPriorityLayer aDefaultLayer,
            int aOrder,
            InteractionRule b,
            InteractionPriorityLayer bDefaultLayer,
            int bOrder)
        {
            if (ReferenceEquals(a, b))
            {
                return 0;
            }

            if (a == null)
            {
                return -1;
            }

            if (b == null)
            {
                return 1;
            }

            int layerCompare = a.GetEffectivePriorityLayer(aDefaultLayer)
                .CompareTo(b.GetEffectivePriorityLayer(bDefaultLayer));
            if (layerCompare != 0)
            {
                return layerCompare;
            }

            int numberCompare = a.priorityNumber.CompareTo(b.priorityNumber);
            if (numberCompare != 0)
            {
                return numberCompare;
            }

            return bOrder.CompareTo(aOrder);
        }

        public bool Matches(
            GameSession state,
            string currentSelectedItemId,
            bool allowAnySelectedItem = true)
        {
            bool matchesAnySelectedItem =
                string.Equals(selectedItemId, AnySelectedItemId, StringComparison.OrdinalIgnoreCase);
            if (matchesAnySelectedItem && !allowAnySelectedItem)
            {
                return false;
            }

            if (matchesAnySelectedItem && string.IsNullOrWhiteSpace(currentSelectedItemId))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(selectedItemId) &&
                !matchesAnySelectedItem &&
                !string.Equals(selectedItemId, currentSelectedItemId, StringComparison.Ordinal))
            {
                return false;
            }

            activeObjects ??= Array.Empty<GameObject>();
            for (int i = 0; i < activeObjects.Length; i++)
            {
                if (!MatchesObjectActiveCondition(activeObjects[i], true))
                {
                    return false;
                }
            }

            inactiveObjects ??= Array.Empty<GameObject>();
            for (int i = 0; i < inactiveObjects.Length; i++)
            {
                if (!MatchesObjectActiveCondition(inactiveObjects[i], false))
                {
                    return false;
                }
            }

            if (state == null)
            {
                bool hasRequiredItems = requiredItems != null && requiredItems.Length > 0;
                bool hasRequiredInfos = requiredInfos != null && requiredInfos.Length > 0;
                return !hasRequiredItems && !hasRequiredInfos;
            }

            // 지정한 아이템을 모두 보유해야 통과한다.
            requiredItems ??= Array.Empty<string>();
            for (int i = 0; i < requiredItems.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(requiredItems[i]) && !state.Items.Contains(requiredItems[i]))
                {
                    return false;
                }
            }

            // 지정한 아이템을 하나라도 보유하면 통과하지 못한다.
            absentItems ??= Array.Empty<string>();
            for (int i = 0; i < absentItems.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(absentItems[i]) && state.Items.Contains(absentItems[i]))
                {
                    return false;
                }
            }

            // 지정한 정보를 모두 보유해야 통과한다.
            requiredInfos ??= Array.Empty<string>();
            for (int i = 0; i < requiredInfos.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(requiredInfos[i]) && !state.Infos.Contains(requiredInfos[i]))
                {
                    return false;
                }
            }

            // 지정한 정보를 하나라도 보유하면 통과하지 못한다.
            absentInfos ??= Array.Empty<string>();
            for (int i = 0; i < absentInfos.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(absentInfos[i]) && state.Infos.Contains(absentInfos[i]))
                {
                    return false;
                }
            }

            return true;
        }

        internal static bool UsesFalseDefaultFlagState(GameObject target)
        {
            if (target == null || string.IsNullOrWhiteSpace(target.name))
            {
                return false;
            }

            return (target.name.StartsWith(EnterFirstFlagPrefix, StringComparison.Ordinal) &&
                    !string.Equals(target.name, EntranceVisitedFlagName, StringComparison.Ordinal)) ||
                string.Equals(target.name, OpenUtilityDoorFlagName, StringComparison.Ordinal);
        }

        private static bool MatchesObjectActiveCondition(GameObject target, bool requiresActive)
        {
            if (target == null)
            {
                return false;
            }

            bool active = target.activeSelf;
            if (UsesFalseDefaultFlagState(target))
            {
                active = !active;
            }

            return requiresActive ? active : !active;
        }
    }
}
