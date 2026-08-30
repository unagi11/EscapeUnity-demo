using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using Escape.Rooms;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Escape.EditorTools
{
    // Aseprite 내보내기 시트(JSON + 텍스처)를 Unity 씬 오브젝트로 구성하는 에디터 창.
    public sealed class AsepriteRoomSceneBuilder : EditorWindow
    {
        private const string DefaultJsonPath = "";
        private const string DefaultTexturePath = "";
        private const string PopupSortingLayerName = "Unlit";
        private const string ExporterScriptPath = "Assets/Scripts/Editor/Aseprite/export_unity_room_sheet.lua";
        private const string AsepriteCliEditorPrefKey = "EscapeUnity.AsepriteRoomSceneBuilder.AsepriteCliPath";
        private static readonly Regex JsonFrameEntryRegex = new(
            "(?<entry>\"(?<raw>[^\"]+)\"\\s*:\\s*\\{\\s*\"frame\"\\s*:\\s*\\{\\s*\"x\"\\s*:\\s*(?<x>\\d+)\\s*,\\s*\"y\"\\s*:\\s*(?<y>\\d+)\\s*,\\s*\"w\"\\s*:\\s*(?<w>\\d+)\\s*,\\s*\"h\"\\s*:\\s*(?<h>\\d+)\\s*\\}.*?\"spriteSourceSize\"\\s*:\\s*\\{\\s*\"x\"\\s*:\\s*(?<sx>\\d+)\\s*,\\s*\"y\"\\s*:\\s*(?<sy>\\d+)\\s*,\\s*\"w\"\\s*:\\s*(?<sw>\\d+)\\s*,\\s*\"h\"\\s*:\\s*(?<sh>\\d+)\\s*\\}.*?\"duration\"\\s*:\\s*(?<duration>\\d+)\\s*\\})",
            RegexOptions.Compiled | RegexOptions.Singleline);
        private static readonly Regex JsonFrameSuffixRegex = new(
            "(__frame_\\d+|_frame_\\d+|__\\d+)$",
            RegexOptions.Compiled);
        private static readonly Regex JsonFrameIndexRegex = new(
            "(?:__frame_|_frame_|__)(?<index>\\d+)$",
            RegexOptions.Compiled);

        [SerializeField] private UnityEngine.Object asepriteSource;
        [SerializeField] private string asepriteCliPath = "";
        [SerializeField] private TextAsset atlasJson;
        [SerializeField] private Texture2D atlasTexture;
        [SerializeField] private float pixelsPerUnit = 100f;
        [SerializeField] private bool resliceTexture = true;
        [SerializeField] private bool migrateExistingSceneObjects = true;
        private Texture2D previousAtlasTexture;

        [MenuItem("Tools/Escape/Rooms/Aseprite Room Builder")]
        // 에디터 창을 열어 기본값을 로드한다.
        private static void OpenWindow()
        {
            var window = GetWindow<AsepriteRoomSceneBuilder>("Room Builder");
            window.minSize = new Vector2(380f, 230f);
            window.LoadDefaults();
        }

        // 인스펙터 UI를 그린다.
        private void OnGUI()
        {
            EditorGUILayout.LabelField("Aseprite Source", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ObjectField("Aseprite File", asepriteSource, typeof(UnityEngine.Object), false);
                }

                if (GUILayout.Button("Browse", GUILayout.Width(72f)))
                {
                    RunAfterGui(SelectAsepriteSource);
                }

                if (asepriteSource != null && GUILayout.Button("Clear", GUILayout.Width(52f)))
                {
                    asepriteSource = null;
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                asepriteCliPath = EditorGUILayout.TextField("Aseprite CLI", asepriteCliPath);
                if (GUILayout.Button("Browse", GUILayout.Width(72f)))
                {
                    RunAfterGui(() =>
                    {
                        var selectedPath = EditorUtility.OpenFilePanel("Select Aseprite CLI", "/Applications", string.Empty);
                        if (!string.IsNullOrEmpty(selectedPath))
                        {
                            asepriteCliPath = selectedPath;
                            EditorPrefs.SetString(AsepriteCliEditorPrefKey, asepriteCliPath);
                        }
                    });
                }
            }

            var resolvedCliPath = ResolveAsepriteCliPath(false);
            EditorGUILayout.LabelField(
                "Resolved CLI",
                string.IsNullOrEmpty(resolvedCliPath) ? "Not found" : resolvedCliPath,
                EditorStyles.miniLabel);

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Aseprite Room Sheet", EditorStyles.boldLabel);
            atlasJson = (TextAsset)EditorGUILayout.ObjectField("Atlas JSON", atlasJson, typeof(TextAsset), false);
            var selectedTexture = (Texture2D)EditorGUILayout.ObjectField("Atlas Texture", atlasTexture, typeof(Texture2D), false);
            if (selectedTexture != atlasTexture)
            {
                atlasTexture = selectedTexture;
                AssignMatchingJsonForTexture();
                previousAtlasTexture = atlasTexture;
            }
            else if (previousAtlasTexture != atlasTexture)
            {
                previousAtlasTexture = atlasTexture;
            }

            EditorGUILayout.LabelField("Root Name", MakeRootName(GetRootSourcePath()), EditorStyles.miniLabel);

            EditorGUILayout.Space(8f);
            pixelsPerUnit = EditorGUILayout.FloatField("Pixels Per Unit", pixelsPerUnit);
            resliceTexture = EditorGUILayout.Toggle("Reslice Texture", resliceTexture);
            migrateExistingSceneObjects = EditorGUILayout.Toggle("Migrate Existing Scene Objects", migrateExistingSceneObjects);

            EditorGUILayout.Space(12f);
            var shouldExportBeforeBuild = !string.IsNullOrEmpty(GetAsepriteAssetPath());
            var canBuildFromSheet = !shouldExportBeforeBuild && atlasJson != null && atlasTexture != null && pixelsPerUnit > 0f;
            var canBuildFromAseprite = shouldExportBeforeBuild && CanExportAndBuildAseprite();
            var buildButtonLabel = shouldExportBeforeBuild ? "Export + Build Scene Objects" : "Build Scene Objects";
            using (new EditorGUI.DisabledScope(!canBuildFromSheet && !canBuildFromAseprite))
            {
                if (GUILayout.Button(buildButtonLabel, GUILayout.Height(32f)))
                {
                    RunAfterGui(() => BuildRoom(shouldExportBeforeBuild));
                }
            }

            EditorGUILayout.Space(16f);
            EditorGUILayout.LabelField("Batch Refresh", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Exports every *_room Aseprite source under Assets/Artworks and applies each room to the current scene.",
                MessageType.Info);
            using (new EditorGUI.DisabledScope(!CanRefreshAllRooms()))
            {
                if (GUILayout.Button("Refresh All Room Sheets + Scene Objects", GUILayout.Height(28f)))
                {
                    RunAfterGui(RefreshAllRoomSources);
                }
            }
        }

        private static void RunAfterGui(Action action)
        {
            EditorApplication.delayCall += () =>
            {
                try
                {
                    action?.Invoke();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            };
            GUIUtility.ExitGUI();
        }

        // 기본 필드 값 로드.
        private void LoadDefaults()
        {
            if (string.IsNullOrEmpty(asepriteCliPath))
            {
                asepriteCliPath = EditorPrefs.GetString(AsepriteCliEditorPrefKey, string.Empty);
            }

            if (atlasJson == null)
            {
                atlasJson = AssetDatabase.LoadAssetAtPath<TextAsset>(DefaultJsonPath);
            }

            if (atlasTexture == null)
            {
                atlasTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(DefaultTexturePath);
            }

            previousAtlasTexture = atlasTexture;
        }

        // 선택한 Aseprite 파일과 같은 이름의 시트 에셋을 연결한다.
        private void AssignMatchingSheetForAseprite()
        {
            var asepritePath = GetAsepriteAssetPath();
            if (string.IsNullOrEmpty(asepritePath))
            {
                return;
            }

            var sheetBasePath = MakeSheetBasePath(asepritePath);
            atlasJson = AssetDatabase.LoadAssetAtPath<TextAsset>(sheetBasePath + ".json");
            atlasTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(sheetBasePath + ".png");
            previousAtlasTexture = atlasTexture;
        }

        // 텍스처 선택 시 같은 이름의 JSON을 자동 연결.
        private void AssignMatchingJsonForTexture()
        {
            if (atlasTexture == null)
            {
                atlasJson = null;
                return;
            }

            var texturePath = AssetDatabase.GetAssetPath(atlasTexture);
            if (string.IsNullOrEmpty(texturePath))
            {
                return;
            }

            var jsonPath = Path.ChangeExtension(texturePath, ".json");
            var matchingJson = AssetDatabase.LoadAssetAtPath<TextAsset>(jsonPath);
            if (matchingJson != null)
            {
                atlasJson = matchingJson;
            }
        }

        // 선택한 Aseprite 파일을 CLI로 내보낸 뒤 Unity 에셋을 갱신한다.
        private bool ExportAsepriteSource(bool showDialog)
        {
            var asepritePath = GetAsepriteAssetPath();
            if (string.IsNullOrEmpty(asepritePath))
            {
                if (showDialog)
                {
                    EditorUtility.DisplayDialog("Room Builder", "Select an .aseprite or .ase asset first.", "OK");
                }

                return false;
            }

            var cliPath = ResolveAsepriteCliPath(true);
            if (string.IsNullOrEmpty(cliPath))
            {
                if (showDialog)
                {
                    EditorUtility.DisplayDialog("Room Builder", "Aseprite CLI was not found. Set the Aseprite CLI path.", "OK");
                }

                return false;
            }

            var scriptFullPath = Path.GetFullPath(ExporterScriptPath);
            if (!File.Exists(scriptFullPath))
            {
                EditorUtility.DisplayDialog("Room Builder", $"Exporter script not found:\n{ExporterScriptPath}", "OK");
                return false;
            }

            var asepriteFullPath = Path.GetFullPath(asepritePath);
            var result = RunAsepriteCli(cliPath, asepriteFullPath, scriptFullPath);
            if (result.ExitCode != 0)
            {
                Debug.LogError(result.Output);
                EditorUtility.DisplayDialog("Room Builder", $"Aseprite export failed.\nExit code: {result.ExitCode}", "OK");
                return false;
            }

            var sheetBasePath = MakeSheetBasePath(asepritePath);
            CleanupStaticDuplicateFrameEntries(sheetBasePath + ".json", sheetBasePath + ".png");
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            AssignMatchingSheetForAseprite();
            if (showDialog)
            {
                EditorUtility.DisplayDialog("Room Builder", "Aseprite sheet exported.", "OK");
            }

            return true;
        }

        // Assets/Artworks의 *_room Aseprite 원본을 모두 export하고 현재 씬 오브젝트까지 갱신한다.
        private void RefreshAllRoomSources()
        {
            var roomPaths = FindRoomAsepriteAssetPaths();
            if (roomPaths.Count == 0)
            {
                EditorUtility.DisplayDialog("Room Builder", "No *_room.aseprite or *_room.ase files were found.", "OK");
                return;
            }

            var previousSource = asepriteSource;
            var previousJson = atlasJson;
            var previousTexture = atlasTexture;
            var failCount = 0;

            try
            {
                for (var i = 0; i < roomPaths.Count; i++)
                {
                    var roomPath = roomPaths[i];
                    EditorUtility.DisplayProgressBar(
                        "Refresh Aseprite Rooms",
                        roomPath,
                        (float)i / roomPaths.Count);

                    asepriteSource = AssetDatabase.LoadMainAssetAtPath(roomPath);
                    AssignMatchingSheetForAseprite();
                    if (!ExportAsepriteSource(false))
                    {
                        failCount++;
                        continue;
                    }

                    BuildRoom(false);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                asepriteSource = previousSource;
                atlasJson = previousJson;
                atlasTexture = previousTexture;
                previousAtlasTexture = atlasTexture;
            }

            EditorUtility.DisplayDialog(
                "Room Builder",
                failCount == 0
                    ? $"Refreshed {roomPaths.Count} room(s)."
                    : $"Refreshed {roomPaths.Count - failCount} room(s), failed {failCount}. See Console for details.",
                "OK");
        }

        private bool CanRefreshAllRooms()
        {
            return !string.IsNullOrEmpty(ResolveAsepriteCliPath(false)) &&
                   File.Exists(Path.GetFullPath(ExporterScriptPath)) &&
                   FindRoomAsepriteAssetPaths().Count > 0;
        }

        private bool CanExportAndBuildAseprite()
        {
            return CanExportAseprite() &&
                   File.Exists(Path.GetFullPath(ExporterScriptPath)) &&
                   pixelsPerUnit > 0f;
        }

        private static List<string> FindRoomAsepriteAssetPaths()
        {
            const string artworkDirectory = "Assets/Artworks";
            var result = new List<string>();
            if (!Directory.Exists(artworkDirectory))
            {
                return result;
            }

            AddRoomAsepritePaths(result, Directory.GetFiles(artworkDirectory, "*_room.aseprite", SearchOption.AllDirectories));
            AddRoomAsepritePaths(result, Directory.GetFiles(artworkDirectory, "*_room.ase", SearchOption.AllDirectories));
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        private static void AddRoomAsepritePaths(ICollection<string> result, IEnumerable<string> paths)
        {
            foreach (var path in paths)
            {
                result.Add(path.Replace('\\', '/'));
            }
        }

        // 움직임 없는 레이어는 JSON에 첫 프레임만 남긴다.
        private static void CleanupStaticDuplicateFrameEntries(string jsonPath, string texturePath)
        {
            if (!File.Exists(jsonPath))
            {
                return;
            }

            var json = File.ReadAllText(jsonPath);
            Texture2D texture = LoadReadableTexture(texturePath);
            var framesBody = TryReadFramesBody(json, out var framesStart, out var framesEnd);
            if (framesBody == null)
            {
                return;
            }

            var matches = JsonFrameEntryRegex.Matches(framesBody);
            if (matches.Count == 0)
            {
                return;
            }

            var entries = new List<JsonFrameEntry>();
            var entriesByLayer = new Dictionary<string, List<JsonFrameEntry>>(StringComparer.Ordinal);
            var occurrenceIndexByLayer = new Dictionary<string, int>(StringComparer.Ordinal);
            var lastFrameIndexByLayer = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (Match match in matches)
            {
                var rawName = Path.GetFileNameWithoutExtension(match.Groups["raw"].Value);
                var layerName = JsonFrameSuffixRegex.Replace(rawName, string.Empty);
                var frameIndex = ReadJsonFrameIndex(rawName);
                occurrenceIndexByLayer.TryGetValue(layerName, out var occurrenceIndex);
                if (frameIndex >= 0 &&
                    lastFrameIndexByLayer.TryGetValue(layerName, out var lastFrameIndex) &&
                    frameIndex <= lastFrameIndex)
                {
                    occurrenceIndex++;
                }

                occurrenceIndexByLayer[layerName] = occurrenceIndex;
                lastFrameIndexByLayer[layerName] = frameIndex;
                var layerOccurrenceKey = $"{layerName}#{occurrenceIndex}";
                var entry = new JsonFrameEntry
                {
                    Text = match.Groups["entry"].Value,
                    LayerName = layerName,
                    FrameKey = MakeFrameKey(match, texture),
                };
                entries.Add(entry);
                if (!entriesByLayer.TryGetValue(layerOccurrenceKey, out var layerEntries))
                {
                    layerEntries = new List<JsonFrameEntry>();
                    entriesByLayer.Add(layerOccurrenceKey, layerEntries);
                }

                layerEntries.Add(entry);
            }

            var keepEntries = new HashSet<JsonFrameEntry>();
            foreach (var layerEntries in entriesByLayer.Values)
            {
                if (layerEntries.Count <= 1 || IsStaticLayer(layerEntries))
                {
                    keepEntries.Add(layerEntries[0]);
                    continue;
                }

                for (var i = 0; i < layerEntries.Count; i++)
                {
                    keepEntries.Add(layerEntries[i]);
                }
            }

            var filteredEntries = new List<string>();
            for (var i = 0; i < entries.Count; i++)
            {
                if (keepEntries.Contains(entries[i]))
                {
                    filteredEntries.Add("   " + entries[i].Text);
                }
            }

            var filteredFramesBody = "\n" + string.Join(",\n", filteredEntries) + "\n  ";
            var nextJson = json.Substring(0, framesStart) +
                           filteredFramesBody +
                           json.Substring(framesEnd);
            File.WriteAllText(jsonPath, nextJson);
        }

        private static Texture2D LoadReadableTexture(string texturePath)
        {
            if (string.IsNullOrWhiteSpace(texturePath) || !File.Exists(texturePath))
            {
                return null;
            }

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            return texture.LoadImage(File.ReadAllBytes(texturePath)) ? texture : null;
        }

        private static int ReadJsonFrameIndex(string rawName)
        {
            var match = JsonFrameIndexRegex.Match(rawName);
            return match.Success && int.TryParse(match.Groups["index"].Value, out var frameIndex)
                ? frameIndex
                : -1;
        }

        private static string TryReadFramesBody(string json, out int bodyStart, out int bodyEnd)
        {
            bodyStart = -1;
            bodyEnd = -1;
            var framesIndex = json.IndexOf("\"frames\"", StringComparison.Ordinal);
            if (framesIndex < 0)
            {
                return null;
            }

            bodyStart = json.IndexOf('{', framesIndex);
            if (bodyStart < 0)
            {
                return null;
            }

            var depth = 0;
            var inString = false;
            var escaped = false;
            for (var i = bodyStart; i < json.Length; i++)
            {
                var c = json[i];
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (c == '\\')
                {
                    escaped = inString;
                    continue;
                }

                if (c == '"')
                {
                    inString = !inString;
                    continue;
                }

                if (inString)
                {
                    continue;
                }

                if (c == '{')
                {
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        bodyEnd = i;
                        return json.Substring(bodyStart + 1, bodyEnd - bodyStart - 1);
                    }
                }
            }

            return null;
        }

        private static bool IsStaticLayer(IReadOnlyList<JsonFrameEntry> entries)
        {
            var firstKey = entries[0].FrameKey;
            for (var i = 1; i < entries.Count; i++)
            {
                if (!string.Equals(firstKey, entries[i].FrameKey, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static string MakeFrameKey(Match match, Texture2D texture)
        {
            return string.Join(
                ",",
                match.Groups["w"].Value,
                match.Groups["h"].Value,
                match.Groups["sx"].Value,
                match.Groups["sy"].Value,
                match.Groups["sw"].Value,
                match.Groups["sh"].Value,
                MakeFramePixelHash(match, texture));
        }

        private static string MakeFramePixelHash(Match match, Texture2D texture)
        {
            if (texture == null)
            {
                return match.Groups["x"].Value + "," + match.Groups["y"].Value;
            }

            var x = ReadMatchInt(match, "x");
            var y = texture.height - ReadMatchInt(match, "y") - ReadMatchInt(match, "h");
            var width = ReadMatchInt(match, "w");
            var height = ReadMatchInt(match, "h");
            Color32[] pixels = texture.GetPixels32();
            unchecked
            {
                uint hash = 2166136261u;
                for (var row = 0; row < height; row++)
                {
                    var pixelIndex = (y + row) * texture.width + x;
                    for (var column = 0; column < width; column++)
                    {
                        Color32 color = pixels[pixelIndex + column];
                        hash = (hash ^ color.r) * 16777619u;
                        hash = (hash ^ color.g) * 16777619u;
                        hash = (hash ^ color.b) * 16777619u;
                        hash = (hash ^ color.a) * 16777619u;
                    }
                }

                return hash.ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        private static int ReadMatchInt(Match match, string groupName)
        {
            return int.TryParse(match.Groups[groupName].Value, out var value) ? value : 0;
        }

        // JSON/텍스처를 파싱하여 씬 오브젝트를 생성한다.
        private void BuildRoom(bool exportAsepriteBeforeBuild)
        {
            if (exportAsepriteBeforeBuild && asepriteSource != null && !ExportAsepriteSource(true))
            {
                return;
            }

            if (atlasJson == null || atlasTexture == null)
            {
                EditorUtility.DisplayDialog("Room Builder", "Atlas JSON and Texture are required.", "OK");
                return;
            }

            if (pixelsPerUnit <= 0f)
            {
                EditorUtility.DisplayDialog("Room Builder", "Pixels Per Unit must be greater than zero.", "OK");
                return;
            }

            var texturePath = AssetDatabase.GetAssetPath(atlasTexture);
            var rootName = MakeRootName(GetRootSourcePath());
            var atlas = AsepriteAtlasParser.Parse(atlasJson.text);
            var animations = AsepriteAnimationParser.Parse(atlasJson.text, atlas.OrderedHierarchy);
            if (resliceTexture)
            {
                SliceTexture(texturePath, CombineFrames(atlas.OrderedFrames, animations));
            }

            var spriteMap = LoadSpritesByName(texturePath);
            if (spriteMap.Count == 0)
            {
                EditorUtility.DisplayDialog("Room Builder", "No sprites were loaded from the texture.", "OK");
                return;
            }

            var oldRoot = GameObject.Find(rootName);
            var root = oldRoot != null && migrateExistingSceneObjects ? oldRoot : new GameObject(rootName);
            if (oldRoot == null || !migrateExistingSceneObjects)
            {
                Undo.RegisterCreatedObjectUndo(root, "Build Room From Sheet");
            }

            EnsureRoomComponent(root, rootName);

            var reusableObjects = migrateExistingSceneObjects
                ? CollectReusableLayerObjects(root.transform)
                : new ReusableLayerObjects();
            var usedObjects = new HashSet<Transform>();
            var generatedLayerObjects = new Dictionary<string, Transform>(StringComparer.Ordinal);

            for (var i = 0; i < atlas.OrderedFrames.Count; i++)
            {
                var frame = atlas.OrderedFrames[i];
                if (!TryGetSprite(spriteMap, frame, out var sprite))
                {
                    Debug.LogWarning($"Sprite not found for frame: {frame.SpriteName}");
                    continue;
                }

                var parent = EnsureHierarchyPath(root.transform, frame.HierarchyPath);
                var hierarchyKey = MakeHierarchyKey(frame.HierarchyPath, frame.Name);
                var layerTransform = GetOrCreateLayerObject(
                    frame.Name,
                    hierarchyKey,
                    parent,
                    reusableObjects,
                    frame.HasDuplicateName);
                usedObjects.Add(layerTransform);
                generatedLayerObjects[hierarchyKey] = layerTransform;

                layerTransform.SetParent(parent, false);
                layerTransform.localPosition = new Vector3(
                    frame.SourceX / pixelsPerUnit,
                    -frame.SourceY / pixelsPerUnit,
                    -i * 0.001f);

                var renderer = layerTransform.GetComponent<SpriteRenderer>();
                if (renderer == null)
                {
                    renderer = Undo.AddComponent<SpriteRenderer>(layerTransform.gameObject);
                }

                renderer.sprite = sprite;
                if (frame.IsPopupLayer)
                {
                    renderer.sortingLayerName = PopupSortingLayerName;
                }

                renderer.sortingOrder = i;
                EditorUtility.SetDirty(layerTransform.gameObject);
            }

            ApplyHierarchyOrder(root.transform, atlas.OrderedHierarchy);
            RemoveObsoleteGeneratedObjects(root.transform, usedObjects);
            BuildRoomAnimators(animations, spriteMap, generatedLayerObjects, pixelsPerUnit);

            Selection.activeGameObject = root;
            EditorUtility.SetDirty(root);
            Debug.Log($"Built {rootName} with {atlas.OrderedFrames.Count} sheet layers.");
        }

        // Aseprite 태그 프레임을 레이어별 룸 애니메이터 정의로 연결한다.
        private static void BuildRoomAnimators(
            IReadOnlyList<AsepriteAnimationData> animations,
            IReadOnlyDictionary<string, Sprite> spriteMap,
            IReadOnlyDictionary<string, Transform> generatedLayerObjects,
            float pixelsPerUnit)
        {
            if (animations == null || animations.Count == 0)
            {
                RemoveObsoleteRoomAnimators(null, generatedLayerObjects);
                return;
            }

            float safePixelsPerUnit = Mathf.Max(0.001f, pixelsPerUnit);
            var animatorDefinitionsByLayer = new Dictionary<string, List<AsepriteRoomAnimator.AnimationDefinition>>(StringComparer.Ordinal);
            foreach (var animation in animations)
            {
                var sprites = new List<Sprite>();
                var frameLocalPositions = new List<Vector2>();
                for (var i = 0; i < animation.Frames.Count; i++)
                {
                    var frame = animation.Frames[i];
                    if (spriteMap.TryGetValue(frame.SpriteName, out var sprite))
                    {
                        sprites.Add(sprite);
                        frameLocalPositions.Add(new Vector2(
                            frame.SourceX / safePixelsPerUnit,
                            -frame.SourceY / safePixelsPerUnit));
                    }
                }

                if (sprites.Count <= 1 ||
                    !HasVisibleSpriteArea(sprites) ||
                    !HasAnimationFrameChange(animation.Frames))
                {
                    continue;
                }

                if (!animatorDefinitionsByLayer.TryGetValue(animation.HierarchyKey, out var definitions))
                {
                    definitions = new List<AsepriteRoomAnimator.AnimationDefinition>();
                    animatorDefinitionsByLayer.Add(animation.HierarchyKey, definitions);
                }

                var frameDurationMs = animation.Frames.Count > 0 ? animation.Frames[0].DurationMs : 100;
                definitions.Add(new AsepriteRoomAnimator.AnimationDefinition(
                    animation.AnimationName,
                    sprites,
                    frameLocalPositions,
                    frameDurationMs,
                    animation.PlaybackMode));
            }

            RemoveObsoleteRoomAnimators(animatorDefinitionsByLayer.Keys, generatedLayerObjects);
            foreach (var entry in animatorDefinitionsByLayer)
            {
                ApplyRoomAnimatorComponent(entry.Key, entry.Value, generatedLayerObjects);
            }
        }

        // 프레임 안에 실제로 보이는 픽셀이 하나라도 있는지 확인한다.
        private static bool HasVisibleSpriteArea(IReadOnlyList<Sprite> sprites)
        {
            for (var i = 0; i < sprites.Count; i++)
            {
                if (HasVisibleSpriteArea(sprites[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasVisibleSpriteArea(Sprite sprite)
        {
            if (sprite == null || sprite.texture == null)
            {
                return false;
            }

            try
            {
                var rect = sprite.rect;
                var x = Mathf.RoundToInt(rect.x);
                var y = Mathf.RoundToInt(rect.y);
                var width = Mathf.RoundToInt(rect.width);
                var height = Mathf.RoundToInt(rect.height);
                Color32[] pixels = sprite.texture.GetPixels32();
                for (var row = 0; row < height; row++)
                {
                    var index = (y + row) * sprite.texture.width + x;
                    for (var column = 0; column < width; column++)
                    {
                        if (pixels[index + column].a > 0)
                        {
                            return true;
                        }
                    }
                }
            }
            catch (UnityException)
            {
                return true;
            }

            return false;
        }

        // 모든 프레임의 보이는 픽셀이 같으면 애니메이션으로 보지 않는다.
        // Aseprite trim 위치 변화까지 포함해 애니메이션 프레임 변화를 판정한다.
        private static bool HasAnimationFrameChange(IReadOnlyList<AsepriteFrameData> frames)
        {
            if (frames == null || frames.Count <= 1)
            {
                return false;
            }

            var first = frames[0];
            for (var i = 1; i < frames.Count; i++)
            {
                if (first.AtlasX != frames[i].AtlasX ||
                    first.AtlasY != frames[i].AtlasY ||
                    first.Width != frames[i].Width ||
                    first.Height != frames[i].Height ||
                    first.SourceX != frames[i].SourceX ||
                    first.SourceY != frames[i].SourceY)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasSpriteFrameChange(IReadOnlyList<Sprite> sprites)
        {
            if (sprites.Count <= 1)
            {
                return false;
            }

            string firstHash = MakeSpritePixelHash(sprites[0]);
            for (var i = 1; i < sprites.Count; i++)
            {
                if (!string.Equals(firstHash, MakeSpritePixelHash(sprites[i]), StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string MakeSpritePixelHash(Sprite sprite)
        {
            if (sprite == null || sprite.texture == null)
            {
                return "empty";
            }

            try
            {
                var rect = sprite.rect;
                var x = Mathf.RoundToInt(rect.x);
                var y = Mathf.RoundToInt(rect.y);
                var width = Mathf.RoundToInt(rect.width);
                var height = Mathf.RoundToInt(rect.height);
                Color32[] pixels = sprite.texture.GetPixels32();
                unchecked
                {
                    uint hash = 2166136261u;
                    hash = (hash ^ (uint)width) * 16777619u;
                    hash = (hash ^ (uint)height) * 16777619u;
                    for (var row = 0; row < height; row++)
                    {
                        var index = (y + row) * sprite.texture.width + x;
                        for (var column = 0; column < width; column++)
                        {
                            Color32 color = pixels[index + column];
                            hash = (hash ^ color.r) * 16777619u;
                            hash = (hash ^ color.g) * 16777619u;
                            hash = (hash ^ color.b) * 16777619u;
                            hash = (hash ^ color.a) * 16777619u;
                        }
                    }

                    return hash.ToString("x8", System.Globalization.CultureInfo.InvariantCulture);
                }
            }
            catch (UnityException)
            {
                return sprite.name;
            }
        }

        // 생성된 레이어 오브젝트에 태그 기반 룸 애니메이터를 붙인다.
        private static void ApplyRoomAnimatorComponent(
            string hierarchyKey,
            IReadOnlyList<AsepriteRoomAnimator.AnimationDefinition> animations,
            IReadOnlyDictionary<string, Transform> generatedLayerObjects)
        {
            if (generatedLayerObjects == null ||
                !generatedLayerObjects.TryGetValue(hierarchyKey, out var layerTransform) ||
                layerTransform == null)
            {
                return;
            }

            var renderer = layerTransform.GetComponent<SpriteRenderer>();
            if (renderer == null)
            {
                return;
            }

            RemoveLegacyAnimationComponent(layerTransform);
            RemoveLegacySpriteLooper(layerTransform);

            var animator = layerTransform.GetComponent<AsepriteRoomAnimator>();
            if (animator == null)
            {
                animator = Undo.AddComponent<AsepriteRoomAnimator>(layerTransform.gameObject);
            }
            else
            {
                Undo.RecordObject(animator, "Bind Room Animator");
            }

            animator.Configure(renderer, animations);
            EditorUtility.SetDirty(animator);
            EditorUtility.SetDirty(layerTransform.gameObject);
        }

        // 더 이상 태그 애니메이션을 갖지 않는 생성 오브젝트의 룸 애니메이터를 제거한다.
        private static void RemoveObsoleteRoomAnimators(
            IEnumerable<string> animatedLayerKeys,
            IReadOnlyDictionary<string, Transform> generatedLayerObjects)
        {
            if (generatedLayerObjects == null || generatedLayerObjects.Count == 0)
            {
                return;
            }

            var animatedLayerNames = new HashSet<string>(StringComparer.Ordinal);
            if (animatedLayerKeys != null)
            {
                foreach (string key in animatedLayerKeys)
                {
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        animatedLayerNames.Add(key);
                    }
                }
            }

            foreach (var entry in generatedLayerObjects)
            {
                if (animatedLayerNames.Contains(entry.Key) || entry.Value == null)
                {
                    continue;
                }

                var animator = entry.Value.GetComponent<AsepriteRoomAnimator>();
                if (animator != null)
                {
                    Undo.DestroyObjectImmediate(animator);
                    EditorUtility.SetDirty(entry.Value.gameObject);
                }

                RemoveLegacyAnimationComponent(entry.Value);
                RemoveLegacySpriteLooper(entry.Value);
            }
        }

        private static void RemoveLegacySpriteLooper(Transform layerTransform)
        {
            var looper = layerTransform.GetComponent<AsepriteSpriteLooper>();
            if (looper != null)
            {
                Undo.DestroyObjectImmediate(looper);
                EditorUtility.SetDirty(layerTransform.gameObject);
            }
        }

        // 이전 AnimationClip 기반 파이프라인에서 붙은 Animation 컴포넌트를 제거한다.
        private static void RemoveLegacyAnimationComponent(Transform layerTransform)
        {
            var animationComponent = layerTransform.GetComponent<Animation>();
            if (animationComponent == null)
            {
                return;
            }

            Undo.DestroyObjectImmediate(animationComponent);
            EditorUtility.SetDirty(layerTransform.gameObject);
        }

        // Aseprite CLI export를 실행할 수 있는 상태인지 확인한다.
        private bool CanExportAseprite()
        {
            return !string.IsNullOrEmpty(GetAsepriteAssetPath()) &&
                   !string.IsNullOrEmpty(ResolveAsepriteCliPath(false));
        }

        // 선택한 Aseprite 에셋의 프로젝트 상대 경로를 반환한다.
        private string GetAsepriteAssetPath()
        {
            if (asepriteSource == null)
            {
                return string.Empty;
            }

            var path = AssetDatabase.GetAssetPath(asepriteSource);
            return IsAsepritePath(path) ? path : string.Empty;
        }

        // .aseprite와 .ase만 표시하는 파일 선택 창에서 프로젝트 에셋을 연결한다.
        private void SelectAsepriteSource()
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var currentPath = GetAsepriteAssetPath();
            var initialDirectory = string.IsNullOrEmpty(currentPath)
                ? Path.Combine(Application.dataPath, "Artworks")
                : Path.GetDirectoryName(Path.Combine(projectRoot, currentPath));
            var selectedPath = EditorUtility.OpenFilePanelWithFilters(
                "Select Aseprite File",
                initialDirectory,
                new[] { "Aseprite Files", "aseprite,ase" });
            if (string.IsNullOrEmpty(selectedPath))
            {
                return;
            }

            var fullPath = Path.GetFullPath(selectedPath);
            var projectPrefix = projectRoot + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(projectPrefix, StringComparison.Ordinal))
            {
                EditorUtility.DisplayDialog("Room Builder", "Select an Aseprite file inside this Unity project.", "OK");
                return;
            }

            var assetPath = fullPath.Substring(projectPrefix.Length).Replace('\\', '/');
            if (!IsAsepritePath(assetPath))
            {
                return;
            }

            var selectedAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (selectedAsset == null)
            {
                EditorUtility.DisplayDialog("Room Builder", $"Could not load Aseprite asset:\n{assetPath}", "OK");
                return;
            }

            asepriteSource = selectedAsset;
            AssignMatchingSheetForAseprite();
        }

        // 지원하는 Aseprite 원본 확장자인지 반환한다.
        private static bool IsAsepritePath(string path)
        {
            var extension = Path.GetExtension(path);
            return string.Equals(extension, ".aseprite", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(extension, ".ase", StringComparison.OrdinalIgnoreCase);
        }

        private string GetSourceAssetName()
        {
            var asepritePath = GetAsepriteAssetPath();
            var sourcePath = string.IsNullOrEmpty(asepritePath) ? GetRootSourcePath() : asepritePath;
            var sourceName = Path.GetFileNameWithoutExtension(sourcePath);
            return sourceName
                .Replace("-sheet", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("_sheet", string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        private static string MakeSheetBasePath(string asepritePath)
        {
            var sourceName = Path.GetFileNameWithoutExtension(asepritePath);
            return $"Assets/Resources/Sheets/{sourceName}-sheet";
        }

        // Aseprite 실행 파일 경로를 사용자 설정과 일반 설치 위치에서 찾는다.
        private string ResolveAsepriteCliPath(bool persistResolvedPath)
        {
            if (!string.IsNullOrEmpty(asepriteCliPath) && File.Exists(asepriteCliPath))
            {
                return asepriteCliPath;
            }

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var candidates = new[]
            {
                "/Applications/Aseprite.app/Contents/MacOS/aseprite",
                "/opt/homebrew/bin/aseprite",
                "/usr/local/bin/aseprite",
                Path.Combine(home, "Library/Application Support/Steam/steamapps/common/Aseprite/Aseprite.app/Contents/MacOS/aseprite"),
                Path.Combine(home, "Library/Application Support/Steam/steamapps/common/Aseprite/aseprite"),
            };

            for (var i = 0; i < candidates.Length; i++)
            {
                if (!File.Exists(candidates[i]))
                {
                    continue;
                }

                if (persistResolvedPath)
                {
                    asepriteCliPath = candidates[i];
                    EditorPrefs.SetString(AsepriteCliEditorPrefKey, asepriteCliPath);
                }

                return candidates[i];
            }

            return string.Empty;
        }

        // Aseprite CLI를 실행하고 표준 출력/오류를 모아 반환한다.
        private static ProcessResult RunAsepriteCli(string cliPath, string asepriteFullPath, string scriptFullPath)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = cliPath,
                Arguments = $"-b --all-layers {QuoteArgument(asepriteFullPath)} --script {QuoteArgument(scriptFullPath)}",
                WorkingDirectory = Path.GetDirectoryName(asepriteFullPath) ?? Directory.GetCurrentDirectory(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return new ProcessResult(-1, "Failed to start Aseprite CLI.");
            }

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return new ProcessResult(process.ExitCode, output + error);
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + value.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"";
        }

        private static List<AsepriteFrameData> CombineFrames(
            IReadOnlyList<AsepriteFrameData> roomFrames,
            IReadOnlyList<AsepriteAnimationData> animations)
        {
            var result = new List<AsepriteFrameData>();
            var seenSpriteNames = new HashSet<string>(StringComparer.Ordinal);
            AddUniqueFrames(roomFrames, result, seenSpriteNames);
            if (animations != null)
            {
                for (var i = 0; i < animations.Count; i++)
                {
                    AddUniqueFrames(animations[i].Frames, result, seenSpriteNames);
                }
            }

            return result;
        }

        private static void AddUniqueFrames(
            IEnumerable<AsepriteFrameData> frames,
            List<AsepriteFrameData> result,
            ISet<string> seenSpriteNames)
        {
            if (frames == null)
            {
                return;
            }

            foreach (var frame in frames)
            {
                if (frame == null || string.IsNullOrEmpty(frame.SpriteName))
                {
                    continue;
                }

                if (seenSpriteNames.Add(frame.SpriteName))
                {
                    result.Add(frame);
                }
            }
        }

        // 부모 아래 자식이 없으면 생성, 있으면 기존 것을 반환.
        private static Transform EnsureChild(Transform parent, string name)
        {
            var child = parent.Find(name);
            if (child != null)
            {
                return child;
            }

            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create Room Group");
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static Transform EnsureHierarchyPath(Transform root, IReadOnlyList<string> hierarchyPath)
        {
            var parent = root;
            for (var i = 0; i < hierarchyPath.Count; i++)
            {
                parent = EnsureChild(parent, hierarchyPath[i]);
            }

            return parent;
        }

        private static void ApplyHierarchyOrder(
            Transform root,
            IReadOnlyList<AsepriteHierarchyEntry> orderedHierarchy)
        {
            var nextSiblingIndexByParent = new Dictionary<Transform, int>();
            for (var i = 0; i < orderedHierarchy.Count; i++)
            {
                var entry = orderedHierarchy[i];
                var parent = EnsureHierarchyPath(root, entry.HierarchyPath);
                var child = parent.Find(entry.Name);
                if (child == null && entry.IsGroup)
                {
                    child = EnsureChild(parent, entry.Name);
                }

                if (child == null)
                {
                    continue;
                }

                nextSiblingIndexByParent.TryGetValue(parent, out var siblingIndex);
                child.SetSiblingIndex(siblingIndex);
                nextSiblingIndexByParent[parent] = siblingIndex + 1;
            }
        }

        // 중복 이름 레이어는 정확한 계층 경로가 맞을 때만 기존 오브젝트를 재사용한다.
        private static Transform GetOrCreateLayerObject(
            string objectName,
            string hierarchyKey,
            Transform parent,
            ReusableLayerObjects reusableObjects,
            bool requirePathMatch)
        {
            if (reusableObjects.ByPath.TryGetValue(hierarchyKey, out var existing) && existing != null)
            {
                Undo.RecordObject(existing, "Migrate Room Layer");
                return existing;
            }

            if (requirePathMatch)
            {
                var pathMatchedObject = new GameObject(objectName);
                Undo.RegisterCreatedObjectUndo(pathMatchedObject, "Build Room From Sheet");
                pathMatchedObject.transform.SetParent(parent, false);
                return pathMatchedObject.transform;
            }

            if (reusableObjects.ByName.TryGetValue(objectName, out existing) && existing != null)
            {
                Undo.RecordObject(existing, "Migrate Room Layer");
                return existing;
            }

            var go = new GameObject(objectName);
            Undo.RegisterCreatedObjectUndo(go, "Build Room From Sheet");
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static ReusableLayerObjects CollectReusableLayerObjects(Transform root)
        {
            var result = new ReusableLayerObjects();
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child == root)
                {
                    continue;
                }

                var path = GetRelativeHierarchyKey(root, child);
                if (!result.ByPath.ContainsKey(path))
                {
                    result.ByPath.Add(path, child);
                }

                if (!result.ByName.ContainsKey(child.name))
                {
                    result.ByName.Add(child.name, child);
                }
            }

            return result;
        }

        private static void RemoveObsoleteGeneratedObjects(Transform root, ISet<Transform> usedObjects)
        {
            for (var i = root.childCount - 1; i >= 0; i--)
            {
                RemoveObsoleteGeneratedObjects(root.GetChild(i), usedObjects);
            }

            if (root.parent == null || usedObjects.Contains(root))
            {
                return;
            }

            if (root.GetComponent<SpriteRenderer>() != null && IsGeneratedLayerName(root.name))
            {
                Undo.DestroyObjectImmediate(root.gameObject);
                return;
            }

            if (root.childCount == 0 &&
                (IsGeneratedLegacyGroupName(root.name) || IsGeneratedFolderName(root.name)))
            {
                Undo.DestroyObjectImmediate(root.gameObject);
            }
        }

        private static string GetRelativeHierarchyKey(Transform root, Transform child)
        {
            var names = new List<string>();
            var current = child;
            while (current != null && current != root)
            {
                names.Add(current.name);
                current = current.parent;
            }

            names.Reverse();
            return string.Join("/", names);
        }

        private static string MakeHierarchyKey(IReadOnlyList<string> hierarchyPath, string objectName)
        {
            if (hierarchyPath.Count == 0)
            {
                return objectName;
            }

            return string.Join("/", hierarchyPath) + "/" + objectName;
        }

        private sealed class ReusableLayerObjects
        {
            public readonly Dictionary<string, Transform> ByPath = new(StringComparer.Ordinal);
            public readonly Dictionary<string, Transform> ByName = new(StringComparer.Ordinal);
        }

        private static bool IsGeneratedLegacyGroupName(string objectName)
        {
            return objectName is "Draw" or "Objects" or "Doors" or "Etc";
        }

        private static bool IsGeneratedFolderName(string objectName)
        {
            return objectName.StartsWith("fld_", StringComparison.Ordinal);
        }

        private static bool IsGeneratedLayerName(string objectName)
        {
            return objectName.StartsWith("drw_", StringComparison.Ordinal) ||
                   objectName.StartsWith("obj_", StringComparison.Ordinal) ||
                   objectName.StartsWith("dor_", StringComparison.Ordinal);
        }

        // 텍스처를 스프라이트 시트로 슬라이스.
        private void SliceTexture(string texturePath, IReadOnlyList<AsepriteFrameData> frames)
        {
            SliceTexture(texturePath, frames, atlasTexture.height);
        }

        // 지정한 텍스처 높이를 기준으로 스프라이트 시트를 슬라이스한다.
        private void SliceTexture(string texturePath, IReadOnlyList<AsepriteFrameData> frames, int textureHeight)
        {
            var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer == null)
            {
                throw new InvalidOperationException($"TextureImporter not found: {texturePath}");
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.mipmapEnabled = false;
            importer.isReadable = true;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            importer.SaveAndReimport();

            var factory = new SpriteDataProviderFactories();
            factory.Init();
            var dataProvider = factory.GetSpriteEditorDataProviderFromObject(importer);
            if (dataProvider == null)
            {
                throw new InvalidOperationException($"Sprite data provider not found: {texturePath}");
            }

            dataProvider.InitSpriteEditorDataProvider();

            var spriteRects = new SpriteRect[frames.Count];
            var nameIdPairs = new SpriteNameFileIdPair[frames.Count];
            for (var i = 0; i < frames.Count; i++)
            {
                var frame = frames[i];
                var spriteId = GUID.Generate();
                spriteRects[i] = new SpriteRect
                {
                    name = frame.SpriteName,
                    rect = new Rect(frame.AtlasX, textureHeight - frame.AtlasY - frame.Height, frame.Width, frame.Height),
                    alignment = SpriteAlignment.Custom,
                    pivot = new Vector2(0f, 1f),
                    spriteID = spriteId,
                };
                nameIdPairs[i] = new SpriteNameFileIdPair(frame.SpriteName, spriteId);
            }

            dataProvider.SetSpriteRects(spriteRects);
            var nameFileIdProvider = dataProvider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            nameFileIdProvider?.SetNameFileIdPairs(nameIdPairs);
            dataProvider.Apply();

            AssetDatabase.ForceReserializeAssets(new[] { texturePath }, ForceReserializeAssetsOptions.ReserializeMetadata);
            AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceUpdate);
        }

        // 텍스처에서 스프라이트를 이름:스프라이트 딕셔너리로 로드.
        private static Dictionary<string, Sprite> LoadSpritesByName(string texturePath)
        {
            var assets = AssetDatabase.LoadAllAssetRepresentationsAtPath(texturePath);
            var result = new Dictionary<string, Sprite>(StringComparer.Ordinal);
            foreach (var asset in assets)
            {
                if (asset is Sprite sprite)
                {
                    result[sprite.name] = sprite;
                }
            }

            return result;
        }

        // 고유 스프라이트 이름을 우선하되, 기존 슬라이스 이름도 호환한다.
        private static bool TryGetSprite(
            IReadOnlyDictionary<string, Sprite> spriteMap,
            AsepriteFrameData frame,
            out Sprite sprite)
        {
            if (spriteMap.TryGetValue(frame.SpriteName, out sprite))
            {
                return true;
            }

            return spriteMap.TryGetValue(frame.Name, out sprite);
        }

        // 파일 경로에서 Room 오브젝트 이름을 생성.
        private static string MakeRootName(string texturePath)
        {
            var fileName = Path.GetFileNameWithoutExtension(texturePath)
                ?.Replace("-sheet", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("_sheet", string.Empty, StringComparison.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(fileName))
            {
                return "Room";
            }

            var parts = fileName.Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var root = string.Empty;
            for (var i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (i == 0 && string.Equals(part, "room", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                root += char.ToUpperInvariant(part[0]) + part.Substring(1);
            }

            return string.IsNullOrEmpty(root) ? "Room" : root;
        }

        private static void EnsureRoomComponent(GameObject root, string rootName)
        {
            var room = root.GetComponent<Room>();
            if (room == null)
            {
                room = Undo.AddComponent<Room>(root);
            }

            var inferredId = InferRoomId(rootName);
            if (inferredId == RoomType.None || room.RoomId == inferredId)
            {
                return;
            }

            Undo.RecordObject(room, "Set Room Id");
            room.RoomId = inferredId;
            EditorUtility.SetDirty(room);
        }

        private static RoomType InferRoomId(string rootName)
        {
            return rootName switch
            {
                "LivingRoom" => RoomType.LivingRoom,
                "BedRoom" => RoomType.BedRoom,
                "KitchenRoom" => RoomType.KitchenRoom,
                "EntranceRoom" => RoomType.EntranceRoom,
                "UtilityRoom" => RoomType.UtilityRoom,
                _ => RoomType.None
            };
        }

        private string GetRootSourcePath()
        {
            if (atlasJson != null)
            {
                var jsonPath = AssetDatabase.GetAssetPath(atlasJson);
                if (!string.IsNullOrEmpty(jsonPath))
                {
                    return jsonPath;
                }
            }

            if (atlasTexture != null)
            {
                var texturePath = AssetDatabase.GetAssetPath(atlasTexture);
                if (!string.IsNullOrEmpty(texturePath))
                {
                    return texturePath;
                }
            }

            return DefaultTexturePath;
        }
    }

    // JSON 파싱 결과를 저장하는 데이터 클래스.
    internal sealed class AsepriteAtlasData
    {
        public readonly List<AsepriteFrameData> OrderedFrames = new();
        public readonly List<AsepriteHierarchyEntry> OrderedHierarchy = new();
        public int SourceWidth;
        public int SourceHeight;
    }

    internal sealed class AsepriteHierarchyEntry
    {
        public string Name = string.Empty;
        public bool IsGroup;
        public List<string> HierarchyPath = new();
    }

    // Aseprite 프레임 하나의 좌표/크기 데이터.
    internal sealed class AsepriteFrameData
    {
        public string Name = string.Empty;
        public string ExportedName = string.Empty;
        public string SpriteName = string.Empty;
        public bool HasDuplicateName;
        public bool IsPopupLayer;
        public int DurationMs = 100;
        public int AtlasX;
        public int AtlasY;
        public int Width;
        public int Height;
        public int SourceX;
        public int SourceY;
        public List<string> HierarchyPath = new();

        public float DurationSeconds => Mathf.Max(0.01f, DurationMs / 1000f);
    }

    // 그룹 레이어의 스프라이트 이름에 전체 계층을 포함해 동일 이름 충돌을 막는다.
    internal static class AsepriteSpriteName
    {
        public static string Build(IReadOnlyList<string> hierarchyPath, string exportedName)
        {
            if (hierarchyPath == null || hierarchyPath.Count == 0)
            {
                return exportedName;
            }

            var frameName = Regex.Replace(exportedName, "(?:__frame_|_frame_|__)(?<index>\\d+)$", "_${index}");
            return string.Join("@", hierarchyPath) + "@" + frameName;
        }
    }

    internal sealed class AsepriteAnimationData
    {
        public string SourceName = string.Empty;
        public string LayerName = string.Empty;
        public string AnimationName = string.Empty;
        public string HierarchyKey = string.Empty;
        public string ObjectName = string.Empty;
        public AsepriteSpritePlaybackMode PlaybackMode = AsepriteSpritePlaybackMode.Once;
        public readonly List<AsepriteFrameData> Frames = new();
    }

    // Aseprite 애니메이션 시트 JSON에서 태그별 룸 애니메이터 데이터를 묶는다.
    internal static class AsepriteAnimationParser
    {
        private static readonly Regex FrameRegex = new(
            "\"(?<raw>[^\"]+)\"\\s*:\\s*\\{\\s*\"frame\"\\s*:\\s*\\{\\s*\"x\"\\s*:\\s*(?<x>\\d+)\\s*,\\s*\"y\"\\s*:\\s*(?<y>\\d+)\\s*,\\s*\"w\"\\s*:\\s*(?<w>\\d+)\\s*,\\s*\"h\"\\s*:\\s*(?<h>\\d+)\\s*\\}.*?\"spriteSourceSize\"\\s*:\\s*\\{\\s*\"x\"\\s*:\\s*(?<sx>\\d+)\\s*,\\s*\"y\"\\s*:\\s*(?<sy>\\d+)\\s*,\\s*\"w\"\\s*:\\s*\\d+\\s*,\\s*\"h\"\\s*:\\s*\\d+\\s*\\}.*?\"sourceSize\"\\s*:\\s*\\{\\s*\"w\"\\s*:\\s*\\d+\\s*,\\s*\"h\"\\s*:\\s*\\d+\\s*\\}.*?\"duration\"\\s*:\\s*(?<duration>\\d+)",
            RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex ImageRegex = new(
            "\"image\"\\s*:\\s*\"(?<image>[^\"]+)\"",
            RegexOptions.Compiled);

        private static readonly Regex AnimationFrameSuffixRegex = new(
            "(__frame_\\d+|_frame_\\d+|__\\d+)$",
            RegexOptions.Compiled);

        private static readonly Regex AnimationFrameIndexRegex = new(
            "(?:__frame_|_frame_|__)(?<index>\\d+)$",
            RegexOptions.Compiled);

        private static readonly Regex FrameTagRegex = new(
            "\\{\\s*\"name\"\\s*:\\s*\"(?<name>[^\"]+)\"\\s*,\\s*\"from\"\\s*:\\s*(?<from>\\d+)\\s*,\\s*\"to\"\\s*:\\s*(?<to>\\d+)(?<body>[^}]*)\\}",
            RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex DirectionRegex = new(
            "\"direction\"\\s*:\\s*\"(?<direction>[^\"]+)\"",
            RegexOptions.Compiled);

        // 같은 레이어 이름도 계층과 프레임 인덱스 재시작을 기준으로 별도 애니메이션으로 묶는다.
        public static List<AsepriteAnimationData> Parse(
            string json,
            IReadOnlyList<AsepriteHierarchyEntry> orderedHierarchy)
        {
            var sourceName = ReadSourceName(json);
            var frameEntries = ParseFrameEntries(json, orderedHierarchy, sourceName);
            var frameTags = ParseFrameTags(json);
            return frameTags.Count > 0
                ? BuildTaggedAnimations(sourceName, frameEntries, frameTags)
                : new List<AsepriteAnimationData>();
        }

        public static List<AsepriteFrameData> FlattenFrames(IReadOnlyList<AsepriteAnimationData> animations)
        {
            var frames = new List<AsepriteFrameData>();
            for (var i = 0; i < animations.Count; i++)
            {
                frames.AddRange(animations[i].Frames);
            }

            return frames;
        }

        private static List<ParsedFrameEntry> ParseFrameEntries(
            string json,
            IReadOnlyList<AsepriteHierarchyEntry> orderedHierarchy,
            string sourceName)
        {
            var result = new List<ParsedFrameEntry>();
            var spriteNameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var hierarchyEntriesByName = BuildHierarchyEntriesByName(orderedHierarchy);
            var sequenceStates = new Dictionary<string, LayerFrameSequenceState>(StringComparer.Ordinal);
            foreach (Match match in FrameRegex.Matches(json))
            {
                var rawName = NormalizeFrameName(match.Groups["raw"].Value);
                var layerName = GetAnimationLayerName(rawName);
                if (string.IsNullOrEmpty(layerName))
                {
                    continue;
                }

                var frameIndex = ReadFrameIndex(rawName);
                var hierarchyPath = ResolveHierarchyPath(
                    layerName,
                    frameIndex,
                    hierarchyEntriesByName,
                    sequenceStates);
                var hierarchyKey = MakeHierarchyKey(hierarchyPath, layerName);

                var qualifiedSpriteName = AsepriteSpriteName.Build(hierarchyPath, rawName);
                spriteNameCounts.TryGetValue(qualifiedSpriteName, out var count);
                spriteNameCounts[qualifiedSpriteName] = count + 1;
                var spriteName = count == 0 ? qualifiedSpriteName : $"{qualifiedSpriteName}__{count}";
                var frame = new AsepriteFrameData
                {
                    Name = rawName,
                    ExportedName = rawName,
                    SpriteName = spriteName,
                    AtlasX = ReadInt(match, "x"),
                    AtlasY = ReadInt(match, "y"),
                    Width = ReadInt(match, "w"),
                    Height = ReadInt(match, "h"),
                    SourceX = ReadInt(match, "sx"),
                    SourceY = ReadInt(match, "sy"),
                    DurationMs = ReadInt(match, "duration"),
                    HierarchyPath = new List<string>(hierarchyPath),
                };
                result.Add(new ParsedFrameEntry
                {
                    SourceName = sourceName,
                    LayerName = layerName,
                    FrameIndex = frameIndex,
                    HierarchyKey = hierarchyKey,
                    Frame = frame,
                });
            }

            return result;
        }

        private static List<AsepriteAnimationData> BuildTaggedAnimations(
            string sourceName,
            IReadOnlyList<ParsedFrameEntry> frameEntries,
            IReadOnlyList<AsepriteFrameTag> frameTags)
        {
            var animations = new Dictionary<string, AsepriteAnimationData>(StringComparer.Ordinal);
            for (var tagIndex = 0; tagIndex < frameTags.Count; tagIndex++)
            {
                var tag = frameTags[tagIndex];
                for (var i = 0; i < frameEntries.Count; i++)
                {
                    var entry = frameEntries[i];
                    if (entry.FrameIndex < tag.From || entry.FrameIndex > tag.To)
                    {
                        continue;
                    }

                    var key = $"{tag.AnimationName}|{entry.HierarchyKey}";
                    if (!animations.TryGetValue(key, out var animation))
                    {
                        animation = new AsepriteAnimationData
                        {
                            SourceName = sourceName,
                            LayerName = entry.LayerName,
                            AnimationName = tag.AnimationName,
                            HierarchyKey = entry.HierarchyKey,
                            ObjectName = MakeObjectName($"{entry.HierarchyKey.Replace('/', '_')}_{tag.AnimationName}"),
                            PlaybackMode = tag.PlaybackMode,
                        };
                        animations.Add(key, animation);
                    }

                    animation.Frames.Add(CloneFrame(entry.Frame));
                }
            }

            var result = new List<AsepriteAnimationData>();
            foreach (var animation in animations.Values)
            {
                if (animation.Frames.Count > 0)
                {
                    result.Add(animation);
                }
            }

            return result;
        }

        private static List<AsepriteFrameTag> ParseFrameTags(string json)
        {
            var result = new List<AsepriteFrameTag>();
            foreach (Match match in FrameTagRegex.Matches(json))
            {
                string rawName = match.Groups["name"].Value;
                if (!TryParseTaggedAnimationName(rawName, out var animationName, out var playbackMode))
                {
                    continue;
                }

                var from = ReadInt(match, "from");
                var to = ReadInt(match, "to");
                if (to < from)
                {
                    (from, to) = (to, from);
                }

                result.Add(new AsepriteFrameTag
                {
                    AnimationName = animationName,
                    From = from,
                    To = to,
                    PlaybackMode = playbackMode,
                    Direction = ReadTagDirection(match.Groups["body"].Value),
                });
            }

            return result;
        }

        private static bool TryParseTaggedAnimationName(
            string rawName,
            out string animationName,
            out AsepriteSpritePlaybackMode playbackMode)
        {
            animationName = string.Empty;
            playbackMode = AsepriteSpritePlaybackMode.Once;
            string value = (rawName ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            if (IsIgnoredAnimationTag(value))
            {
                return false;
            }

            var colonIndex = value.IndexOf(':');
            if (colonIndex >= 0)
            {
                string mode = value.Substring(0, colonIndex).Trim();
                if (string.Equals(mode, "loop", StringComparison.OrdinalIgnoreCase))
                {
                    playbackMode = AsepriteSpritePlaybackMode.Loop;
                    value = value.Substring(colonIndex + 1).Trim();
                }
                else if (string.Equals(mode, "once", StringComparison.OrdinalIgnoreCase))
                {
                    playbackMode = AsepriteSpritePlaybackMode.Once;
                    value = value.Substring(colonIndex + 1).Trim();
                }
                else
                {
                    string suffixMode = value.Substring(colonIndex + 1).Trim();
                    if (string.Equals(suffixMode, "loop", StringComparison.OrdinalIgnoreCase))
                    {
                        playbackMode = AsepriteSpritePlaybackMode.Loop;
                        value = value.Substring(0, colonIndex).Trim();
                    }
                    else if (string.Equals(suffixMode, "once", StringComparison.OrdinalIgnoreCase))
                    {
                        playbackMode = AsepriteSpritePlaybackMode.Once;
                        value = value.Substring(0, colonIndex).Trim();
                    }
                }
            }

            animationName = value;
            if (IsIgnoredAnimationTag(animationName))
            {
                animationName = string.Empty;
                return false;
            }

            return !string.IsNullOrEmpty(animationName);
        }

        private static bool IsIgnoredAnimationTag(string value)
        {
            value = (value ?? string.Empty).Trim();
            return string.Equals(value, "default", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "once", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "loop", StringComparison.OrdinalIgnoreCase);
        }

        private static string ReadTagDirection(string body)
        {
            var match = DirectionRegex.Match(body ?? string.Empty);
            return match.Success ? match.Groups["direction"].Value : "forward";
        }

        private static AsepriteFrameData CloneFrame(AsepriteFrameData frame)
        {
            return new AsepriteFrameData
            {
                Name = frame.Name,
                ExportedName = frame.ExportedName,
                SpriteName = frame.SpriteName,
                HasDuplicateName = frame.HasDuplicateName,
                IsPopupLayer = frame.IsPopupLayer,
                DurationMs = frame.DurationMs,
                AtlasX = frame.AtlasX,
                AtlasY = frame.AtlasY,
                Width = frame.Width,
                Height = frame.Height,
                SourceX = frame.SourceX,
                SourceY = frame.SourceY,
                HierarchyPath = new List<string>(frame.HierarchyPath),
            };
        }

        private static string ReadSourceName(string json)
        {
            var match = ImageRegex.Match(json);
            if (!match.Success)
            {
                return "Aseprite";
            }

            var fileName = Path.GetFileNameWithoutExtension(match.Groups["image"].Value);
            return fileName
                .Replace("-animations", string.Empty, StringComparison.Ordinal)
                .Replace("-sheet", string.Empty, StringComparison.Ordinal);
        }

        private static string GetAnimationLayerName(string rawName)
        {
            return AnimationFrameSuffixRegex.Replace(rawName, string.Empty);
        }

        private static int ReadFrameIndex(string rawName)
        {
            var match = AnimationFrameIndexRegex.Match(rawName);
            return match.Success && int.TryParse(match.Groups["index"].Value, out var frameIndex)
                ? frameIndex
                : 0;
        }

        private static Dictionary<string, List<AsepriteHierarchyEntry>> BuildHierarchyEntriesByName(
            IReadOnlyList<AsepriteHierarchyEntry> orderedHierarchy)
        {
            var result = new Dictionary<string, List<AsepriteHierarchyEntry>>(StringComparer.Ordinal);
            if (orderedHierarchy == null)
            {
                return result;
            }

            for (var i = 0; i < orderedHierarchy.Count; i++)
            {
                var entry = orderedHierarchy[i];
                if (entry == null || entry.IsGroup)
                {
                    continue;
                }

                if (!result.TryGetValue(entry.Name, out var entries))
                {
                    entries = new List<AsepriteHierarchyEntry>();
                    result.Add(entry.Name, entries);
                }

                entries.Add(entry);
            }

            return result;
        }

        private static IReadOnlyList<string> ResolveHierarchyPath(
            string layerName,
            int frameIndex,
            IReadOnlyDictionary<string, List<AsepriteHierarchyEntry>> hierarchyEntriesByName,
            IDictionary<string, LayerFrameSequenceState> sequenceStates)
        {
            if (!sequenceStates.TryGetValue(layerName, out var state))
            {
                state = new LayerFrameSequenceState();
                sequenceStates.Add(layerName, state);
            }

            if (state.OccurrenceIndex < 0)
            {
                state.OccurrenceIndex = 0;
            }
            else if (frameIndex <= state.LastFrameIndex)
            {
                state.OccurrenceIndex++;
            }

            state.LastFrameIndex = frameIndex;
            if (hierarchyEntriesByName.TryGetValue(layerName, out var entries) &&
                state.OccurrenceIndex < entries.Count)
            {
                return entries[state.OccurrenceIndex].HierarchyPath;
            }

            return Array.Empty<string>();
        }

        private static string MakeHierarchyKey(IReadOnlyList<string> hierarchyPath, string layerName)
        {
            return hierarchyPath == null || hierarchyPath.Count == 0
                ? layerName
                : string.Join("/", hierarchyPath) + "/" + layerName;
        }

        private static string MakeObjectName(string layerName)
        {
            return SanitizeAssetName(layerName);
        }

        private static string NormalizeFrameName(string rawName)
        {
            return Path.GetFileNameWithoutExtension(rawName);
        }

        // 좌표와 스프라이트 영역이 모두 같으면 실제로 움직이지 않는 프레임 묶음으로 본다.
        private static bool HasVisibleFrameChange(IReadOnlyList<AsepriteFrameData> frames)
        {
            if (frames.Count <= 1)
            {
                return false;
            }

            var first = frames[0];
            for (var i = 1; i < frames.Count; i++)
            {
                if (first.AtlasX != frames[i].AtlasX ||
                    first.AtlasY != frames[i].AtlasY ||
                    first.Width != frames[i].Width ||
                    first.Height != frames[i].Height ||
                    first.SourceX != frames[i].SourceX ||
                    first.SourceY != frames[i].SourceY)
                {
                    return true;
                }
            }

            return false;
        }

        private static string SanitizeAssetName(string value)
        {
            var result = Regex.Replace(value, "[^A-Za-z0-9_\\-가-힣]", "_");
            return string.IsNullOrEmpty(result) ? "Animation" : result;
        }

        private static int ReadInt(Match match, string groupName)
        {
            return int.Parse(match.Groups[groupName].Value);
        }

        private sealed class LayerFrameSequenceState
        {
            public int OccurrenceIndex = -1;
            public int LastFrameIndex = -1;
        }

        private sealed class ParsedFrameEntry
        {
            public string SourceName = string.Empty;
            public string LayerName = string.Empty;
            public string HierarchyKey = string.Empty;
            public int FrameIndex;
            public AsepriteFrameData Frame;
        }

        private sealed class AsepriteFrameTag
        {
            public string AnimationName = string.Empty;
            public int From;
            public int To;
            public string Direction = "forward";
            public AsepriteSpritePlaybackMode PlaybackMode = AsepriteSpritePlaybackMode.Once;
        }
    }

    internal readonly struct ProcessResult
    {
        public readonly int ExitCode;
        public readonly string Output;

        public ProcessResult(int exitCode, string output)
        {
            ExitCode = exitCode;
            Output = output;
        }
    }

    internal sealed class JsonFrameEntry
    {
        public string Text = string.Empty;
        public string LayerName = string.Empty;
        public string FrameKey = string.Empty;
    }

    // Aseprite JSON 텍스트를 파싱하여 Atlas 데이터를 구성.
    internal static class AsepriteAtlasParser
    {
        private static readonly Regex FrameRegex = new(
            "\"(?<raw>[^\"]+)\"\\s*:\\s*\\{\\s*\"frame\"\\s*:\\s*\\{\\s*\"x\"\\s*:\\s*(?<x>\\d+)\\s*,\\s*\"y\"\\s*:\\s*(?<y>\\d+)\\s*,\\s*\"w\"\\s*:\\s*(?<w>\\d+)\\s*,\\s*\"h\"\\s*:\\s*(?<h>\\d+)\\s*\\}.*?\"spriteSourceSize\"\\s*:\\s*\\{\\s*\"x\"\\s*:\\s*(?<sx>\\d+)\\s*,\\s*\"y\"\\s*:\\s*(?<sy>\\d+)\\s*,\\s*\"w\"\\s*:\\s*\\d+\\s*,\\s*\"h\"\\s*:\\s*\\d+\\s*\\}.*?\"sourceSize\"\\s*:\\s*\\{\\s*\"w\"\\s*:\\s*(?<sw>\\d+)\\s*,\\s*\"h\"\\s*:\\s*(?<sh>\\d+)\\s*\\}.*?\"duration\"\\s*:\\s*(?<duration>\\d+)",
            RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex LayerRegex = new(
            "\\{\\s*\"name\"\\s*:\\s*\"(?<name>[^\"]+)\"(?<body>[^}]*)\\}",
            RegexOptions.Compiled | RegexOptions.Singleline);

        private static readonly Regex GroupRegex = new(
            "\"group\"\\s*:\\s*\"(?<group>[^\"]+)\"",
            RegexOptions.Compiled);

        private static readonly Regex NameInParenthesesRegex = new("\\((?<name>[^)]+)\\)", RegexOptions.Compiled);
        private static readonly Regex FrameSuffixRegex = new(
            "(__frame_\\d+|_frame_\\d+|__\\d+)$",
            RegexOptions.Compiled);
        private static readonly Regex FrameIndexRegex = new(
            "(?:__frame_|_frame_|__)(?<index>\\d+)$",
            RegexOptions.Compiled);

        public static AsepriteAtlasData Parse(string json)
        {
            var result = new AsepriteAtlasData();
            var framesByLayerOccurrence = new Dictionary<string, Queue<AsepriteFrameData>>(StringComparer.Ordinal);
            var spriteNameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var allFrames = new List<AsepriteFrameData>();
            var occurrenceIndexByLayer = new Dictionary<string, int>(StringComparer.Ordinal);
            var lastFrameIndexByLayer = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (Match match in FrameRegex.Matches(json))
            {
                var exportedName = NormalizeExportedFrameName(match.Groups["raw"].Value);
                spriteNameCounts.TryGetValue(exportedName, out var spriteNameCount);
                spriteNameCounts[exportedName] = spriteNameCount + 1;
                var layerName = NormalizeLayerFrameName(exportedName);
                var frame = new AsepriteFrameData
                {
                    Name = layerName,
                    ExportedName = exportedName,
                    SpriteName = spriteNameCount == 0 ? exportedName : $"{exportedName}__{spriteNameCount}",
                    AtlasX = ReadInt(match, "x"),
                    AtlasY = ReadInt(match, "y"),
                    Width = ReadInt(match, "w"),
                    Height = ReadInt(match, "h"),
                    SourceX = ReadInt(match, "sx"),
                    SourceY = ReadInt(match, "sy"),
                    DurationMs = ReadInt(match, "duration"),
                };

                result.SourceWidth = Math.Max(result.SourceWidth, Math.Max(ReadInt(match, "sw"), frame.SourceX + frame.Width));
                result.SourceHeight = Math.Max(result.SourceHeight, Math.Max(ReadInt(match, "sh"), frame.SourceY + frame.Height));
                allFrames.Add(frame);

                var frameIndex = ReadFrameIndex(exportedName);
                occurrenceIndexByLayer.TryGetValue(layerName, out var occurrenceIndex);
                if (frameIndex >= 0 &&
                    lastFrameIndexByLayer.TryGetValue(layerName, out var lastFrameIndex) &&
                    frameIndex <= lastFrameIndex)
                {
                    occurrenceIndex++;
                }

                occurrenceIndexByLayer[layerName] = occurrenceIndex;
                lastFrameIndexByLayer[layerName] = frameIndex;
                var layerOccurrenceKey = MakeLayerOccurrenceKey(layerName, occurrenceIndex);
                if (!framesByLayerOccurrence.TryGetValue(layerOccurrenceKey, out var sameLayerFrames))
                {
                    sameLayerFrames = new Queue<AsepriteFrameData>();
                    framesByLayerOccurrence.Add(layerOccurrenceKey, sameLayerFrames);
                }

                sameLayerFrames.Enqueue(frame);
            }

            var layerEntries = ParseLayerEntries(json);
            var groupByLayer = new Dictionary<string, string>(StringComparer.Ordinal);
            var layerEntryNameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            var assignedNameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var layer in layerEntries)
            {
                if (!string.IsNullOrEmpty(layer.Group))
                {
                    groupByLayer[layer.Name] = layer.Group;
                }

                layerEntryNameCounts.TryGetValue(layer.Name, out var layerEntryNameCount);
                layerEntryNameCounts[layer.Name] = layerEntryNameCount + 1;
            }

            foreach (var layer in layerEntries)
            {
                var hierarchyPath = BuildHierarchyPath(layer, groupByLayer);
                result.OrderedHierarchy.Add(new AsepriteHierarchyEntry
                {
                    Name = layer.Name,
                    IsGroup = !HasFrame(framesByLayerOccurrence, layer.Name),
                    HierarchyPath = hierarchyPath,
                });

                if (TryDequeueFrame(framesByLayerOccurrence, layer.Name, assignedNameCounts, out var frame))
                {
                    frame.HierarchyPath = hierarchyPath;
                    frame.SpriteName = AsepriteSpriteName.Build(frame.HierarchyPath, frame.ExportedName);
                    layerEntryNameCounts.TryGetValue(frame.Name, out var duplicateCount);
                    frame.HasDuplicateName = duplicateCount > 1;
                    frame.IsPopupLayer = IsUnderPopupGroup(frame.HierarchyPath);
                    result.OrderedFrames.Add(frame);
                }
            }

            if (result.OrderedFrames.Count == 0)
            {
                for (var i = 0; i < allFrames.Count; i++)
                {
                    var frame = allFrames[i];
                    layerEntryNameCounts.TryGetValue(frame.Name, out var duplicateCount);
                    frame.HasDuplicateName = duplicateCount > 1;
                    frame.IsPopupLayer = IsUnderPopupGroup(frame.HierarchyPath);
                    result.OrderedFrames.Add(frame);
                }
            }

            if (result.OrderedFrames.Count == 0)
            {
                throw new InvalidDataException("Aseprite atlas frame data was not found.");
            }

            return result;
        }

        // 남아 있는 프레임 큐에 해당 레이어 이름이 있는지 확인한다.
        private static bool HasFrame(
            IReadOnlyDictionary<string, Queue<AsepriteFrameData>> framesByLayerOccurrence,
            string layerName)
        {
            return framesByLayerOccurrence.TryGetValue(MakeLayerOccurrenceKey(layerName, 0), out var frames) &&
                   frames.Count > 0;
        }

        // 같은 이름의 레이어도 프레임 번호가 재시작되는 지점으로 부모별 프레임 묶음을 나눠 꺼낸다.
        private static bool TryDequeueFrame(
            IReadOnlyDictionary<string, Queue<AsepriteFrameData>> framesByLayerOccurrence,
            string layerName,
            IDictionary<string, int> assignedNameCounts,
            out AsepriteFrameData frame)
        {
            assignedNameCounts.TryGetValue(layerName, out var occurrenceIndex);
            assignedNameCounts[layerName] = occurrenceIndex + 1;
            if (framesByLayerOccurrence.TryGetValue(MakeLayerOccurrenceKey(layerName, occurrenceIndex), out var frames) &&
                frames.Count > 0)
            {
                frame = frames.Dequeue();
                return true;
            }

            frame = null;
            return false;
        }

        private static string MakeLayerOccurrenceKey(string layerName, int occurrenceIndex)
        {
            return $"{layerName}#{occurrenceIndex}";
        }

        private static List<AsepriteLayerEntry> ParseLayerEntries(string json)
        {
            var result = new List<AsepriteLayerEntry>();
            var layersIndex = json.IndexOf("\"layers\"", StringComparison.Ordinal);
            if (layersIndex < 0)
            {
                return result;
            }

            var layersJson = json.Substring(layersIndex);
            foreach (Match match in LayerRegex.Matches(layersJson))
            {
                var body = match.Groups["body"].Value;
                var groupMatch = GroupRegex.Match(body);
                result.Add(new AsepriteLayerEntry
                {
                    Name = match.Groups["name"].Value,
                    Group = groupMatch.Success ? groupMatch.Groups["group"].Value : string.Empty,
                });
            }

            return result;
        }

        // popup_ 그룹 아래 레이어인지 확인한다.
        private static bool IsUnderPopupGroup(IReadOnlyList<string> hierarchyPath)
        {
            for (var i = 0; i < hierarchyPath.Count; i++)
            {
                if (hierarchyPath[i].StartsWith("popup_", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static List<string> BuildHierarchyPath(
            AsepriteLayerEntry layer,
            IReadOnlyDictionary<string, string> groupByLayer)
        {
            var reversedPath = new List<string>();
            var visited = new HashSet<string>(StringComparer.Ordinal) { layer.Name };
            var current = layer.Group;
            while (!string.IsNullOrEmpty(current) && visited.Add(current))
            {
                reversedPath.Add(current);
                current = groupByLayer.TryGetValue(current, out var parentGroup)
                    ? parentGroup
                    : string.Empty;
            }

            reversedPath.Reverse();
            return reversedPath;
        }

        private static string NormalizeExportedFrameName(string rawName)
        {
            var match = NameInParenthesesRegex.Match(rawName);
            if (match.Success)
            {
                return match.Groups["name"].Value;
            }

            return Path.GetFileNameWithoutExtension(rawName);
        }

        private static string NormalizeLayerFrameName(string exportedName)
        {
            return FrameSuffixRegex.Replace(exportedName, string.Empty);
        }

        private static int ReadFrameIndex(string exportedName)
        {
            var match = FrameIndexRegex.Match(exportedName);
            return match.Success && int.TryParse(match.Groups["index"].Value, out var frameIndex)
                ? frameIndex
                : -1;
        }

        private static int ReadInt(Match match, string groupName)
        {
            return int.Parse(match.Groups[groupName].Value);
        }

        private sealed class AsepriteLayerEntry
        {
            public string Name = string.Empty;
            public string Group = string.Empty;
        }
    }
}
