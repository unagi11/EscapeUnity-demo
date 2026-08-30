using System;
using Escape.Progress;
using UnityEngine;

namespace Escape.Rooms
{
    // 상호작용으로 이동할 방을 지정한다.
    public enum RoomType
    {
        None = 0,
        LivingRoom = 1,
        BedRoom = 2,
        KitchenRoom = 3,
        EntranceRoom = 4,
        UtilityRoom = 5,
    }

    // 상호작용 전후에 실행할 화면 효과를 지정한다.
    public enum ScreenEffectType
    {
        None = 0,
        ResolutionFade = 1,
        FadeBlack = 2,
        BlackSlideFromLeft = 3,
        BlackSlideFromRight = 4,
        Instant = 5,
        [InspectorName("Transition Effect/Spiral Pixel")]
        TransitionSpiralPixel = 6,
        [InspectorName("Transition Effect/Fade Right")]
        TransitionFadeRight = 7,
        [InspectorName("Transition Effect/Fade Down")]
        TransitionFadeDown = 8,
        [InspectorName("Transition Effect/Vignette Radial")]
        TransitionVignetteRadial = 9,
        [InspectorName("Transition Effect/Spiral Inward")]
        TransitionSpiralInward = 10,
        [InspectorName("Transition Effect/Fade Left")]
        TransitionFadeLeft = 11,
        [InspectorName("Transition Effect/Fade Up")]
        TransitionFadeUp = 12
    }

    // UnityEvent 대신 상호작용 시퀀스가 직접 실행할 특수 액션을 지정한다.
    public enum InteractionSpecialAction
    {
        None = 0,
        DoorLockPress0 = 1,
        DoorLockPress1 = 2,
        DoorLockPress2 = 3,
        DoorLockPress3 = 4,
        DoorLockPress4 = 5,
        DoorLockPress5 = 6,
        DoorLockPress6 = 7,
        DoorLockPress7 = 8,
        DoorLockPress8 = 9,
        DoorLockPress9 = 10,
        DoorLockConfirm = 11,
        DoorLockReset = 12,
        SofaRestPrompt = 13,
        RecoveryFoodPrompt = 14,
        FlashlightAtPointer = 15,
        CycleLockNumber = 16,
        TryOpenChainLock = 17,
        ReadBook = 18,
        TelevisionPrompt = 19,
        OpenMiniGame = 20,
        OpenChainLockPopup = 21,
        PullChainLock = 22,
        PlayEntranceEnding = 23,
        OpenRythmRecycle = 24,
        OpenLockPick = 25,
        OpenUtilityDoorLockPick = 26,
        CycleDrawerDiaryLockNumber = 27,
        TryOpenDrawerDiaryLock = 28,
        StopWasherPrompt = 29,
        PlaySelfDefenseEnding = 30,
        PlayGoodDayEnding = 31,
        PlayOrthodoxEnding = 32,
        BreakKeyLockTimingCheck = 33,
        OpenEntranceLockPick = 34,
        RevealDoorLockFingerprints = 35,
        PullFlourBag = 36,
        OpenHandcuffLockPick = 37,
        OpenBedInteractionMenu = 38,
    }

    // 상호작용 규칙을 비교할 기본 우선순위 계층이다.
    public enum InteractionPriorityLayer
    {
        Default = 0,
        Fallback = 10,
        Room = 20,
        Object = 30,
        Override = 40,
    }

    [DisallowMultipleComponent]
    public sealed class RoomInteractor : MonoBehaviour
    {
        private const string LockNumberRootPrefix = "drw_locknum_";
        private const string LockNumberBackgroundName = "drw_locknum_bg";
        private const string LockNumberChildPrefix = "drw_num_";
        private const int LockNumberCount = 10;

        [SerializeField] private InteractionRule[] itemInteractions = Array.Empty<InteractionRule>();

        public InteractionRule[] ItemInteractions => itemInteractions;

