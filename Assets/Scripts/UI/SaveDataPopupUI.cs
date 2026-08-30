using System;
using Escape.Storage;
using Escape.SceneFlow;
using Escape.Localization;
using Escape.Progress;
using System.Collections.Generic;
using System.Globalization;
using Escape.Rooms;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Escape.UI
{
    // 저장/로드 팝업의 모드 진입, 슬롯 선택, PlayerPrefs 저장 데이터를 관리한다.
    public sealed class SaveDataPopupUI : PopupUIBase
    {
        private const int SlotCount = 3;
        private const string SaveKeyPrefix = "escape.save.slot.";
        private const string SaveLogPrefix = "[SaveData]";
        private const string RoomImageName = "RoomImage";
        private const int ThumbnailWidth = 72;
        private const int ThumbnailHeight = 54;
        private static readonly int ClipRectId = Shader.PropertyToID("_ClipRect");

        [Header("Popup")]
        [SerializeField] private GameObject root;
        [SerializeField] private Button openButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private bool hideOnAwake = true;
        [SerializeField, Min(0f)] private float fadeDuration = 0.18f;
        [SerializeField] private CanvasGroup canvasGroup;

        [Header("Controls")]
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text descText;
        [SerializeField] private Button saveButton;
        [SerializeField] private Button loadButton;
        [SerializeField] private SaveDataSlotButtonUI[] slots = Array.Empty<SaveDataSlotButtonUI>();

        private SaveDataMode mode = SaveDataMode.Save;
        private int selectedSlotIndex;
        private bool loadOnlyMode;
        private string loadSceneNameAfterLoad = string.Empty;
        private static SaveData pendingSceneLoadData;
        private static readonly Dictionary<string, string> roomSheetThumbnailCache = new(StringComparer.Ordinal);

        protected override GameObject PopupRoot { get => root; set => root = value; }
        protected override Button PopupOpenButton { get => openButton; set => openButton = value; }
        protected override Button PopupCloseButton { get => closeButton; set => closeButton = value; }
        protected override bool PopupHideOnAwake => hideOnAwake;
        protected override float PopupFadeDuration => fadeDuration;
        protected override CanvasGroup PopupCanvasGroup { get => canvasGroup; set => canvasGroup = value; }

        private enum SaveDataMode
        {
            Save,
            Load
        }

        [Serializable]
        private sealed class SaveData
        {
            public string savedAt;
            public string roomId;
            public string playerName;
            public string selectedItemId;
            public int runSeed;
            public int tutorialSeenMask;
            public float health = -1f;
            public string thumbnailPngBase64;
            public List<string> items = new();
            public List<string> infos = new();
            public List<string> objectStates = new();
            public List<string> animationStates = new();
        }

        private void Awake()
        {
            ResolveReferences();
            InitializePopupChrome();
            BindControls();
            Refresh();
        }

        private void OnEnable()
        {
            LocalizationService.Ensure().LanguageChanged += Refresh;
        }

        private void OnDisable()
        {
            if (LocalizationService.Instance != null)
            {
                LocalizationService.Instance.LanguageChanged -= Refresh;
            }
        }

        // 다시 열 때 이전 슬롯의 선택 강조를 제거한다.
        protected override void OnBeforeOpen()
        {
            selectedSlotIndex = -1;
        }

        protected override void OnAfterOpen()
        {
            Refresh();
        }

        public static bool HasAnySaveData()
        {
            for (int i = 0; i < SlotCount; i++)
            {
                if (HasSaveData(i))
                {
                    return true;
                }
            }

            return false;
        }

        public static void ClearPendingLoad()
        {
            pendingSceneLoadData = null;
            SceneManager.sceneLoaded -= ApplyPendingSceneLoadData;
        }

        // 모든 세이브 슬롯을 로컬과 플랫폼 저장소에서 함께 삭제한다.
        public static void DeleteAllSaveData()
        {
            ClearPendingLoad();
            roomSheetThumbnailCache.Clear();

            for (int slotIndex = 0; slotIndex < SlotCount; slotIndex++)
            {
                SaveDataStore.Current.Delete(GetSlotKey(slotIndex));
            }
        }

        public bool LoadMostRecent(string sceneName)
        {
            if (!TryGetMostRecentSaveData(out SaveData data, out int slotIndex))
            {
                ToastPresenter.Show(LocalizationService.Text("save_no_data"));
                return false;
            }

            LoadSaveData(data, sceneName, slotIndex);
            // ToastPresenter.Show($"LOAD SLOT {slotIndex + 1}");
            return true;
        }

        public void OpenLoadOnly(string sceneName)
        {
            loadOnlyMode = true;
            loadSceneNameAfterLoad = sceneName ?? string.Empty;
            mode = SaveDataMode.Load;
            Open();
        }

        private void SelectSlot(SaveDataSlotButtonUI slot)
        {
            selectedSlotIndex = slot != null ? slot.SlotIndex : selectedSlotIndex;
            RefreshSlots();

            if (mode == SaveDataMode.Save)
            {
                SaveSelectedSlot();
                return;
            }

            LoadSelectedSlot();
            Refresh();
        }

        private void SaveSelectedSlot()
        {
            if (HasSaveData(selectedSlotIndex))
            {
                YesNoUI.Show(
                    LocalizationService.Text("save_overwrite_confirm", "덮어쓰시겠습니까?"),
                    SaveSelectedSlotImmediate,
                    Refresh);
                return;
            }

            SaveSelectedSlotImmediate();
        }

        private void SaveSelectedSlotImmediate()
        {
            SaveData data = CaptureSaveData();
            if (string.IsNullOrWhiteSpace(data.thumbnailPngBase64))
            {
                data.thumbnailPngBase64 = GetRoomSheetThumbnailPngBase64(data.roomId);
            }

            if (string.IsNullOrWhiteSpace(data.thumbnailPngBase64))
            {
                Debug.LogWarning($"Save thumbnail capture failed: room={data.roomId}");
            }

            string json = JsonUtility.ToJson(data);
            LogSaveData("SAVE", selectedSlotIndex, data);
            if (!SaveDataStore.Current.Write(GetSlotKey(selectedSlotIndex), json))
            {
                Debug.LogWarning($"{SaveLogPrefix} 저장소 기록 실패: slot={selectedSlotIndex + 1}");
                ToastPresenter.Show(LocalizationService.Text("save_failed", "저장에 실패했습니다."));
                return;
            }

            ToastPresenter.Show(string.Format(
                LocalizationService.Text("save_slot_saved"),
                selectedSlotIndex + 1));
            Refresh();
        }

        private void LoadSelectedSlot()
        {
            if (!TryLoadSaveData(selectedSlotIndex, out SaveData data))
            {
                ToastPresenter.Show(string.Format(
                    LocalizationService.Text("save_slot_empty_toast"),
                    selectedSlotIndex + 1));
                Refresh();
                return;
            }

            string loadConfirmFormat = LocalizationService.Text(
                "load_confirm",
                "불러오시겠습니까?\n{0}");
            YesNoUI.Show(string.Format(loadConfirmFormat, BuildSlotDescription(selectedSlotIndex, data)), () =>
            {
                string sceneName = loadSceneNameAfterLoad;
                // 토스트가 뒤 팝업 위로 뜨지 않도록, 로드/토스트 전에 모든 팝업을 먼저 닫는다.
                PopupUIBase.CloseAll();
                LoadSaveData(data, sceneName, selectedSlotIndex);
                // ToastPresenter.Show($"LOAD SLOT {selectedSlotIndex + 1}");
            }, Refresh);
        }

        private SaveData CaptureSaveData()
        {
            GameSession state = ResolveGameSession();
            RoomController roomController = FindFirstObjectByType<RoomController>(FindObjectsInactive.Include);
            var data = new SaveData
            {
                savedAt = DateTime.Now.ToString("yyyy.MM.dd HH:mm:ss"),
                roomId = roomController != null ? roomController.CurrentRoomId.ToString() : RoomType.None.ToString(),
                playerName = GameSession.GetPlayerNameOrDefault(),
                selectedItemId = state != null ? state.SelectedItemId : string.Empty,
                runSeed = state != null ? state.RunSeed : 0,
                tutorialSeenMask = state != null ? state.TutorialSeenMask : 0,
                health = state != null ? state.CurrentHealth : -1f,
                thumbnailPngBase64 = CaptureRoomThumbnail()
            };

            if (state != null)
            {
                data.items.AddRange(state.ItemsInAcquiredOrder);

                data.infos.AddRange(state.Infos);
                data.infos.Sort(StringComparer.Ordinal);
            }

            if (roomController != null)
            {
                data.objectStates.AddRange(roomController.CaptureObjectActiveStates());
                data.animationStates.AddRange(roomController.CaptureObjectAnimationStates());
            }

            return data;
        }

        private static void ApplySaveData(SaveData data)
        {
            GameSession state = ResolveGameSession();
            if (state == null || data == null)
            {
                return;
            }

            // 불러오기 적용 시 열려 있던 팝업(일시정지 등)을 모두 닫는다.
            PopupUIBase.CloseAll();

            state.ResetState();
            state.SetPlayerName(data.playerName);
            state.SetRunSeed(data.runSeed);
            state.RestoreTutorialSeenMask(data.tutorialSeenMask);
            if (data.health > 0f)
            {
                state.SetHealth(Mathf.RoundToInt(data.health));
            }

            for (int i = 0; i < data.items.Count; i++)
            {
                state.AddItem(data.items[i]);
            }

            if (data.infos != null)
            {
                for (int i = 0; i < data.infos.Count; i++)
                {
                    state.AddInfo(data.infos[i]);
                }
            }

            state.SelectItem(data.selectedItemId);
            PlayerInventory.Instance?.EnsureDefaults();
            InfoCollection.Instance?.EnsureDefaults();

            RoomController roomController = FindFirstObjectByType<RoomController>(FindObjectsInactive.Include);
            if (Enum.TryParse(data.roomId, out RoomType roomId))
            {
                roomController?.RestoreRoom(roomId);
            }

            roomController?.RestoreObjectActiveStates(data.objectStates);
            roomController?.RestoreObjectAnimationStates(data.animationStates);
        }

        private void Refresh()
        {
            ResolveReferences();
            RefreshModeControls();
            RefreshSlots();
        }

        // 현재 진입 모드에 맞춰 외부 버튼과 제목을 갱신한다.
        private void RefreshModeControls()
        {
            if (loadOnlyMode)
            {
                mode = SaveDataMode.Load;
            }

            SetButtonVisible(saveButton, !loadOnlyMode);
            SetButtonVisible(loadButton, true);

            if (descText != null)
            {
                descText.text = mode == SaveDataMode.Save
                    ? LocalizationService.Text("save_mode_description", "저장할 슬롯을 선택하세요.")
                    : LocalizationService.Text("load_mode_description", "불러올 슬롯을 선택하세요.");
            }

            if (titleText != null)
            {
                titleText.text = mode == SaveDataMode.Save
                    ? LocalizationService.Text("save", "저장")
                    : LocalizationService.Text("load", "불러오기");
            }
        }

        private void RefreshSlots()
        {
            for (int i = 0; i < slots.Length; i++)
            {
                SaveDataSlotButtonUI slot = slots[i];
                if (slot == null)
                {
                    continue;
                }

                bool hasData = TryLoadSaveData(i, out SaveData data);
                slot.SetState(i == selectedSlotIndex, hasData, BuildSlotDescription(i, data), GetThumbnailPngBase64(data));
            }
        }

        private static string BuildSlotDescription(int slotIndex, SaveData data)
        {
            if (data == null || string.IsNullOrWhiteSpace(data.savedAt))
            {
                return string.Format(
                    LocalizationService.Text("save_slot_empty_label"),
                    slotIndex + 1);
            }

            string playerName = GameSession.NormalizePlayerName(data.playerName);
            return $"{playerName}\n{FormatSlotSavedAt(data.savedAt)}";
        }

        // 저장 시간은 슬롯에서 읽기 쉬운 짧은 형식으로 표시한다.
        private static string FormatSlotSavedAt(string savedAt)
        {
            DateTime parsed = ParseSavedAt(savedAt);
            return parsed == DateTime.MinValue
                ? savedAt ?? string.Empty
                : parsed.ToString("yy.MM.dd HH:mm", CultureInfo.InvariantCulture);
        }

        // 복구용 방 ID를 유지하면서 슬롯에는 현재 언어의 방 이름을 표시한다.
        private static string GetLocalizedRoomName(string roomId)
        {
            if (!Enum.TryParse(roomId, out RoomType roomType))
            {
                return string.IsNullOrWhiteSpace(roomId)
                    ? LocalizationService.Text("save_room_unknown")
                    : roomId;
            }

            return roomType switch
            {
                RoomType.LivingRoom => LocalizationService.Text("room_living", "거실"),
                RoomType.BedRoom => LocalizationService.Text("room_bedroom", "침실"),
                RoomType.KitchenRoom => LocalizationService.Text("room_kitchen", "부엌"),
                RoomType.EntranceRoom => LocalizationService.Text("room_entrance", "현관"),
                RoomType.UtilityRoom => LocalizationService.Text("room_utility", "다용도실"),
                _ => string.IsNullOrWhiteSpace(roomId)
                    ? LocalizationService.Text("save_room_unknown")
                    : roomId
            };
        }

        private void BindControls()
        {
            saveButton?.onClick.AddListener(OpenSaveMode);
            loadButton?.onClick.AddListener(OpenLoadMode);
            closeButton?.onClick.AddListener(Close);

            for (int i = 0; i < slots.Length; i++)
            {
                slots[i]?.Configure(i, SelectSlot);
            }
        }

        private void OpenSaveMode()
        {
            loadOnlyMode = false;
            loadSceneNameAfterLoad = string.Empty;
            mode = SaveDataMode.Save;
            Open();
        }

        private void OpenLoadMode()
        {
            loadOnlyMode = false;
            loadSceneNameAfterLoad = string.Empty;
            mode = SaveDataMode.Load;
            Open();
        }

        private void ResolveReferences()
        {
            root ??= gameObject;
            canvasGroup ??= root.GetComponent<CanvasGroup>();
            titleText ??= FindChildByName("TitleText")?.GetComponent<TMP_Text>();
            descText ??= FindChildByName("Desc Text")?.GetComponent<TMP_Text>();
            descText ??= FindChildByName("DescText")?.GetComponent<TMP_Text>();
            closeButton ??= FindChildButton("CloseButton");
            saveButton ??= FindChildButton("SaveButton");
            loadButton ??= FindChildButton("LoadButton");

            if (slots == null || slots.Length == 0)
            {
                slots = ResolveSlots();
            }
        }

        protected override void ResolvePopupChromeReferences()
        {
            root ??= gameObject;
            canvasGroup ??= root.GetComponent<CanvasGroup>();
            closeButton ??= FindChildButton("CloseButton");
        }

        private SaveDataSlotButtonUI[] ResolveSlots()
        {
            Button[] buttons = root.GetComponentsInChildren<Button>(true);
            var resolved = new List<SaveDataSlotButtonUI>();
            for (int i = 0; i < buttons.Length; i++)
            {
                if (!buttons[i].name.StartsWith("SaveDataSlotButton", StringComparison.Ordinal))
                {
                    continue;
                }

                SaveDataSlotButtonUI slot = buttons[i].GetComponent<SaveDataSlotButtonUI>();
                if (slot == null)
                {
                    slot = buttons[i].gameObject.AddComponent<SaveDataSlotButtonUI>();
                }

                resolved.Add(slot);
            }

            resolved.Sort(CompareSlotNames);
            return resolved.ToArray();
        }

        private static int CompareSlotNames(SaveDataSlotButtonUI left, SaveDataSlotButtonUI right)
        {
            return ExtractSlotOrder(left != null ? left.name : string.Empty)
                .CompareTo(ExtractSlotOrder(right != null ? right.name : string.Empty));
        }

        private static int ExtractSlotOrder(string objectName)
        {
            if (string.Equals(objectName, "SaveDataSlotButton", StringComparison.Ordinal))
            {
                return 0;
            }

            int open = objectName.LastIndexOf('(');
            int close = objectName.LastIndexOf(')');
            if (open >= 0 && close > open &&
                int.TryParse(objectName.Substring(open + 1, close - open - 1), out int index))
            {
                return index;
            }

            return int.MaxValue;
        }

        private Button FindChildButton(string childName)
        {
            Transform child = FindChildByName(childName);
            return child != null ? child.GetComponent<Button>() : null;
        }

        private Transform FindChildByName(string childName)
        {
            Transform searchRoot = root != null ? root.transform : transform;
            foreach (Transform child in searchRoot.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == childName)
                {
                    return child;
                }
            }

            return null;
        }

        private static bool HasSaveData(int slotIndex)
        {
            return TryLoadSaveData(slotIndex, out _);
        }

        private static bool TryLoadSaveData(int slotIndex, out SaveData data)
        {
            if (!SaveDataStore.Current.TryRead(GetSlotKey(slotIndex), out string json))
            {
                data = null;
                return false;
            }

            data = DeserializeSaveData(json);
            return data != null;
        }

        // 손상되거나 빈 슬롯 JSON을 안전하게 무시한다.
        private static SaveData DeserializeSaveData(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            try
            {
                return JsonUtility.FromJson<SaveData>(json);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private static bool TryGetMostRecentSaveData(out SaveData data, out int slotIndex)
        {
            data = null;
            slotIndex = -1;
            DateTime latestTime = DateTime.MinValue;
            string latestSavedAt = string.Empty;

            for (int i = 0; i < SlotCount; i++)
            {
                if (!TryLoadSaveData(i, out SaveData candidate))
                {
                    continue;
                }

                DateTime savedAt = ParseSavedAt(candidate.savedAt);
                bool isNewer = data == null ||
                    savedAt > latestTime ||
                    (savedAt == latestTime && string.Compare(candidate.savedAt, latestSavedAt, StringComparison.Ordinal) > 0);

                if (!isNewer)
                {
                    continue;
                }

                data = candidate;
                slotIndex = i;
                latestTime = savedAt;
                latestSavedAt = candidate.savedAt ?? string.Empty;
            }

            return data != null;
        }

        private static DateTime ParseSavedAt(string value)
        {
            return DateTime.TryParseExact(
                value,
                "yyyy.MM.dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateTime savedAt)
                ? savedAt
                : DateTime.MinValue;
        }

        private static void LoadSaveData(SaveData data, string fallbackSceneName, int slotIndex)
        {
            if (data == null)
            {
                return;
            }

            LogSaveData("LOAD", slotIndex, data);
            string sceneName = ResolveLoadSceneName(data, fallbackSceneName);

            if (string.IsNullOrWhiteSpace(sceneName))
            {
                ApplySaveData(data);
                return;
            }

            pendingSceneLoadData = data;
            SceneManager.sceneLoaded -= ApplyPendingSceneLoadData;
            SceneManager.sceneLoaded += ApplyPendingSceneLoadData;
            if (!EscapeSceneLoader.Load(sceneName))
            {
                pendingSceneLoadData = null;
                SceneManager.sceneLoaded -= ApplyPendingSceneLoadData;
                ApplySaveData(data);
            }
        }

        // 저장 슬롯은 상태만 들고, 로드 대상 씬은 호출자가 지정한 현재 플로우를 따른다.
        private static string ResolveLoadSceneName(SaveData data, string fallbackSceneName)
        {
            fallbackSceneName = (fallbackSceneName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(fallbackSceneName))
            {
                fallbackSceneName = SceneManager.GetActiveScene().name;
            }

            if (Application.CanStreamedLevelBeLoaded(fallbackSceneName))
            {
                return fallbackSceneName;
            }

            Debug.LogWarning($"{SaveLogPrefix} Fallback scene is not loadable: {fallbackSceneName}");
            return string.Empty;
        }

        private static void LogSaveData(string operation, int slotIndex, SaveData data)
        {
            Debug.Log($"{SaveLogPrefix} {operation} slot={slotIndex + 1}\n{JsonUtility.ToJson(data, true)}");
        }

        private static void ApplyPendingSceneLoadData(Scene scene, LoadSceneMode mode)
        {
            if (pendingSceneLoadData == null)
            {
                SceneManager.sceneLoaded -= ApplyPendingSceneLoadData;
                return;
            }

            SaveData data = pendingSceneLoadData;
            pendingSceneLoadData = null;
            SceneManager.sceneLoaded -= ApplyPendingSceneLoadData;
            ApplySaveData(data);
        }

        private static string GetSlotKey(int slotIndex)
        {
            return $"{SaveKeyPrefix}{Mathf.Clamp(slotIndex, 0, SlotCount - 1)}";
        }


        private static string CaptureRoomThumbnail()
        {
            RawImage roomImage = FindRoomImage();
            Texture sourceTexture = roomImage != null ? roomImage.texture : null;
            if (sourceTexture == null)
            {
                return string.Empty;
            }

            return EncodeThumbnail(sourceTexture, roomImage.materialForRendering);
        }

        private static string GetThumbnailPngBase64(SaveData data)
        {
            if (data == null)
            {
                return null;
            }

            return string.IsNullOrWhiteSpace(data.thumbnailPngBase64)
                ? GetRoomSheetThumbnailPngBase64(data.roomId)
                : data.thumbnailPngBase64;
        }

        private static string GetRoomSheetThumbnailPngBase64(string roomId)
        {
            string resourcePath = roomId switch
            {
                nameof(RoomType.LivingRoom) => "Sheets/living_room-sheet",
                nameof(RoomType.BedRoom) => "Sheets/bed_room-sheet",
                nameof(RoomType.KitchenRoom) => "Sheets/kitchen_room-sheet",
                nameof(RoomType.EntranceRoom) => "Sheets/entrance_room-sheet",
                nameof(RoomType.UtilityRoom) => "Sheets/utility_room-sheet",
                _ => string.Empty,
            };

            if (string.IsNullOrWhiteSpace(resourcePath))
            {
                return string.Empty;
            }

            if (roomSheetThumbnailCache.TryGetValue(resourcePath, out string cachedThumbnail))
            {
                return cachedThumbnail;
            }

            Texture2D roomTexture = Resources.Load<Texture2D>(resourcePath);
            string thumbnail = roomTexture != null ? EncodeThumbnail(roomTexture, FindRoomImage()?.materialForRendering) : string.Empty;
            roomSheetThumbnailCache[resourcePath] = thumbnail;
            return thumbnail;
        }

        private static string EncodeThumbnail(Texture sourceTexture, Material effectMaterial = null)
        {
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture renderTexture = RenderTexture.GetTemporary(ThumbnailWidth, ThumbnailHeight, 0, RenderTextureFormat.ARGB32);
            var readableTexture = new Texture2D(ThumbnailWidth, ThumbnailHeight, TextureFormat.RGBA32, false);
            Material thumbnailMaterial = null;

            try
            {
                if (effectMaterial != null)
                {
                    thumbnailMaterial = new Material(effectMaterial);
                    thumbnailMaterial.SetVector(ClipRectId, new Vector4(-100000f, -100000f, 100000f, 100000f));
                    Graphics.Blit(sourceTexture, renderTexture, thumbnailMaterial);
                }
                else
                {
                    Graphics.Blit(sourceTexture, renderTexture);
                }

                RenderTexture.active = renderTexture;
                readableTexture.ReadPixels(new Rect(0f, 0f, ThumbnailWidth, ThumbnailHeight), 0, 0);
                readableTexture.Apply();
                return Convert.ToBase64String(readableTexture.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(renderTexture);
                Destroy(readableTexture);

                if (thumbnailMaterial != null)
                {
                    Destroy(thumbnailMaterial);
                }
            }
        }

        private static RawImage FindRoomImage()
        {
            RawImage[] images = FindObjectsByType<RawImage>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < images.Length; i++)
            {
                if (images[i] != null && images[i].name == RoomImageName)
                {
                    return images[i];
                }
            }

            return null;
        }

        private static GameSession ResolveGameSession()
        {
            GameSession state = GameSession.Instance;
            if (state != null)
            {
                return state;
            }

            GameObject stateObject = new(nameof(GameSession));
            return stateObject.AddComponent<GameSession>();
        }

        private static void SetButtonVisible(Button button, bool visible)
        {
            if (button != null)
            {
                button.gameObject.SetActive(visible);
            }
        }
    }
}
