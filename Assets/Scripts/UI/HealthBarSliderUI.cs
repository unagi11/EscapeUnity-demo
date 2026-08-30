using Escape.Progress;
using UnityEngine;
using UnityEngine.UI;

namespace Escape.UI
{
    // 체력 상태를 심전도 파형 그래프로 반영한다.
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class HealthBarSliderUI : MonoBehaviour
    {
        [SerializeField] private Slider healthSlider;
        [SerializeField] private Image waveformImage;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField, Range(0f, 1f)] private float visibleAlpha = 1f;
        [SerializeField, Range(0f, 1f)] private float hiddenAlpha = 0f;
        [SerializeField, Min(0f)] private float healthDecreaseDuration = 0.42f;
        [SerializeField, Min(32)] private int waveformTextureWidth = 96;
        [SerializeField, Min(8)] private int waveformTextureHeight = 24;
        [SerializeField, Min(1)] private int waveformLineThickness = 1;
        [SerializeField] private Color waveformColor = new(0.47f, 0.96f, 0.74f, 1f);
        [SerializeField] private Color weakWaveformColor = new(1f, 0.38f, 0.34f, 1f);
        [SerializeField, Range(0f, 1f)] private float baselineAlpha = 0.28f;
        [SerializeField, Min(0f)] private float waveformScrollSpeed = 0.45f;
        [SerializeField, Min(1f)] private float waveformFramesPerSecond = 30f;
        [SerializeField, Range(0.1f, 1f)] private float waveformTrailLength = 0.62f;
        [SerializeField, Range(0.2f, 3f)] private float waveformTrailFadePower = 1.35f;

        private GameSession state;
        private float lastHealth = float.NaN;
        private float displayedHealth = float.NaN;
        private float healthAnimationFrom;
        private float healthAnimationTo;
        private float healthAnimationRemaining;
        private bool hiddenByAdventurePanel;
        private Texture2D waveformTexture;
        private Sprite waveformSprite;
        private Color32[] waveformPixels;
        private float waveformScroll;
        private float waveformRenderElapsed;
        private bool waveformRenderRequested;

        private void Awake()
        {
            ResolveReferences();
            ConfigureWaveformImage();
            SetVisible(true);
        }

        private void OnEnable()
        {
            BindState(GameSession.Instance);
            Refresh(true);
        }

        private void OnDisable()
        {
            UnbindState();
        }

        private void OnDestroy()
        {
            ReleaseWaveformTexture();
        }

        private void Update()
        {
            if (state == null)
            {
                BindState(GameSession.Instance);
                Refresh(true);
            }

            UpdateHealthAnimation();
            UpdateWaveformPulse();
        }

        // 현재 GameSession를 구독해 체력 변경을 받는다.
        private void BindState(GameSession nextState)
        {
            if (state == nextState)
            {
                return;
            }

            UnbindState();
            state = nextState;
            lastHealth = float.NaN;
            if (state != null)
            {
                state.Changed += HandleStateChanged;
            }
        }

        // 이전 GameSession 구독을 해제한다.
        private void UnbindState()
        {
            if (state != null)
            {
                state.Changed -= HandleStateChanged;
                state = null;
            }
        }

        // 체력 변경 시 파형과 표시 상태를 갱신한다.
        private void HandleStateChanged()
        {
            Refresh(false);
        }

        // 현재 체력을 파형에 반영한다.
        private void Refresh(bool immediate)
        {
            ResolveReferences();
            ConfigureWaveformImage();
            if (state == null)
            {
                lastHealth = float.NaN;
                SetVisible(!hiddenByAdventurePanel);
                return;
            }

            float currentHealth = state.CurrentHealth;
            bool hasPreviousHealth = !float.IsNaN(lastHealth);
            bool healthDecreased = hasPreviousHealth && currentHealth < lastHealth;

            if (healthDecreased)
            {
                float fromHealth = float.IsNaN(displayedHealth) ? lastHealth : displayedHealth;
                StartHealthDecreaseAnimation(fromHealth, currentHealth);
            }
            else
            {
                healthAnimationRemaining = 0f;
                SetDisplayedHealth(currentHealth);
            }

            lastHealth = currentHealth;

            SetVisible(!hiddenByAdventurePanel);
        }

