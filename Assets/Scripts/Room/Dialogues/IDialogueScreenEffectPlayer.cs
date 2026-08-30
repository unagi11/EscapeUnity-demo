using System.Threading;
using Cysharp.Threading.Tasks;

namespace Escape.Rooms
{
    // 대사가 요청할 수 있는 화면 효과의 의미를 구분한다.
    internal enum DialogueScreenEffectKind
    {
        Blackout,
        Whiteout,
        DangerOverlay,
        RecoveryOverlay,
    }

    // 화면 효과를 표시할지 숨길지 구분한다.
    internal enum DialogueScreenEffectState
    {
        Show,
        Hide,
    }

    // 화면 효과의 전환 속도를 구분한다.
    internal enum DialogueScreenEffectSpeed
    {
        Instant,
        Normal,
        Slow,
    }

    // 대사가 화면 효과 시스템에 전달하는 한 번의 요청이다.
    internal readonly struct DialogueScreenEffectCue
    {
        public DialogueScreenEffectKind Kind { get; }
        public DialogueScreenEffectState State { get; }
        public DialogueScreenEffectSpeed Speed { get; }

        public DialogueScreenEffectCue(
            DialogueScreenEffectKind kind,
            DialogueScreenEffectState state,
            DialogueScreenEffectSpeed speed = DialogueScreenEffectSpeed.Normal)
        {
            Kind = kind;
            State = state;
            Speed = speed;
        }
    }

    // DialoguePlayer가 화면 효과를 요청할 때 사용하는 최소 계약이다.
    internal interface IDialogueScreenEffectPlayer
    {
        // 대기할 필요가 없는 화면 효과를 즉시 적용한다.
        void ApplyImmediate(DialogueScreenEffectCue cue);

        // 화면 효과의 구체적인 색상과 지속시간은 구현체에 맡기고 재생한다.
        UniTask PlayAsync(
            DialogueScreenEffectCue cue,
            CancellationToken cancellationToken);
    }
}
