using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Escape.Rooms
{
    // 검정 오버레이를 좌우로 이동시키는 슬라이드 화면 전환을 처리한다.
    internal sealed class BlackSlideScreenEffectHandler : IRoomScreenEffectHandler
    {
        private static readonly ScreenEffectType[] SupportedTypes =
        {
            ScreenEffectType.BlackSlideFromLeft,
            ScreenEffectType.BlackSlideFromRight,
        };

        private readonly RoomScreenEffectResources resources;
        private readonly float duration;

        public IReadOnlyList<ScreenEffectType> Types => SupportedTypes;

        // 공유 검정 오버레이와 슬라이드 지속시간을 연결한다.
        public BlackSlideScreenEffectHandler(
            RoomScreenEffectResources resources,
            float duration)
        {
            this.resources = resources;
            this.duration = duration;
        }

        // 타입의 방향에 맞춰 검정 오버레이를 진입시키거나 퇴장시킨다.
        public async UniTask PlayAsync(
            ScreenEffectType screenEffect,
            ScreenEffectPhase phase,
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

            float direction = screenEffect == ScreenEffectType.BlackSlideFromLeft
                ? -1f
                : 1f;
            RectTransform rectTransform = image.rectTransform;
            float width = Mathf.Max(1f, rectTransform.rect.width);
            float startX = width * direction;

            resources.ActivateBlackImage();
            resources.SetBlackColorAlpha(Color.black, 1f);
            if (phase == ScreenEffectPhase.FadeOut)
            {
                await AnimateAsync(
                    rectTransform,
                    startX,
                    0f,
                    cancellationToken);
            }
            else
            {
                rectTransform.anchoredPosition = Vector2.zero;
                resources.HideCaptureImage();
                await AnimateAsync(
                    rectTransform,
                    0f,
                    -startX,
                    cancellationToken);
                resources.HideBlackImage();
            }
        }

        // 검정 오버레이의 X 좌표를 지정 시간 동안 보간한다.
        private async UniTask AnimateAsync(
            RectTransform rectTransform,
            float fromX,
            float toX,
            CancellationToken cancellationToken)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                float progress = Mathf.Clamp01(elapsed / duration);
                float x = Mathf.Lerp(
                    fromX,
                    toX,
                    Mathf.SmoothStep(0f, 1f, progress));
                rectTransform.anchoredPosition = new Vector2(x, 0f);
                elapsed += Time.deltaTime;
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }

            rectTransform.anchoredPosition = new Vector2(toX, 0f);
        }
    }
}
