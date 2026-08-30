using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Escape.Rooms
{
    // 픽셀 해상도를 단계적으로 낮추거나 복원하는 화면 전환을 처리한다.
    internal sealed class ResolutionFadeScreenEffectHandler :
        IRoomScreenEffectHandler,
        IDisposable
    {
        private const string ShaderName = "Hidden/Escape/RoomResolutionFade";
        private static readonly int PixelHeightId = Shader.PropertyToID("_PixelHeight");
        private static readonly int TextureSizeId = Shader.PropertyToID("_TextureSize");
        private static readonly ScreenEffectType[] SupportedTypes =
        {
            ScreenEffectType.ResolutionFade,
        };

        private readonly RoomScreenEffectResources resources;
        private readonly float duration;
        private Material material;

        public IReadOnlyList<ScreenEffectType> Types => SupportedTypes;

        // 공유 캡처 자원과 해상도 페이드 지속시간을 연결한다.
        public ResolutionFadeScreenEffectHandler(
            RoomScreenEffectResources resources,
            float duration)
        {
            this.resources = resources;
            this.duration = duration;
        }

        // 해상도 페이드의 캡처와 단계 애니메이션을 실행한다.
        public async UniTask PlayAsync(
            ScreenEffectType screenEffect,
            ScreenEffectPhase phase,
            CancellationToken cancellationToken)
        {
            if (duration <= 0f ||
                resources.GetCaptureImage() == null ||
                GetMaterial() == null)
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
                if (!CaptureFrame())
                {
                    return;
                }

                await FadeResolutionAsync(1f, 0f, cancellationToken);
            }
            else
            {
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                Canvas.ForceUpdateCanvases();
                if (!CaptureFrame())
                {
                    resources.HideAll();
                    return;
                }

                SetQuality(0f);
                await FadeResolutionAsync(0f, 1f, cancellationToken);
                resources.HideCaptureImage();
            }
        }

        // 현재 방 프레임을 해상도 페이드 머티리얼로 준비한다.
        private bool CaptureFrame()
        {
            Material resolvedMaterial = GetMaterial();
            if (resolvedMaterial == null || !resources.CaptureRoomImage())
            {
                Debug.LogWarning(
                    "Room transition resolution fade could not prepare its overlay image or material.",
                    resources.Owner);
                return false;
            }

            resources.ActivateCaptureImage(resolvedMaterial);
            SetQuality(1f);
            return true;
        }

        // 해상도 픽셀 높이를 단계적으로 변경한다.
        private async UniTask FadeResolutionAsync(
            float fromQuality,
            float toQuality,
            CancellationToken cancellationToken)
        {
            RawImage image = resources.GetCaptureImage();
            if (image == null)
            {
                return;
            }

            image.gameObject.SetActive(true);
            image.raycastTarget = true;

            int textureHeight = resources.CaptureTextureHeight;
            int stepCount = Mathf.Max(1, Mathf.CeilToInt(Mathf.Log(textureHeight, 2f)));
            float stepDuration = duration / stepCount;

            for (int step = 0; step <= stepCount; step++)
            {
                int orderedStep = fromQuality > toQuality
                    ? step
                    : stepCount - step;
                SetPixelHeight(Mathf.Max(1, textureHeight >> orderedStep));
                if (step < stepCount && stepDuration > 0f)
                {
                    await UniTask.Delay(
                        TimeSpan.FromSeconds(stepDuration),
                        ignoreTimeScale: false,
                        cancellationToken: cancellationToken);
                }
            }

            SetQuality(toQuality);
        }

        // 품질 비율을 셰이더의 픽셀 높이와 텍스처 크기로 변환한다.
        private void SetQuality(float quality)
        {
            Material resolvedMaterial = GetMaterial();
            if (resolvedMaterial == null)
            {
                return;
            }

            quality = Mathf.Clamp01(quality);
            int textureWidth = resources.CaptureTextureWidth;
            int textureHeight = resources.CaptureTextureHeight;
            SetPixelHeight(Mathf.Lerp(1f, textureHeight, quality));
            resolvedMaterial.SetVector(
                TextureSizeId,
                new Vector4(
                    textureWidth,
                    textureHeight,
                    1f / textureWidth,
                    1f / textureHeight));
        }

        // 해상도 페이드 셰이더의 픽셀 높이를 설정한다.
        private void SetPixelHeight(float pixelHeight)
        {
            material?.SetFloat(PixelHeightId, Mathf.Max(1f, pixelHeight));
        }

        // 해상도 페이드 전용 머티리얼을 반환한다.
        private Material GetMaterial()
        {
            if (material != null)
            {
                return material;
            }

            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogWarning(
                    $"Room transition shader not found: {ShaderName}. " +
                    "Ensure it is listed in Graphics Settings > Always Included Shaders.",
                    resources.Owner);
                return null;
            }

            material = new Material(shader)
            {
                name = "RoomTransitionResolutionFadeMaterial",
                hideFlags = HideFlags.DontSave
            };
            return material;
        }

        // 해상도 페이드 전용 머티리얼을 해제한다.
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
