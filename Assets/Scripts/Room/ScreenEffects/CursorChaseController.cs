using System.Collections.Generic;
using Escape.Dialogues;
using Escape.Progress;
using Escape.Audio;
using Escape.Data;
using Escape.Input;
using Escape.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Escape.Rooms
{
    [DisallowMultipleComponent]
    public sealed class CursorChaseController : MonoBehaviour
    {
        private const string IconResourcePath = "Sprites/icon";

        [Header("References")]
        [SerializeField] private Camera targetCamera;
        [SerializeField, FormerlySerializedAs("roomManager")] private RoomController roomController;
        [SerializeField, FormerlySerializedAs("dialogueManager")] private DialoguePlayer dialoguePlayer;
        [SerializeField, FormerlySerializedAs("itemManager")] private PlayerInventory inventory;
        [SerializeField] private InventoryPopupUI inventoryPopup;
        [SerializeField] private InfomationPopupUI infomationPopup;
        [SerializeField] private PausePopupUI pausePopup;

        [Header("Cursor")]
        [SerializeField] private Vector2 touchHotspotNormalized = new(0.3f, 0.3f);
        [SerializeField] private bool matchGameScreenPixelScale = true;
        [SerializeField, Min(1)] private int fallbackCursorScale = 4;
        [SerializeField, Min(0.05f)] private float shortTouchMaxDuration = 0.25f;
        [SerializeField, Min(1f)] private float shortTouchMaxDistance = 10f;

        private readonly Dictionary<int, Sprite> icons = new();
        private readonly Dictionary<int, Texture2D> cursorTextures = new();
        private readonly List<RaycastResult> uiRaycastResults = new();

        private TsvTable<Item> items;
        private GameSession state;
        private PointerEventData pointerEventData;
        private EventSystem pointerEventSystem;
        private Vector2 touchStartPosition;
        private float touchStartTime;
        private float touchMaxDistance;
        private bool touchStartedInGameArea;
        private int appliedCursorIcon = int.MinValue;
        private int appliedCursorScale = int.MinValue;
        private int generatedCursorScale = int.MinValue;
        private bool appliedCursorHidden;

        public static bool IsMouseTouchGestureActive { get; private set; }
        public static int MouseTouchReleaseConsumedFrame { get; private set; } = -1;

        private void Awake()
        {
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }
        }

        private void Start()
        {
            state = inventory != null ? inventory.State : null;
            if (inventory != null)
            {
                inventory.Changed += HandleItemChanged;
            }

            items = new TsvDataLoader<Item>().LoadTable();
            LoadCursorIcons();
            RebuildCursorTextures(GetCursorTextureScale());
            ApplySystemCursor(true);
        }

        private void OnDisable()
        {
            if (inventory != null)
            {
                inventory.Changed -= HandleItemChanged;
            }

            IsMouseTouchGestureActive = false;
            RestoreDefaultCursor();
        }

        private void OnDestroy()
        {
            foreach (Texture2D texture in cursorTextures.Values)
            {
                if (texture != null)
                {
                    Destroy(texture);
                }
            }

            cursorTextures.Clear();
        }

        private void Update()
        {
            state = inventory != null ? inventory.State : state;

            HandleClosePopupShortcut();
            HandleInventoryShortcut();
            HandleInformationShortcut();
            ApplySystemCursor(false);

            if (IsInputPaused())
            {
                ResetTouchTracking();
                return;
            }

            HandleMouseRelease();
            HandleTouchRelease();
            HandleUseShortcut();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus)
            {
                ApplySystemCursor(true);
            }
            else
            {
                RestoreDefaultCursor();
            }
        }

        private void HandleItemChanged()
        {
            ApplySystemCursor(true);
        }

        private void LoadCursorIcons()
        {
            icons.Clear();
            ClearCursorTextures();

            Sprite[] sprites = Resources.LoadAll<Sprite>(IconResourcePath);
            for (int i = 0; i < sprites.Length; i++)
            {
                Sprite sprite = sprites[i];
                if (sprite == null || !TryGetCursorIconIndex(sprite.name, out int iconNo))
                {
                    continue;
                }

                icons[iconNo] = sprite;
            }
        }

        private void ApplySystemCursor(bool force)
        {
            if (ShouldHideCursor())
            {
                if (force || !appliedCursorHidden)
                {
                    Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
                    Cursor.visible = false;
                    appliedCursorHidden = true;
                    appliedCursorIcon = int.MinValue;
                }

                return;
            }

            Cursor.visible = true;
            int cursorScale = GetCursorTextureScale();
            if (generatedCursorScale != cursorScale)
            {
                RebuildCursorTextures(cursorScale);
                force = true;
            }

            bool pointerOverUi = TryGetPointerScreenPosition(out Vector2 screenPosition) &&
                IsInteractiveUiAtScreenPosition(screenPosition);
            int iconNo = pointerOverUi || ShouldUseDefaultCursor() ? 0 : GetCurrentCursorIconNo();
            if (!cursorTextures.ContainsKey(iconNo))
            {
                iconNo = 0;
            }

            if (!cursorTextures.TryGetValue(iconNo, out Texture2D texture))
            {
                RestoreDefaultCursor();
                return;
            }

            if (!force &&
                !appliedCursorHidden &&
                appliedCursorIcon == iconNo &&
                appliedCursorScale == cursorScale)
            {
                return;
            }

            Cursor.SetCursor(texture, GetCursorHotspot(texture), CursorMode.ForceSoftware);
            Cursor.visible = true;
            appliedCursorHidden = false;
            appliedCursorIcon = iconNo;
            appliedCursorScale = cursorScale;
        }

        private static bool ShouldHideCursor()
        {
            return Application.isMobilePlatform;
        }

        private void RestoreDefaultCursor()
        {
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            Cursor.visible = true;
            appliedCursorHidden = false;
            appliedCursorIcon = int.MinValue;
            appliedCursorScale = int.MinValue;
        }

        private int GetCursorTextureScale()
        {
            if (!matchGameScreenPixelScale)
            {
                return Mathf.Max(1, fallbackCursorScale);
            }

            Rect screenRect = GameScreenInputArea.GetScreenRect();
            float scale = screenRect.height / GameScreenInputArea.ReferenceHeight;
            return Mathf.Max(1, Mathf.RoundToInt(scale));
        }

        private void RebuildCursorTextures(int scale)
        {
            ClearCursorTextures();
            generatedCursorScale = Mathf.Max(1, scale);
            foreach (var pair in icons)
            {
                Texture2D texture = CreateCursorTexture(pair.Value, generatedCursorScale);
                if (texture != null)
                {
                    cursorTextures[pair.Key] = texture;
                }
            }
        }

        private void ClearCursorTextures()
        {
            foreach (Texture2D texture in cursorTextures.Values)
            {
                if (texture != null)
                {
                    Destroy(texture);
                }
            }

            cursorTextures.Clear();
        }

        private int GetCurrentCursorIconNo()
        {
            string itemId = inventory != null ? inventory.SelectedItemId : string.Empty;
            if (items != null &&
                items.TryGet(itemId, out Item item) &&
                int.TryParse(item.icon_idx, out int iconNo) &&
                icons.ContainsKey(iconNo))
            {
                return iconNo;
            }

            return 0;
        }

        private Vector2 GetCursorHotspot(Texture2D texture)
        {
            return new Vector2(
                Mathf.Clamp01(touchHotspotNormalized.x) * texture.width,
                Mathf.Clamp01(touchHotspotNormalized.y) * texture.height);
        }

        private static Texture2D CreateCursorTexture(Sprite sprite, int scale)
        {
            Rect rect = sprite.textureRect;
            int x = Mathf.RoundToInt(rect.x);
            int y = Mathf.RoundToInt(rect.y);
            int width = Mathf.RoundToInt(rect.width);
            int height = Mathf.RoundToInt(rect.height);
            if (width <= 0 || height <= 0)
            {
                return null;
            }

            try
            {
                scale = Mathf.Max(1, scale);
                Color[] sourcePixels = sprite.texture.GetPixels(x, y, width, height);
                int scaledWidth = width * scale;
                int scaledHeight = height * scale;
                var scaledPixels = new Color[scaledWidth * scaledHeight];
                for (int sy = 0; sy < scaledHeight; sy++)
                {
                    int sourceY = sy / scale;
                    for (int sx = 0; sx < scaledWidth; sx++)
                    {
                        int sourceX = sx / scale;
                        scaledPixels[sy * scaledWidth + sx] = sourcePixels[sourceY * width + sourceX];
                    }
                }

                var texture = new Texture2D(scaledWidth, scaledHeight, TextureFormat.RGBA32, false)
                {
                    name = $"{sprite.name}_cursor",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
                texture.SetPixels(scaledPixels);
                texture.Apply(false, false);
                return texture;
            }
            catch (UnityException)
            {
                Debug.LogWarning($"Cursor sprite texture is not readable: {sprite.name}");
                return null;
            }
        }

        private void HandleMouseRelease()
        {
            if (Application.isMobilePlatform)
            {
                return;
            }

#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasReleasedThisFrame)
            {
                return;
            }

            Vector2 screenPosition = mouse.position.ReadValue();
            MouseTouchReleaseConsumedFrame = Time.frameCount;
            UseAtScreenPosition(screenPosition);
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (!Input.GetMouseButtonUp(0))
            {
                return;
            }

            MouseTouchReleaseConsumedFrame = Time.frameCount;
            UseAtScreenPosition(Input.mousePosition);
#endif
        }

        private void HandleTouchRelease()
        {
#if ENABLE_INPUT_SYSTEM
            Touchscreen touchscreen = Touchscreen.current;
            if (touchscreen == null)
            {
                return;
            }

            var touch = touchscreen.primaryTouch;
            Vector2 currentPosition = touch.position.ReadValue();
            if (touch.press.wasPressedThisFrame)
            {
                BeginTouch(currentPosition);
            }

            if (touch.press.isPressed && touchStartedInGameArea)
            {
                touchMaxDistance = Mathf.Max(touchMaxDistance, Vector2.Distance(touchStartPosition, currentPosition));
            }

            if (touch.press.wasReleasedThisFrame)
            {
                CompleteTouch(currentPosition);
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.touchCount <= 0)
            {
                return;
            }

            Touch touch = Input.GetTouch(0);
            if (touch.phase == TouchPhase.Began)
            {
                BeginTouch(touch.position);
            }
            else if ((touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary) && touchStartedInGameArea)
            {
                touchMaxDistance = Mathf.Max(touchMaxDistance, Vector2.Distance(touchStartPosition, touch.position));
            }
            else if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
            {
                CompleteTouch(touch.position);
            }
#endif
        }

        private void BeginTouch(Vector2 screenPosition)
        {
            touchStartPosition = screenPosition;
            touchStartTime = Time.unscaledTime;
            touchMaxDistance = 0f;
            touchStartedInGameArea = GameScreenInputArea.Contains(screenPosition) &&
                !IsInteractiveUiAtScreenPosition(screenPosition);
            IsMouseTouchGestureActive = touchStartedInGameArea;
        }

        private void CompleteTouch(Vector2 screenPosition)
        {
            if (!touchStartedInGameArea)
            {
                ResetTouchTracking();
                return;
            }

            float duration = Time.unscaledTime - touchStartTime;
            touchMaxDistance = Mathf.Max(touchMaxDistance, Vector2.Distance(touchStartPosition, screenPosition));
            if (duration <= shortTouchMaxDuration && touchMaxDistance <= shortTouchMaxDistance)
            {
                UseAtScreenPosition(screenPosition);
            }

            ResetTouchTracking();
        }

        private void ResetTouchTracking()
        {
            touchStartedInGameArea = false;
            IsMouseTouchGestureActive = false;
        }

        private void UseAtScreenPosition(Vector2 screenPosition)
        {
            if (roomController == null ||
                PopupUIBase.IsAnyOpen ||
                !GameScreenInputArea.Contains(screenPosition) ||
                IsInteractiveUiAtScreenPosition(screenPosition))
            {
                return;
            }

            roomController.TryInspectAtScreenPosition(screenPosition);
        }

        private bool IsInteractiveUiAtScreenPosition(Vector2 screenPosition)
        {
            EventSystem currentEventSystem = EventSystem.current;
            if (currentEventSystem == null)
            {
                return false;
            }

            PointerEventData pointerData = PreparePointerData(currentEventSystem, screenPosition);
            uiRaycastResults.Clear();
            currentEventSystem.RaycastAll(pointerData, uiRaycastResults);
            bool hasInteractiveTarget = false;
            try
            {
                for (int i = 0; i < uiRaycastResults.Count; i++)
                {
                    GameObject target = uiRaycastResults[i].gameObject;
                    if (target == null)
                    {
                        continue;
                    }

                    if (ExecuteEvents.GetEventHandler<IPointerClickHandler>(target) != null ||
                        ExecuteEvents.GetEventHandler<IBeginDragHandler>(target) != null ||
                        ExecuteEvents.GetEventHandler<IScrollHandler>(target) != null)
                    {
                        hasInteractiveTarget = true;
                        break;
                    }
                }
            }
            finally
            {
                // 파괴 직전 UI를 다음 프레임까지 붙잡지 않도록 즉시 비운다.
                uiRaycastResults.Clear();
            }

            return hasInteractiveTarget;
        }

        // EventSystem별 PointerEventData를 재사용해 매 프레임 GC 할당을 없앤다.
        private PointerEventData PreparePointerData(EventSystem currentEventSystem, Vector2 screenPosition)
        {
            if (pointerEventData == null || pointerEventSystem != currentEventSystem)
            {
                pointerEventSystem = currentEventSystem;
                pointerEventData = new PointerEventData(currentEventSystem);
            }

            pointerEventData.Reset();
            pointerEventData.position = screenPosition;
            pointerEventData.button = PointerEventData.InputButton.Left;
            pointerEventData.clickCount = 1;
            return pointerEventData;
        }

        private void HandleUseShortcut()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.spaceKey.wasPressedThisFrame && TryGetPointerScreenPosition(out Vector2 screenPosition))
            {
                SoundPlayer.PlayClickSfx();
                UseAtScreenPosition(screenPosition);
            }

            if (keyboard.tabKey.wasPressedThisFrame)
            {
                inventory?.SelectNextItem();
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Space) && TryGetPointerScreenPosition(out Vector2 screenPosition))
            {
                SoundPlayer.PlayClickSfx();
                UseAtScreenPosition(screenPosition);
            }

            if (Input.GetKeyDown(KeyCode.Tab))
            {
                inventory?.SelectNextItem();
            }
