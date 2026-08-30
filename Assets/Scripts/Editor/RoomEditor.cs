using Escape.Rooms;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Escape.EditorTools
{
    // Room 인스펙터에서 입장 애니메이션 후보 배열의 추가 동작을 제어한다.
    [CustomEditor(typeof(Room))]
    public sealed class RoomEditor : UnityEditor.Editor
    {
        private SerializedProperty entranceAnimationCandidatesProperty;
        private ReorderableList entranceAnimationCandidatesList;

        private void OnEnable()
        {
            entranceAnimationCandidatesProperty = serializedObject.FindProperty("entranceAnimationCandidates");
            if (entranceAnimationCandidatesProperty == null)
            {
                return;
            }

            entranceAnimationCandidatesList = new ReorderableList(
                serializedObject,
                entranceAnimationCandidatesProperty,
                true,
                true,
                true,
                true)
            {
                drawHeaderCallback = DrawEntranceAnimationCandidatesHeader,
                drawElementCallback = DrawEntranceAnimationCandidateElement,
                elementHeightCallback = GetEntranceAnimationCandidateElementHeight,
                onAddCallback = AddEmptyEntranceAnimationCandidate,
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script", "entranceAnimationCandidates");
            entranceAnimationCandidatesList?.DoLayoutList();
            serializedObject.ApplyModifiedProperties();
        }

        // 입장 애니메이션 후보 목록 제목을 그린다.
        private static void DrawEntranceAnimationCandidatesHeader(Rect rect)
        {
            EditorGUI.LabelField(rect, "Entrance Animation Candidates");
        }

        // 기존 후보 PropertyDrawer를 사용해 후보 한 칸을 그린다.
        private void DrawEntranceAnimationCandidateElement(
            Rect rect,
            int index,
            bool isActive,
            bool isFocused)
        {
            SerializedProperty element = entranceAnimationCandidatesList?.serializedProperty?.GetArrayElementAtIndex(index);
            if (element == null)
            {
                return;
            }

            rect.y += 2f;
            rect.height = EditorGUI.GetPropertyHeight(element, true);
            EditorGUI.PropertyField(rect, element, GUIContent.none, true);
        }

        // 후보 drawer 높이에 여백을 더해 목록 행 높이를 맞춘다.
        private float GetEntranceAnimationCandidateElementHeight(int index)
        {
            SerializedProperty element = entranceAnimationCandidatesList?.serializedProperty?.GetArrayElementAtIndex(index);
            return element == null
                ? EditorGUIUtility.singleLineHeight
                : EditorGUI.GetPropertyHeight(element, true) + 4f;
        }

        // Unity 기본 추가가 마지막 후보를 복제하므로 새 칸을 빈 값으로 초기화한다.
        private static void AddEmptyEntranceAnimationCandidate(ReorderableList list)
        {
            SerializedProperty candidates = list.serializedProperty;
            int nextIndex = candidates.arraySize;
            candidates.arraySize++;

            SerializedProperty element = candidates.GetArrayElementAtIndex(nextIndex);
            ClearEntranceAnimationCandidate(element);
        }

        // 후보 참조와 애니메이션 이름을 빈 값으로 되돌린다.
        private static void ClearEntranceAnimationCandidate(SerializedProperty element)
        {
            element.FindPropertyRelative("asepriteRoomAnimator").objectReferenceValue = null;
            element.FindPropertyRelative("animationName").stringValue = string.Empty;
        }
    }
}
