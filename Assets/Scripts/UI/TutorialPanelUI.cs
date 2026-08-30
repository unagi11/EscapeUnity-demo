using System;
using Escape.Audio;
using Escape.Localization;
using Escape.Progress;
using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;
using Escape.Data;
using Escape.Rooms;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Escape.UI
{
    // 공용 튜토리얼 오버레이를 한 번만 표시하고 사용자의 전체 화면 터치를 기다린다.
    public sealed class TutorialPanelUI : MonoBehaviour
    {
        private const string TutorialResourcePath = "Data/tutorial";

        public enum TutorialId
        {
            LockPick = 0,
            RythmRecycle = 1,
            Inventory = 2,
            TimingCheck = 3
        }

        private static TutorialPanelUI instance;

        [Header("Panel")]
        [SerializeField] private Button dismissButton;
        [SerializeField] private Image panelBackground;
        [SerializeField] private TMP_Text tutorialText;
        [SerializeField, Range(0f, 1f)] private float guideOverlayAlpha = 245f / 255f;
        [SerializeField] private string showSfxId = "question_bell";

        [Header("Show Animation")]
        [SerializeField, Min(0f)] private float showDuration = 0.24f;
        [SerializeField, Range(0.1f, 1f)] private float showStartScale = 0.82f;

        private TsvTable<Tutorial> tutorialTable;
        private Tutorial currentDefinition;
        private UniTaskCompletionSource<bool> dismissCompletion;
        private CancellationTokenRegistration cancellationRegistration;

        public bool IsShowing => gameObject.activeSelf;

        private void Awake()
        {
            instance = this;
            tutorialTable = new TsvDataLoader<Tutorial>().LoadTable(TutorialResourcePath);
            dismissButton?.onClick.AddListener(Dismiss);
        }

        private void OnEnable()
        {
            LocalizationService.Ensure().LanguageChanged += RefreshText;
        }

        private void OnDisable()
        {
            if (LocalizationService.Instance != null)
            {
                LocalizationService.Instance.LanguageChanged -= RefreshText;
            }
        }

        private void OnDestroy()
        {
            dismissButton?.onClick.RemoveListener(Dismiss);
            cancellationRegistration.Dispose();
            dismissCompletion?.TrySetCanceled();
            dismissCompletion = null;

            if (instance == this)
            {
                instance = null;
            }
        }

        // 아직 보지 않은 튜토리얼이면 표시하고 닫힐 때까지 기다린다.
        public static UniTask ShowOnceAsync(TutorialId tutorialId, CancellationToken cancellationToken)
        {
            TutorialPanelUI panel = Ensure();
            if (panel == null || HasSeen(tutorialId))
            {
                return UniTask.CompletedTask;
            }

            return panel.ShowInternalAsync(tutorialId, cancellationToken);
        }

        // 인벤토리처럼 진행을 멈출 호출자가 없는 곳에서 튜토리얼을 요청한다.
        public static void ShowOnce(TutorialId tutorialId)
        {
            ShowOnceAsync(tutorialId, CancellationToken.None).Forget();
        }

        // 테스트와 옵션 초기화에서 특정 튜토리얼의 열람 기록을 지운다.
        public static void ResetSeen(TutorialId tutorialId)
        {
            GameSession state = GameSession.Instance;
            if (state == null || !state.HasSeenTutorial((int)tutorialId))
            {
                return;
            }

            state.RestoreTutorialSeenMask(state.TutorialSeenMask & ~(1 << (int)tutorialId));
        }

        private static TutorialPanelUI Ensure()
        {
            if (instance != null)
            {
                return instance;
            }

            SceneTransitionFadeUI transitionUI = SceneTransitionFadeUI.Ensure();
            instance = transitionUI != null ? transitionUI.TutorialPanel : null;
            return instance;
        }

        private async UniTask ShowInternalAsync(TutorialId tutorialId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryGetDefinition(tutorialId, out Tutorial definition))
            {
                Debug.LogWarning($"Tutorial definition not found: {tutorialId}", this);
                return;
            }

            if (dismissCompletion != null)
            {
                await dismissCompletion.Task.AttachExternalCancellation(cancellationToken);
                return;
            }

            currentDefinition = definition;
            ApplyDefinition(definition);
            transform.SetAsLastSibling();
            dismissCompletion = new UniTaskCompletionSource<bool>();
            cancellationRegistration = cancellationToken.Register(Dismiss);
            if (dismissButton != null)
            {
                dismissButton.interactable = false;
            }

            gameObject.SetActive(true);
            SoundPlayer.PlaySfx(showSfxId, false);

            GameSession.Instance?.MarkTutorialSeen((int)tutorialId);

            try
            {
                await PlayShowAnimation(cancellationToken);
                if (dismissButton != null)
                {
                    dismissButton.interactable = true;
                }

                await dismissCompletion.Task;
            }
            finally
            {
                cancellationRegistration.Dispose();
                dismissCompletion = null;
            }
        }

        // 게임 배속과 함께 배경 페이드와 문구 스케일업 등장 연출을 재생한다.
        private async UniTask PlayShowAnimation(CancellationToken cancellationToken)
        {
            Color targetBackgroundColor = panelBackground != null
                ? panelBackground.color
                : Color.white;
            RectTransform textRect = tutorialText != null ? tutorialText.rectTransform : null;
            Vector3 targetTextScale = textRect != null ? textRect.localScale : Vector3.one;

            if (panelBackground != null)
            {
                Color startColor = targetBackgroundColor;
                startColor.a = 0f;
                panelBackground.color = startColor;
            }

            if (textRect != null)
            {
                textRect.localScale = targetTextScale * showStartScale;
            }

            if (showDuration <= 0f)
            {
                RestoreShowVisuals(targetBackgroundColor, textRect, targetTextScale);
                return;
            }

            using CancellationTokenSource linkedCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, destroyCancellationToken);
            float animationElapsed = 0f;
            try
            {
                while (animationElapsed < showDuration)
                {
                    linkedCancellation.Token.ThrowIfCancellationRequested();
                    float t = Mathf.Clamp01(animationElapsed / showDuration);
                    float fade = Mathf.SmoothStep(0f, 1f, t);
                    float scale = Mathf.LerpUnclamped(showStartScale, 1f, EaseOutBack(t));

                    if (panelBackground != null)
                    {
                        Color color = targetBackgroundColor;
                        color.a *= fade;
                        panelBackground.color = color;
                    }

                    if (textRect != null)
                    {
                        textRect.localScale = targetTextScale * scale;
                    }

                    await UniTask.Yield(PlayerLoopTiming.Update, linkedCancellation.Token);
                    animationElapsed += Time.deltaTime;
                }
            }
            finally
            {
                RestoreShowVisuals(targetBackgroundColor, textRect, targetTextScale);
            }
        }

        // 연출 완료 또는 취소 뒤 배경과 문구를 원래 표시 상태로 복구한다.
        private void RestoreShowVisuals(
            Color targetBackgroundColor,
            RectTransform textRect,
            Vector3 targetTextScale)
        {
            if (panelBackground != null)
            {
                panelBackground.color = targetBackgroundColor;
            }

            if (textRect != null)
            {
                textRect.localScale = targetTextScale;
            }
        }

        // 끝 지점을 살짝 넘겼다가 돌아오는 스케일 곡선을 계산한다.
        private static float EaseOutBack(float t)
        {
            const float overshoot = 1.70158f;
            const float shiftedOvershoot = overshoot + 1f;
            float shiftedT = t - 1f;
            return 1f + shiftedOvershoot * shiftedT * shiftedT * shiftedT +
                overshoot * shiftedT * shiftedT;
        }

        // 전체 화면 버튼을 누르면 현재 튜토리얼을 닫고 대기 중인 게임을 이어 간다.
        private void Dismiss()
        {
            if (!gameObject.activeSelf)
            {
                return;
            }

            gameObject.SetActive(false);
            currentDefinition = null;
            if (dismissButton != null)
            {
                dismissButton.interactable = true;
            }
            dismissCompletion?.TrySetResult(true);
        }

        // TSV의 가이드 이미지를 화면 전체 스포트라이트 마스크로 적용한다.
        private void ApplyDefinition(Tutorial definition)
        {
            RefreshText();

            if (panelBackground != null)
            {
                Sprite guideImage = string.IsNullOrWhiteSpace(definition.guide_image)
                    ? null
                    : Resources.Load<Sprite>(definition.guide_image.Trim());
                panelBackground.sprite = guideImage;
                panelBackground.color = guideImage != null
                    ? new Color(1f, 1f, 1f, guideOverlayAlpha)
                    : new Color(0f, 0f, 0f, 0.88f);
            }

            ApplyTextRect(definition.text_rect);
        }

        // TSV의 정규화 좌표를 텍스트 RectTransform 앵커에 적용한다.
        private void ApplyTextRect(string rectValue)
        {
            if (tutorialText == null)
            {
                return;
            }

            Vector2 anchorMin = new(0.08f, 0.08f);
            Vector2 anchorMax = new(0.92f, 0.39f);
            string[] values = string.IsNullOrWhiteSpace(rectValue)
                ? Array.Empty<string>()
                : rectValue.Split(',');
            if (values.Length == 4 &&
                TryParseCoordinate(values[0], out float xMin) &&
                TryParseCoordinate(values[1], out float yMin) &&
                TryParseCoordinate(values[2], out float xMax) &&
                TryParseCoordinate(values[3], out float yMax))
            {
                anchorMin = new Vector2(xMin, yMin);
                anchorMax = new Vector2(xMax, yMax);
            }

            RectTransform textRect = tutorialText.rectTransform;
            textRect.anchorMin = anchorMin;
            textRect.anchorMax = anchorMax;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
        }

        private static bool TryParseCoordinate(string value, out float coordinate)
        {
            return float.TryParse(
                value.Trim(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out coordinate);
        }

        private void RefreshText()
        {
            if (currentDefinition == null)
            {
                return;
            }

            if (tutorialText != null)
            {
                tutorialText.text = LocalizationService.Localized(
                        currentDefinition,
                        nameof(Tutorial.text))
                    .Replace("\\n", "\n", StringComparison.Ordinal);
            }
        }

        private bool TryGetDefinition(TutorialId tutorialId, out Tutorial definition)
        {
            tutorialTable ??= new TsvDataLoader<Tutorial>().LoadTable(TutorialResourcePath);
            return tutorialTable.TryGet(GetDataId(tutorialId), out definition);
        }

        private static string GetDataId(TutorialId tutorialId)
        {
            return tutorialId switch
            {
                TutorialId.LockPick => "lockpick",
                TutorialId.RythmRecycle => "rythm_recycle",
                TutorialId.Inventory => "inventory",
                TutorialId.TimingCheck => "timing_check",
                _ => tutorialId.ToString().ToLowerInvariant(),
            };
        }

        private static bool HasSeen(TutorialId tutorialId)
        {
            return GameSession.Instance != null &&
                GameSession.Instance.HasSeenTutorial((int)tutorialId);
        }
    }
}
