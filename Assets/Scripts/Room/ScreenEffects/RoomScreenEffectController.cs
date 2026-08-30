using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Escape.Rooms
{
    // 화면 전환과 상태 오버레이 요청을 기능별 핸들러에 전달한다.
    internal sealed class RoomScreenEffectController :
        IDialogueScreenEffectPlayer,
        IDisposable
    {
        private readonly Dictionary<ScreenEffectType, IRoomScreenEffectHandler> handlers = new();
        private readonly RoomScreenEffectResources resources;
        private readonly ColorFadeScreenEffectHandler colorFadeHandler;
        private readonly ResolutionFadeScreenEffectHandler resolutionFadeHandler;
        private readonly TransitionMaskScreenEffectHandler transitionMaskHandler;
        private readonly float slowBlackFadeDuration;
        private bool isPlayingScreenEffect;

        public bool IsPlaying => isPlayingScreenEffect;

        // 직렬화된 화면 효과 참조와 설정값으로 공유 자원과 기능별 핸들러를 구성한다.
        public RoomScreenEffectController(
            MonoBehaviour owner,
            RawImage roomImage,
            float screenEffectDuration,
            float redFadeDuration,
            Color redFadeColor,
            float greenFadeDuration,
            Color greenFadeColor,
            float slowBlackFadeDuration,
            float resolutionFadeDuration)
        {
            resources = new RoomScreenEffectResources(owner, roomImage);
            colorFadeHandler = new ColorFadeScreenEffectHandler(
                resources,
                screenEffectDuration,
                redFadeDuration,
                redFadeColor,
                greenFadeDuration,
                greenFadeColor);
            resolutionFadeHandler = new ResolutionFadeScreenEffectHandler(
                resources,
                resolutionFadeDuration);
            var blackSlideHandler = new BlackSlideScreenEffectHandler(
                resources,
                screenEffectDuration);
            transitionMaskHandler = new TransitionMaskScreenEffectHandler(
                resources,
                screenEffectDuration);

            Register(colorFadeHandler);
            Register(resolutionFadeHandler);
            Register(blackSlideHandler);
            Register(transitionMaskHandler);

            this.slowBlackFadeDuration = Mathf.Max(5f, slowBlackFadeDuration);
        }

        // 상호작용 단계에 지정된 화면 효과를 실행한다.
        public async UniTask PlayAsync(
            ScreenEffectType screenEffect,
            bool fadeOut,
            CancellationToken cancellationToken)
        {
            isPlayingScreenEffect = true;
            try
            {
                if (handlers.TryGetValue(screenEffect, out IRoomScreenEffectHandler handler))
                {
                    await handler.PlayAsync(
                        screenEffect,
                        fadeOut ? ScreenEffectPhase.FadeOut : ScreenEffectPhase.FadeIn,
                        cancellationToken);
                }
                else if (!fadeOut)
                {
                    resources.HideAll();
                }
            }
            finally
            {
                isPlayingScreenEffect = false;
            }
        }

        // 대사가 요청한 즉시 화면 효과를 적용한다.
        public void ApplyImmediate(DialogueScreenEffectCue cue)
        {
            if (cue.Kind != DialogueScreenEffectKind.Blackout ||
                cue.Speed != DialogueScreenEffectSpeed.Instant)
            {
                throw new ArgumentException("Only instant blackout cues can be applied synchronously.");
            }

            if (cue.State == DialogueScreenEffectState.Show)
            {
                colorFadeHandler.ShowBlackImmediate();
            }
            else
            {
                resources.HideAll();
            }
        }

        // 대사가 요청한 의미를 실제 화면 효과와 지속시간으로 변환해 실행한다.
        public async UniTask PlayAsync(
            DialogueScreenEffectCue cue,
            CancellationToken cancellationToken)
        {
            if (cue.Speed == DialogueScreenEffectSpeed.Instant)
            {
                ApplyImmediate(cue);
                return;
            }

            isPlayingScreenEffect = true;
            try
            {
                await PlayDialogueEffectAsync(cue, cancellationToken);
            }
            finally
            {
                isPlayingScreenEffect = false;
            }
        }

        // 대사 의미에 맞는 색상 효과를 선택하고 실제 설정값은 내부에 숨긴다.
        private UniTask PlayDialogueEffectAsync(
            DialogueScreenEffectCue cue,
            CancellationToken cancellationToken)
        {
            bool show = cue.State == DialogueScreenEffectState.Show;
            switch (cue.Kind)
            {
                case DialogueScreenEffectKind.Blackout:
                    ScreenEffectPhase phase = show
                        ? ScreenEffectPhase.FadeOut
                        : ScreenEffectPhase.FadeIn;
                    return cue.Speed == DialogueScreenEffectSpeed.Slow
                        ? colorFadeHandler.PlayBlackFadeAsync(
                            phase,
                            slowBlackFadeDuration,
                            cancellationToken)
                        : colorFadeHandler.PlayBlackFadeAsync(phase, cancellationToken);

                case DialogueScreenEffectKind.Whiteout:
                    return colorFadeHandler.PlayWhiteFadeAsync(show, cancellationToken);

                case DialogueScreenEffectKind.DangerOverlay:
                    return colorFadeHandler.PlayRedFadeAsync(show, cancellationToken);

                case DialogueScreenEffectKind.RecoveryOverlay:
                    return colorFadeHandler.PlayGreenFadeAsync(show, cancellationToken);

                default:
                    throw new ArgumentOutOfRangeException(nameof(cue));
            }
        }

        // 핸들러가 담당하는 화면 효과를 중복 없이 조회표에 등록한다.
        private void Register(IRoomScreenEffectHandler handler)
        {
            foreach (ScreenEffectType type in handler.Types)
            {
                if (!handlers.TryAdd(type, handler))
                {
                    throw new InvalidOperationException(
                        $"Duplicate room screen effect handler: {type}");
                }
            }
        }

        // 핸들러 전용 머티리얼과 공유 그래픽 자원을 해제한다.
        public void Dispose()
        {
            resolutionFadeHandler.Dispose();
            transitionMaskHandler.Dispose();
            resources.Dispose();
        }
    }
}
