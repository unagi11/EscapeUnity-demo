using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Escape.Rooms;
using UnityEditor;
using UnityEngine;

namespace Escape.Editor
{
    [CustomPropertyDrawer(typeof(TsvIdAttribute))]
    public sealed class TsvIdDrawer : PropertyDrawer
    {
        private const string EmptyLabel = "(None)";

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            var attr = (TsvIdAttribute)attribute;
            if (IsDialogueAssetPath(attr.AssetPath))
            {
                DrawDialogueIdMenu(position, property, label, attr);
                return;
            }

            string[] ids = LoadIds(attr.AssetPath, attr.ExtraIds, property.stringValue);
            string[] labels = BuildLabels(ids);
            int index = FindIndex(ids, property.stringValue);

            EditorGUI.BeginProperty(position, label, property);
            int nextIndex = EditorGUI.Popup(position, label.text, index, labels);
            if (nextIndex >= 0 && nextIndex < ids.Length)
            {
                property.stringValue = ids[nextIndex];
            }

            EditorGUI.EndProperty();
        }

        private static void DrawDialogueIdMenu(
            Rect position,
            SerializedProperty property,
            GUIContent label,
            TsvIdAttribute attr)
        {
            List<TsvIdOption> options = LoadIdOptions(attr.AssetPath, attr.ExtraIds, property.stringValue);
            string currentValue = property.stringValue;
            string buttonLabel = string.IsNullOrWhiteSpace(currentValue) ? EmptyLabel : currentValue;

            EditorGUI.BeginProperty(position, label, property);
            Rect fieldRect = EditorGUI.PrefixLabel(position, label);
            if (GUI.Button(fieldRect, buttonLabel, EditorStyles.popup))
            {
                var menu = new GenericMenu();
                SerializedObject serializedObject = property.serializedObject;
                string propertyPath = property.propertyPath;
                for (int i = 0; i < options.Count; i++)
                {
                    TsvIdOption option = options[i];
                    string nextValue = option.Id;
                    bool isSelected = string.Equals(nextValue, currentValue, StringComparison.Ordinal);
                    menu.AddItem(
                        new GUIContent(BuildMenuPath(option)),
                        isSelected,
                        () =>
                        {
                            serializedObject.Update();
                            SerializedProperty nextProperty = serializedObject.FindProperty(propertyPath);
                            if (nextProperty != null)
                            {
                                nextProperty.stringValue = nextValue;
                                serializedObject.ApplyModifiedProperties();
                            }
                        });
                }

                menu.DropDown(fieldRect);
            }

            EditorGUI.EndProperty();
        }

        private static string[] LoadIds(string assetPath, string[] extraIds, string currentValue)
        {
            var ids = new List<string> { string.Empty };
            if (extraIds != null)
            {
                for (int i = 0; i < extraIds.Length; i++)
                {
                    string extraId = extraIds[i];
                    if (!string.IsNullOrWhiteSpace(extraId) && !ids.Contains(extraId))
                    {
                        ids.Add(extraId);
                    }
                }
            }

            foreach (string fullPath in EnumerateTsvPaths(assetPath))
            {
                string[] lines = File.ReadAllLines(fullPath);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrWhiteSpace(line) ||
                        line.TrimStart().StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string id = line.Split('\t')[0].Trim();
                    if (string.Equals(id, "id", StringComparison.OrdinalIgnoreCase) ||
                        string.IsNullOrWhiteSpace(id) ||
                        ids.Contains(id))
                    {
                        continue;
                    }

                    ids.Add(id);
                }
            }

            if (!string.IsNullOrWhiteSpace(currentValue) && !ids.Contains(currentValue))
            {
                ids.Add(currentValue);
            }

