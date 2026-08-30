using System;
using UnityEngine;
using UnityEngine.UI;

namespace Escape.Rooms
{
    // 모든 화면 효과가 공유하는 오버레이 이미지와 캡처 텍스처를 관리한다.
    internal sealed class RoomScreenEffectResources : IDisposable
    {
        private const string RoomImageName = "RoomImage";
        private static readonly int ClipRectId = Shader.PropertyToID("_ClipRect");

        private readonly MonoBehaviour owner;
        private RawImage roomImage;
        private RawImage captureImage;
        private Image blackImage;
        private RenderTexture captureTexture;
        private int captureTextureWidth;
        private int captureTextureHeight;

        public MonoBehaviour Owner => owner;
        public RenderTexture CaptureTexture => captureTexture;
        public int CaptureTextureWidth => captureTexture != null
            ? captureTexture.width
            : Mathf.Max(1, Screen.width);
        public int CaptureTextureHeight => captureTexture != null
            ? captureTexture.height
            : Mathf.Max(1, Screen.height);

        // 화면 효과의 실행 컨텍스트와 캡처 원본 이미지를 연결한다.
        public RoomScreenEffectResources(MonoBehaviour owner, RawImage roomImage)
        {
            this.owner = owner;
            this.roomImage = roomImage;
        }

        // 단색 페이드에 사용할 전체화면 이미지를 반환한다.
        public Image GetBlackImage()
        {
            if (blackImage != null)
            {
                return blackImage;
            }

            RectTransform parent = ResolveBlackParent();
            if (parent == null)
            {
                Debug.LogWarning("Screen effect requires a Canvas in the scene.", owner);
                return null;
            }

            var screenEffectObject = new GameObject(
                "RoomScreenEffectOverlay",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            screenEffectObject.transform.SetParent(parent, false);
            screenEffectObject.transform.SetAsLastSibling();

            var rectTransform = (RectTransform)screenEffectObject.transform;
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;

            blackImage = screenEffectObject.GetComponent<Image>();
            blackImage.color = Color.black;
            blackImage.raycastTarget = false;
            screenEffectObject.SetActive(false);
            return blackImage;
        }

        // 단색 오버레이를 입력 차단 상태로 활성화한다.
        public void ActivateBlackImage()
        {
            Image image = GetBlackImage();
            if (image == null)
            {
                return;
            }

            image.gameObject.SetActive(true);
            image.transform.SetAsLastSibling();
            image.raycastTarget = true;
        }

        // 단색 오버레이의 색과 알파를 설정한다.
        public void SetBlackColorAlpha(Color color, float alpha)
        {
            if (blackImage == null)
            {
                return;
            }

            color.a = Mathf.Clamp01(alpha);
            blackImage.color = color;
        }

        // 단색 오버레이를 초기 위치로 되돌리고 숨긴다.
        public void HideBlackImage()
        {
            if (blackImage == null)
            {
                return;
            }

            blackImage.raycastTarget = false;
            blackImage.rectTransform.anchoredPosition = Vector2.zero;
            blackImage.gameObject.SetActive(false);
        }

        // 방 화면 캡처를 표시할 이미지를 반환한다.
        public RawImage GetCaptureImage()
        {
            if (captureImage != null)
            {
                return captureImage;
            }

            RectTransform parent = ResolveScreenEffectParent();
            if (parent == null)
            {
                Debug.LogWarning("Room transition requires RoomImage.", owner);
                return null;
            }

            var captureObject = new GameObject(
                "RoomTransitionResolutionFade",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(RawImage));
            captureObject.transform.SetParent(parent, false);
            captureObject.transform.SetAsLastSibling();

            captureImage = captureObject.GetComponent<RawImage>();
            captureImage.color = Color.white;
            captureImage.raycastTarget = false;
            UpdateCaptureImageRect(captureImage);
            captureObject.SetActive(false);
            return captureImage;
        }

        // 현재 RoomImage를 연출용 렌더 텍스처에 복사한다.
        public bool CaptureRoomImage()
        {
            RawImage image = GetCaptureImage();
            if (image == null)
            {
                return false;
            }

            RectTransform area = ResolveScreenEffectParent();
            Rect areaRect = area != null
                ? area.rect
                : new Rect(
                    0f,
                    0f,
                    Escape.Input.GameScreenInputArea.ReferenceWidth,
                    Escape.Input.GameScreenInputArea.ReferenceHeight);
            int width = Mathf.Max(1, Mathf.RoundToInt(areaRect.width));
            int height = Mathf.Max(1, Mathf.RoundToInt(areaRect.height));
            EnsureCaptureTexture(width, height);
            UpdateCaptureImageRect(image);

            return CopyRoomImageToCaptureTexture();
        }

        // 캡처 이미지를 지정한 머티리얼로 화면에 표시한다.
        public void ActivateCaptureImage(Material material)
        {
            RawImage image = GetCaptureImage();
            if (image == null)
            {
                return;
            }

            image.texture = captureTexture;
            image.material = material;
            image.color = Color.white;
            image.gameObject.SetActive(true);
            image.raycastTarget = true;
            image.transform.SetAsLastSibling();
            UpdateCaptureImageRect(image);
        }

        // 캡처 이미지가 사용 중인 특정 머티리얼을 분리한다.
        public void DetachCaptureMaterial(Material material)
        {
            if (captureImage != null && captureImage.material == material)
            {
                captureImage.material = null;
            }
        }

        // 캡처 이미지와 렌더 텍스처를 정리한다.
        public void HideCaptureImage()
        {
            if (captureImage != null)
            {
                captureImage.raycastTarget = false;
                captureImage.texture = null;
                captureImage.material = null;
                captureImage.gameObject.SetActive(false);
            }

            ReleaseCaptureTexture();
        }

        // 공유하는 모든 화면 효과 이미지를 숨긴다.
        public void HideAll()
        {
            HideCaptureImage();
            HideBlackImage();
        }

        // RoomImage 영역을 화면 캡처 오버레이의 부모로 반환한다.
        private RectTransform ResolveScreenEffectParent()
        {
            RawImage resolvedRoomImage = ResolveRoomImage();
            return resolvedRoomImage != null ? resolvedRoomImage.rectTransform : null;
        }

        // 검정 페이드가 상위 대사 UI를 가리지 않는 캔버스 부모를 반환한다.
        private RectTransform ResolveBlackParent()
        {
            RawImage resolvedRoomImage = ResolveRoomImage();
            Canvas roomCanvas = resolvedRoomImage != null ? resolvedRoomImage.canvas : null;
            if (roomCanvas != null && roomCanvas.rootCanvas != null)
            {
                return roomCanvas.rootCanvas.transform as RectTransform;
            }

            Canvas[] canvases = UnityEngine.Object.FindObjectsByType<Canvas>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            RectTransform fallback = null;

            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas canvas = canvases[i];
                if (canvas == null)
                {
                    continue;
                }

                var rectTransform = canvas.transform as RectTransform;
                fallback ??= rectTransform;
                if (canvas.isRootCanvas && canvas.gameObject.activeInHierarchy)
                {
                    return rectTransform;
                }
            }

            for (int i = 0; i < canvases.Length; i++)
            {
                Canvas canvas = canvases[i];
                if (canvas != null && canvas.gameObject.activeInHierarchy)
                {
                    return canvas.transform as RectTransform;
                }
            }

            return fallback;
        }

