using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Escape.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Escape.EditorTools
{
    // 모든 프리팹/씬의 TMP 텍스트를 훑어 LocalizedTextUI 부착 상태와 tid를 일괄 점검하고 배정하는 에디터 창.
    public sealed class LocalizedTextAuditor : EditorWindow
    {
        private const string TextTsvPath = "Assets/Resources/Data/text.tsv";
        private const string SceneSearchFolder = "Assets/Scenes";

        // 한 TMP 텍스트 오브젝트의 점검 결과 한 줄.
        private sealed class Entry
        {
            public bool isScene;
            public string sourcePath;
            public string hierarchyPath;
            public string currentText;
            public bool hasLocalized;
            public string tid;
            public bool tidValid;
            public string suggestedTid;
            public bool suggestionAmbiguous;
            public string plannedTid;
        }

        private readonly List<Entry> entries = new();
        private HashSet<string> knownIds = new();
        private Dictionary<string, string> textToId = new();
        private HashSet<string> ambiguousTexts = new();

        private Vector2 scroll;
        private bool onlyProblems = true;

        [MenuItem("Tools/Escape/Localization/Text Object Auditor")]
        private static void Open()
        {
            GetWindow<LocalizedTextAuditor>("Text Localization Audit");
        }

        [MenuItem("Tools/Escape/Localization/Apply Exact Suggestions")]
        private static void ApplyExactSuggestions()
        {
            LocalizedTextAuditor auditor = CreateInstance<LocalizedTextAuditor>();
            try
            {
                auditor.Scan();
                auditor.FillSuggestions();
                auditor.ApplyPlanned(false);
            }
            finally
            {
                DestroyImmediate(auditor);
            }
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Scan", EditorStyles.toolbarButton, GUILayout.Width(70f)))
                {
                    Scan();
                }

                if (GUILayout.Button("Fill Suggestions", EditorStyles.toolbarButton, GUILayout.Width(120f)))
                {
                    FillSuggestions();
                }

                if (GUILayout.Button("Apply Planned", EditorStyles.toolbarButton, GUILayout.Width(110f)))
                {
                    ApplyPlanned(true);
                }

                GUILayout.FlexibleSpace();
                onlyProblems = GUILayout.Toggle(onlyProblems, "미배정만 보기", EditorStyles.toolbarButton, GUILayout.Width(110f));
            }

            if (entries.Count == 0)
            {
                EditorGUILayout.HelpBox("Scan을 눌러 프리팹/씬의 TMP 텍스트를 점검합니다.", MessageType.Info);
                return;
            }

            DrawSummary();

            scroll = EditorGUILayout.BeginScrollView(scroll);
            string lastSource = null;
            foreach (Entry entry in entries)
            {
                if (onlyProblems && entry.hasLocalized && entry.tidValid)
                {
                    continue;
                }

                if (!string.Equals(lastSource, entry.sourcePath, StringComparison.Ordinal))
                {
                    lastSource = entry.sourcePath;
                    EditorGUILayout.Space(4f);
                    EditorGUILayout.LabelField($"{(entry.isScene ? "[Scene] " : "[Prefab] ")}{entry.sourcePath}", EditorStyles.boldLabel);
                }

                DrawEntryRow(entry);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawSummary()
        {
            int total = entries.Count;
            int missing = entries.Count(e => !e.hasLocalized);
            int invalid = entries.Count(e => e.hasLocalized && !e.tidValid);
            EditorGUILayout.HelpBox(
                $"총 {total}개 · LocalizedTextUI 없음 {missing}개 · tid가 text.tsv에 없음 {invalid}개",
                missing + invalid > 0 ? MessageType.Warning : MessageType.Info);
        }

        private void DrawEntryRow(Entry entry)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    string status = !entry.hasLocalized
                        ? "● 컴포넌트 없음"
                        : entry.tidValid ? "✓ OK" : "▲ tid 없음";
                    EditorGUILayout.LabelField(status, GUILayout.Width(110f));
                    EditorGUILayout.LabelField(entry.hierarchyPath, EditorStyles.miniLabel);
                }

                EditorGUILayout.LabelField("텍스트", TruncateSingleLine(entry.currentText), EditorStyles.miniLabel);

                using (new EditorGUILayout.HorizontalScope())
                {
                    if (!string.IsNullOrEmpty(entry.suggestedTid))
                    {
                        string label = entry.suggestionAmbiguous ? $"추천(중복): {entry.suggestedTid}" : $"추천: {entry.suggestedTid}";
                        EditorGUILayout.LabelField(label, GUILayout.Width(220f));
                        if (GUILayout.Button("채택", GUILayout.Width(50f)))
                        {
                            entry.plannedTid = entry.suggestedTid;
                        }
                    }

                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField("배정 tid", GUILayout.Width(55f));
                    entry.plannedTid = EditorGUILayout.TextField(entry.plannedTid ?? string.Empty, GUILayout.Width(200f));
                }
            }
        }

        // 프리팹과 씬을 훑어 TMP 텍스트 목록을 수집한다.
        private void Scan()
        {
            LoadTextTable();
            entries.Clear();

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    CollectFrom(root.transform, path, false);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { SceneSearchFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                ProcessScene(path, null);
            }

            entries.Sort((a, b) => string.Compare(a.sourcePath, b.sourcePath, StringComparison.Ordinal));
            Repaint();
        }

        // 한 트랜스폼 계층의 모든 TMP 텍스트를 Entry로 만든다.
        private void CollectFrom(Transform root, string sourcePath, bool isScene)
        {
            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                string current = text.text ?? string.Empty;
                if (ShouldIgnoreLocalization(text, current))
                {
                    continue;
                }

                var localized = text.GetComponent<LocalizedTextUI>();
                string tid = localized != null ? new SerializedObject(localized).FindProperty("tid").stringValue : string.Empty;
            string suggested = ResolveSuggestion(sourcePath, current, out bool ambiguous);

                entries.Add(new Entry
                {
                    isScene = isScene,
                    sourcePath = sourcePath,
                    hierarchyPath = GetHierarchyPath(text.transform),
                    currentText = current,
                    hasLocalized = localized != null,
                    tid = tid,
                    tidValid = !string.IsNullOrEmpty(tid) && knownIds.Contains(tid),
                    suggestedTid = suggested,
                    suggestionAmbiguous = ambiguous,
                    plannedTid = localized != null ? tid : string.Empty
                });
            }
        }

        // 추천 tid를 채워 넣는다(모호하지 않은 매칭만).
        private void FillSuggestions()
        {
            foreach (Entry entry in entries)
            {
                if (!string.IsNullOrEmpty(entry.plannedTid) || entry.suggestionAmbiguous)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(entry.suggestedTid))
                {
                    entry.plannedTid = entry.suggestedTid;
                }
            }

            Repaint();
        }

        // 배정 tid가 채워진 항목에 LocalizedTextUI를 부착/갱신한다.
        private void ApplyPlanned(bool confirmSceneSave)
        {
            var perPrefab = entries
                .Where(e => !e.isScene && ShouldApply(e))
                .GroupBy(e => e.sourcePath);
            foreach (var group in perPrefab)
            {
                ApplyToPrefab(group.Key, group.ToList());
            }

            var perScene = entries
                .Where(e => e.isScene && ShouldApply(e))
                .GroupBy(e => e.sourcePath)
                .ToList();
            if (confirmSceneSave && perScene.Count > 0 &&
                !EditorUtility.DisplayDialog(
                    "씬 저장 확인",
                    "씬에 LocalizedTextUI를 부착하고 저장합니다. 진행할까요?",
                    "적용", "취소"))
            {
                return;
            }

            foreach (var group in perScene)
            {
                // 같은 이름의 형제 오브젝트로 계층 경로가 겹칠 수 있어 첫 항목만 채택한다.
                var planned = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (Entry entry in group)
                {
                    planned.TryAdd(entry.hierarchyPath, entry.plannedTid);
                }

                ProcessScene(group.Key, planned);
            }

            AssetDatabase.SaveAssets();
            Scan();
        }

        private static bool ShouldApply(Entry entry)
        {
            return !string.IsNullOrWhiteSpace(entry.plannedTid) &&
                   !(entry.hasLocalized && string.Equals(entry.tid, entry.plannedTid, StringComparison.Ordinal));
        }

        private static bool ShouldIgnoreLocalization(TMP_Text text, string current)
        {
            string normalized = Normalize(current);
            if (normalized == "<" || normalized == ">")
            {
                return true;
            }

            return normalized == "X" &&
                text != null &&
                text.transform.parent != null &&
                string.Equals(text.transform.parent.name, "CloseButton", StringComparison.Ordinal);
        }

        private void ApplyToPrefab(string path, List<Entry> targets)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                bool changed = false;
                foreach (Entry target in targets)
                {
                    Transform node = FindByHierarchyPath(root.transform, target.hierarchyPath);
                    if (node != null && AssignLocalized(node.gameObject, target.plannedTid))
                    {
                        changed = true;
                    }
                }

                if (changed)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // 씬을 (필요 시 임시로) 열어 스캔하거나 배정을 적용한다. planned가 null이면 스캔만 한다.
        private void ProcessScene(string path, Dictionary<string, string> planned)
        {
            Scene scene = SceneManager.GetSceneByPath(path);
            bool alreadyOpen = scene.IsValid() && scene.isLoaded;
            if (!alreadyOpen)
            {
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            }

            try
            {
                if (planned == null)
                {
                    foreach (GameObject go in scene.GetRootGameObjects())
                    {
                        CollectFrom(go.transform, path, true);
                    }

                    return;
                }

                bool changed = false;
                foreach (GameObject go in scene.GetRootGameObjects())
                {
                    foreach (TMP_Text text in go.GetComponentsInChildren<TMP_Text>(true))
                    {
                        string hierarchy = GetHierarchyPath(text.transform);
                        if (planned.TryGetValue(hierarchy, out string tid) && AssignLocalized(text.gameObject, tid))
                        {
                            changed = true;
                        }
                    }
                }

                if (changed)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
            }
            finally
            {
                if (!alreadyOpen)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        // LocalizedTextUI를 없으면 추가하고 tid/참조/폴백을 채운다. 실제 변경이 있었는지 반환한다.
        private static bool AssignLocalized(GameObject go, string tid)
        {
            var text = go.GetComponent<TMP_Text>();
            if (text == null)
            {
                return false;
            }

            var localized = go.GetComponent<LocalizedTextUI>();
            if (localized == null)
            {
                localized = go.AddComponent<LocalizedTextUI>();
            }

            var so = new SerializedObject(localized);
            var tidProp = so.FindProperty("tid");
            var tmpProp = so.FindProperty("tmpText");
            var fallbackProp = so.FindProperty("fallbackText");

            bool changed = false;
            if (tidProp.stringValue != tid)
            {
                tidProp.stringValue = tid;
                changed = true;
            }

            if (tmpProp.objectReferenceValue != text)
            {
                tmpProp.objectReferenceValue = text;
                changed = true;
            }

            if (string.IsNullOrEmpty(fallbackProp.stringValue) && !string.IsNullOrEmpty(text.text))
            {
                fallbackProp.stringValue = text.text;
                changed = true;
            }

            if (changed)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(localized);
            }

            return changed;
        }

        // 현재 텍스트를 text.tsv 셀 값에 역매핑해 tid 후보를 찾는다.
        private string ResolveSuggestion(string sourcePath, string current, out bool ambiguous)
        {
            ambiguous = false;
            string key = Normalize(current);
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            string explicitTid = ResolveExplicitSuggestion(sourcePath, key);
            if (!string.IsNullOrEmpty(explicitTid))
            {
                return explicitTid;
            }

            if (ambiguousTexts.Contains(key))
            {
                ambiguous = true;
            }

            return textToId.TryGetValue(key, out string id) ? id : string.Empty;
        }

        // 같은 표시 문구가 여러 tid에 쓰이는 경우 씬 문맥으로 정확한 키를 선택한다.
        private static string ResolveExplicitSuggestion(string sourcePath, string current)
        {
            string fileName = Path.GetFileName(sourcePath);
            return (fileName, current) switch
            {
                ("0_TitleScene.unity", "당신의 이름은 무엇입니까?") => "title_name_prompt",
                ("0_TitleScene.unity", "이름을 입력하세요") => "name_placeholder",
                ("0_TitleScene.unity", "확인") => "confirm",
                ("0_TitleScene.unity", "뒤로가기") => "back",
                ("2_SpaceShooterScene.unity", "SPACE SHOOTER") => "space_shooter_title",
                ("2_SpaceShooterScene.unity", "START") => "space_shooter_start",
                ("2_SpaceShooterScene.unity", "EXIT") => "space_shooter_exit",
                ("2_SpaceShooterScene.unity", "RANK") => "space_shooter_rank",
                ("2_SpaceShooterScene.unity", "BACK") => "space_shooter_back",
                ("3_RythmRecycleScene.unity", "타이밍에 맞춰 분리수거 하자!") => "rhythm_recycle_title",
                ("3_RythmRecycleScene.unity", "결과") => "rhythm_recycle_result_title",
                ("4_LockPickScene.unity", "자물쇠를 피킹하자!") => "lockpick_title",
                ("4_LockPickScene.unity", "다시하기") => "lockpick_retry",
                ("4_LockPickScene.unity", "해제 성공!") => "lockpick_success",
                ("4_LockPickScene.unity", "그만두기") => "lockpick_exit",
                _ => string.Empty
            };
        }

        private void LoadTextTable()
        {
            knownIds = new HashSet<string>(StringComparer.Ordinal);
            textToId = new Dictionary<string, string>(StringComparer.Ordinal);
            ambiguousTexts = new HashSet<string>(StringComparer.Ordinal);

            string fullPath = Path.GetFullPath(TextTsvPath);
            if (!File.Exists(fullPath))
            {
                return;
            }

            string[] lines = File.ReadAllLines(fullPath);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                string[] cells = line.Split('\t');
                string id = cells[0].Trim();
                if (id.Length == 0 || string.Equals(id, "id", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                knownIds.Add(id);
                for (int c = 1; c < cells.Length; c++)
                {
                    string key = Normalize(cells[c]);
                    if (key.Length == 0 || ambiguousTexts.Contains(key))
                    {
                        continue;
                    }

                    if (textToId.TryGetValue(key, out string existing))
                    {
                        if (!string.Equals(existing, id, StringComparison.Ordinal))
                        {
                            ambiguousTexts.Add(key);
                        }

                        continue;
                    }

                    textToId[key] = id;
                }
            }
        }

        private static string Normalize(string value)
        {
            return value == null ? string.Empty : value.Trim();
        }

        private static string TruncateSingleLine(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "(빈 문자열)";
            }

            string flat = value.Replace("\n", " ").Replace("\r", " ");
            return flat.Length > 80 ? flat.Substring(0, 80) + "…" : flat;
        }

        private static string GetHierarchyPath(Transform target)
        {
            var builder = new StringBuilder(target.name);
            Transform current = target.parent;
            while (current != null)
            {
                builder.Insert(0, current.name + "/");
                current = current.parent;
            }

            return builder.ToString();
        }

        private static Transform FindByHierarchyPath(Transform root, string hierarchyPath)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (string.Equals(GetHierarchyPath(child), hierarchyPath, StringComparison.Ordinal))
                {
                    return child;
                }
            }

            return null;
        }
    }
}
