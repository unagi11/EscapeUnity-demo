using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Escape.Rooms
{
    // 특수 행동 핸들러가 제공해야 하는 실행 계약이다.
    internal interface IRoomSpecialActionHandler
    {
        IReadOnlyList<InteractionSpecialAction> Actions { get; }

        // 자신이 담당하는 특수 행동을 실행한다.
        UniTask ExecuteAsync(
            InteractionSpecialAction action,
            RoomInteractor interactable,
            CancellationToken cancellationToken);
    }

    // 사전 대사 선택 결과에 따라 실행 여부를 결정하는 선택적 계약이다.
    internal interface IConditionalRoomSpecialActionHandler
    {
        // 상태 변경 전에 특수 행동을 계속 실행할지 판단한다.
        bool ShouldExecute(
            InteractionSpecialAction action,
            DialogueStoryResult preDialogueResult);
    }
}