        // 체력 감소가 눈에 보이도록 파형 상태를 천천히 줄인다.
        private void StartHealthDecreaseAnimation(float fromHealth, float toHealth)
        {
            if (healthDecreaseDuration <= 0f)
            {
                SetDisplayedHealth(toHealth);
                return;
            }

            healthAnimationFrom = fromHealth;
            healthAnimationTo = toHealth;
            healthAnimationRemaining = healthDecreaseDuration;
            SetDisplayedHealth(fromHealth);
        }

        private void UpdateHealthAnimation()
        {
            if (healthAnimationRemaining <= 0f)
            {
                return;
            }

            healthAnimationRemaining = Mathf.Max(0f, healthAnimationRemaining - Time.unscaledDeltaTime);
            float t = healthDecreaseDuration > 0f
                ? 1f - healthAnimationRemaining / healthDecreaseDuration
                : 1f;
            float smoothT = t * t * (3f - 2f * t);
            SetDisplayedHealth(Mathf.Lerp(healthAnimationFrom, healthAnimationTo, smoothT));
            if (healthAnimationRemaining <= 0f)
            {
                SetDisplayedHealth(healthAnimationTo);
            }
        }

        private void SetDisplayedHealth(float health)
        {
            displayedHealth = health;
            waveformRenderRequested = true;
            if (waveformTexture == null)
            {
                RenderWaveform();
            }
        }

        // AdventurePanelUI 숨김 중에는 숨고, 숨김이 풀리면 기본 표시 상태로 돌아간다.
        public void SetAdventurePanelHidden(bool hidden)
        {
            hiddenByAdventurePanel = hidden;
            SetVisible(!hiddenByAdventurePanel);
        }

