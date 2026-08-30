using System;
using Escape.Audio;
using Escape.Localization;
using Escape.Progress;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Escape.Data;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Escape.UI
{
    // 도전과제 달성 순간에 TopUICanvas의 알림 패널을 슬라이드 표시한다.
    public sealed class AchievementToastUI : MonoBehaviour
    {
        private const string AchievementResourcePath = "Data/achievement";
        private const string AchievementIconResourcePath = "Sprites/icon_achv";
        private const string IconAchvPrefix = "icon_achv_";
        private const string LegacyIconPrefix = "achievement_";
        private const float EntranceStartScale = 0.92f;
        private static readonly Color32 AchievementTitleColor = new(255, 214, 64, 255);

        [SerializeField] private RectTransform panelRect;
        [FormerlySerializedAs("messageText")]
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text descText;
        [SerializeField] private Image iconImage;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField, Min(0f)] private float slideDistance = 110f;
        [SerializeField, Min(0f)] private float slideInSeconds = 0.32f;
        [SerializeField, Min(0f)] private float holdSeconds = 3f;
        [SerializeField, Min(0f)] private float slideOutSeconds = 0.24f;
        [SerializeField] private string unlockSfxId = "achievement_unlock";

        private readonly Queue<string> pendingIds = new();
        private readonly Dictionary<int, Sprite> iconsByNo = new();
        private TsvTable<Achievement> achievementTable;
        private CancellationTokenSource showCts;
        private Vector2 visiblePosition;
        private Vector2 hiddenPosition;
        private Vector3 visibleScale;
        private bool isShowing;

        private void Awake()
        {
            achievementTable = new TsvDataLoader<Achievement>().LoadTable(AchievementResourcePath);
            LoadAchievementIcons();
            visiblePosition = panelRect != null ? panelRect.anchoredPosition : Vector2.zero;
            hiddenPosition = visiblePosition + Vector2.right * GetHiddenOffset();
            visibleScale = panelRect != null ? panelRect.localScale : Vector3.one;
            SetVisibleImmediate(false);
        }

        private void OnEnable()
        {
            AchievementProgress.Unlocked += Enqueue;
        }

        private void OnDisable()
        {
            AchievementProgress.Unlocked -= Enqueue;
            showCts?.Cancel();
        }

        private void OnDestroy()
        {
            showCts?.Cancel();
            showCts?.Dispose();
            showCts = null;
        }

        // 새로 달성한 도전과제를 순서대로 표시한다.
        private void Enqueue(string achievementId)
        {
            if (string.IsNullOrWhiteSpace(achievementId))
            {
                return;
            }

            pendingIds.Enqueue(achievementId.Trim());
            if (!isShowing)
            {
                PlayQueueAsync(destroyCancellationToken).Forget();
            }
        }

        private async UniTaskVoid PlayQueueAsync(CancellationToken destroyToken)
        {
            isShowing = true;
            showCts?.Cancel();
            showCts?.Dispose();
            showCts = CancellationTokenSource.CreateLinkedTokenSource(destroyToken);
            CancellationToken ct = showCts.Token;

            try
            {
                while (pendingIds.Count > 0)
                {
                    string achievementId = pendingIds.Dequeue();
                    if (!TryGetAchievement(achievementId, out Achievement achievement))
                    {
                        continue;
                    }

                    await ShowAsync(achievement, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
            }
            finally
            {
                SetVisibleImmediate(false);
                isShowing = false;
            }
        }

        private async UniTask ShowAsync(Achievement achievement, CancellationToken ct)
        {
            ApplyAchievement(achievement);
            SoundPlayer.PlaySfx(unlockSfxId, false);

            await SlideAsync(hiddenPosition, visiblePosition, slideInSeconds, true, ct);
            if (holdSeconds > 0f)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(holdSeconds), ignoreTimeScale: false, cancellationToken: ct);
            }

            await SlideAsync(visiblePosition, hiddenPosition, slideOutSeconds, false, ct);
        }

        private void ApplyAchievement(Achievement achievement)
        {
            string achievementName = LocalizationService.Localized(achievement, nameof(Achievement.name), achievement.id);
            if (titleText != null)
            {
                titleText.text = LocalizationService.Text("achievement_unlocked", "도전과제 달성!");
                titleText.color = AchievementTitleColor;
            }

            if (descText != null)
            {
                descText.text = achievementName;
            }

            if (iconImage != null)
            {
                iconImage.sprite = GetIcon(achievement.icon_achv_idx);
                iconImage.enabled = iconImage.sprite != null;
                iconImage.color = AchievementTitleColor;
            }
        }

        // 등장 시 살짝 튀어 들어오고, 퇴장 시에는 자연스럽게 투명해지도록 패널 상태를 보간한다.
        private async UniTask SlideAsync(Vector2 from, Vector2 to, float duration, bool isEntering, CancellationToken ct)
        {
            if (panelRect == null)
            {
                return;
            }

            if (duration <= 0f)
            {
                SetPanelState(to, isEntering ? 1f : 0f, 1f);
                return;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                ct.ThrowIfCancellationRequested();
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float smoothT = Mathf.SmoothStep(0f, 1f, t);
                float movementT = isEntering ? EaseOutBack(t) : smoothT;
                float alpha = isEntering ? smoothT : 1f - smoothT;
                float scale = isEntering ? Mathf.LerpUnclamped(EntranceStartScale, 1f, movementT) : 1f;
                SetPanelState(Vector2.LerpUnclamped(from, to, movementT), alpha, scale);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            SetPanelState(to, isEntering ? 1f : 0f, 1f);
        }

        private void SetVisibleImmediate(bool visible)
        {
            SetPanelState(visible ? visiblePosition : hiddenPosition, visible ? 1f : 0f, 1f);
        }

        private void SetPanelState(Vector2 position, float alpha, float scaleMultiplier)
        {
            if (panelRect != null)
            {
                panelRect.anchoredPosition = position;
                panelRect.localScale = visibleScale * scaleMultiplier;
            }

            if (canvasGroup != null)
            {
                canvasGroup.alpha = alpha;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }
        }

        // 패널이 도착 지점을 조금 넘었다가 안착하는 등장 곡선을 만든다.
        private static float EaseOutBack(float t)
        {
            const float overshoot = 1.70158f;
            const float shiftedOvershoot = overshoot + 1f;
            float shiftedT = t - 1f;
            return 1f + shiftedOvershoot * shiftedT * shiftedT * shiftedT + overshoot * shiftedT * shiftedT;
        }

        private bool TryGetAchievement(string achievementId, out Achievement achievement)
        {
            achievementTable ??= new TsvDataLoader<Achievement>().LoadTable(AchievementResourcePath);
            if (achievementTable == null)
            {
                achievement = null;
                return false;
            }

            return achievementTable.TryGet(achievementId, out achievement);
        }

        private Sprite GetIcon(string iconIndexText)
        {
            return int.TryParse(iconIndexText, out int iconIndex) && iconsByNo.TryGetValue(iconIndex, out Sprite sprite)
                ? sprite
                : null;
        }

        private void LoadAchievementIcons()
        {
            iconsByNo.Clear();
            Sprite[] sprites = Resources.LoadAll<Sprite>(AchievementIconResourcePath);
            for (int i = 0; i < sprites.Length; i++)
            {
                Sprite sprite = sprites[i];
                if (sprite != null && TryGetAchievementIconIdx(sprite.name, out int iconIdx))
                {
                    iconsByNo[iconIdx] = sprite;
                }
            }
        }

        private static bool TryGetAchievementIconIdx(string spriteName, out int iconIdx)
        {
            return TryGetIconIdx(spriteName, IconAchvPrefix, out iconIdx) ||
                TryGetIconIdx(spriteName, LegacyIconPrefix, out iconIdx);
        }

        private static bool TryGetIconIdx(string spriteName, string prefix, out int iconIdx)
        {
            iconIdx = 0;
            return !string.IsNullOrWhiteSpace(spriteName) &&
                spriteName.StartsWith(prefix, StringComparison.Ordinal) &&
                int.TryParse(spriteName[prefix.Length..], out iconIdx);
        }

        private float GetHiddenOffset()
        {
            float width = panelRect != null ? panelRect.rect.width : 0f;
            return Mathf.Max(width, 1f) + slideDistance;
        }

    }
}