#endif
        }

        private void HandleInventoryShortcut()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.iKey.wasPressedThisFrame)
            {
                ToggleInventoryPopup();
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.I))
            {
                ToggleInventoryPopup();
            }
#endif
        }

        private void ToggleInventoryPopup()
        {
            bool canOpen = inventoryPopup != null &&
                (state == null || !state.IsInputLocked) &&
                (dialoguePlayer == null || !dialoguePlayer.IsPlaying) &&
                !PopupUIBase.IsAnyOpen;
            if (inventoryPopup != null && (inventoryPopup.IsOpen || canOpen))
            {
                inventoryPopup.Toggle();
            }
        }

        // J 키로 정보 팝업을 열고 닫는다.
        private void HandleInformationShortcut()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.jKey.wasPressedThisFrame)
            {
                ToggleInformationPopup();
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.J))
            {
                ToggleInformationPopup();
            }
#endif
        }

        // 다른 팝업 및 진행 입력을 침범하지 않는 범위에서 정보 팝업을 전환한다.
        private void ToggleInformationPopup()
        {
            bool canOpen = infomationPopup != null &&
                (state == null || !state.IsInputLocked) &&
                (dialoguePlayer == null || !dialoguePlayer.IsPlaying) &&
                !PopupUIBase.IsAnyOpen;
            if (infomationPopup != null && (infomationPopup.IsOpen || canOpen))
            {
                infomationPopup.Toggle();
            }
        }

        private void HandleClosePopupShortcut()
        {
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null &&
                Keyboard.current.escapeKey.wasPressedThisFrame &&
                CanHandleClosePopupShortcut())
            {
                CloseTopmostOrOpenPause();
            }
