using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Escape.Data;
using Escape.UI;
using UnityEngine;

namespace Escape.Rooms
{
    // 마지막 조사 좌표로 현재 방의 손전등을 이동한다.
    internal sealed class FlashlightSpecialActionHandler : IRoomSpecialActionHandler
    {
        private const float MoveDuration = 0.18f;
        private static readonly InteractionSpecialAction[] SupportedActions =
        {
            InteractionSpecialAction.FlashlightAtPointer,
        };

        private readonly Func<Transform> getCurrentRoom;
        private readonly Func<bool> hasLastInspectWorldPoint;
        private readonly Func<Vector2> getLastInspectWorldPoint;
        private readonly Func<Transform, bool> isFlashlightTransform;

        public IReadOnlyList<InteractionSpecialAction> Actions => SupportedActions;

        // 현재 방과 마지막 조사 좌표 조회 수단을 연결한다.
        public FlashlightSpecialActionHandler(
            Func<Transform> getCurrentRoom,
            Func<bool> hasLastInspectWorldPoint,
            Func<Vector2> getLastInspectWorldPoint,
            Func<Transform, bool> isFlashlightTransform)
        {
            this.getCurrentRoom = getCurrentRoom;
            this.hasLastInspectWorldPoint = hasLastInspectWorldPoint;
            this.getLastInspectWorldPoint = getLastInspectWorldPoint;
            this.isFlashlightTransform = isFlashlightTransform;
        }

        // 마지막 조사 좌표까지 현재 방의 모든 손전등을 부드럽게 이동한다.
        public async UniTask ExecuteAsync(
            InteractionSpecialAction action,
            RoomInteractor interactable,
            CancellationToken cancellationToken)
        {
            Transform currentRoom = getCurrentRoom();
            if (!hasLastInspectWorldPoint() || currentRoom == null)
            {
                return;
            }

            Vector2 targetWorldPoint = getLastInspectWorldPoint();
            Transform[] transforms = currentRoom.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform target = transforms[i];
                if (!isFlashlightTransform(target))
                {
                    continue;
                }

                await MoveFlashlight(target, targetWorldPoint, cancellationToken);
            }
        }

        // 손전등 하나를 지정한 월드 좌표로 이동한다.
        private static async UniTask MoveFlashlight(
            Transform target,
            Vector2 targetWorldPoint,
            CancellationToken cancellationToken)
        {
            Vector3 startPosition = target.position;
            Vector3 targetPosition = new(targetWorldPoint.x, targetWorldPoint.y, startPosition.z);
            target.gameObject.SetActive(true);

            float elapsed = 0f;
            while (elapsed < MoveDuration)
            {
                float progress = Mathf.Clamp01(elapsed / MoveDuration);
                target.position = Vector3.Lerp(startPosition, targetPosition, progress);
                elapsed += Time.deltaTime;
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }

            target.position = targetPosition;
        }
    }
}
