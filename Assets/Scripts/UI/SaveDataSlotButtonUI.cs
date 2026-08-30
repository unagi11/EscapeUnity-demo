using System;
using Escape.Localization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Escape.UI
{
    // 저장 슬롯 버튼의 선택 상태와 표시 텍스트를 관리한다.
    public sealed class SaveDataSlotButtonUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text labelText;
        [SerializeField] private Image thumbnailImage;
        [SerializeField] private Image backgroundImage;

        private Button button;
        private Action<SaveDataSlotButtonUI> clicked;
        private Sprite thumbnailSprite;
        private Texture2D thumbnailTexture;
        private string currentThumbnailPngBase64;
        private bool currentHasData;

        public int SlotIndex { get; private set; }

        private void Awake()
        {
            ResolveReferences();
        }

        public void Configure(int slotIndex, Action<SaveDataSlotButtonUI> onClicked)
        {
            SlotIndex = slotIndex;
            clicked = onClicked;
            ResolveReferences();

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => clicked?.Invoke(this));
        }

        private void OnDestroy()
        {
            ReleaseThumbnail();
        }

        public void SetState(bool selected, bool hasData, string description, string thumbnailPngBase64 = null)
        {
            ResolveReferences();
            currentHasData = hasData;

            if (labelText != null)
            {
                labelText.text = string.IsNullOrWhiteSpace(description)
                    ? string.Format(
                        LocalizationService.Text("save_slot_empty_label"),
                        SlotIndex + 1)
                    : description;
            }

            SetThumbnail(hasData ? thumbnailPngBase64 : null);
            ApplyVisualState();
        }

        // 저장 슬롯의 선택/비선택/빈 상태 색상을 실제 UI에 반영한다.
        private void ApplyVisualState()
        {
            ResolveReferences();
            if (labelText != null)
            {
                labelText.raycastTarget = false;
            }

            if (backgroundImage != null)
            {
                backgroundImage.raycastTarget = true;
            }

            if (thumbnailImage != null)
            {
                thumbnailImage.enabled = currentHasData && thumbnailImage.sprite != null;
                thumbnailImage.preserveAspect = true;
                thumbnailImage.raycastTarget = false;
            }

        }

        // Unity Button 상태 변화가 프리팹 색을 바꾸지 않게 한다.
        // 저장 데이터의 PNG 썸네일을 슬롯 표시용 Sprite로 변환한다.
        private void SetThumbnail(string thumbnailPngBase64)
        {
            if (thumbnailImage == null || currentThumbnailPngBase64 == thumbnailPngBase64)
            {
                return;
            }

            ReleaseThumbnail();
            currentThumbnailPngBase64 = thumbnailPngBase64;

            if (string.IsNullOrWhiteSpace(thumbnailPngBase64))
            {
                thumbnailImage.sprite = null;
                return;
            }

            try
            {
                byte[] pngBytes = Convert.FromBase64String(thumbnailPngBase64);
                thumbnailTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!thumbnailTexture.LoadImage(pngBytes))
                {
                    Debug.LogWarning($"Save slot thumbnail load failed: slot={SlotIndex + 1}");
                    ReleaseThumbnail();
                    return;
                }

                thumbnailTexture.name = $"SaveSlotThumbnail{SlotIndex + 1}";
                thumbnailSprite = Sprite.Create(
                    thumbnailTexture,
                    new Rect(0f, 0f, thumbnailTexture.width, thumbnailTexture.height),
                    new Vector2(0.5f, 0.5f));
                thumbnailSprite.name = thumbnailTexture.name;
                thumbnailImage.sprite = thumbnailSprite;
            }
            catch (FormatException)
            {
                Debug.LogWarning($"Save slot thumbnail data is invalid: slot={SlotIndex + 1}");
                ReleaseThumbnail();
            }
        }

        private void ReleaseThumbnail()
        {
            if (thumbnailImage != null)
            {
                thumbnailImage.sprite = null;
            }

            if (thumbnailSprite != null)
            {
                Destroy(thumbnailSprite);
                thumbnailSprite = null;
            }

            if (thumbnailTexture != null)
            {
                Destroy(thumbnailTexture);
                thumbnailTexture = null;
            }

            currentThumbnailPngBase64 = null;
        }

        private void ResolveReferences()
        {
            button ??= GetComponent<Button>();
            backgroundImage ??= GetComponent<Image>();
            labelText ??= transform.Find("Text")?.GetComponent<TMP_Text>();
            thumbnailImage ??= transform.Find("ThumbnailImage")?.GetComponent<Image>();
            thumbnailImage ??= transform.Find("Thumbnail Image")?.GetComponent<Image>();
        }
    }
}