        // 체력 그래프의 표시 여부를 alpha로만 제어한다.
        private void SetVisible(bool visible)
        {
            if (canvasGroup == null)
            {
                return;
            }

            canvasGroup.alpha = visible ? visibleAlpha : hiddenAlpha;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        // Inspector 참조가 비어 있어도 같은 오브젝트/자식의 컴포넌트를 사용한다.
        private void ResolveReferences()
        {
            healthSlider ??= GetComponent<Slider>();
            waveformImage ??= transform.Find("Fill")?.GetComponent<Image>();
            canvasGroup ??= GetComponent<CanvasGroup>();
        }

        private void ConfigureWaveformImage()
        {
            if (healthSlider != null)
            {
                healthSlider.enabled = false;
            }

            if (waveformImage == null)
            {
                return;
            }

            waveformImage.type = Image.Type.Simple;
            waveformImage.preserveAspect = false;
            waveformImage.raycastTarget = false;
            waveformImage.color = Color.white;
        }

        // 현재 체력 비율에 맞춰 심전도 파형 텍스처를 다시 그린다.
        private void RenderWaveform()
        {
            if (waveformImage == null || state == null)
            {
                return;
            }

            EnsureWaveformTexture();
            ClearWaveformTexture();

            float health01 = Mathf.Clamp01(displayedHealth / Mathf.Max(1f, state.MaxHealth));
            int healthTier = GetDisplayedHealthTier();
            int width = waveformTexture.width;
            int height = waveformTexture.height;
            int baselineY = Mathf.RoundToInt((height - 1) * 0.5f);
            int previousX = 0;
            int previousY = baselineY;
            float previousAlpha = GetTrailAlpha(0f);
            Color color = GetWaveformColor(health01);

            for (int x = 0; x < width; x++)
            {
                float t = width > 1 ? x / (float)(width - 1) : 0f;
                float alpha = GetTrailAlpha(t);
                int y = Mathf.Clamp(
                    Mathf.RoundToInt(baselineY + GetWaveformSample(t, healthTier) * height * 0.36f),
                    1,
                    height - 2);

                if (x > 0 && (alpha > 0f || previousAlpha > 0f))
                {
                    float segmentAlpha = Mathf.Max(previousAlpha, alpha);
                    DrawLine(previousX, baselineY, x, baselineY, 1, WithAlpha(color, baselineAlpha * segmentAlpha));
                    DrawLine(previousX, previousY, x, y, waveformLineThickness, WithAlpha(color, segmentAlpha));
                }

                previousX = x;
                previousY = y;
                previousAlpha = alpha;
            }

            waveformTexture.SetPixels32(waveformPixels);
            waveformTexture.Apply(false, false);
            waveformImage.sprite = waveformSprite;
            waveformRenderRequested = false;
        }

        private void UpdateWaveformPulse()
        {
            if (state == null || waveformImage == null || !IsWaveformVisible())
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            if (waveformScrollSpeed > 0f)
            {
                waveformScroll = Mathf.Repeat(waveformScroll + deltaTime * waveformScrollSpeed, 1f);
            }

            if (!waveformRenderRequested && waveformScrollSpeed <= 0f)
            {
                return;
            }

            waveformRenderElapsed += deltaTime;
            float frameInterval = 1f / Mathf.Max(1f, waveformFramesPerSecond);
            if (waveformRenderElapsed < frameInterval)
            {
                return;
            }

            waveformRenderElapsed %= frameInterval;
            RenderWaveform();
        }

        private bool IsWaveformVisible()
        {
            return canvasGroup == null || canvasGroup.alpha > hiddenAlpha + 0.001f;
        }

        private float GetTrailAlpha(float t)
        {
            float age = Mathf.Repeat(waveformScroll - t + 1f, 1f);
            if (age > waveformTrailLength)
            {
                return 0f;
            }

            float normalized = 1f - age / Mathf.Max(0.001f, waveformTrailLength);
            return Mathf.Pow(normalized, waveformTrailFadePower);
        }

        private int GetDisplayedHealthTier()
        {
            if (float.IsNaN(displayedHealth))
            {
                return 3;
            }

            return Mathf.Clamp(Mathf.RoundToInt(displayedHealth), 0, 3);
        }

        private float GetWaveformSample(float t, int healthTier)
        {
            float shiftedT = Mathf.Repeat(t + waveformScroll, 1f);
            return healthTier switch
            {
                >= 3 => GetStableWaveformSample(shiftedT),
                2 => GetUnsteadyWaveformSample(shiftedT),
                1 => GetCriticalWaveformSample(shiftedT),
                _ => GetFlatlineWaveformSample(shiftedT)
            };
        }

        private static float GetStableWaveformSample(float t)
        {
            float wobble = Mathf.Sin(t * Mathf.PI * 2f) * 0.018f;
            float pulse =
                Spike(t, 0.42f, 0.035f, 0.07f) -
                Spike(t, 0.50f, 0.026f, 0.16f) +
                Spike(t, 0.54f, 0.018f, 0.58f) -
                Spike(t, 0.59f, 0.026f, 0.14f) +
                Spike(t, 0.73f, 0.070f, 0.08f);
            return wobble + pulse;
        }

        private static float GetUnsteadyWaveformSample(float t)
        {
            float beat = Mathf.Repeat(t * 2.45f, 1f);
            float jitter =
                Mathf.Sin(t * 37f) * 0.08f +
                Mathf.Sin(t * 83f + 0.7f) * 0.04f;
            float pulse =
                Spike(beat, 0.14f, 0.038f, 0.16f) -
                Spike(beat, 0.28f, 0.024f, 0.42f) +
                Spike(beat, 0.34f, 0.018f, 1.25f) -
                Spike(beat, 0.41f, 0.022f, 0.36f) +
                Spike(beat, 0.64f, 0.078f, 0.22f);
            return jitter + pulse;
        }

        private float GetCriticalWaveformSample(float t)
        {
            float beat = Mathf.Repeat(t * 4.35f, 1f);
            float noise = Mathf.PerlinNoise(t * 18f, waveformScroll * 7f) * 2f - 1f;
            float surge =
                Mathf.Sin(t * 29f) * 0.22f +
                Mathf.Sin(t * 71f + 1.4f) * 0.16f +
                noise * 0.34f;
            float pulse =
                Spike(beat, 0.15f, 0.045f, 0.34f) -
                Spike(beat, 0.30f, 0.030f, 0.78f) +
                Spike(beat, 0.39f, 0.020f, 1.80f) -
                Spike(beat, 0.49f, 0.022f, 0.90f) +
                Spike(beat, 0.70f, 0.060f, 0.42f);
            return surge + pulse;
        }

        private static float GetFlatlineWaveformSample(float t)
        {
            return Mathf.Sin(t * 24f) * 0.018f;
        }

        private static float Spike(float value, float center, float width, float amplitude)
        {
            float distance = Mathf.Abs(value - center);
            float t = Mathf.Clamp01(1f - distance / Mathf.Max(0.001f, width));
            return amplitude * t * t;
        }

        private Color GetWaveformColor(float health01)
        {
            return Color.Lerp(weakWaveformColor, waveformColor, health01);
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = Mathf.Clamp01(alpha);
            return color;
        }

        private void EnsureWaveformTexture()
        {
            int width = Mathf.Max(32, waveformTextureWidth);
            int height = Mathf.Max(8, waveformTextureHeight);
            if (waveformTexture != null &&
                waveformTexture.width == width &&
                waveformTexture.height == height)
            {
                if (waveformPixels == null || waveformPixels.Length != width * height)
                {
                    waveformPixels = new Color32[width * height];
                }

                return;
            }

            ReleaseWaveformTexture();
            waveformTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "HealthWaveformTexture",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            waveformPixels = new Color32[width * height];
            waveformSprite = Sprite.Create(
                waveformTexture,
                new Rect(0f, 0f, width, height),
                new Vector2(0.5f, 0.5f),
                100f);
            waveformSprite.name = "HealthWaveformSprite";
        }

        private void ClearWaveformTexture()
        {
            System.Array.Clear(waveformPixels, 0, waveformPixels.Length);
        }

        private void DrawLine(int x0, int y0, int x1, int y1, int thickness, Color color)
        {
            int dx = Mathf.Abs(x1 - x0);
            int sx = x0 < x1 ? 1 : -1;
            int dy = -Mathf.Abs(y1 - y0);
            int sy = y0 < y1 ? 1 : -1;
            int error = dx + dy;

            while (true)
            {
                DrawPoint(x0, y0, thickness, color);
                if (x0 == x1 && y0 == y1)
                {
                    break;
                }

                int e2 = 2 * error;
                if (e2 >= dy)
                {
                    error += dy;
                    x0 += sx;
                }

                if (e2 <= dx)
                {
                    error += dx;
                    y0 += sy;
                }
            }
        }

        private void DrawPoint(int centerX, int centerY, int thickness, Color color)
        {
            int radius = Mathf.Max(0, thickness - 1);
            for (int y = centerY - radius; y <= centerY + radius; y++)
            {
                if (y < 0 || y >= waveformTexture.height)
                {
                    continue;
                }

                for (int x = centerX - radius; x <= centerX + radius; x++)
                {
                    if (x >= 0 && x < waveformTexture.width)
                    {
                        waveformPixels[y * waveformTexture.width + x] = color;
                    }
                }
            }
        }

        private void ReleaseWaveformTexture()
        {
            if (waveformImage != null)
            {
                waveformImage.sprite = null;
            }

            if (waveformSprite != null)
            {
                Destroy(waveformSprite);
                waveformSprite = null;
            }

            if (waveformTexture != null)
            {
                Destroy(waveformTexture);
                waveformTexture = null;
            }

            waveformPixels = null;
            waveformRenderElapsed = 0f;
            waveformRenderRequested = false;
        }
    }
}
