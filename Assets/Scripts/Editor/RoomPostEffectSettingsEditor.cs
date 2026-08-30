using Escape.Rooms;
using UnityEditor;
using UnityEngine;

namespace Escape.Editor
{
    [CustomEditor(typeof(RoomPostEffectSettings))]
    public sealed class RoomPostEffectSettingsEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            if (EditorGUI.EndChangeCheck())
            {
                ScheduleApply();
            }
        }

        private void ScheduleApply()
        {
            RoomPostEffectSettings[] settings = new RoomPostEffectSettings[targets.Length];
            for (int i = 0; i < targets.Length; i++)
            {
                settings[i] = targets[i] as RoomPostEffectSettings;
            }

            EditorApplication.delayCall += () => ApplyWhenReady(settings);
        }

        private static void ApplyWhenReady(RoomPostEffectSettings[] settings)
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += () => ApplyWhenReady(settings);
                return;
            }

            for (int i = 0; i < settings.Length; i++)
            {
                if (settings[i] != null)
                {
                    settings[i].ApplyToDefaultMaterialAsset(true);
                }
            }
        }
    }
}
