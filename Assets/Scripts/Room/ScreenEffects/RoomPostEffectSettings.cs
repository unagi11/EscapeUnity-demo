using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Escape.Rooms
{
    [CreateAssetMenu(fileName = "RoomPostEffectSettings", menuName = "Tools/Escape/Rooms/Room Post Effect Settings")]
    public sealed class RoomPostEffectSettings : ScriptableObject
    {
#if UNITY_EDITOR
        public const string DefaultAssetPath = "Assets/Settings/RoomPostEffectSettings/Default.asset";
        public const string DefaultMaterialPath = "Assets/Materials/RoomRawImagePostEffect.mat";
#endif

        [Header("Blur")]
        [SerializeField, Range(0f, 8f)] private float blurRadius = 0.3f;
        [SerializeField, Range(0f, 1f)] private float intensity = 0f;

        [Header("CRT")]
        [SerializeField, Range(0f, 1f)] private float crtScanlineIntensity = 0.2f;

        [Header("Flicker")]
        [SerializeField, Range(0f, 1f)] private float flickerIntensity = 0.08f;
        [SerializeField, Range(0f, 60f)] private float flickerSpeed = 18f;
        [SerializeField, Range(0f, 1f)] private float flickerLineIntensity = 0.35f;

        [Header("Noise")]
        [SerializeField, Range(0f, 1f)] private float noiseIntensity = 0.06f;
        [SerializeField, Range(16f, 512f)] private float noiseScale = 180f;
        [SerializeField, Range(0f, 60f)] private float noiseSpeed = 24f;

        [Header("Palette")]
        [SerializeField, Range(0f, 1f)] private float paletteIntensity = 0.35f;
        [SerializeField, Range(2, 8)] private int paletteColorCount = 8;
        [SerializeField, Range(0f, 1f)] private float paletteDitherIntensity = 0.25f;
        [SerializeField] private Color paletteColor0 = new Color(0.024f, 0.09f, 0.102f, 1f);
        [SerializeField] private Color paletteColor1 = new Color(0.043f, 0.161f, 0.188f, 1f);
        [SerializeField] private Color paletteColor2 = new Color(0.063f, 0.314f, 0.353f, 1f);
        [SerializeField] private Color paletteColor3 = new Color(0.086f, 0.451f, 0.478f, 1f);
        [SerializeField] private Color paletteColor4 = new Color(0.18f, 0.588f, 0.565f, 1f);
        [SerializeField] private Color paletteColor5 = new Color(0.388f, 0.714f, 0.647f, 1f);
        [SerializeField] private Color paletteColor6 = new Color(0.604f, 0.82f, 0.722f, 1f);
        [SerializeField] private Color paletteColor7 = new Color(0.824f, 0.902f, 0.812f, 1f);

        [Header("Glitch")]
        [SerializeField, Range(0f, 1f)] private float glitchIntensity = 1f;
        [SerializeField, Range(0.1f, 3f)] private float glitchInterval = 1f;
        [SerializeField, Range(1, 4)] private int glitchSliceCount = 3;
        [SerializeField, Range(0.01f, 0.25f)] private float glitchSliceHeight = 0.08f;
        [SerializeField, Range(0.05f, 1f)] private float glitchSliceWidth = 0.5f;
        [SerializeField, Range(0f, 0.5f)] private float glitchHorizontalShift = 0.18f;
        [SerializeField, Range(0f, 0.5f)] private float glitchVerticalShift = 0.12f;

        [Header("Distortion")]
        [SerializeField, Range(0f, 0.03f)] private float chromaticSplit;
        [SerializeField, Range(0f, 0.05f)] private float warpIntensity;
        [SerializeField, Range(0f, 10f)] private float warpSpeed = 1f;
        [SerializeField, Range(1f, 30f)] private float warpScale = 12f;

        [Header("Psychedelic")]
        [SerializeField, Range(0f, 1f)] private float hueShift;
        [SerializeField, Range(-1f, 1f)] private float hueShiftSpeed;
        [SerializeField, Range(0f, 1f)] private float vignetteIntensity;
        [SerializeField, Range(0f, 10f)] private float vignettePulseSpeed = 1f;

        [Header("Audio")]
        [SerializeField, Range(0.5f, 1.5f)] private float bgmPitch = 1f;

        public float BgmPitch => bgmPitch;

        public static RoomPostEffectSettings CreateRythmComboFireRuntimeProfile()
        {
            RoomPostEffectSettings settings = CreateInstance<RoomPostEffectSettings>();
            settings.name = "RythmComboFireRuntime";
            settings.ApplyRythmComboFirePreset();
            return settings;
        }

        public static RoomPostEffectSettings CreateRythmComboRainbowRuntimeProfile()
        {
            RoomPostEffectSettings settings = CreateInstance<RoomPostEffectSettings>();
            settings.name = "RythmComboRainbowRuntime";
            settings.ApplyRythmComboRainbowPreset();
            return settings;
        }

        public void ApplyTo(Material material)
        {
            if (material == null)
            {
                return;
            }

            ValidateValues();
            SetFloat(material, "_BlurRadius", blurRadius);
            SetFloat(material, "_Intensity", intensity);
            SetVector(material, "_VectorBlurDirection", Vector4.zero);
            SetFloat(material, "_VectorBlurIntensity", 0f);
            SetFloat(material, "_VectorBlurLength", 1f);
            SetFloat(material, "_CrtScanlineIntensity", crtScanlineIntensity);
            SetFloat(material, "_FlickerIntensity", flickerIntensity);
            SetFloat(material, "_FlickerSpeed", flickerSpeed);
            SetFloat(material, "_FlickerLineIntensity", flickerLineIntensity);
            SetFloat(material, "_NoiseIntensity", noiseIntensity);
            SetFloat(material, "_NoiseScale", noiseScale);
            SetFloat(material, "_NoiseSpeed", noiseSpeed);
            SetFloat(material, "_PaletteIntensity", paletteIntensity);
            SetFloat(material, "_PaletteColorCount", paletteColorCount);
            SetFloat(material, "_PaletteDitherIntensity", paletteDitherIntensity);
            SetColor(material, "_PaletteColor0", paletteColor0);
            SetColor(material, "_PaletteColor1", paletteColor1);
            SetColor(material, "_PaletteColor2", paletteColor2);
            SetColor(material, "_PaletteColor3", paletteColor3);
            SetColor(material, "_PaletteColor4", paletteColor4);
            SetColor(material, "_PaletteColor5", paletteColor5);
            SetColor(material, "_PaletteColor6", paletteColor6);
            SetColor(material, "_PaletteColor7", paletteColor7);
            SetFloat(material, "_GlitchIntensity", glitchIntensity);
            SetFloat(material, "_GlitchInterval", glitchInterval);
            SetFloat(material, "_GlitchSliceCount", glitchSliceCount);
            SetFloat(material, "_GlitchSliceHeight", glitchSliceHeight);
            SetFloat(material, "_GlitchSliceWidth", glitchSliceWidth);
            SetFloat(material, "_GlitchHorizontalShift", glitchHorizontalShift);
            SetFloat(material, "_GlitchVerticalShift", glitchVerticalShift);
            SetFloat(material, "_ChromaticSplit", chromaticSplit);
            SetFloat(material, "_WarpIntensity", warpIntensity);
            SetFloat(material, "_WarpSpeed", warpSpeed);
            SetFloat(material, "_WarpScale", warpScale);
            SetFloat(material, "_HueShift", hueShift);
            SetFloat(material, "_HueShiftSpeed", hueShiftSpeed);
            SetFloat(material, "_VignetteIntensity", vignetteIntensity);
            SetFloat(material, "_VignettePulseSpeed", vignettePulseSpeed);
        }

        // 두 심도 프로필 사이 값을 보간해 런타임 Material에 적용한다.
        public static void ApplyBlend(
            Material material,
            RoomPostEffectSettings from,
            RoomPostEffectSettings to,
            float t)
        {
            if (material == null || from == null || to == null)
            {
                return;
            }

            from.ValidateValues();
            to.ValidateValues();
            t = Mathf.Clamp01(t);

            SetLerpFloat(material, "_BlurRadius", from.blurRadius, to.blurRadius, t);
            SetLerpFloat(material, "_Intensity", from.intensity, to.intensity, t);
            SetVector(material, "_VectorBlurDirection", Vector4.zero);
            SetFloat(material, "_VectorBlurIntensity", 0f);
            SetFloat(material, "_VectorBlurLength", 1f);
            SetLerpFloat(material, "_CrtScanlineIntensity", from.crtScanlineIntensity, to.crtScanlineIntensity, t);
            SetLerpFloat(material, "_FlickerIntensity", from.flickerIntensity, to.flickerIntensity, t);
            SetLerpFloat(material, "_FlickerSpeed", from.flickerSpeed, to.flickerSpeed, t);
            SetLerpFloat(material, "_FlickerLineIntensity", from.flickerLineIntensity, to.flickerLineIntensity, t);
            SetLerpFloat(material, "_NoiseIntensity", from.noiseIntensity, to.noiseIntensity, t);
            SetLerpFloat(material, "_NoiseScale", from.noiseScale, to.noiseScale, t);
            SetLerpFloat(material, "_NoiseSpeed", from.noiseSpeed, to.noiseSpeed, t);
            SetLerpFloat(material, "_PaletteIntensity", from.paletteIntensity, to.paletteIntensity, t);
            SetFloat(material, "_PaletteColorCount", Mathf.RoundToInt(Mathf.Lerp(from.paletteColorCount, to.paletteColorCount, t)));
            SetLerpFloat(material, "_PaletteDitherIntensity", from.paletteDitherIntensity, to.paletteDitherIntensity, t);
            SetLerpColor(material, "_PaletteColor0", from.paletteColor0, to.paletteColor0, t);
            SetLerpColor(material, "_PaletteColor1", from.paletteColor1, to.paletteColor1, t);
            SetLerpColor(material, "_PaletteColor2", from.paletteColor2, to.paletteColor2, t);
            SetLerpColor(material, "_PaletteColor3", from.paletteColor3, to.paletteColor3, t);
            SetLerpColor(material, "_PaletteColor4", from.paletteColor4, to.paletteColor4, t);
            SetLerpColor(material, "_PaletteColor5", from.paletteColor5, to.paletteColor5, t);
            SetLerpColor(material, "_PaletteColor6", from.paletteColor6, to.paletteColor6, t);
            SetLerpColor(material, "_PaletteColor7", from.paletteColor7, to.paletteColor7, t);
            SetLerpFloat(material, "_GlitchIntensity", from.glitchIntensity, to.glitchIntensity, t);
            SetLerpFloat(material, "_GlitchInterval", from.glitchInterval, to.glitchInterval, t);
            SetFloat(material, "_GlitchSliceCount", Mathf.RoundToInt(Mathf.Lerp(from.glitchSliceCount, to.glitchSliceCount, t)));
            SetLerpFloat(material, "_GlitchSliceHeight", from.glitchSliceHeight, to.glitchSliceHeight, t);
            SetLerpFloat(material, "_GlitchSliceWidth", from.glitchSliceWidth, to.glitchSliceWidth, t);
            SetLerpFloat(material, "_GlitchHorizontalShift", from.glitchHorizontalShift, to.glitchHorizontalShift, t);
            SetLerpFloat(material, "_GlitchVerticalShift", from.glitchVerticalShift, to.glitchVerticalShift, t);
            SetLerpFloat(material, "_ChromaticSplit", from.chromaticSplit, to.chromaticSplit, t);
            SetLerpFloat(material, "_WarpIntensity", from.warpIntensity, to.warpIntensity, t);
            SetLerpFloat(material, "_WarpSpeed", from.warpSpeed, to.warpSpeed, t);
            SetLerpFloat(material, "_WarpScale", from.warpScale, to.warpScale, t);
            SetLerpFloat(material, "_HueShift", from.hueShift, to.hueShift, t);
            SetLerpFloat(material, "_HueShiftSpeed", from.hueShiftSpeed, to.hueShiftSpeed, t);
            SetLerpFloat(material, "_VignetteIntensity", from.vignetteIntensity, to.vignetteIntensity, t);
            SetLerpFloat(material, "_VignettePulseSpeed", from.vignettePulseSpeed, to.vignettePulseSpeed, t);
        }

        // 두 프로필 사이의 BGM 피치를 화면 효과와 같은 비율로 보간한다.
        public static float LerpBgmPitch(RoomPostEffectSettings from, RoomPostEffectSettings to, float t)
        {
            if (from == null || to == null)
            {
                return 1f;
            }

            return Mathf.Lerp(from.bgmPitch, to.bgmPitch, Mathf.Clamp01(t));
        }

        private void OnEnable()
        {
            ValidateValues();
        }

        private void ValidateValues()
        {
            blurRadius = Mathf.Clamp(blurRadius, 0f, 8f);
            intensity = Mathf.Clamp01(intensity);
            crtScanlineIntensity = Mathf.Clamp01(crtScanlineIntensity);
            flickerIntensity = Mathf.Clamp01(flickerIntensity);
            flickerSpeed = Mathf.Clamp(flickerSpeed, 0f, 60f);
            flickerLineIntensity = Mathf.Clamp01(flickerLineIntensity);
            noiseIntensity = Mathf.Clamp01(noiseIntensity);
            noiseScale = Mathf.Clamp(noiseScale, 16f, 512f);
            noiseSpeed = Mathf.Clamp(noiseSpeed, 0f, 60f);
            paletteIntensity = Mathf.Clamp01(paletteIntensity);
            paletteColorCount = Mathf.Clamp(paletteColorCount, 2, 8);
            paletteDitherIntensity = Mathf.Clamp01(paletteDitherIntensity);
            glitchIntensity = Mathf.Clamp01(glitchIntensity);
            glitchInterval = Mathf.Clamp(glitchInterval, 0.1f, 3f);
            glitchSliceCount = Mathf.Clamp(glitchSliceCount, 1, 4);
            glitchSliceHeight = Mathf.Clamp(glitchSliceHeight, 0.01f, 0.25f);
            glitchSliceWidth = Mathf.Clamp(glitchSliceWidth, 0.05f, 1f);
            glitchHorizontalShift = Mathf.Clamp(glitchHorizontalShift, 0f, 0.5f);
            glitchVerticalShift = Mathf.Clamp(glitchVerticalShift, 0f, 0.5f);
            chromaticSplit = Mathf.Clamp(chromaticSplit, 0f, 0.03f);
            warpIntensity = Mathf.Clamp(warpIntensity, 0f, 0.05f);
            warpSpeed = Mathf.Clamp(warpSpeed, 0f, 10f);
            warpScale = Mathf.Clamp(warpScale, 1f, 30f);
            hueShift = Mathf.Clamp01(hueShift);
            hueShiftSpeed = Mathf.Clamp(hueShiftSpeed, -1f, 1f);
            vignetteIntensity = Mathf.Clamp01(vignetteIntensity);
            vignettePulseSpeed = Mathf.Clamp(vignettePulseSpeed, 0f, 10f);
            bgmPitch = Mathf.Clamp(bgmPitch, 0.5f, 1.5f);
        }

        private void ApplyRythmComboFirePreset()
        {
            blurRadius = 0f;
            intensity = 0f;
            crtScanlineIntensity = 0.025f;
            flickerIntensity = 0.18f;
            flickerSpeed = 2.75f;
            flickerLineIntensity = 0.14f;
            noiseIntensity = 0f;
            noiseScale = 120f;
            noiseSpeed = 10f;
            paletteIntensity = 0.26f;
            paletteColorCount = 8;
            paletteDitherIntensity = 0.08f;
            paletteColor0 = new Color(0.035f, 0.05f, 0.075f, 1f);
            paletteColor1 = new Color(0.055f, 0.11f, 0.13f, 1f);
            paletteColor2 = new Color(0.10f, 0.20f, 0.20f, 1f);
            paletteColor3 = new Color(0.28f, 0.24f, 0.14f, 1f);
            paletteColor4 = new Color(0.58f, 0.34f, 0.16f, 1f);
            paletteColor5 = new Color(0.86f, 0.52f, 0.22f, 1f);
            paletteColor6 = new Color(0.98f, 0.72f, 0.34f, 1f);
            paletteColor7 = new Color(1f, 0.90f, 0.62f, 1f);
            glitchIntensity = 0f;
            glitchInterval = 0.55f;
            glitchSliceCount = 2;
            glitchSliceHeight = 0.045f;
            glitchSliceWidth = 0.42f;
            glitchHorizontalShift = 0f;
            glitchVerticalShift = 0f;
            chromaticSplit = 0f;
            warpIntensity = 0f;
            warpSpeed = 2.2f;
            warpScale = 9f;
            hueShift = 0.01f;
            hueShiftSpeed = 0.01f;
            vignetteIntensity = 0.08f;
            vignettePulseSpeed = 0f;
            bgmPitch = 1f;
        }

        private void ApplyRythmComboRainbowPreset()
        {
            blurRadius = 0f;
            intensity = 0f;
            crtScanlineIntensity = 0.018f;
            flickerIntensity = 0.16f;
            flickerSpeed = 2.75f;
            flickerLineIntensity = 0.12f;
            noiseIntensity = 0f;
            noiseScale = 96f;
            noiseSpeed = 12f;
            paletteIntensity = 0.20f;
            paletteColorCount = 8;
            paletteDitherIntensity = 0.07f;
            paletteColor0 = new Color(0.035f, 0.045f, 0.09f, 1f);
            paletteColor1 = new Color(0.06f, 0.10f, 0.22f, 1f);
            paletteColor2 = new Color(0.08f, 0.25f, 0.38f, 1f);
            paletteColor3 = new Color(0.08f, 0.42f, 0.38f, 1f);
            paletteColor4 = new Color(0.36f, 0.56f, 0.34f, 1f);
            paletteColor5 = new Color(0.78f, 0.58f, 0.30f, 1f);
            paletteColor6 = new Color(0.82f, 0.34f, 0.42f, 1f);
            paletteColor7 = new Color(0.90f, 0.86f, 0.96f, 1f);
            glitchIntensity = 0f;
            glitchInterval = 0.8f;
            glitchSliceCount = 2;
            glitchSliceHeight = 0.04f;
            glitchSliceWidth = 0.36f;
            glitchHorizontalShift = 0f;
            glitchVerticalShift = 0f;
            chromaticSplit = 0f;
            warpIntensity = 0f;
            warpSpeed = 1.8f;
            warpScale = 7f;
            hueShift = 0.03f;
            hueShiftSpeed = 0.04f;
            vignetteIntensity = 0.07f;
            vignettePulseSpeed = 0f;
            bgmPitch = 1f;
        }

        private static void SetLerpFloat(Material material, string propertyName, float from, float to, float t)
        {
            SetFloat(material, propertyName, Mathf.Lerp(from, to, t));
        }

        private static void SetLerpColor(Material material, string propertyName, Color from, Color to, float t)
        {
            SetColor(material, propertyName, Color.Lerp(from, to, t));
        }

        private static void SetFloat(Material material, string propertyName, float value)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetFloat(propertyName, value);
            }
        }

        private static void SetColor(Material material, string propertyName, Color value)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetColor(propertyName, value);
            }
        }

        private static void SetVector(Material material, string propertyName, Vector4 value)
        {
            if (material.HasProperty(propertyName))
            {
                material.SetVector(propertyName, value);
            }
        }

#if UNITY_EDITOR
        public void ApplyToDefaultMaterialAsset(bool save)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(DefaultMaterialPath);
            if (material == null)
            {
                return;
            }

            ApplyTo(material);
            EditorUtility.SetDirty(material);
            if (save)
            {
                AssetDatabase.SaveAssetIfDirty(material);
            }
        }
#endif
    }
}
