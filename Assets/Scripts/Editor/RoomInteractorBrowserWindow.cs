using System;
using System.Collections.Generic;
using Escape.Rooms;
using UnityEditor;
using UnityEngine;

namespace Escape.EditorTools
{
    // 현재 씬에 등록된 RoomInteractor를 검색, 정렬, 수정, 추가하는 에디터 창.
    public sealed class RoomInteractorBrowserWindow : EditorWindow
    {
        private enum ActiveFilter
        {
            All,
            ActiveOnly,
            InactiveOnly
        }

        private enum RuleFilterMode
        {
            All,
            EmptyRules,
            HasDialogue,
            HasGrant,
            HasTransition,
            HasSpecialAction,
            HasObjectToggle
        }

        private enum SortMode
        {
            Hierarchy,
            Priority,
            Room,
            Name,
            RuleCount,
            SpecialAction,
            Dialogue
        }

        private readonly List<InteractorRow> rows = new();
        private readonly List<InteractorRow> filteredRows = new();
        private Vector2 listScroll;
        private Vector2 inspectorScroll;
        private string searchText = string.Empty;
        private bool useRoomFilter;
        private RoomType roomFilter = RoomType.None;
        private ActiveFilter activeFilter = ActiveFilter.All;
        private RuleFilterMode ruleFilter = RuleFilterMode.All;
        private SortMode sortMode = SortMode.Priority;
        private bool descending = true;
        private bool autoRefresh = true;
        private RoomInteractor selectedInteractor;
        private UnityEditor.Editor selectedEditor;

        [MenuItem("Tools/Escape/Rooms/Room Interactor Browser")]
        // RoomInteractor 관리 창을 연다.
        private static void OpenWindow()
        {
            var window = GetWindow<RoomInteractorBrowserWindow>("Room Interactors");
            window.minSize = new Vector2(920f, 520f);
            window.RefreshRows();
        }

        // 창이 활성화될 때 현재 선택 오브젝트를 추가 대상으로 반영한다.
        private void OnEnable()
        {
            EditorApplication.hierarchyChanged += HandleHierarchyChanged;
            RefreshRows();
        }

        // 캐시된 인스펙터 에디터를 정리한다.
        private void OnDisable()
        {
            EditorApplication.hierarchyChanged -= HandleHierarchyChanged;
            DestroySelectedEditor();
        }

        // 선택 변경 시 목록 선택과 추가 대상을 갱신한다.
        private void OnSelectionChange()
        {
            if (Selection.activeGameObject != null &&
                Selection.activeGameObject.TryGetComponent(out RoomInteractor interactor))
            {
                SetSelectedInteractor(interactor, false);
            }

            Repaint();
        }

        // 에디터 UI를 그린다.
        private void OnGUI()
        {
            DrawToolbar();
            RefreshFilteredRows();

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawInteractorList();
                DrawInspectorPanel();
            }
        }

