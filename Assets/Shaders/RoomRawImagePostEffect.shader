Shader "UI/Escape/Room Raw Image Post Effect"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}

        [Header(Blur)]
        _BlurRadius ("Blur Radius", Range(0, 8)) = 2
        _Intensity ("Intensity", Range(0, 1)) = 1
        _VectorBlurDirection ("Vector Blur Direction", Vector) = (0, 0, 0, 0)
        _VectorBlurIntensity ("Vector Blur Intensity", Range(0, 1)) = 0
        _VectorBlurLength ("Vector Blur Length", Range(1, 12)) = 6

        [Header(CRT)]
        _CrtScanlineIntensity ("Scanline Intensity", Range(0, 1)) = 0.18
        _CrtScanlineCount ("Scanline Count", Range(48, 512)) = 320
        _CrtScanlineScrollSpeed ("Scanline Scroll Speed", Range(-4, 4)) = 0.6

        [Header(Flicker)]
        _FlickerIntensity ("Flicker Intensity", Range(0, 1)) = 0.08
        _FlickerSpeed ("Flicker Speed", Range(0, 60)) = 18
        _FlickerLineIntensity ("Flicker Line Intensity", Range(0, 1)) = 0.35

        [Header(Noise)]
        _NoiseIntensity ("Noise Intensity", Range(0, 1)) = 0.06
        _NoiseScale ("Noise Scale", Range(16, 512)) = 180
        _NoiseSpeed ("Noise Speed", Range(0, 60)) = 24

        [Header(Palette)]
        _PaletteIntensity ("Palette Intensity", Range(0, 1)) = 0.35
        _PaletteColorCount ("Palette Color Count", Range(2, 8)) = 8
        _PaletteDitherIntensity ("Palette Dither Intensity", Range(0, 1)) = 0.25
        _PaletteColor0 ("Palette Color 1", Color) = (0.024, 0.09, 0.102, 1)
        _PaletteColor1 ("Palette Color 2", Color) = (0.043, 0.161, 0.188, 1)
        _PaletteColor2 ("Palette Color 3", Color) = (0.063, 0.314, 0.353, 1)
        _PaletteColor3 ("Palette Color 4", Color) = (0.086, 0.451, 0.478, 1)
        _PaletteColor4 ("Palette Color 5", Color) = (0.18, 0.588, 0.565, 1)
        _PaletteColor5 ("Palette Color 6", Color) = (0.388, 0.714, 0.647, 1)
        _PaletteColor6 ("Palette Color 7", Color) = (0.604, 0.82, 0.722, 1)
        _PaletteColor7 ("Palette Color 8", Color) = (0.824, 0.902, 0.812, 1)

        [Header(Glitch)]
        _GlitchIntensity ("Glitch Intensity", Range(0, 1)) = 1
        _GlitchInterval ("Glitch Interval", Range(0.1, 3)) = 1
        _GlitchSliceCount ("Glitch Slice Count", Range(1, 4)) = 3
        _GlitchSliceHeight ("Glitch Slice Height", Range(0.01, 0.25)) = 0.08
        _GlitchSliceWidth ("Glitch Slice Width", Range(0.05, 1)) = 0.5
        _GlitchHorizontalShift ("Glitch Horizontal Shift", Range(0, 0.5)) = 0.18
        _GlitchVerticalShift ("Glitch Vertical Shift", Range(0, 0.5)) = 0.12

        [Header(Distortion)]
        _ChromaticSplit ("Chromatic Split", Range(0, 0.03)) = 0
        _WarpIntensity ("Warp Intensity", Range(0, 0.05)) = 0
        _WarpSpeed ("Warp Speed", Range(0, 10)) = 1
        _WarpScale ("Warp Scale", Range(1, 30)) = 12

        [Header(Psychedelic)]
        _HueShift ("Hue Shift", Range(0, 1)) = 0
        _HueShiftSpeed ("Hue Shift Speed", Range(-1, 1)) = 0
        _VignetteIntensity ("Vignette Intensity", Range(0, 1)) = 0
        _VignettePulseSpeed ("Vignette Pulse Speed", Range(0, 10)) = 1

        [Header(Resample)]
        _ResampleResolution ("Resample Resolution (0 = off)", Vector) = (256, 192, 0, 0)

        [Header(UI Mask)]
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Room Raw Image Post Effect"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
            };

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _BlurRadius;
            float _Intensity;
            float4 _VectorBlurDirection;
            float _VectorBlurIntensity;
            float _VectorBlurLength;
            float _GlitchIntensity;
            float _GlitchInterval;
            float _GlitchSliceCount;
            float _GlitchSliceHeight;
            float _GlitchSliceWidth;
            float _GlitchHorizontalShift;
            float _GlitchVerticalShift;
            float _ChromaticSplit;
            float _WarpIntensity;
            float _WarpSpeed;
            float _WarpScale;
            float _HueShift;
            float _HueShiftSpeed;
            float _VignetteIntensity;
            float _VignettePulseSpeed;
            float _CrtScanlineIntensity;
            float _CrtScanlineCount;
            float _CrtScanlineScrollSpeed;
            float _FlickerIntensity;
            float _FlickerSpeed;
            float _FlickerLineIntensity;
            float _NoiseIntensity;
            float _NoiseScale;
            float _NoiseSpeed;
            float _PaletteIntensity;
            float _PaletteColorCount;
            float _PaletteDitherIntensity;
            fixed4 _PaletteColor0;
            fixed4 _PaletteColor1;
            fixed4 _PaletteColor2;
            fixed4 _PaletteColor3;
            fixed4 _PaletteColor4;
            fixed4 _PaletteColor5;
            fixed4 _PaletteColor6;
            fixed4 _PaletteColor7;
            float4 _ClipRect;
            float4 _ResampleResolution;

            v2f vert(appdata_t input)
            {
                v2f output;
                output.worldPosition = input.vertex;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.texcoord = input.texcoord;
                output.color = input.color;
                return output;
            }

            float Hash(float value)
            {
                return frac(sin(value) * 43758.5453123);
            }

            float2 ApplyBreakGlitch(float2 originalUv)
            {
                float interval = max(_GlitchInterval, 0.1);
                float glitchStep = floor(_Time.y / interval);
                float sliceCount = clamp(round(_GlitchSliceCount), 1.0, 4.0);
                float2 displacedUv = originalUv;

                [unroll]
                for (int index = 0; index < 4; index++)
                {
                    float sliceEnabled = step((float)index + 0.5, sliceCount);
                    float seed = glitchStep * 53.0 + (float)index * 97.0;
                    float sliceHeight = max(
                        _MainTex_TexelSize.y,
                        _GlitchSliceHeight * lerp(0.55, 1.45, Hash(seed + 1.0)));
                    float centerY = lerp(sliceHeight * 0.5, 1.0 - sliceHeight * 0.5, Hash(seed + 2.0));
                    float sliceWidth = clamp(_GlitchSliceWidth, 0.05, 1.0);
                    float centerX = lerp(sliceWidth * 0.5, 1.0 - sliceWidth * 0.5, Hash(seed + 3.0));
                    float verticalMask = step(abs(originalUv.y - centerY), sliceHeight * 0.5);
                    float horizontalMask = step(abs(originalUv.x - centerX), sliceWidth * 0.5);
                    float randomEnabled = step(Hash(seed + 5.0), saturate(_GlitchIntensity));
                    float breakMask = sliceEnabled * verticalMask * horizontalMask * randomEnabled;

                    float horizontalDirection = Hash(seed + 6.0) * 2.0 - 1.0;
                    float verticalDirection = Hash(seed + 7.0) * 2.0 - 1.0;
                    float2 offset = float2(
                        horizontalDirection * _GlitchHorizontalShift,
                        verticalDirection * _GlitchVerticalShift);
                    float2 sampledUv = frac(originalUv + offset);
                    displacedUv = lerp(displacedUv, sampledUv, breakMask);
                }

                return displacedUv;
            }

            float2 ApplyWarp(float2 uv)
            {
                float intensity = max(_WarpIntensity, 0.0);
                float scale = max(_WarpScale, 1.0);
                float time = _Time.y * max(_WarpSpeed, 0.0);
                float2 wave = float2(
                    sin(uv.y * scale * 6.2831853 + time) * intensity,
                    sin(uv.x * scale * 4.3982297 - time * 0.83) * intensity * 0.55);
                return frac(uv + wave);
            }

            fixed4 SampleChromatic(float2 uv)
            {
                float split = max(_ChromaticSplit, 0.0);
                if (split <= 0.00001)
                {
                    return tex2D(_MainTex, uv);
                }

                float angle = _Time.y * 0.7;
                float2 direction = float2(cos(angle), sin(angle));
                float2 offset = direction * split;
                fixed4 center = tex2D(_MainTex, uv);
                center.r = tex2D(_MainTex, frac(uv + offset)).r;
                center.b = tex2D(_MainTex, frac(uv - offset)).b;
                return center;
            }

            fixed3 RotateHue(fixed3 color, float turns)
            {
                float angle = turns * 6.2831853;
                float cosine = cos(angle);
                float sine = sin(angle);
                fixed3 axis = fixed3(0.57735027, 0.57735027, 0.57735027);
                return saturate(
                    color * cosine +
                    cross(axis, color) * sine +
                    axis * dot(axis, color) * (1.0 - cosine));
            }

            fixed3 ApplyPsychedelic(fixed3 source, float2 uv)
            {
                float hueTurns = frac(_HueShift + _Time.y * _HueShiftSpeed);
                fixed3 color = source;
                if (abs(hueTurns) > 0.0001)
                {
                    color = RotateHue(source, hueTurns);
                }

                float vignetteIntensity = saturate(_VignetteIntensity);
                if (vignetteIntensity > 0.0001)
                {
                    float2 centered = (uv - 0.5) * 2.0;
                    float edge = smoothstep(0.35, 1.25, length(centered));
                    float pulse = 0.75 + 0.25 * sin(_Time.y * max(_VignettePulseSpeed, 0.0) * 6.2831853);
                    color *= 1.0 - edge * vignetteIntensity * pulse;
                }

                return saturate(color);
            }

            void ComparePaletteColor(fixed3 lookupColor, fixed3 candidate, inout fixed3 nearestColor, inout float nearestDistance)
            {
                fixed3 difference = lookupColor - candidate;
                float candidateDistance = dot(difference, difference);
                if (candidateDistance < nearestDistance)
                {
                    nearestColor = candidate;
                    nearestDistance = candidateDistance;
                }
            }

            float GetPaletteDither(float2 uv)
            {
                float2 pixel = floor(uv * _MainTex_TexelSize.zw);
                float x = fmod(pixel.x, 4.0);
                float y = fmod(pixel.y, 4.0);
                float index = x + y * 4.0;

                if (index < 0.5) return 0.0 / 16.0;
                if (index < 1.5) return 8.0 / 16.0;
                if (index < 2.5) return 2.0 / 16.0;
                if (index < 3.5) return 10.0 / 16.0;
                if (index < 4.5) return 12.0 / 16.0;
                if (index < 5.5) return 4.0 / 16.0;
                if (index < 6.5) return 14.0 / 16.0;
                if (index < 7.5) return 6.0 / 16.0;
                if (index < 8.5) return 3.0 / 16.0;
                if (index < 9.5) return 11.0 / 16.0;
                if (index < 10.5) return 1.0 / 16.0;
                if (index < 11.5) return 9.0 / 16.0;
                if (index < 12.5) return 15.0 / 16.0;
                if (index < 13.5) return 7.0 / 16.0;
                if (index < 14.5) return 13.0 / 16.0;
                return 5.0 / 16.0;
            }

            fixed3 ApplyPalette(fixed3 source, float2 uv)
            {
                float intensity = saturate(_PaletteIntensity);
                if (intensity <= 0.0001)
                {
                    return source;
                }

                float dither = (GetPaletteDither(uv) - 0.5) * saturate(_PaletteDitherIntensity) * 0.125;
                fixed3 lookupColor = saturate(source + dither);
                fixed3 nearestColor = _PaletteColor0.rgb;
                fixed3 difference = lookupColor - nearestColor;
                float nearestDistance = dot(difference, difference);
                int colorCount = (int)clamp(round(_PaletteColorCount), 2.0, 8.0);

                if (colorCount > 1) ComparePaletteColor(lookupColor, _PaletteColor1.rgb, nearestColor, nearestDistance);
                if (colorCount > 2) ComparePaletteColor(lookupColor, _PaletteColor2.rgb, nearestColor, nearestDistance);
                if (colorCount > 3) ComparePaletteColor(lookupColor, _PaletteColor3.rgb, nearestColor, nearestDistance);
                if (colorCount > 4) ComparePaletteColor(lookupColor, _PaletteColor4.rgb, nearestColor, nearestDistance);
                if (colorCount > 5) ComparePaletteColor(lookupColor, _PaletteColor5.rgb, nearestColor, nearestDistance);
                if (colorCount > 6) ComparePaletteColor(lookupColor, _PaletteColor6.rgb, nearestColor, nearestDistance);
                if (colorCount > 7) ComparePaletteColor(lookupColor, _PaletteColor7.rgb, nearestColor, nearestDistance);

                return lerp(source, nearestColor, intensity);
            }

            fixed4 ApplyRoomEffects(float2 originalUv)
            {
                float2 uv = ApplyWarp(ApplyBreakGlitch(originalUv));
                fixed4 color = SampleChromatic(uv);

                float scanlineScroll = _Time.y * _CrtScanlineScrollSpeed;
                float scanlineWave = 0.5 + 0.5 * sin((originalUv.y * max(_CrtScanlineCount, 1.0) - scanlineScroll) * 6.2831853);
                float scanlineMask = pow(saturate(scanlineWave), 2.5);
                color.rgb *= lerp(1.0 - saturate(_CrtScanlineIntensity), 1.0, scanlineMask);

                float flickerIntensity = saturate(_FlickerIntensity);
                float flickerStep = floor(_Time.y * max(_FlickerSpeed, 0.0));
                float flickerNoise = Hash(flickerStep * 19.0 + 3.0);
                float flickerWave = 0.5 + 0.5 * sin(_Time.y * max(_FlickerSpeed, 0.0) * 6.2831853);
                float flicker = saturate(flickerNoise * 0.75 + flickerWave * 0.25);
                float brightness = lerp(1.0, 0.82 + flicker * 0.28, flickerIntensity);
                float lineNoise = Hash(floor(originalUv.y * 96.0) + flickerStep * 23.0);
                float lineBrightness = lerp(1.0, 0.95 + lineNoise * 0.1, flickerIntensity * saturate(_FlickerLineIntensity));
                color.rgb *= brightness * lineBrightness;

                float noiseStep = floor(_Time.y * max(_NoiseSpeed, 0.0));
                float2 noiseCell = floor(originalUv * max(_NoiseScale, 1.0));
                float grain = Hash(dot(noiseCell, float2(12.9898, 78.233)) + noiseStep * 37.719);
                color.rgb += (grain - 0.5) * saturate(_NoiseIntensity);
                color.rgb = saturate(color.rgb);
                return color;
            }

            fixed4 ApplyComposedEffects(float2 uv, fixed4 vertexColor)
            {
                fixed4 color = ApplyRoomEffects(uv) * vertexColor;
                color.rgb = ApplyPalette(color.rgb, uv);
                color.rgb = ApplyPsychedelic(color.rgb, uv);
                return color;
            }

            fixed4 SampleBlur(float2 uv, float2 texel, fixed4 vertexColor, fixed4 source)
            {
                // Keep the final blur compact enough for Metal mobile shader compilation.
                fixed4 blurred = source * 0.25;

                blurred += ApplyComposedEffects(uv + texel * float2( 1.0,  0.0), vertexColor) * 0.125;
                blurred += ApplyComposedEffects(uv + texel * float2(-1.0,  0.0), vertexColor) * 0.125;
                blurred += ApplyComposedEffects(uv + texel * float2( 0.0,  1.0), vertexColor) * 0.125;
                blurred += ApplyComposedEffects(uv + texel * float2( 0.0, -1.0), vertexColor) * 0.125;

                blurred += ApplyComposedEffects(uv + texel * float2( 1.0,  1.0), vertexColor) * 0.0625;
                blurred += ApplyComposedEffects(uv + texel * float2(-1.0,  1.0), vertexColor) * 0.0625;
                blurred += ApplyComposedEffects(uv + texel * float2( 1.0, -1.0), vertexColor) * 0.0625;
                blurred += ApplyComposedEffects(uv + texel * float2(-1.0, -1.0), vertexColor) * 0.0625;

                return blurred;
            }

            fixed4 SampleVectorBlur(float2 uv, float2 texel, fixed4 vertexColor, fixed4 source)
            {
                float2 direction = _VectorBlurDirection.xy;
                float directionLength = max(length(direction), 0.0001);
                direction /= directionLength;
                float blurLength = max(_VectorBlurLength, 1.0);

                // 이동 방향의 반대편으로 잔상이 남도록, 진행 방향의 픽셀을 단계적으로 샘플링한다.
                fixed4 blurred = source * 0.34;
                blurred += ApplyComposedEffects(saturate(uv + texel * direction * blurLength * 0.25), vertexColor) * 0.26;
                blurred += ApplyComposedEffects(saturate(uv + texel * direction * blurLength * 0.5), vertexColor) * 0.20;
                blurred += ApplyComposedEffects(saturate(uv + texel * direction * blurLength * 0.75), vertexColor) * 0.12;
                blurred += ApplyComposedEffects(saturate(uv + texel * direction * blurLength), vertexColor) * 0.08;
                return blurred;
            }

            // 최종 출력을 지정 해상도(예: 256x192) 격자로 스냅해 모든 이펙트를 픽셀 단위로 잠근다.
            float2 ApplyResample(float2 uv)
            {
                float2 cells = max(_ResampleResolution.xy, float2(1.0, 1.0));
                float2 snappedUv = (floor(uv * cells) + 0.5) / cells;
                float enabled = step(0.5, _ResampleResolution.x) * step(0.5, _ResampleResolution.y);
                return lerp(uv, snappedUv, enabled);
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float radius = max(_BlurRadius, 0.0);
                float2 uv = ApplyResample(input.texcoord);
                fixed4 source = ApplyComposedEffects(uv, input.color);
                fixed4 color = source;

                if (_Intensity > 0.0001 && radius > 0.0001)
                {
                    float2 texel = _MainTex_TexelSize.xy * radius;
                    fixed4 blurred;
                    if (_VectorBlurIntensity > 0.0001)
                    {
                        blurred = SampleVectorBlur(uv, texel, input.color, source);
                    }
                    else
                    {
                        blurred = SampleBlur(uv, texel, input.color, source);
                    }

                    color = lerp(source, blurred, saturate(_Intensity));
                }

                color.a *= UnityGet2DClipping(input.worldPosition.xy, _ClipRect);
                return color;
            }
            ENDCG
        }
    }
}