#elif ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetKeyDown(KeyCode.Escape) && CanHandleClosePopupShortcut())
            {
                CloseTopmostOrOpenPause();
            }
#endif
        }

        private bool CanHandleClosePopupShortcut()
        {
            return !YesNoUI.IsShowing &&
                (state == null || !state.IsInputLocked) &&
                (dialoguePlayer == null || !dialoguePlayer.IsPlaying);
        }

        private void CloseTopmostOrOpenPause()
        {
            if (!PopupUIBase.CloseTopmost())
            {
                pausePopup?.Open();
            }
        }

        private bool IsInputPaused()
        {
            return PopupUIBase.IsAnyOpen ||
                (roomController != null && roomController.IsExecutingInteractionSequence) ||
                (state != null && state.IsInputLocked) ||
                (dialoguePlayer != null && dialoguePlayer.IsPlaying);
        }

        private bool ShouldUseDefaultCursor()
        {
            return PopupUIBase.IsAnyOpen ||
                (state != null && state.IsInputLocked) ||
                (dialoguePlayer != null && dialoguePlayer.IsPlaying);
        }

        private static bool TryGetPointerScreenPosition(out Vector2 screenPosition)
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                screenPosition = Mouse.current.position.ReadValue();
                return true;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            screenPosition = Input.mousePosition;
            return true;
#else
            screenPosition = default;
            return false;
#endif
        }

        private static bool TryGetCursorIconIndex(string name, out int iconNo)
        {
            const string Prefix = "icon_";
            iconNo = 0;
            return !string.IsNullOrWhiteSpace(name) &&
                name.StartsWith(Prefix, System.StringComparison.Ordinal) &&
                int.TryParse(name[Prefix.Length..], out iconNo);
        }
    }
}