        // 검색, 필터, 정렬 컨트롤을 그린다.
        private void DrawToolbar()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    searchText = EditorGUILayout.TextField("Search", searchText);
                    autoRefresh = EditorGUILayout.ToggleLeft("Auto", autoRefresh, GUILayout.Width(56f));
                    if (GUILayout.Button("Refresh", GUILayout.Width(86f)))
                    {
                        RefreshRows();
                    }
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    activeFilter = (ActiveFilter)EditorGUILayout.EnumPopup("Active", activeFilter);
                    ruleFilter = (RuleFilterMode)EditorGUILayout.EnumPopup("Rule", ruleFilter);
                    sortMode = (SortMode)EditorGUILayout.EnumPopup("Sort", sortMode);
                    descending = EditorGUILayout.ToggleLeft("Desc", descending, GUILayout.Width(58f));
                    useRoomFilter = EditorGUILayout.ToggleLeft("Room", useRoomFilter, GUILayout.Width(58f));
                    using (new EditorGUI.DisabledScope(!useRoomFilter))
                    {
                        roomFilter = (RoomType)EditorGUILayout.EnumPopup(roomFilter);
                    }
                }

                EditorGUILayout.LabelField(
                    $"Showing {filteredRows.Count} / {rows.Count}",
                    EditorStyles.miniLabel);
            }
        }

        // RoomInteractor 목록을 그린다.
        private void DrawInteractorList()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(Mathf.Max(420f, position.width * 0.48f))))
            {
                EditorGUILayout.LabelField("Registered Interactors", EditorStyles.boldLabel);
                listScroll = EditorGUILayout.BeginScrollView(listScroll);
                for (int i = 0; i < filteredRows.Count; i++)
                {
                    DrawInteractorRow(filteredRows[i]);
                }

                EditorGUILayout.EndScrollView();
            }
        }

        // 목록의 한 행을 그린다.
        private void DrawInteractorRow(InteractorRow row)
        {
            bool isSelected = selectedInteractor == row.Interactor;
            GUIStyle rowStyle = isSelected ? "MeTransitionSelectHead" : EditorStyles.helpBox;
            Rect rowRect = EditorGUILayout.BeginVertical(rowStyle);
            Rect focusButtonRect = Rect.zero;
            {
                EditorGUIUtility.AddCursorRect(rowRect, MouseCursor.Link);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Label(row.Name, EditorStyles.boldLabel, GUILayout.MinWidth(120f));
                    GUILayout.Label(row.RoomLabel, GUILayout.Width(92f));
                    GUILayout.Label(row.ActiveLabel, GUILayout.Width(64f));
                    GUILayout.Label($"Rules {row.RuleCount}", GUILayout.Width(64f));
                    GUILayout.Label($"P {row.PriorityLabel}", GUILayout.Width(116f));

                    if (GUILayout.Button("Focus", GUILayout.Width(56f)))
                    {
                        FocusInteractor(row.Interactor);
                    }

                    focusButtonRect = GUILayoutUtility.GetLastRect();
                }

                EditorGUILayout.LabelField(row.HierarchyPath, EditorStyles.miniLabel);
                EditorGUILayout.LabelField(row.Summary, EditorStyles.wordWrappedMiniLabel);
            }

            EditorGUILayout.EndVertical();

            if (Event.current.type == EventType.MouseDown &&
                rowRect.Contains(Event.current.mousePosition) &&
                !focusButtonRect.Contains(Event.current.mousePosition))
            {
                SetSelectedInteractor(row.Interactor, false);
                Event.current.Use();
            }
        }

        // 선택된 RoomInteractor의 수정 패널과 추가 패널을 그린다.
        private void DrawInspectorPanel()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                EditorGUILayout.LabelField("Edit Selected", EditorStyles.boldLabel);

                if (selectedInteractor == null)
                {
                    EditorGUILayout.HelpBox("Select a RoomInteractor from the list.", MessageType.Info);
                    return;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.ObjectField("Target", selectedInteractor, typeof(RoomInteractor), true);
                    if (GUILayout.Button("Focus", GUILayout.Width(64f)))
                    {
                        FocusInteractor(selectedInteractor);
                    }
                }

                inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll);
                DrawSelectedInspector();
                EditorGUILayout.EndScrollView();
            }
        }

        // 선택된 컴포넌트의 기본 인스펙터를 그린다.
        private void DrawSelectedInspector()
        {
            if (selectedEditor == null || selectedEditor.target != selectedInteractor)
            {
                DestroySelectedEditor();
                UnityEditor.Editor.CreateCachedEditor(selectedInteractor, null, ref selectedEditor);
            }

            if (selectedEditor != null)
            {
                selectedEditor.OnInspectorGUI();
            }
        }

        // 현재 로드된 씬의 RoomInteractor를 다시 수집한다.
        private void RefreshRows()
        {
            rows.Clear();
            RoomInteractor[] interactors = UnityEngine.Object.FindObjectsByType<RoomInteractor>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);

            for (int i = 0; i < interactors.Length; i++)
            {
                RoomInteractor interactor = interactors[i];
                if (interactor == null ||
                    !interactor.gameObject.scene.IsValid() ||
                    EditorUtility.IsPersistent(interactor))
                {
                    continue;
                }

                rows.Add(new InteractorRow(interactor));
            }

            RefreshFilteredRows();
        }

        // Hierarchy 변경 시 목록을 자동 갱신한다.
        private void HandleHierarchyChanged()
        {
            if (!autoRefresh)
            {
                return;
            }

            RefreshRows();
            Repaint();
        }

        // 현재 검색, 필터, 정렬 상태를 목록에 적용한다.
        private void RefreshFilteredRows()
        {
            if (autoRefresh && Event.current != null && Event.current.type == EventType.Layout)
            {
                rows.RemoveAll(row => row.Interactor == null);
            }

            filteredRows.Clear();
            for (int i = 0; i < rows.Count; i++)
            {
                InteractorRow row = rows[i];
                row.Refresh();
                if (MatchesFilters(row))
                {
                    filteredRows.Add(row);
                }
            }

            filteredRows.Sort(CompareRows);
        }

        // 행이 현재 필터 조건에 맞는지 반환한다.
        private bool MatchesFilters(InteractorRow row)
        {
            if (useRoomFilter && row.RoomId != roomFilter)
            {
                return false;
            }

            if (activeFilter == ActiveFilter.ActiveOnly && !row.IsActiveInHierarchy)
            {
                return false;
            }

            if (activeFilter == ActiveFilter.InactiveOnly && row.IsActiveInHierarchy)
            {
                return false;
            }

            if (!MatchesRuleFilter(row))
            {
                return false;
            }

            return string.IsNullOrWhiteSpace(searchText) ||
                row.SearchText.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // 행이 현재 규칙 필터에 맞는지 반환한다.
        private bool MatchesRuleFilter(InteractorRow row)
        {
            return ruleFilter switch
            {
                RuleFilterMode.EmptyRules => row.RuleCount == 0,
                RuleFilterMode.HasDialogue => row.HasDialogue,
                RuleFilterMode.HasGrant => row.HasGrant,
                RuleFilterMode.HasTransition => row.HasTransition,
                RuleFilterMode.HasSpecialAction => row.HasSpecialAction,
                RuleFilterMode.HasObjectToggle => row.HasObjectToggle,
                _ => true
            };
        }

        // 현재 정렬 상태로 두 행을 비교한다.
        private int CompareRows(InteractorRow a, InteractorRow b)
        {
            int compare = sortMode switch
            {
                SortMode.Priority => ComparePriority(a, b),
                SortMode.Room => CompareRoom(a, b),
                SortMode.Name => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
                SortMode.RuleCount => a.RuleCount.CompareTo(b.RuleCount),
                SortMode.SpecialAction => string.Compare(
                    a.FirstSpecialAction,
                    b.FirstSpecialAction,
                    StringComparison.OrdinalIgnoreCase),
                SortMode.Dialogue => string.Compare(
                    a.FirstDialogueId,
                    b.FirstDialogueId,
                    StringComparison.OrdinalIgnoreCase),
                _ => string.Compare(a.HierarchyPath, b.HierarchyPath, StringComparison.OrdinalIgnoreCase)
            };

            if (compare == 0)
            {
                compare = string.Compare(a.HierarchyPath, b.HierarchyPath, StringComparison.OrdinalIgnoreCase);
            }

            return descending ? -compare : compare;
        }

        // 방 기준 정렬 값을 비교한다.
        private static int CompareRoom(InteractorRow a, InteractorRow b)
        {
            int compare = a.RoomId.CompareTo(b.RoomId);
            if (compare != 0)
            {
                return compare;
            }

            return string.Compare(a.HierarchyPath, b.HierarchyPath, StringComparison.OrdinalIgnoreCase);
        }

        // 목록 선택 상태를 변경한다.
        private static int ComparePriority(InteractorRow a, InteractorRow b)
        {
            int compare = a.PriorityLayerValue.CompareTo(b.PriorityLayerValue);
            if (compare != 0)
            {
                return compare;
            }

            compare = a.PriorityNumber.CompareTo(b.PriorityNumber);
            if (compare != 0)
            {
                return compare;
            }

            return b.PriorityRuleOrder.CompareTo(a.PriorityRuleOrder);
        }

        private void SetSelectedInteractor(RoomInteractor interactor, bool focus)
        {
            if (selectedInteractor != interactor)
            {
                selectedInteractor = interactor;
                DestroySelectedEditor();
            }

            if (focus)
            {
                FocusInteractor(interactor);
            }
            else if (interactor != null)
            {
                Selection.activeGameObject = interactor.gameObject;
            }

            Repaint();
        }

        // Scene 뷰와 Hierarchy에서 RoomInteractor를 포커싱한다.
        private static void FocusInteractor(RoomInteractor interactor)
        {
            if (interactor == null)
            {
                return;
            }

            Selection.activeGameObject = interactor.gameObject;
            EditorGUIUtility.PingObject(interactor.gameObject);
            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.FrameSelected();
                SceneView.lastActiveSceneView.Focus();
            }
        }

        // 캐시된 인스펙터 에디터를 제거한다.
        private void DestroySelectedEditor()
        {
            if (selectedEditor != null)
            {
                DestroyImmediate(selectedEditor);
                selectedEditor = null;
            }
        }

        private sealed class InteractorRow
        {
            public readonly RoomInteractor Interactor;
            public string Name { get; private set; }
            public string HierarchyPath { get; private set; }
            public string RoomLabel { get; private set; }
            public RoomType RoomId { get; private set; }
            public bool IsActiveInHierarchy { get; private set; }
            public string ActiveLabel { get; private set; }
            public int RuleCount { get; private set; }
            public bool HasDialogue { get; private set; }
            public bool HasGrant { get; private set; }
            public bool HasTransition { get; private set; }
            public bool HasSpecialAction { get; private set; }
            public bool HasObjectToggle { get; private set; }
            public string FirstSpecialAction { get; private set; }
            public string FirstDialogueId { get; private set; }
            public string PriorityLabel { get; private set; }
            public int PriorityLayerValue { get; private set; }
            public int PriorityNumber { get; private set; }
            public int PriorityRuleOrder { get; private set; }
            public string Summary { get; private set; }
            public string SearchText { get; private set; }

            // RoomInteractor 한 개의 목록 표시 정보를 만든다.
            public InteractorRow(RoomInteractor interactor)
            {
                Interactor = interactor;
                Refresh();
            }

            // 현재 씬/직렬화 상태를 목록 정보로 다시 읽는다.
            public void Refresh()
            {
                if (Interactor == null)
                {
                    return;
                }

                Name = Interactor.name;
                HierarchyPath = BuildHierarchyPath(Interactor.transform);
                Room room = Interactor.GetComponentInParent<Room>(true);
                RoomId = room != null ? room.RoomId : RoomType.None;
                RoomLabel = room != null ? room.RoomId.ToString() : "(No Room)";
                IsActiveInHierarchy = Interactor.gameObject.activeInHierarchy;
                ActiveLabel = IsActiveInHierarchy ? "Active" : "Inactive";

                InteractionRule[] rules = Interactor.ItemInteractions ?? Array.Empty<InteractionRule>();
                RuleCount = rules.Length;
                ResetRuleFlags();

                var summaries = new List<string>();
                for (int i = 0; i < rules.Length; i++)
                {
                    InteractionRule rule = rules[i];
                    if (rule == null)
                    {
                        continue;
                    }

                    string summary = BuildRuleSummary(i, rule);
                    if (!string.IsNullOrWhiteSpace(summary))
                    {
                        summaries.Add(summary);
                    }

                    AccumulateRuleFlags(rule);
                    AccumulateRulePriority(rule, i);
                }

                Summary = summaries.Count == 0 ? "(no rules)" : string.Join(" | ", summaries);
                SearchText = string.Join(
                    "\n",
                    Name,
                    HierarchyPath,
                    RoomLabel,
                    Summary,
                    PriorityLabel,
                    FirstSpecialAction,
                    FirstDialogueId);
            }

            // 규칙 관련 표시 플래그를 초기화한다.
            private void ResetRuleFlags()
            {
                HasDialogue = false;
                HasGrant = false;
                HasTransition = false;
                HasSpecialAction = false;
                HasObjectToggle = false;
                FirstSpecialAction = string.Empty;
                FirstDialogueId = string.Empty;
                PriorityLabel = "-";
                PriorityLayerValue = int.MinValue;
                PriorityNumber = int.MinValue;
                PriorityRuleOrder = int.MaxValue;
            }

            // 규칙 하나의 필터용 상태를 누적한다.
            private void AccumulateRulePriority(InteractionRule rule, int ruleOrder)
            {
                InteractionPriorityLayer layer = rule.GetEffectivePriorityLayer(InteractionPriorityLayer.Object);
                int layerValue = (int)layer;
                int number = rule.PriorityNumber;
                if (layerValue < PriorityLayerValue)
                {
                    return;
                }

                if (layerValue == PriorityLayerValue)
                {
                    if (number < PriorityNumber)
                    {
                        return;
                    }

                    if (number == PriorityNumber && ruleOrder >= PriorityRuleOrder)
                    {
                        return;
                    }
                }

                PriorityLayerValue = layerValue;
                PriorityNumber = number;
                PriorityRuleOrder = ruleOrder;
                PriorityLabel = $"{layer}/{number}";
            }

            private void AccumulateRuleFlags(InteractionRule rule)
            {
                bool hasDialogue = !string.IsNullOrWhiteSpace(rule.PreDialogueId) ||
                    !string.IsNullOrWhiteSpace(rule.ActionDialogueId) ||
                    !string.IsNullOrWhiteSpace(rule.PostDialogueId);
                HasDialogue |= hasDialogue;
                HasGrant |= !string.IsNullOrWhiteSpace(rule.GrantItem) ||
                    !string.IsNullOrWhiteSpace(rule.GrantInfo) ||
                    HasAny(rule.GrantInfos);
                HasTransition |= rule.TransitionDestination != RoomType.None;
                HasSpecialAction |= rule.SpecialAction != InteractionSpecialAction.None;
                HasObjectToggle |= rule.DeactivateTouchedObject ||
                    HasAny(rule.ActiveObjects) ||
                    HasAny(rule.InactiveObjects) ||
                    HasAny(rule.ActivateObjects) ||
                    HasAny(rule.DeactivateObjects);

                if (string.IsNullOrWhiteSpace(FirstSpecialAction) &&
                    rule.SpecialAction != InteractionSpecialAction.None)
                {
                    FirstSpecialAction = rule.SpecialAction.ToString();
                }

                if (string.IsNullOrWhiteSpace(FirstDialogueId))
                {
                    FirstDialogueId = FirstNonEmpty(
                        rule.PreDialogueId,
                        rule.ActionDialogueId,
                        rule.PostDialogueId);
                }
            }

            // 계층 경로를 문자열로 만든다.
            private static string BuildHierarchyPath(Transform transform)
            {
                if (transform == null)
                {
                    return string.Empty;
                }

                var names = new Stack<string>();
                Transform current = transform;
                while (current != null)
                {
                    names.Push(current.name);
                    current = current.parent;
                }

                return string.Join("/", names);
            }

            // 규칙 한 개를 한 줄 요약으로 만든다.
            private static string BuildRuleSummary(int index, InteractionRule rule)
            {
                var parts = new List<string>();
                InteractionPriorityLayer layer = rule.GetEffectivePriorityLayer(InteractionPriorityLayer.Object);
                parts.Add($"prio:{layer}/{rule.PriorityNumber}");

                if (!string.IsNullOrWhiteSpace(rule.SelectedItemId))
                {
                    parts.Add($"item:{rule.SelectedItemId}");
                }

                string dialogue = FirstNonEmpty(
                    rule.PreDialogueId,
                    rule.ActionDialogueId,
                    rule.PostDialogueId);
                if (!string.IsNullOrWhiteSpace(dialogue))
                {
                    parts.Add($"dlg:{dialogue}");
                }

                if (!string.IsNullOrWhiteSpace(rule.GrantItem))
                {
                    parts.Add($"grantItem:{rule.GrantItem}");
                }

                if (!string.IsNullOrWhiteSpace(rule.GrantInfo))
                {
                    parts.Add($"grantInfo:{rule.GrantInfo}");
                }

                if (HasAny(rule.GrantInfos))
                {
                    parts.Add($"grantInfos:{JoinNonEmpty(rule.GrantInfos)}");
                }

                if (rule.SpecialAction != InteractionSpecialAction.None)
                {
                    parts.Add($"special:{rule.SpecialAction}");
                }

                if (rule.TransitionDestination != RoomType.None)
                {
                    parts.Add($"move:{rule.TransitionDestination}");
                }

                if (!string.IsNullOrWhiteSpace(rule.Comment))
                {
                    parts.Add(rule.Comment);
                }

                string body = parts.Count == 0 ? "empty" : string.Join(", ", parts);
                return $"#{index + 1} {body}";
            }

            // 배열에 유효한 값이 하나라도 있는지 확인한다.
            private static bool HasAny<T>(T[] values)
            {
                if (values == null)
                {
                    return false;
                }

                for (int i = 0; i < values.Length; i++)
                {
                    T value = values[i];
                    if (value is string text)
                    {
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            return true;
                        }
                    }
                    else if (value is UnityEngine.Object unityObject)
                    {
                        if (unityObject != null)
                        {
                            return true;
                        }
                    }
                    else if (value != null)
                    {
                        return true;
                    }
                }

                return false;
            }

            // 문자열 배열 요약에서 빈 값은 제외한다.
            private static string JoinNonEmpty(string[] values)
            {
                if (values == null)
                {
                    return string.Empty;
                }

                var nonEmptyValues = new List<string>();
                for (int i = 0; i < values.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(values[i]))
                    {
                        nonEmptyValues.Add(values[i]);
                    }
                }

                return string.Join("|", nonEmptyValues);
            }

            // 주어진 문자열 중 첫 번째 유효한 값을 반환한다.
            private static string FirstNonEmpty(params string[] values)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(values[i]))
                    {
                        return values[i];
                    }
                }

                return string.Empty;
            }
        }
    }
}