        // 잠금 숫자 상호작용이면 시작 숫자 하나만 보이도록 정리한다.
        private void Awake()
        {
            InitializeLockNumberDisplay();
        }

        // 현재 게임 상태에 맞는 첫 번째 상호작용 규칙을 찾는다.
        public InteractionRule ResolveInteraction(
            GameSession state,
            bool allowAnySelectedItem = true,
            InteractionPriorityLayer defaultPriorityLayer = InteractionPriorityLayer.Object)
        {
            string selectedItemId = state != null ? state.SelectedItemId : string.Empty;
            InteractionRule bestRule = null;
            int bestOrder = -1;
            for (int i = 0; i < itemInteractions.Length; i++)
            {
                InteractionRule rule = itemInteractions[i];
                if (rule != null && rule.Matches(state, selectedItemId, allowAnySelectedItem))
                {
                    if (bestRule == null ||
                        InteractionRule.ComparePriority(
                            rule,
                            defaultPriorityLayer,
                            i,
                            bestRule,
                            defaultPriorityLayer,
                            bestOrder) > 0)
                    {
                        bestRule = rule;
                        bestOrder = i;
                    }
                }
            }

            return bestRule;
        }

        // 잠금 숫자를 0 다음 1, ..., 9 다음 0 순서로 한 칸 넘긴다.
        public bool TryCycleLockNumberDisplay(
            string correctCombination,
            bool isPulled,
            out bool blockedByPull)
        {
            return TryCycleLockNumberDisplay(
                correctCombination,
                string.Empty,
                isPulled,
                out blockedByPull);
        }

        // 지정된 맞물림 순서에서 앞선 숫자가 모두 맞았을 때 현재 숫자판을 고정한다.
        public bool TryCycleLockNumberDisplay(
            string correctCombination,
            string lockOrder,
            bool isPulled,
            out bool blockedByPull)
        {
            blockedByPull = false;
            if (!TryGetLockNumberRoot(out Transform root))
            {
                return false;
            }

            if (isPulled && IsLockNumberBlockedByPull(root, correctCombination, lockOrder))
            {
                blockedByPull = true;
                return false;
            }

            int currentNumber = FindVisibleLockNumber(root);
            int nextNumber = (Mathf.Max(currentNumber, 0) + 1) % LockNumberCount;
            SetVisibleLockNumber(root, nextNumber);
            return true;
        }

        // 같은 팝업 안의 잠금 숫자들을 왼쪽부터 읽는다.
        public bool TryReadLockNumberCombination(out string combination)
        {
            combination = string.Empty;
            if (!TryGetLockNumberPopupRoot(out Transform popupRoot))
            {
                return false;
            }

            int slotCount = CountLockNumberSlots(popupRoot);
            if (slotCount <= 0)
            {
                return false;
            }

            var digits = new char[slotCount];
            for (int i = 0; i < digits.Length; i++)
            {
                Transform slot = popupRoot.Find($"{LockNumberRootPrefix}{i + 1}");
                if (slot == null)
                {
                    return false;
                }

                int number = FindVisibleLockNumber(slot);
                if (number < 0)
                {
                    return false;
                }

                digits[i] = (char)('0' + number);
            }

            combination = new string(digits);
            return true;
        }

        // 숫자 배경이나 확인 오브젝트에서 잠금 숫자 팝업 루트를 찾는다.
        private bool TryGetLockNumberPopupRoot(out Transform popupRoot)
        {
            popupRoot = null;
            if (TryGetLockNumberRoot(out Transform numberRoot) && numberRoot.parent != null)
            {
                popupRoot = numberRoot.parent;
                return true;
            }

            Transform parent = transform.parent;
            if (parent == null)
            {
                return false;
            }

            if (CountLockNumberSlots(parent) <= 0)
            {
                return false;
            }

            popupRoot = parent;
            return true;
        }

