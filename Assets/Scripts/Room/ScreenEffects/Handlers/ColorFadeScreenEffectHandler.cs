using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Escape.Rooms
{
    // 단색 오버레이를 이용한 검정·흰색·상태 색상 페이드를 처리한다.
    internal sealed class ColorFadeScreenEffectHandler : IRoomScreenEffectHandler
    {
        private static readonly ScreenEffectType[] SupportedTypes =
        {
            ScreenEffectType.FadeBlack,
        };

        private readonly RoomScreenEffectResources resources;
        private readonly float screenEffectDuration;
        private readonly float redFadeDuration;
        private readonly Color redFadeColor;
        private readonly float greenFadeDuration;
        private readonly Color greenFadeColor;

        public IReadOnlyList<ScreenEffectType> Types => SupportedTypes;

        // 공유 오버레이와 색상별 설정을 연결한다.
        public ColorFadeScreenEffectHandler(
            RoomScreenEffectResources resources,
            float screenEffectDuration,
            float redFadeDuration,
            Color redFadeColor,
            float greenFadeDuration,
            Color greenFadeColor)
        {
            this.resources = resources;
            this.screenEffectDuration = screenEffectDuration;
            this.redFadeDuration = redFadeDuration;
            this.redFadeColor = redFadeColor;
            this.greenFadeDuration = greenFadeDuration;
            this.greenFadeColor = greenFadeColor;
        }

        // 기본 검정 화면 전환을 실행한다.
        public UniTask PlayAsync(
            ScreenEffectType screenEffect,
            ScreenEffectPhase phase,
            CancellationToken cancellationToken)
        {
            return PlayBlackFadeAsync(phase, screenEffectDuration, cancellationToken);
        }

        // 기본 지속시간으로 검정 페이드를 실행한다.
        public UniTask PlayBlackFadeAsync(
            ScreenEffectPhase phase,
            CancellationToken cancellationToken)
        {
            return PlayBlackFadeAsync(phase, screenEffectDuration, cancellationToken);
        }

        // 지정한 지속시간으로 검정 페이드를 실행한다.
        public async UniTask PlayBlackFadeAsync(
            ScreenEffectPhase phase,
            float duration,
            CancellationToken cancellationToken)
        {
            Image image = resources.GetBlackImage();
            if (image == null || duration <= 0f)
            {
                if (phase == ScreenEffectPhase.FadeIn)
                {
                    resources.HideAll();
                }

                return;
            }

            if (phase == ScreenEffectPhase.FadeOut)
            {
                resources.SetBlackColorAlpha(Color.black, 0f);
                resources.ActivateBlackImage();
                await AnimateColorAlpha(
                    Color.black,
                    0f,
                    1f,
                    duration,
                    cancellationToken);
            }
            else
            {
                resources.ActivateBlackImage();
                resources.SetBlackColorAlpha(Color.black, 1f);
                resources.HideCaptureImage();
                await AnimateColorAlpha(
                    Color.black,
                    1f,
                    0f,
                    duration,
                    cancellationToken);
                resources.HideBlackImage();
            }
        }

        // 흰색 불투명 오버레이 페이드를 실행한다.
        public async UniTask PlayWhiteFadeAsync(
            bool fadeOn,
            CancellationToken cancellationToken)
        {
            if (resources.GetBlackImage() == null)
            {
                return;
            }

            resources.ActivateBlackImage();
            float fromAlpha = fadeOn ? 0f : 1f;
            float toAlpha = fadeOn ? 1f : 0f;
            resources.SetBlackColorAlpha(Color.white, fromAlpha);
            await AnimateColorAlpha(
                Color.white,
                fromAlpha,
                toAlpha,
                screenEffectDuration,
                cancellationToken);
            if (!fadeOn)
            {
                resources.HideBlackImage();
            }
        }

        // 붉은 상태 오버레이 페이드를 실행한다.
        public UniTask PlayRedFadeAsync(
            bool fadeOn,
            CancellationToken cancellationToken)
        {
            return PlayTintFadeAsync(
                redFadeColor,
                redFadeDuration,
                fadeOn,
                cancellationToken);
        }

        // 초록 상태 오버레이 페이드를 실행한다.
        public UniTask PlayGreenFadeAsync(
            bool fadeOn,
            CancellationToken cancellationToken)
        {
            return PlayTintFadeAsync(
                greenFadeColor,
                greenFadeDuration,
                fadeOn,
                cancellationToken);
        }

        // 페이드 없이 검정 화면을 표시한다.
        public void ShowBlackImmediate()
        {
            if (resources.GetBlackImage() == null)
            {
                return;
            }

            resources.ActivateBlackImage();
            resources.SetBlackColorAlpha(Color.black, 1f);
        }

        // 지정한 색상의 최대 알파까지 상태 페이드를 실행한다.
        private async UniTask PlayTintFadeAsync(
            Color color,
            float duration,
            bool fadeOn,
            CancellationToken cancellationToken)
        {
            if (resources.GetBlackImage() == null)
            {
                return;
            }

            float targetAlpha = Mathf.Clamp01(color.a);
            if (fadeOn)
            {
                resources.SetBlackColorAlpha(color, 0f);
                resources.ActivateBlackImage();
                await AnimateColorAlpha(
                    color,
                    0f,
                    targetAlpha,
                    duration,
                    cancellationToken);
                return;
            }

            resources.ActivateBlackImage();
            resources.SetBlackColorAlpha(color, targetAlpha);
            await AnimateColorAlpha(
                color,
                targetAlpha,
                0f,
                duration,
                cancellationToken);
            resources.HideBlackImage();
        }

        // 단색 오버레이 알파를 지정 시간 동안 보간한다.
        private async UniTask AnimateColorAlpha(
            Color color,
            float fromAlpha,
            float toAlpha,
            float duration,
            CancellationToken cancellationToken)
        {
            if (duration <= 0f)
            {
                resources.SetBlackColorAlpha(color, toAlpha);
                return;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                float progress = Mathf.Clamp01(elapsed / duration);
                float alpha = Mathf.Lerp(
                    fromAlpha,
                    toAlpha,
                    Mathf.SmoothStep(0f, 1f, progress));
                resources.SetBlackColorAlpha(color, alpha);
                elapsed += Time.deltaTime;
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }

            resources.SetBlackColorAlpha(color, toAlpha);
        }
    }
}
