using System;
using Escape.SceneFlow;
using Escape.Progress;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Escape.UI;
using UnityEngine;

namespace Escape.Rooms
{
    // 별도 씬에서 실행되는 미니게임 진입을 담당한다.
    internal sealed class MiniGameSpecialActionHandler : IRoomSpecialActionHandler
    {
        private const string LockPickItemId = "lockpick_set";
        private static readonly InteractionSpecialAction[] SupportedActions =
        {
                InteractionSpecialAction.OpenHandcuffLockPick,
            };

        private readonly Func<RoomType> getCurrentRoomId;
        private readonly PlayerInventory inventory;
        private readonly float screenEffectDuration;

        public IReadOnlyList<InteractionSpecialAction> Actions => SupportedActions;

        // 미니게임 진입에 필요한 상태와 실행 함수만 연결한다.
        public MiniGameSpecialActionHandler(
            Func<RoomType> getCurrentRoomId,
            PlayerInventory inventory,
            float screenEffectDuration)
        {
            this.getCurrentRoomId = getCurrentRoomId;
            this.inventory = inventory;
            this.screenEffectDuration = screenEffectDuration;
        }

        // 미니게임 종류에 맞는 전환 흐름을 실행한다.
        public async UniTask ExecuteAsync(
            InteractionSpecialAction action,
            RoomInteractor interactable,
            CancellationToken cancellationToken)
        {
            if (action != InteractionSpecialAction.OpenHandcuffLockPick)
            {
                return;
            }

            if (inventory == null || !inventory.HasItem(LockPickItemId))
            {
                Debug.LogWarning(
                    "Handcuff lockpick interaction requires the dialogue effect to grant the lockpick tool.");
                return;
            }

            await PlayLockPickTransition(LockPickUnlockTarget.Handcuffs, cancellationToken);
        }

        // 해정 대상과 현재 방을 보존해 미니게임으로 전환한다.
        private UniTask PlayLockPickTransition(
            LockPickUnlockTarget target,
            CancellationToken cancellationToken)
        {
            return PlayMiniGameTransition(
                () => EscapeSceneLoader.LoadLockPickMiniGame(target, getCurrentRoomId()),
                cancellationToken);
        }

        // 공통 나선 전환 연출 뒤 미니게임 씬 로더를 실행한다.
        private UniTask PlayMiniGameTransition(
            Action loadScene,
            CancellationToken cancellationToken)
        {
            return SceneTransitionFadeUI.PlaySpiralTransitionAsync(
                loadScene,
                screenEffectDuration,
                cancellationToken);
        }
    }
}
