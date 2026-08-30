using System;
using Escape.Localization;
using System.Threading;
using Cysharp.Threading.Tasks;
using Escape.Rooms;
using TMPro;
using UnityEngine;

namespace Escape.UI
{
    // 방 이동 시 현재 장소명을 잠깐 보여주는 패널을 관리한다.
    public sealed class LocationPanelUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text locationText;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField, Min(0f)] private float fadeInSeconds = 0f;
        [SerializeField, Min(0f)] private float holdSeconds = 2f;
        [SerializeField, Min(0f)] private float fadeOutSeconds = 0.45f;

        private CancellationTokenSource showCts;

        private void Awake()
        {
            SetVisibleImmediate(false);
        }

        private void OnDestroy()
        {
            showCts?.Cancel();
            showCts?.Dispose();
            showCts = null;
        }

        // 지정된 방 이름으로 패널을 페이드 인, 유지, 페이드 아웃한다.
        public void Show(RoomType roomType)
        {
            string locationName = GetLocationName(roomType);
            if (string.IsNullOrEmpty(locationName))
            {
                return;
            }

            gameObject.SetActive(true);

            showCts?.Cancel();
            showCts?.Dispose();
            showCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            ShowAsync(locationName, showCts.Token).Forget();
        }

        private async UniTaskVoid ShowAsync(string locationName, CancellationToken ct)
        {
            if (locationText != null)
            {
                locationText.text = locationName;
            }

            await Fade(1f, fadeInSeconds, ct);

            if (holdSeconds > 0f)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(holdSeconds), ignoreTimeScale: true, cancellationToken: ct);
            }

            await Fade(0f, fadeOutSeconds, ct);
            SetVisibleImmediate(false);
        }

        private async UniTask Fade(float targetAlpha, float duration, CancellationToken ct)
        {
            if (canvasGroup == null || duration <= 0f)
            {
                SetAlpha(targetAlpha);
                return;
            }

            float startAlpha = canvasGroup.alpha;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                t = t * t * (3f - 2f * t);
                SetAlpha(Mathf.Lerp(startAlpha, targetAlpha, t));
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            SetAlpha(targetAlpha);
        }

        private void SetVisibleImmediate(bool visible)
        {
            SetAlpha(visible ? 1f : 0f);
        }

        private void SetAlpha(float alpha)
        {
            if (canvasGroup == null)
            {
                return;
            }

            canvasGroup.alpha = alpha;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        private static string GetLocationName(RoomType roomType)
        {
            return roomType switch
            {
                RoomType.LivingRoom => LocalizationService.Text("room_living", "거실"),
                RoomType.BedRoom => LocalizationService.Text("room_bedroom", "침실"),
                RoomType.KitchenRoom => LocalizationService.Text("room_kitchen", "부엌"),
                RoomType.EntranceRoom => LocalizationService.Text("room_entrance", "현관"),
                RoomType.UtilityRoom => LocalizationService.Text("room_utility", "다용도실"),
                _ => string.Empty
            };
        }
    }
}
