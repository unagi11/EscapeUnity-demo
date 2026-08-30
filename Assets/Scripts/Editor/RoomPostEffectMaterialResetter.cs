using Escape.Rooms;
using UnityEditor;
using UnityEngine;

namespace Escape.Editor
{
    // 에디터 복귀 시 플레이 중 변경된 RoomImage 포스트 이펙트 Material 값을 기본값으로 되돌린다.
    [InitializeOnLoad]
    public static class RoomPostEffectMaterialResetter
    {
        static RoomPostEffectMaterialResetter()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.delayCall += ResetMaterialDefaults;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                ResetMaterialDefaults();
            }
        }

        private static void ResetMaterialDefaults()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += ResetMaterialDefaults;
                return;
            }

            RoomPostEffectSettings settings = GetOrCreateSettings();
            Material material = AssetDatabase.LoadAssetAtPath<Material>(RoomPostEffectSettings.DefaultMaterialPath);
            if (material == null)
            {
                return;
            }

            settings.ApplyTo(material);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
        }

        private static RoomPostEffectSettings GetOrCreateSettings()
        {
            RoomPostEffectSettings settings = AssetDatabase.LoadAssetAtPath<RoomPostEffectSettings>(
                RoomPostEffectSettings.DefaultAssetPath);
            if (settings != null)
            {
                return settings;
            }

            settings = ScriptableObject.CreateInstance<RoomPostEffectSettings>();
            AssetDatabase.CreateAsset(settings, RoomPostEffectSettings.DefaultAssetPath);
            AssetDatabase.SaveAssets();
            return settings;
        }

    }
}
