using UnityEngine;
#if UNITY_EDITOR
using TMPro;
using UnityEngine.UI;
#endif

namespace Escape.UI
{
    // 공용 UI 색상과 에디터 전용 일괄 적용 규칙을 제공한다.
    public static class UIDarkTheme
    {
        public static readonly Color Background = FromHex(0x030B0D, 0.96f);
        public static readonly Color Surface = FromHex(0x071719, 1f);
        public static readonly Color SurfaceRaised = FromHex(0x0C2527, 0.96f);
        public static readonly Color SurfaceMuted = FromHex(0x123638, 0.97f);
        public static readonly Color SurfaceNested = FromHex(0x19484A, 0.98f);
        public static readonly Color TextPrimary = FromHex(0xE6DFC2);
        public static readonly Color Accent = FromHex(0x4CB9AD);
        public static readonly Color InfoAccent = FromHex(0x8FC7FF);
        public static readonly Color InfoSurface = FromHex(0x10253A, 0.96f);
        public static readonly Color ItemSlotEmpty = WithAlpha(SurfaceMuted, 0.62f);
        public static readonly Color ItemSlotIdle = SurfaceRaised;
        public static readonly Color ItemSlotSelected = Accent;
        public static readonly Color ItemSlotIdleContent = WithAlpha(Accent, 0.82f);
        public static readonly Color ItemSlotSelectedContent = WithAlpha(Background, 1f);
        public static readonly Color ItemSlotEmptyContent = WithAlpha(Accent, 0.38f);

#if UNITY_EDITOR
        public static void Apply(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                text.color = TextPrimary;
            }

            foreach (Image image in root.GetComponentsInChildren<Image>(true))
            {
                ApplyImage(image);
            }

            foreach (Selectable selectable in root.GetComponentsInChildren<Selectable>(true))
            {
                ApplySelectable(selectable);
            }
        }

        public static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        private static void ApplyImage(Image image)
        {
            string objectName = image.gameObject.name;
            float alpha = image.color.a;

            ClearBuiltInBackgroundSprite(image);
            bool isDropdownGraphic = image.GetComponent<TMP_Dropdown>() != null;
            bool isScrollViewGraphic = image.GetComponent<ScrollRect>() != null;
            bool isControlGraphic = image.GetComponent<Button>() != null ||
                isDropdownGraphic ||
                isScrollViewGraphic ||
                objectName.Contains("Handle") ||
                objectName.Contains("Fill");
            if (image.sprite != null && !isControlGraphic)
            {
                return;
            }

            if (objectName == "TouchPanel" || objectName == "DownSidePanel")
            {
                return;
            }

            if (objectName.Contains("Handle") || objectName.Contains("Fill"))
            {
                image.color = WithSourceAlpha(Accent, alpha);
                return;
            }

            if (image.GetComponent<Button>() != null)
            {
                image.color = WithSourceAlpha(GetButtonSurface(image.transform), alpha);
                return;
            }

            if (isDropdownGraphic || objectName == "Dropdown")
            {
                image.color = WithSourceAlpha(SurfaceRaised, alpha);
                return;
            }

            if (isScrollViewGraphic || objectName == "Template" || objectName == "Scroll View" || objectName == "Viewport")
            {
                image.color = WithSourceAlpha(Surface, alpha);
                return;
            }

            if (objectName.Contains("Scrollbar"))
            {
                image.color = WithSourceAlpha(SurfaceRaised, alpha);
                return;
            }

            if (objectName.Contains("Background"))
            {
                image.color = WithSourceAlpha(GetNestedSurface(image.transform, 1), alpha);
                return;
            }

            if (IsGenericImage(objectName))
            {
                image.color = WithSourceAlpha(GetNestedSurface(image.transform, 1), alpha);
                return;
            }

            if (IsPanel(objectName))
            {
                image.color = WithSourceAlpha(GetNestedSurface(image.transform, 0), alpha);
            }
        }

        // 숨겨진 이미지는 유지하고 표시 중인 이미지는 테마 고유 불투명도를 사용한다.
        private static Color WithSourceAlpha(Color color, float sourceAlpha)
        {
            color.a = sourceAlpha <= 0f ? 0f : color.a;
            return color;
        }

        // Unity 기본 배경 스프라이트만 제거하고 커스텀 아이콘은 보존한다.
        private static void ClearBuiltInBackgroundSprite(Image image)
        {
            if (image.sprite == null)
            {
                return;
            }

            string spriteName = image.sprite.name;
            if (spriteName == "UISprite" || spriteName == "Background")
            {
                image.sprite = null;
                image.type = Image.Type.Simple;
            }
        }

        private static Color GetButtonSurface(Transform transform)
        {
            return GetPanelDepth(transform) > 0 ? SurfaceMuted : SurfaceRaised;
        }

        private static Color GetNestedSurface(Transform transform, int additionalDepth)
        {
            int depth = Mathf.Min(GetPanelDepth(transform) + additionalDepth, 3);
            return depth switch
            {
                0 => Surface,
                1 => SurfaceRaised,
                2 => SurfaceMuted,
                _ => SurfaceNested,
            };
        }

        private static int GetPanelDepth(Transform transform)
        {
            int depth = 0;

            for (Transform parent = transform.parent; parent != null; parent = parent.parent)
            {
                if (IsPanel(parent.gameObject.name))
                {
                    depth++;
                }
            }

            return depth;
        }

        private static bool IsPanel(string objectName)
        {
            return objectName.EndsWith("Panel") ||
                objectName.EndsWith("PopupUI") ||
                objectName == "Panel" ||
                objectName == "ToastUI" ||
                objectName == "ToastPanelUI";
        }

        private static bool IsGenericImage(string objectName)
        {
            return objectName == "Image" || objectName.StartsWith("Image (");
        }

        private static void ApplySelectable(Selectable selectable)
        {
            ColorBlock colors = selectable.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = FromHex(0xB7E5DE);
            colors.pressedColor = FromHex(0x4D9C94);
            colors.selectedColor = FromHex(0x79CEC4);
            colors.disabledColor = FromHex(0x52706F, 0.45f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            selectable.colors = colors;
        }

        private static Color FromHex(int rgb, float alpha = 1f)
        {
            return new Color(
                ((rgb >> 16) & 0xFF) / 255f,
                ((rgb >> 8) & 0xFF) / 255f,
                (rgb & 0xFF) / 255f,
                alpha);
        }
#else
        public static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        private static Color FromHex(int rgb, float alpha = 1f)
        {
            return new Color(
                ((rgb >> 16) & 0xFF) / 255f,
                ((rgb >> 8) & 0xFF) / 255f,
                (rgb & 0xFF) / 255f,
                alpha);
        }
#endif
    }
}
