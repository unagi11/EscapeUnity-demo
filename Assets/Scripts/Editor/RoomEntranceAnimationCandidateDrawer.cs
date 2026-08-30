using System;
using System.Collections.Generic;
using Escape.Rooms;
using UnityEditor;
using UnityEngine;

namespace Escape.EditorTools
{
    // Room 입장 연출 후보에서 Aseprite 애니메이션 이름을 드롭다운으로 선택하게 한다.
    [CustomPropertyDrawer(typeof(Room.EntranceAnimationCandidate))]
    public sealed class RoomEntranceAnimationCandidateDrawer : PropertyDrawer
    {
        private const string EmptyLabel = "None";
        private const float LineSpacing = 2f;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return (EditorGUIUtility.singleLineHeight * 2f) + LineSpacing;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            Rect animatorRect = new(
                position.x,
                position.y,
                position.width,
                EditorGUIUtility.singleLineHeight);
            Rect animationRect = new(
                position.x,
                animatorRect.yMax + LineSpacing,
                position.width,
                EditorGUIUtility.singleLineHeight);

            SerializedProperty animatorProperty = property.FindPropertyRelative("asepriteRoomAnimator");
            SerializedProperty animationNameProperty = property.FindPropertyRelative("animationName");

            EditorGUI.PropertyField(animatorRect, animatorProperty, new GUIContent("Aseprite Room Animator"));
            DrawAnimationNamePopup(animationRect, animatorProperty, animationNameProperty);

            EditorGUI.EndProperty();
        }

        // 선택한 AsepriteRoomAnimator에 등록된 애니메이션 태그 이름을 드롭다운으로 그린다.
        private static void DrawAnimationNamePopup(
            Rect position,
            SerializedProperty animatorProperty,
            SerializedProperty animationNameProperty)
        {
            string currentName = animationNameProperty.stringValue;
            string[] names = BuildAnimationNameOptions(animatorProperty.objectReferenceValue as AsepriteRoomAnimator, currentName);
            int currentIndex = FindIndex(names, currentName);
            int nextIndex = EditorGUI.Popup(position, "Animation Name", currentIndex, names);
            animationNameProperty.stringValue = nextIndex <= 0 ? string.Empty : names[nextIndex];
        }

        // 애니메이터의 serialized animations 배열에서 선택 가능한 이름 목록을 만든다.
        private static string[] BuildAnimationNameOptions(AsepriteRoomAnimator animator, string currentName)
        {
            var names = new List<string> { EmptyLabel };
            if (animator != null)
            {
                var serializedAnimator = new SerializedObject(animator);
                SerializedProperty animationsProperty = serializedAnimator.FindProperty("animations");
                if (animationsProperty != null && animationsProperty.isArray)
                {
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
                }
            }

            if (!string.IsNullOrWhiteSpace(currentName) && !names.Contains(currentName))
            {
                names.Add(currentName);
            }

            return names.ToArray();
        }

        private static int FindIndex(IReadOnlyList<string> names, string currentName)
        {
            for (int i = 1; i < names.Count; i++)
            {
                if (string.Equals(names[i], currentName, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return 0;
        }
    }
}
