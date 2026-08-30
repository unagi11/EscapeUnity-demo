using Escape.Rooms;
using UnityEditor;
using UnityEngine;

namespace Escape.EditorTools
{
    // AsepriteRoomAnimator의 정적 애니메이션 선택을 태그 이름 드롭다운으로 제공한다.
    [CustomEditor(typeof(AsepriteRoomAnimator))]
    [CanEditMultipleObjects]
    public sealed class AsepriteRoomAnimatorEditor : UnityEditor.Editor
    {
        private SerializedProperty targetRendererProperty;
        private SerializedProperty selectedAnimationNameProperty;
        private SerializedProperty selectedPlaybackModeProperty;
        private SerializedProperty animationsProperty;

        private void OnEnable()
        {
            targetRendererProperty = serializedObject.FindProperty("targetRenderer");
            selectedAnimationNameProperty = serializedObject.FindProperty("selectedAnimationName");
            selectedPlaybackModeProperty = serializedObject.FindProperty("selectedPlaybackMode");
            animationsProperty = serializedObject.FindProperty("animations");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.PropertyField(targetRendererProperty);
            DrawStaticAnimationControls();
            EditorGUILayout.Space(4f);
            EditorGUILayout.PropertyField(animationsProperty, true);

            bool changed = serializedObject.ApplyModifiedProperties();
            if (changed)
            {
                ApplySelectedStateToTargets();
            }

            using (new EditorGUI.DisabledScope(targets.Length == 0))
            {
                if (GUILayout.Button("Apply Static Animation"))
                {
                    ApplySelectedStateToTargets();
                }
            }
        }

        private void DrawStaticAnimationControls()
        {
            string[] names = BuildAnimationNameOptions();
            string currentName = selectedAnimationNameProperty.stringValue;
            int currentIndex = 0;
            for (int i = 1; i < names.Length; i++)
            {
                if (string.Equals(names[i], currentName, System.StringComparison.Ordinal))
                {
                    currentIndex = i;
                    break;
                }
            }

            int nextIndex = EditorGUILayout.Popup("Static Animation", currentIndex, names);
            selectedAnimationNameProperty.stringValue = nextIndex <= 0 ? string.Empty : names[nextIndex];
            EditorGUILayout.PropertyField(selectedPlaybackModeProperty, new GUIContent("Static Playback"));
        }

        private string[] BuildAnimationNameOptions()
        {
            if (animationsProperty == null || !animationsProperty.isArray || animationsProperty.arraySize == 0)
            {
                return new[] { "None" };
            }

            var names = new System.Collections.Generic.List<string> { "None" };
            for (int i = 0; i < animationsProperty.arraySize; i++)
            {
                SerializedProperty animation = animationsProperty.GetArrayElementAtIndex(i);
                SerializedProperty nameProperty = animation.FindPropertyRelative("animationName");
                string animationName = nameProperty != null ? nameProperty.stringValue : string.Empty;
                if (!string.IsNullOrWhiteSpace(animationName) && !names.Contains(animationName))
                {
                    names.Add(animationName);
                }
            }

            return names.ToArray();
        }

        private void ApplySelectedStateToTargets()
        {
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] is AsepriteRoomAnimator animator)
                {
                    animator.ApplySelectedAnimationState();
                    EditorUtility.SetDirty(animator);
                }
            }
        }
    }
}
