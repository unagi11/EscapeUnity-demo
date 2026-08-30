using System.Collections.Generic;
using Escape.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Escape.Editor
{
    public static class UIDarkThemeEditor
    {
        private const string ApplyMenuPath = "Tools/Escape/UI/Apply Dark Theme";

        [MenuItem(ApplyMenuPath)]
        public static void ApplyToSelectedNodes()
        {
            GameObject[] selectedObjects = Selection.gameObjects;
            if (selectedObjects == null || selectedObjects.Length == 0)
            {
                EditorUtility.DisplayDialog("UI Dark Theme", "Select a UI node to apply the dark theme.", "OK");
                return;
            }

            int changedCount = 0;
            var processedPrefabPaths = new HashSet<string>();
            var processedSceneObjects = new HashSet<int>();

            foreach (GameObject selectedObject in selectedObjects)
            {
                if (selectedObject == null)
                {
                    continue;
                }

                string assetPath = AssetDatabase.GetAssetPath(selectedObject);
                if (!string.IsNullOrWhiteSpace(assetPath) && PrefabUtility.IsPartOfPrefabAsset(selectedObject))
                {
                    if (processedPrefabPaths.Add(assetPath) && ApplyToPrefabAsset(assetPath))
                    {
                        changedCount++;
                    }

                    continue;
                }

                if (!processedSceneObjects.Add(selectedObject.GetInstanceID()))
                {
                    continue;
                }

                Undo.RegisterFullObjectHierarchyUndo(selectedObject, "Apply UI Dark Theme");
                ApplyToNode(selectedObject);
                if (selectedObject.scene.IsValid())
                {
                    EditorSceneManager.MarkSceneDirty(selectedObject.scene);
                }

                changedCount++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[UIDarkTheme] Applied the dark theme to {changedCount} selected UI node(s).");
        }

        [MenuItem(ApplyMenuPath, true)]
        private static bool CanApplyToSelectedNodes()
        {
            return Selection.gameObjects.Length > 0;
        }

        private static bool ApplyToPrefabAsset(string path)
        {
            if (!path.EndsWith(".prefab"))
            {
                Debug.LogWarning($"[UIDarkTheme] Selected asset is not a prefab: {path}");
                return false;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            if (root == null)
            {
                Debug.LogWarning($"[UIDarkTheme] Could not load prefab: {path}");
                return false;
            }

            try
            {
                ApplyToNode(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void ApplyToNode(GameObject root)
        {
            UIDarkTheme.Apply(root);
        }
    }
}