        // 직렬화된 RoomImage 또는 현재 씬의 동명 이미지를 반환한다.
        private RawImage ResolveRoomImage()
        {
            if (roomImage != null)
            {
                return roomImage;
            }

            RawImage[] images = UnityEngine.Object.FindObjectsByType<RawImage>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
            for (int i = 0; i < images.Length; i++)
            {
                if (images[i] != null &&
                    images[i].name == RoomImageName &&
                    images[i].gameObject.scene == owner.gameObject.scene)
                {
                    roomImage = images[i];
                    return images[i];
                }
            }

            return null;
        }

        // RoomImage의 실제 출력 텍스처를 캡처 텍스처에 복사한다.
        private bool CopyRoomImageToCaptureTexture()
        {
            RawImage sourceImage = ResolveRoomImage();
            Texture sourceTexture = sourceImage != null ? sourceImage.texture : null;
            if (sourceTexture == null || captureTexture == null)
            {
                return false;
            }

            Material copyMaterial = null;
            try
            {
                Material sourceMaterial = sourceImage.materialForRendering;
                if (sourceMaterial != null)
                {
                    copyMaterial = new Material(sourceMaterial)
                    {
                        hideFlags = HideFlags.DontSave
                    };
                    copyMaterial.SetVector(
                        ClipRectId,
                        new Vector4(-100000f, -100000f, 100000f, 100000f));
                    Graphics.Blit(sourceTexture, captureTexture, copyMaterial);
                }
                else
                {
                    Graphics.Blit(sourceTexture, captureTexture);
                }

                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"Room transition RawImage copy failed: {exception.Message}",
                    owner);
                return false;
            }
            finally
            {
                if (copyMaterial != null)
                {
                    UnityEngine.Object.Destroy(copyMaterial);
                }
            }
        }

        // 현재 캡처 크기에 맞는 렌더 텍스처를 준비한다.
        private void EnsureCaptureTexture(int width, int height)
        {
            if (captureTexture != null &&
                captureTextureWidth == width &&
                captureTextureHeight == height &&
                captureTexture.IsCreated())
            {
                return;
            }

            ReleaseCaptureTexture();

            captureTexture = new RenderTexture(
                width,
                height,
                0,
                RenderTextureFormat.Default)
            {
                name = "RoomTransitionResolutionFadeTexture",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
                antiAliasing = 1
            };
            captureTexture.Create();
            captureTextureWidth = width;
            captureTextureHeight = height;

            if (captureImage != null)
            {
                captureImage.texture = captureTexture;
            }
        }

        // 캡처 이미지를 RoomImage 영역에 맞춘다.
        private static void UpdateCaptureImageRect(RawImage image)
        {
            if (image == null)
            {
                return;
            }

            RectTransform rectTransform = image.rectTransform;
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
            rectTransform.anchoredPosition = Vector2.zero;
        }

        // 생성한 렌더 텍스처를 해제한다.
        private void ReleaseCaptureTexture()
        {
            if (captureImage != null)
            {
                captureImage.texture = null;
            }

            if (captureTexture != null)
            {
                captureTexture.Release();
                UnityEngine.Object.Destroy(captureTexture);
                captureTexture = null;
            }

            captureTextureWidth = 0;
            captureTextureHeight = 0;
        }

        // 공유 캡처 자원을 해제한다.
        public void Dispose()
        {
            ReleaseCaptureTexture();
        }
    }
}
