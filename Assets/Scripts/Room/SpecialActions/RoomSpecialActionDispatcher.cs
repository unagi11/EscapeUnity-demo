using System;
using System.Collections.Generic;

namespace Escape.Rooms
{
    // 특수 행동을 담당 기능의 핸들러로 전달한다.
    internal sealed class RoomSpecialActionDispatcher
    {
        private readonly Dictionary<InteractionSpecialAction, IRoomSpecialActionHandler> handlers = new();

        // 중복 액션을 검증하며 핸들러 조회표를 만든다.
        public RoomSpecialActionDispatcher(params IRoomSpecialActionHandler[] actionHandlers)
        {
            foreach (IRoomSpecialActionHandler handler in actionHandlers)
            {
                foreach (InteractionSpecialAction action in handler.Actions)
                {
                    if (!handlers.TryAdd(action, handler))
                    {
                        throw new InvalidOperationException($"Duplicate special action handler: {action}");
                    }
                }
            }
        }

        // 주어진 행동을 담당하는 핸들러를 반환한다.
        public bool TryGetHandler(
            InteractionSpecialAction action,
            out IRoomSpecialActionHandler handler)
        {
            return handlers.TryGetValue(action, out handler);
        }

        // 담당 핸들러가 사전 조건을 제공하면 상태 변경 전에 실행 여부를 확인한다.
        public bool ShouldExecute(
            InteractionSpecialAction action,
            DialogueStoryResult preDialogueResult)
        {
            return !handlers.TryGetValue(action, out IRoomSpecialActionHandler handler) ||
                handler is not IConditionalRoomSpecialActionHandler conditionalHandler ||
                conditionalHandler.ShouldExecute(action, preDialogueResult);
        }
    }
}