            return ids.ToArray();
        }

        private static List<TsvIdOption> LoadIdOptions(string assetPath, string[] extraIds, string currentValue)
        {
            var options = new List<TsvIdOption>
            {
                new TsvIdOption(string.Empty, string.Empty, 0)
            };
            var seen = new HashSet<string>(StringComparer.Ordinal) { string.Empty };

            if (extraIds != null)
            {
                for (int i = 0; i < extraIds.Length; i++)
                {
                    string extraId = extraIds[i];
                    if (!string.IsNullOrWhiteSpace(extraId) && seen.Add(extraId))
                    {
                        options.Add(new TsvIdOption(extraId, "Special", 1));
                    }
                }
            }

            foreach (string fullPath in EnumerateTsvPaths(assetPath))
            {
                var groupOptions = new List<TsvIdOption>();
                string group = BuildDialogueGroupName(fullPath);
                int groupOrder = GetDialogueTsvRank(fullPath);
                string[] lines = File.ReadAllLines(fullPath);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrWhiteSpace(line) ||
                        line.TrimStart().StartsWith("#", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string id = line.Split('\t')[0].Trim();
                    if (string.Equals(id, "id", StringComparison.OrdinalIgnoreCase) ||
                        string.IsNullOrWhiteSpace(id) ||
                        !seen.Add(id))
                    {
                        continue;
                    }

                    groupOptions.Add(new TsvIdOption(id, group, groupOrder));
                }

                groupOptions.Sort(CompareTsvIdOptions);
                options.AddRange(groupOptions);
            }

            if (!string.IsNullOrWhiteSpace(currentValue) && seen.Add(currentValue))
            {
                options.Add(new TsvIdOption(currentValue, "Missing", 99));
            }

            return options;
        }

        private static IEnumerable<string> EnumerateTsvPaths(string assetPath)
        {
            string fullPath = Path.GetFullPath(assetPath);
            if (!File.Exists(fullPath))
            {
                yield break;
            }

            yield return fullPath;

            if (!string.Equals(Path.GetFileName(fullPath), "dialogue.tsv", StringComparison.OrdinalIgnoreCase))
            {
                yield break;
            }

            string directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                yield break;
            }

            string[] splitPaths = Directory.GetFiles(directory, "dialogue_*.tsv", SearchOption.TopDirectoryOnly);
            Array.Sort(splitPaths, CompareDialogueTsvPaths);
            for (int i = 0; i < splitPaths.Length; i++)
            {
                yield return splitPaths[i];
            }
        }

        private static int CompareDialogueTsvPaths(string left, string right)
        {
            int leftRank = GetDialogueTsvRank(left);
            int rightRank = GetDialogueTsvRank(right);
            if (leftRank != rightRank)
            {
                return leftRank.CompareTo(rightRank);
            }

            return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        }

        private static int GetDialogueTsvRank(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            return name switch
            {
                "dialogue" => 0,
                "dialogue_common" => 1,
                "dialogue_bedroom" => 2,
                "dialogue_entrance" => 3,
                "dialogue_kitchen" => 4,
                "dialogue_livingroom" => 5,
                "dialogue_utility" => 6,
                "dialogue_intro" => 20,
                "dialogue_ending" => 21,
                "dialogue_timing_test" => 22,
                _ => 10
            };
        }

        private static string BuildDialogueGroupName(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (string.Equals(name, "dialogue", StringComparison.OrdinalIgnoreCase))
            {
                return "Main";
            }

            const string prefix = "dialogue_";
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(prefix.Length);
            }

            string normalizedName = name.ToLowerInvariant();
            if (string.Equals(normalizedName, "livingroom", StringComparison.Ordinal))
            {
                return "Living Room";
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                return "Other";
            }

            string[] words = name.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
            {
                string word = words[i];
                words[i] = word.Length <= 1
                    ? word.ToUpperInvariant()
                    : char.ToUpperInvariant(word[0]) + word.Substring(1);
            }

            return string.Join(" ", words);
        }

        private static bool IsDialogueAssetPath(string assetPath)
        {
            return string.Equals(
                Path.GetFileName(assetPath),
                "dialogue.tsv",
                StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildMenuPath(TsvIdOption option)
        {
            if (string.IsNullOrWhiteSpace(option.Id))
            {
                return EmptyLabel;
            }

            return string.IsNullOrWhiteSpace(option.Group)
                ? option.Id
                : $"{option.Group}/{option.Id}";
        }

        private static int CompareTsvIdOptions(TsvIdOption left, TsvIdOption right)
        {
            int groupOrder = left.GroupOrder.CompareTo(right.GroupOrder);
            if (groupOrder != 0)
            {
                return groupOrder;
            }

            int group = string.Compare(left.Group, right.Group, StringComparison.OrdinalIgnoreCase);
            if (group != 0)
            {
                return group;
            }

            return string.Compare(left.Id, right.Id, StringComparison.OrdinalIgnoreCase);
        }

        private static string[] BuildLabels(string[] ids)
        {
            var labels = new string[ids.Length];
            for (int i = 0; i < ids.Length; i++)
            {
                labels[i] = string.IsNullOrWhiteSpace(ids[i]) ? EmptyLabel : ids[i];
            }

            return labels;
        }

        private static int FindIndex(string[] ids, string value)
        {
            for (int i = 0; i < ids.Length; i++)
            {
                if (string.Equals(ids[i], value, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return 0;
        }

        private readonly struct TsvIdOption
        {
            public TsvIdOption(string id, string group, int groupOrder)
            {
                Id = id;
                Group = group;
                GroupOrder = groupOrder;
            }

            public string Id { get; }
            public string Group { get; }
            public int GroupOrder { get; }
        }
    }

    [CustomPropertyDrawer(typeof(InteractionRule))]
    public sealed class InteractionRuleDrawer : PropertyDrawer
    {
        private static readonly Dictionary<string, bool> ExpandedSections = new();
        private static readonly Dictionary<string, int> ArraySizes = new();
        private static readonly GUIStyle SummaryStyle = new(EditorStyles.wordWrappedMiniLabel)
        {
            wordWrap = true
        };

        private static readonly string[] StringFields =
        {
            "selectedItemId",
            "comment",
            "preDialogueId",
            "grantItem",
            "grantInfo",
            "actionDialogueId",
            "postDialogueId"
        };

        private static readonly string[] ArrayFields =
        {
            "activeObjects",
            "inactiveObjects",
            "requiredItems",
            "absentItems",
            "requiredInfos",
            "absentInfos",
            "grantInfos",
            "activateObjects",
            "deactivateObjects"
        };

        private static readonly string[] BoolFields =
        {
            "consumeSelectedItem",
            "deactivateTouchedObject"
        };

        private static readonly string[] EnumFields =
        {
            "priorityLayer",
            "screenEffect",
            "specialAction",
            "transitionDestination",
            "touchSfx"
        };

        private static readonly string[] IntFields =
        {
            "priorityNumber"
        };

        private static readonly string[] PriorityFields =
        {
            "priorityLayer",
            "priorityNumber"
        };

        private static readonly string[] ConditionFields =
        {
            "selectedItemId",
            "activeObjects",
            "inactiveObjects",
            "requiredItems",
            "absentItems",
            "requiredInfos",
            "absentInfos"
        };

        private static readonly string[] DialogueFields =
        {
            "preDialogueId",
            "actionDialogueId",
            "postDialogueId"
        };

        private static readonly string[] EffectFields =
        {
            "screenEffect",
            "specialAction",
            "transitionDestination",
            "touchSfx"
        };

        private static readonly string[] RewardFields =
        {
            "grantItem",
            "grantInfo",
            "grantInfos",
            "consumeSelectedItem"
        };

        private static readonly string[] ObjectFields =
        {
            "deactivateTouchedObject",
            "activateObjects",
            "deactivateObjects"
        };

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            ResetAppendedRules(property);
            EditorGUI.BeginProperty(position, label, property);

            var row = TakeLine(ref position);
            property.isExpanded = EditorGUI.Foldout(
                row,
                property.isExpanded,
                BuildFoldoutLabel(property),
                true);

            if (property.isExpanded)
            {
                EditorGUI.indentLevel++;
                DrawComment(ref position, property);
                DrawSection(ref position, property, "Priority", PriorityFields);
                DrawSection(ref position, property, "Condition", ConditionFields);
                DrawSection(ref position, property, "Dialogue", DialogueFields);
                DrawSection(ref position, property, "Effect", EffectFields);
                DrawSection(ref position, property, "Reward", RewardFields);
                DrawSection(ref position, property, "Object", ObjectFields);
                EditorGUI.indentLevel--;
            }

            EditorGUI.EndProperty();
        }

        private static void ResetAppendedRules(SerializedProperty property)
        {
            int arrayMarker = property.propertyPath.LastIndexOf(".Array.data[", StringComparison.Ordinal);
            if (arrayMarker < 0)
            {
                return;
            }

            string arrayPath = property.propertyPath.Substring(0, arrayMarker);
            var array = property.serializedObject.FindProperty(arrayPath);
            if (array == null || !array.isArray)
            {
                return;
            }

            string key = BuildArrayKey(array);
            int currentSize = array.arraySize;
            if (!ArraySizes.TryGetValue(key, out int previousSize) || currentSize <= previousSize)
            {
                ArraySizes[key] = currentSize;
                return;
            }

            for (int i = previousSize; i < currentSize; i++)
            {
                ResetInteractionRule(array.GetArrayElementAtIndex(i));
            }

            ArraySizes[key] = currentSize;
            property.serializedObject.ApplyModifiedProperties();
        }

        private static void ResetInteractionRule(SerializedProperty property)
        {
            for (int i = 0; i < StringFields.Length; i++)
            {
                ResetString(property, StringFields[i]);
            }

            for (int i = 0; i < ArrayFields.Length; i++)
            {
                ClearArray(property, ArrayFields[i]);
            }

            for (int i = 0; i < BoolFields.Length; i++)
            {
                ResetBool(property, BoolFields[i]);
            }

            for (int i = 0; i < EnumFields.Length; i++)
            {
                ResetEnum(property, EnumFields[i]);
            }

            for (int i = 0; i < IntFields.Length; i++)
            {
                ResetInt(property, IntFields[i]);
            }

        }

        private static void ResetString(SerializedProperty property, string relativePath)
        {
            var child = property.FindPropertyRelative(relativePath);
            if (child != null)
            {
                child.stringValue = string.Empty;
            }
        }

        private static void ClearArray(SerializedProperty property, string relativePath)
        {
            var child = property.FindPropertyRelative(relativePath);
            if (child != null)
            {
                child.arraySize = 0;
            }
        }

        private static void ResetBool(SerializedProperty property, string relativePath)
        {
            var child = property.FindPropertyRelative(relativePath);
            if (child != null)
            {
                child.boolValue = false;
            }
        }

        private static void ResetEnum(SerializedProperty property, string relativePath)
        {
            var child = property.FindPropertyRelative(relativePath);
            if (child != null)
            {
                child.enumValueIndex = 0;
            }
        }

        private static void ResetInt(SerializedProperty property, string relativePath)
        {
            var child = property.FindPropertyRelative(relativePath);
            if (child != null)
            {
                child.intValue = 0;
            }
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded)
            {
                return height;
            }

            height += EditorGUIUtility.standardVerticalSpacing;
            height += GetCommentHeight(property);
            height += GetSectionHeight(property, "Priority", PriorityFields);
            height += GetSectionHeight(property, "Condition", ConditionFields);
            height += GetSectionHeight(property, "Dialogue", DialogueFields);
            height += GetSectionHeight(property, "Effect", EffectFields);
            height += GetSectionHeight(property, "Reward", RewardFields);
            height += GetSectionHeight(property, "Object", ObjectFields);
            return height;
        }

        private static void DrawSection(
            ref Rect position,
            SerializedProperty property,
            string title,
            string[] fieldNames)
        {
            var row = TakeLine(ref position);
            bool isExpanded = IsSectionExpanded(property, title);
            bool nextIsExpanded = EditorGUI.Foldout(row, isExpanded, title, true);
            if (nextIsExpanded != isExpanded)
            {
                ExpandedSections[BuildSectionKey(property, title)] = nextIsExpanded;
            }

            if (!nextIsExpanded)
            {
                DrawCollapsedSectionSummary(ref position, property, title);
                return;
            }

            EditorGUI.indentLevel++;
            for (int i = 0; i < fieldNames.Length; i++)
            {
                var child = property.FindPropertyRelative(fieldNames[i]);
                float height = EditorGUI.GetPropertyHeight(child, true);
                row = TakeHeight(ref position, height);
                EditorGUI.PropertyField(row, child, true);
            }
            EditorGUI.indentLevel--;
        }

        private static float GetSectionHeight(
            SerializedProperty property,
            string title,
            string[] fieldNames)
        {
            float height = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            if (!IsSectionExpanded(property, title))
            {
                string summary = BuildSectionSummary(property, title);
                if (!string.IsNullOrWhiteSpace(summary))
                {
                    height += GetSummaryHeight(summary) + EditorGUIUtility.standardVerticalSpacing;
                }

                return height;
            }

            for (int i = 0; i < fieldNames.Length; i++)
            {
                var child = property.FindPropertyRelative(fieldNames[i]);
                height += EditorGUI.GetPropertyHeight(child, true) +
                          EditorGUIUtility.standardVerticalSpacing;
            }

            return height;
        }

        private static bool IsSectionExpanded(SerializedProperty property, string title)
        {
            return ExpandedSections.TryGetValue(BuildSectionKey(property, title), out bool isExpanded) &&
                   isExpanded;
        }

        private static string BuildSectionKey(SerializedProperty property, string title)
        {
            UnityEngine.Object target = property.serializedObject.targetObject;
            int instanceId = target != null ? target.GetInstanceID() : 0;
            return $"{instanceId}:{property.propertyPath}:{title}";
        }

        private static string BuildArrayKey(SerializedProperty property)
        {
            UnityEngine.Object target = property.serializedObject.targetObject;
            int instanceId = target != null ? target.GetInstanceID() : 0;
            return $"{instanceId}:{property.propertyPath}";
        }

        // 접힌 interaction 라벨은 선택 아이템만 보여 리스트를 짧게 유지한다.
        private static GUIContent BuildFoldoutLabel(SerializedProperty property)
        {
            string selectedItemId = property.FindPropertyRelative("selectedItemId").stringValue;
            string itemLabel = string.IsNullOrWhiteSpace(selectedItemId) ? "(None)" : selectedItemId;
            return new GUIContent(itemLabel);
        }

        // 상호작용 규칙에 작업자가 남기는 메모를 그린다.
        private static void DrawComment(ref Rect position, SerializedProperty property)
        {
            var row = TakeLine(ref position);
            bool isExpanded = IsSectionExpanded(property, "Comment");
            string summary = BuildCommentSummary(property);
            bool nextIsExpanded = EditorGUI.Foldout(
                row,
                isExpanded,
                new GUIContent("Comment", summary),
                true);

            if (nextIsExpanded != isExpanded)
            {
                ExpandedSections[BuildSectionKey(property, "Comment")] = nextIsExpanded;
            }

            if (!nextIsExpanded)
            {
                DrawCollapsedCommentSummary(ref position, property);
                return;
            }

            var comment = property.FindPropertyRelative("comment");
            float height = EditorGUI.GetPropertyHeight(comment, new GUIContent("Comment"), true);
            row = TakeHeight(ref position, height);
            EditorGUI.PropertyField(row, comment, new GUIContent("Comment"), true);
        }

        // 코멘트 필드의 Inspector 높이를 계산한다.
        private static float GetCommentHeight(SerializedProperty property)
        {
            float height = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            if (!IsSectionExpanded(property, "Comment"))
            {
                height += GetSummaryHeight(BuildCommentSummary(property)) +
                    EditorGUIUtility.standardVerticalSpacing;
                return height;
            }

            var comment = property.FindPropertyRelative("comment");
            return height +
                EditorGUI.GetPropertyHeight(comment, new GUIContent("Comment"), true) +
                EditorGUIUtility.standardVerticalSpacing;
        }

        // 접힌 코멘트 아래에 코멘트 본문을 읽기 좋게 보여준다.
        private static void DrawCollapsedCommentSummary(ref Rect position, SerializedProperty property)
        {
            string summary = BuildCommentSummary(property);
            Rect row = TakeHeight(ref position, GetSummaryHeight(summary));
            row.xMin += EditorGUIUtility.singleLineHeight;
            EditorGUI.LabelField(row, summary, SummaryStyle);
        }

        // 비어 있는 코멘트도 접힌 상태에서 구분할 수 있게 표시한다.
        private static string BuildCommentSummary(SerializedProperty property)
        {
            string comment = property.FindPropertyRelative("comment").stringValue?.Trim();
            return string.IsNullOrWhiteSpace(comment) ? "(코멘트 없음)" : comment;
        }

        // 접힌 섹션 아래에 현재 입력된 값을 문장으로 보여준다.
        private static void DrawCollapsedSectionSummary(
            ref Rect position,
            SerializedProperty property,
            string title)
        {
            string summary = BuildSectionSummary(property, title);
            if (string.IsNullOrWhiteSpace(summary))
            {
                return;
            }

            Rect row = TakeHeight(ref position, GetSummaryHeight(summary));
            row.xMin += EditorGUIUtility.singleLineHeight;
            EditorGUI.LabelField(row, summary, SummaryStyle);
        }

        // 섹션 이름에 맞는 요약 문장을 만든다.
        private static string BuildSectionSummary(SerializedProperty property, string title)
        {
            return title switch
            {
                "Priority" => BuildPrioritySummary(property),
                "Condition" => BuildConditionSummary(property),
                "Dialogue" => BuildDialogueSummary(property, true),
                "Effect" => BuildEffectSummary(property, true),
                "Reward" => BuildRewardSummary(property, true),
                "Object" => BuildObjectSummary(property, true),
                _ => string.Empty
            };
        }

        // 우선순위 섹션의 레이어와 번호를 요약한다.
        private static string BuildPrioritySummary(SerializedProperty property)
        {
            var parts = new List<string>();
            AppendEnumPart(parts, "layer", property.FindPropertyRelative("priorityLayer"), "Default");
            AppendIntPart(parts, "number", property.FindPropertyRelative("priorityNumber"));
            return FormatParts(parts, true);
        }

        // 조건 섹션의 선택 아이템과 플래그 조건을 요약한다.
        private static string BuildConditionSummary(SerializedProperty property)
        {
            var parts = new List<string>();
            string selectedItemId = property.FindPropertyRelative("selectedItemId").stringValue;
            parts.Add($"item={FormatEmpty(selectedItemId)}");
            AppendObjectArrayPart(parts, "active", property.FindPropertyRelative("activeObjects"));
            AppendObjectArrayPart(parts, "inactive", property.FindPropertyRelative("inactiveObjects"));
            AppendStringArrayPart(parts, "hasItems", property.FindPropertyRelative("requiredItems"));
            AppendStringArrayPart(parts, "lacksItems", property.FindPropertyRelative("absentItems"));
            AppendStringArrayPart(parts, "hasInfos", property.FindPropertyRelative("requiredInfos"));
            AppendStringArrayPart(parts, "lacksInfos", property.FindPropertyRelative("absentInfos"));
            return string.Join("\n", parts);
        }

        // 보상, 토글, 콜백, 이동, 효과음을 요약한다.
        private static string BuildDialogueSummary(SerializedProperty property, bool includeEmpty)
        {
            var parts = new List<string>();
            AppendStringPart(parts, "preDialogue", property.FindPropertyRelative("preDialogueId"));
            AppendStringPart(parts, "actionDialogue", property.FindPropertyRelative("actionDialogueId"));
            AppendStringPart(parts, "postDialogue", property.FindPropertyRelative("postDialogueId"));
            return FormatParts(parts, includeEmpty);
        }

        private static string BuildEffectSummary(SerializedProperty property, bool includeEmpty)
        {
            var parts = new List<string>();
            AppendEnumPart(parts, "screenEffect", property.FindPropertyRelative("screenEffect"), "None");
            AppendEnumPart(parts, "special", property.FindPropertyRelative("specialAction"), "None");
            AppendEnumPart(parts, "move", property.FindPropertyRelative("transitionDestination"), "None");
            AppendEnumPart(parts, "sfx", property.FindPropertyRelative("touchSfx"), "Default");
            return FormatParts(parts, includeEmpty);
        }

        private static string BuildRewardSummary(SerializedProperty property, bool includeEmpty)
        {
            var parts = new List<string>();
            AppendStringPart(parts, "grantItem", property.FindPropertyRelative("grantItem"));
            AppendStringPart(parts, "grantInfo", property.FindPropertyRelative("grantInfo"));
            AppendStringArrayPart(parts, "grantInfos", property.FindPropertyRelative("grantInfos"));
            AppendBoolPart(parts, "consumeSelectedItem", property.FindPropertyRelative("consumeSelectedItem"));
            return FormatParts(parts, includeEmpty);
        }

        private static string BuildObjectSummary(SerializedProperty property, bool includeEmpty)
        {
            var parts = new List<string>();
            AppendBoolPart(parts, "deactivateTouchedObject", property.FindPropertyRelative("deactivateTouchedObject"));
            AppendObjectArrayPart(parts, "activate", property.FindPropertyRelative("activateObjects"));
            AppendObjectArrayPart(parts, "deactivate", property.FindPropertyRelative("deactivateObjects"));
            return FormatParts(parts, includeEmpty);
        }

        // 문자열 필드가 비어 있지 않으면 요약에 추가한다.
        private static void AppendStringPart(List<string> parts, string label, SerializedProperty property)
        {
            if (property != null && !string.IsNullOrWhiteSpace(property.stringValue))
            {
                parts.Add($"{label}={property.stringValue}");
            }
        }

        // 문자열 배열 필드가 비어 있지 않으면 쉼표로 묶어 요약에 추가한다.
        private static void AppendStringArrayPart(List<string> parts, string label, SerializedProperty property)
        {
            var values = new List<string>();
            if (property != null)
            {
                for (int i = 0; i < property.arraySize; i++)
                {
                    string value = property.GetArrayElementAtIndex(i).stringValue;
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        values.Add(value);
                    }
                }
            }

            if (values.Count > 0)
            {
                parts.Add($"{label}=[{string.Join(", ", values)}]");
            }
        }

        // enum 배열 필드가 비어 있지 않으면 이름 목록으로 요약한다.
        private static void AppendEnumArrayPart(List<string> parts, string label, SerializedProperty property)
        {
            var values = new List<string>();
            if (property != null)
            {
                for (int i = 0; i < property.arraySize; i++)
                {
                    var element = property.GetArrayElementAtIndex(i);
                    if (element.propertyType == SerializedPropertyType.Enum)
                    {
                        values.Add(element.enumDisplayNames[element.enumValueIndex]);
                    }
                }
            }

            if (values.Count > 0)
            {
                parts.Add($"{label}=[{string.Join(", ", values)}]");
            }
        }

        // bool 필드가 켜져 있으면 요약에 추가한다.
        private static void AppendBoolPart(List<string> parts, string label, SerializedProperty property)
        {
            if (property != null && property.boolValue)
            {
                parts.Add($"{label}=true");
            }
        }

        // 정수 필드 값을 요약에 추가한다.
        private static void AppendIntPart(List<string> parts, string label, SerializedProperty property)
        {
            if (property != null)
            {
                parts.Add($"{label}={property.intValue}");
            }
        }

        // 오브젝트 배열 필드가 비어 있지 않으면 이름 목록으로 요약한다.
        private static void AppendObjectArrayPart(List<string> parts, string label, SerializedProperty property)
        {
            var values = new List<string>();
            if (property != null)
            {
                for (int i = 0; i < property.arraySize; i++)
                {
                    var element = property.GetArrayElementAtIndex(i);
                    UnityEngine.Object reference = element.objectReferenceValue;
                    values.Add(reference != null ? reference.name : "(Missing)");
                }
            }

            if (values.Count > 0)
            {
                parts.Add($"{label}=[{string.Join(", ", values)}]");
            }
        }

        // 기본값이 아닌 enum 입력만 요약에 추가한다.
        private static void AppendEnumPart(
            List<string> parts,
            string label,
            SerializedProperty property,
            string defaultName)
        {
            if (property == null)
            {
                return;
            }

            string value = property.enumDisplayNames[property.enumValueIndex];
            if (!string.Equals(value, defaultName, StringComparison.Ordinal))
            {
                parts.Add($"{label}={value}");
            }
        }

        // 비어 있는 섹션도 접힌 상태에서 구분할 수 있게 표시한다.
        private static string FormatParts(List<string> parts, bool includeEmpty)
        {
            if (parts.Count > 0)
            {
                return string.Join("\n", parts);
            }

            return includeEmpty ? "(empty)" : string.Empty;
        }

        // 빈 문자열 필드를 Inspector 요약용 텍스트로 바꾼다.
        private static string FormatEmpty(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "(None)" : value;
        }

        // 접힌 섹션 요약 문장의 높이를 계산한다.
        private static float GetSummaryHeight(string summary)
        {
            float width = Mathf.Max(120f, EditorGUIUtility.currentViewWidth - 72f);
            return Mathf.Max(
                EditorGUIUtility.singleLineHeight,
                SummaryStyle.CalcHeight(new GUIContent(summary), width));
        }

        private static Rect TakeLine(ref Rect position)
        {
            return TakeHeight(ref position, EditorGUIUtility.singleLineHeight);
        }

        private static Rect TakeHeight(ref Rect position, float height)
        {
            var row = new Rect(position.x, position.y, position.width, height);
            position.y += height + EditorGUIUtility.standardVerticalSpacing;
            return row;
        }
    }
}
