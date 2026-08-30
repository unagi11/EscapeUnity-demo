using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Escape.Rooms
{
    // 캡처 이미지와 마스크 셰이더를 이용한 방향·나선형 화면 전환을 처리한다.
    internal sealed class TransitionMaskScreenEffectHandler :
        IRoomScreenEffectHandler,
        IDisposable
    {
        private const string ShaderName = "Hidden/Escape/RoomTransitionMask";
        private const string ShaderResourcePath = "Shaders/RoomTransitionMask";
        private const string SpiralInwardResourcePath = "Effect/transition_spiral_inward";
        private const string SpiralPixelResourcePath = "Effect/transition_spiral_pixel";
        private const float ClearProgress = -0.01f;

        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int ModeId = Shader.PropertyToID("_Mode");
        private static readonly int MaskTextureId = Shader.PropertyToID("_MaskTex");
        private static readonly int UseTextureColorId = Shader.PropertyToID("_UseTextureColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly ScreenEffectType[] SupportedTypes =
        {
            ScreenEffectType.TransitionSpiralPixel,
            ScreenEffectType.TransitionFadeRight,
            ScreenEffectType.TransitionFadeDown,
            ScreenEffectType.TransitionVignetteRadial,
            ScreenEffectType.TransitionSpiralInward,
            ScreenEffectType.TransitionFadeLeft,
            ScreenEffectType.TransitionFadeUp,
        };

        private readonly RoomScreenEffectResources resources;
        private readonly float duration;
        private readonly Dictionary<ScreenEffectType, Texture2D> maskTextures = new();
        private Material material;

        public IReadOnlyList<ScreenEffectType> Types => SupportedTypes;

        // 공유 캡처 자원과 마스크 전환 지속시간을 연결한다.
        public TransitionMaskScreenEffectHandler(
            RoomScreenEffectResources resources,
            float duration)
        {
            this.resources = resources;
            this.duration = duration;
        }

        // 마스크 타입과 진행 방향에 맞는 화면 전환을 실행한다.
        public async UniTask PlayAsync(
            ScreenEffectType screenEffect,
            ScreenEffectPhase phase,
            CancellationToken cancellationToken)
        {
            Material resolvedMaterial = GetMaterial();
            Texture maskTexture = GetMaskTexture(screenEffect);
            if (resolvedMaterial == null || maskTexture == null || duration <= 0f)
            {
                if (phase == ScreenEffectPhase.FadeIn)
                {
                    resources.HideAll();
                }

                return;
            }

            resources.HideBlackImage();
            if (phase == ScreenEffectPhase.FadeOut)
            {
                await PrepareCaptureFrameAsync(cancellationToken);
                if (!CaptureFrame(screenEffect, maskTexture))
                {
                    resources.HideAll();
                }
            }
            else
            {
                if (resources.GetCaptureImage() == null ||
                    resources.CaptureTexture == null)
                {
                    resources.HideAll();
                    return;
                }

                ActivateCapture(
                    maskTexture,
                    GetMode(screenEffect, phase));
                SetProgress(1f);
                await AnimateMaskAsync(
                    1f,
                    ClearProgress,
                    cancellationToken);
                resources.HideCaptureImage();
            }
        }

        // 커서를 숨기고 UI 렌더가 끝난 프레임을 캡처하도록 준비한다.
        private static async UniTask PrepareCaptureFrameAsync(
            CancellationToken cancellationToken)
        {
            Cursor.visible = false;
            await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            Canvas.ForceUpdateCanvases();
        }

        // 현재 방 프레임을 마스크 전환 이미지로 준비한다.
        private bool CaptureFrame(
            ScreenEffectType screenEffect,
            Texture maskTexture)
        {
            if (!resources.CaptureRoomImage())
            {
                Debug.LogWarning(
                    "Room transition effect could not copy RoomImage texture.",
                    resources.Owner);
                return false;
            }

            ActivateCapture(
                maskTexture,
                GetMode(screenEffect, ScreenEffectPhase.FadeOut));
            SetProgress(1f);
            return true;
        }

        // 캡처 이미지와 마스크 셰이더 매개변수를 활성화한다.
        private void ActivateCapture(Texture maskTexture, float mode)
        {
            resources.ActivateCaptureImage(GetMaterial());
            SetMaskTexture(maskTexture);
            SetMode(mode);
            SetUseTextureColor(true);
        }

        // 마스크 진행도를 지정 시간 동안 보간한다.
        private async UniTask AnimateMaskAsync(
            float fromProgress,
            float toProgress,
            CancellationToken cancellationToken)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                float progress = Mathf.Clamp01(elapsed / duration);
                SetProgress(Mathf.Lerp(fromProgress, toProgress, progress));
                elapsed += Time.deltaTime;
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }

            SetProgress(toProgress);
        }

        // 전환 방향과 진행 단계에 맞는 셰이더 모드를 반환한다.
        private static float GetMode(
            ScreenEffectType screenEffect,
            ScreenEffectPhase phase)
        {
            bool isFadeIn = phase == ScreenEffectPhase.FadeIn;
            return screenEffect switch
            {
                ScreenEffectType.TransitionFadeRight => isFadeIn ? 4f : 1f,
                ScreenEffectType.TransitionFadeDown => isFadeIn ? 5f : 2f,
                ScreenEffectType.TransitionVignetteRadial => 3f,
                ScreenEffectType.TransitionFadeLeft => isFadeIn ? 1f : 4f,
                ScreenEffectType.TransitionFadeUp => isFadeIn ? 2f : 5f,
                _ => 0f,
            };
        }

        // 타입에 맞는 텍스처 기반 또는 절차 기반 마스크를 반환한다.
        private Texture GetMaskTexture(ScreenEffectType screenEffect)
        {
            if (UsesProceduralMask(screenEffect))
            {
                return Texture2D.whiteTexture;
            }

            if (!TryGetMaskResourcePath(screenEffect, out string resourcePath))
            {
                return null;
            }

            if (maskTextures.TryGetValue(screenEffect, out Texture2D texture) &&
                texture != null)
            {
                return texture;
            }

            texture = Resources.Load<Texture2D>(resourcePath);
            if (texture == null)
            {
                Debug.LogWarning(
                    $"Room transition mask texture not found: Resources/{resourcePath}.png",
                    resources.Owner);
                return null;
            }

            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            maskTextures[screenEffect] = texture;
            return texture;
        }

        // 셰이더 계산만으로 표현되는 방향·비네트 마스크인지 확인한다.
        private static bool UsesProceduralMask(ScreenEffectType screenEffect)
        {
            return screenEffect is
                ScreenEffectType.TransitionFadeRight or
                ScreenEffectType.TransitionFadeDown or
                ScreenEffectType.TransitionVignetteRadial or
                ScreenEffectType.TransitionFadeLeft or
                ScreenEffectType.TransitionFadeUp;
        }

        // 텍스처 기반 마스크의 Resources 경로를 반환한다.
        private static bool TryGetMaskResourcePath(
            ScreenEffectType screenEffect,
            out string resourcePath)
        {
            switch (screenEffect)
            {
                case ScreenEffectType.TransitionSpiralPixel:
                    resourcePath = SpiralPixelResourcePath;
                    return true;
                case ScreenEffectType.TransitionSpiralInward:
                    resourcePath = SpiralInwardResourcePath;
                    return true;
                default:
                    resourcePath = string.Empty;
                    return false;
            }
        }

        // 마스크 화면 전환 전용 머티리얼을 반환한다.
        private Material GetMaterial()
        {
            if (material != null)
            {
                return material;
            }

            Shader shader = Resources.Load<Shader>(ShaderResourcePath);
            if (shader == null)
            {
                shader = Shader.Find(ShaderName);
            }

            if (shader == null)
            {
                Debug.LogWarning(
                    $"Room transition mask shader not found: Resources/{ShaderResourcePath}.shader",
                    resources.Owner);
                return null;
            }

            material = new Material(shader)
            {
                name = "RoomTransitionMaskMaterial",
                hideFlags = HideFlags.DontSave
            };
            material.SetColor(ColorId, Color.white);
            SetProgress(ClearProgress);
            SetMode(0f);
            SetMaskTexture(Texture2D.whiteTexture);
            SetUseTextureColor(true);
            return material;
        }

        // 마스크 셰이더의 진행도를 설정한다.
        private void SetProgress(float progress)
        {
            material?.SetFloat(ProgressId, progress);
        }

        // 마스크 셰이더의 방향·형태 모드를 설정한다.
        private void SetMode(float mode)
        {
            material?.SetFloat(ModeId, mode);
        }

        // 마스크 셰이더의 텍스처를 설정한다.
        private void SetMaskTexture(Texture texture)
        {
            material?.SetTexture(MaskTextureId, texture);
        }

        // 마스크 텍스처의 원본 색상 사용 여부를 설정한다.
        private void SetUseTextureColor(bool useTextureColor)
        {
            material?.SetFloat(UseTextureColorId, useTextureColor ? 1f : 0f);
        }

        // 마스크 화면 전환 전용 머티리얼을 해제한다.
        public void Dispose()
        {
            resources.DetachCaptureMaterial(material);
            if (material != null)
            {
                UnityEngine.Object.Destroy(material);
                material = null;
            }
        }
    }
}