        // 팝업에 연속으로 배치된 잠금 숫자 자리 수를 센다.
        private static int CountLockNumberSlots(Transform popupRoot)
        {
            if (popupRoot == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 1; ; i++)
            {
                if (popupRoot.Find($"{LockNumberRootPrefix}{i}") == null)
                {
                    return count;
                }

                count++;
            }
        }

        // 당김 상태에서는 회차별 맞물림 순서에 따라 정답 숫자판이 차례로 움직이지 않는다.
        private bool IsLockNumberBlockedByPull(
            Transform root,
            string correctCombination,
            string lockOrder)
        {
            if (root == null ||
                string.IsNullOrWhiteSpace(correctCombination) ||
                !TryGetLockNumberPopupRoot(out Transform popupRoot))
            {
                return false;
            }

            int slotIndex = GetLockNumberSlotIndex(root);
            if (slotIndex < 1 || slotIndex > correctCombination.Length)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(lockOrder))
            {
                lockOrder = string.Empty;
                for (int i = correctCombination.Length; i >= 1; i--)
                {
                    lockOrder += (char)('0' + i);
                }
            }

            int orderIndex = lockOrder.IndexOf((char)('0' + slotIndex));
            if (orderIndex < 0)
            {
                return false;
            }

            for (int i = 0; i <= orderIndex; i++)
            {
                int requiredSlotIndex = lockOrder[i] - '0';
                if (requiredSlotIndex < 1 || requiredSlotIndex > correctCombination.Length)
                {
                    return false;
                }

                Transform slot = popupRoot.Find($"{LockNumberRootPrefix}{requiredSlotIndex}");
                if (slot == null ||
                    FindVisibleLockNumber(slot) != correctCombination[requiredSlotIndex - 1] - '0')
                {
                    return false;
                }
            }

            return true;
        }

        // drw_locknum_N 이름에서 N을 읽어 자리 번호로 사용한다.
        private static int GetLockNumberSlotIndex(Transform root)
        {
            if (root == null || string.IsNullOrWhiteSpace(root.name))
            {
                return -1;
            }

            string suffix = root.name.StartsWith(LockNumberRootPrefix, StringComparison.Ordinal)
                ? root.name.Substring(LockNumberRootPrefix.Length)
                : string.Empty;
            return int.TryParse(suffix, out int slotIndex) ? slotIndex : -1;
        }

        // 현재 활성 숫자가 없거나 여러 개면 0 하나만 켜진 상태로 맞춘다.
        private void InitializeLockNumberDisplay()
        {
            if (!TryGetLockNumberRoot(out Transform root))
            {
                return;
            }

            int currentNumber = FindVisibleLockNumber(root);
            SetVisibleLockNumber(root, Mathf.Max(currentNumber, 0));
        }

        // 잠금 숫자 그룹의 배경 오브젝트인지 확인하고 부모 그룹을 돌려준다.
        private bool TryGetLockNumberRoot(out Transform root)
        {
            root = null;
            if (!string.Equals(name, LockNumberBackgroundName, StringComparison.Ordinal))
            {
                return false;
            }

            Transform parent = transform.parent;
            if (parent == null ||
                !parent.name.StartsWith(LockNumberRootPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            root = parent;
            return true;
        }

        // 현재 보이는 숫자 하나를 찾는다.
        private static int FindVisibleLockNumber(Transform root)
        {
            for (int i = 0; i < LockNumberCount; i++)
            {
                Transform number = root.Find($"{LockNumberChildPrefix}{i}");
                if (number != null && number.gameObject.activeSelf)
                {
                    return i;
                }
            }

            return -1;
        }

        // 지정한 숫자만 활성화하고 나머지 숫자 레이어를 숨긴다.
        private static void SetVisibleLockNumber(Transform root, int visibleNumber)
        {
            for (int i = 0; i < LockNumberCount; i++)
            {
                Transform number = root.Find($"{LockNumberChildPrefix}{i}");
                if (number != null)
                {
                    number.gameObject.SetActive(i == visibleNumber);
                }
            }
        }
    }

}
