using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Escape.Rooms
{
    // 화면 효과의 진행 방향을 구분한다.
    internal enum ScreenEffectPhase
    {
        FadeOut,
        FadeIn
    }

    // 화면 효과 기능군이 제공해야 하는 실행 계약이다.
    internal interface IRoomScreenEffectHandler
    {
        IReadOnlyList<ScreenEffectType> Types { get; }

        // 자신이 담당하는 화면 효과를 실행한다.
        UniTask PlayAsync(
            ScreenEffectType screenEffect,
            ScreenEffectPhase phase,
            CancellationToken cancellationToken);
    }
}
