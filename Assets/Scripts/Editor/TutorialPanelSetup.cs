#if UNITY_EDITOR
using System;
using System.Linq;
using Escape.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Escape.EditorTools
{
    // 공용 TopUICanvas의 튜토리얼 오버레이 레이아웃과 직렬화 참조를 구성한다.
    public static class TutorialPanelSetup
    {
        private const string PrefabPath = "Assets/Resources/Prefabs/TopUICanvas.prefab";
        private static readonly string[] GuideImagePaths =
        {
            "Assets/Resources/Sprites/Tutorial/tutorial_guide 1.png",
            "Assets/Resources/Sprites/Tutorial/tutorial_guide 2.png",
            "Assets/Resources/Sprites/Tutorial/tutorial_guide 3.png",
            "Assets/Resources/Sprites/Tutorial/tutorial_guide 4.png",
        };

        public static void Setup()
        {
            ConfigureGuideImageImporters();
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                Transform panelTransform = prefabRoot.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(child => child.name == "TutorialPanelUI");
                if (panelTransform == null)
                {
                    throw new InvalidOperationException("TutorialPanelUI object is missing from TopUICanvas.");
                }

                GameObject panelObject = panelTransform.gameObject;
                Image background = panelObject.GetComponent<Image>();
                background.color = new Color(0f, 0f, 0f, 0.88f);
                background.raycastTarget = true;

                Button dismissButton = panelObject.GetComponent<Button>() ?? panelObject.AddComponent<Button>();
                dismissButton.targetGraphic = background;
                dismissButton.transition = Selectable.Transition.None;
                dismissButton.navigation = new Navigation { mode = Navigation.Mode.None };

                TutorialPanelUI panel = panelObject.GetComponent<TutorialPanelUI>() ??
                    panelObject.AddComponent<TutorialPanelUI>();

                TMP_Text tutorialText = panelTransform.Find("TutorialText")?.GetComponent<TMP_Text>() ??
                    panelObject.GetComponentInChildren<TMP_Text>(true);
                tutorialText.gameObject.name = "TutorialText";
                ConfigureTextRect(tutorialText.rectTransform, new Vector2(0.08f, 0.08f), new Vector2(0.92f, 0.39f));
                tutorialText.text = "픽을 움직여 핀을 들어 올리세요.";
                tutorialText.fontSize = 13f;
                tutorialText.fontSizeMin = 8f;
                tutorialText.fontSizeMax = 13f;
                tutorialText.raycastTarget = false;

                Transform titleTransform = panelTransform.Find("TutorialTitleText");
                if (titleTransform != null)
                {
                    UnityEngine.Object.DestroyImmediate(titleTransform.gameObject);
                }

                string[] obsoleteGuideObjects = { "TutorialImage0", "TutorialImage1", "Arrow0", "Arrow1" };
                for (int i = 0; i < obsoleteGuideObjects.Length; i++)
                {
                    Transform obsolete = panelTransform.Find(obsoleteGuideObjects[i]);
                    if (obsolete != null)
                    {
                        UnityEngine.Object.DestroyImmediate(obsolete.gameObject);
                    }
                }

                TMP_Text tapHint = CreateTextCopy(
                    tutorialText,
                    panelTransform,
                    "TapHint",
                    "화면 아무 곳이나 눌러 계속",
                    new Vector2(0f, -84f),
                    new Vector2(220f, 18f),
                    8f);
                tapHint.color = new Color(1f, 1f, 1f, 0.62f);
                LocalizedTextUI tapHintLocalization = tapHint.GetComponent<LocalizedTextUI>() ??
                    tapHint.gameObject.AddComponent<LocalizedTextUI>();
                var tapHintSerialized = new SerializedObject(tapHintLocalization);
                tapHintSerialized.FindProperty("tid").stringValue = "tutorial_tap_to_close";
                tapHintSerialized.FindProperty("tmpText").objectReferenceValue = tapHint;
                tapHintSerialized.FindProperty("fallbackText").stringValue = "화면 아무 곳이나 눌러 계속";
                tapHintSerialized.ApplyModifiedPropertiesWithoutUndo();

                var serialized = new SerializedObject(panel);
                serialized.FindProperty("dismissButton").objectReferenceValue = dismissButton;
                serialized.FindProperty("panelBackground").objectReferenceValue = background;
                serialized.FindProperty("tutorialText").objectReferenceValue = tutorialText;
                serialized.FindProperty("guideOverlayAlpha").floatValue = 245f / 255f;
                serialized.FindProperty("showSfxId").stringValue = "question_bell";
                serialized.FindProperty("showDuration").floatValue = 0.24f;
                serialized.FindProperty("showStartScale").floatValue = 0.82f;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                SceneTransitionFadeUI transitionUI = prefabRoot.GetComponent<SceneTransitionFadeUI>();
                var transitionSerialized = new SerializedObject(transitionUI);
                transitionSerialized.FindProperty("tutorialPanel").objectReferenceValue = panel;
                transitionSerialized.ApplyModifiedPropertiesWithoutUndo();

                panelObject.SetActive(false);
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, PrefabPath);
                AssetDatabase.SaveAssets();
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        // 가이드 이미지의 투명 구멍과 픽셀 경계를 그대로 유지하도록 임포트한다.
        private static void ConfigureGuideImageImporters()
        {
            for (int i = 0; i < GuideImagePaths.Length; i++)
            {
                string assetPath = GuideImagePaths[i];
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                if (AssetImporter.GetAtPath(assetPath) is not TextureImporter importer)
                {
                    continue;
                }

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.filterMode = FilterMode.Point;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
            }
        }

        // 락픽 씬의 기존 도움말 버튼과 팝업을 새 공용 튜토리얼로 교체한다.
        public static void RemoveLegacyLockPickTutorial()
        {
            const string scenePath = "Assets/Scenes/4_LockPickScene.unity";
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                    {
                        if (child != null && (child.name == "TipButton" || child.name == "TipPopup"))
                        {
                            UnityEngine.Object.DestroyImmediate(child.gameObject);
                        }
                    }
                }

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static TMP_Text CreateTextCopy(
            TMP_Text source,
            Transform parent,
            string name,
            string text,
            Vector2 anchoredPosition,
            Vector2 size,
            float fontSize)
        {
            Transform existing = parent.Find(name);
            TMP_Text copy;
            if (existing != null)
            {
                copy = existing.GetComponent<TMP_Text>();
            }
            else
            {
                GameObject copyObject = UnityEngine.Object.Instantiate(source.gameObject, parent);
                copyObject.name = name;
                copy = copyObject.GetComponent<TMP_Text>();
            }

            RectTransform rect = copy.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            copy.text = text;
            copy.fontSize = fontSize;
            copy.fontSizeMin = fontSize;
            copy.fontSizeMax = fontSize;
            copy.enableAutoSizing = false;
            copy.alignment = TextAlignmentOptions.Center;
            copy.raycastTarget = false;
            return copy;
        }

        private static void ConfigureTextRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

    }
}
#endif
