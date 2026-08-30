using System;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace Escape.EditorTools
{
    // 동적 TMP 폰트 에셋이 저장될 때 생성된 글리프/아틀라스 데이터를 비워 Git diff 오염을 막는다.
    internal sealed class TmpDynamicFontAssetCleaner : AssetModificationProcessor
    {
        [MenuItem("Tools/TextMesh Pro/Clear Dynamic Font Asset Data")]
        // 프로젝트 안의 모든 Dynamic TMP 폰트 에셋을 즉시 정리한다.
        private static void ClearAllDynamicFontAssets()
        {
            int clearedCount = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:TMP_FontAsset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                if (!ShouldClear(fontAsset))
                {
                    continue;
                }

                Clear(fontAsset);
                EditorUtility.SetDirty(fontAsset);
                AssetDatabase.SaveAssetIfDirty(fontAsset);
                clearedCount++;
            }

            Debug.Log($"Cleared dynamic TMP font asset data: {clearedCount}");
        }

        // Unity가 에셋을 저장하기 직전에 Dynamic TMP 폰트 데이터만 정리한다.
        private static string[] OnWillSaveAssets(string[] paths)
        {
            try
            {
                foreach (string path in paths)
                {
                    TMP_FontAsset fontAsset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                    if (!ShouldClear(fontAsset))
                    {
                        continue;
                    }

                    Clear(fontAsset);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Debug.LogError("Failed to clear dynamic TMP font asset data before save.");
            }

            return paths;
        }

        // 런타임 글리프 추가가 가능한 Dynamic 폰트인지 확인한다.
        private static bool ShouldClear(TMP_FontAsset fontAsset)
        {
            return fontAsset != null && fontAsset.atlasPopulationMode == AtlasPopulationMode.Dynamic;
        }

        // 글리프 테이블과 아틀라스 텍스처 데이터를 저장 가능한 최소 상태로 비운다.
        private static void Clear(TMP_FontAsset fontAsset)
        {
            fontAsset.ClearFontAssetData(setAtlasSizeToZero: true);
        }
    }
}
