using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Escape.Data
{
    // Unity Resources 또는 실제 파일 경로에서 TSV 텍스트를 읽는 내부 유틸리티.
    static class TsvTextLoader
    {
        // "Assets/Resources/Data/item.tsv"와 "Data/item"을 같은 Resources 경로로 정규화한다.
        public static string NormalizeResourcePath(string resourcePath)
        {
            if (string.IsNullOrWhiteSpace(resourcePath))
            {
                return string.Empty;
            }

            string path = resourcePath.Trim().Replace('\\', '/');
            const string resourcesPrefix = "Assets/Resources/";
            if (path.StartsWith(resourcesPrefix, StringComparison.OrdinalIgnoreCase))
            {
                path = path.Substring(resourcesPrefix.Length);
            }

            return path.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase)
                ? path.Substring(0, path.Length - ".tsv".Length)
                : path;
        }

        // 먼저 Resources.Load로 읽고, 실패하면 개발 중 파일 직접 읽기로 한 번 더 시도한다.
        public static bool TryLoadText(string resourcePath, out string text)
        {
            TextAsset asset = Resources.Load<TextAsset>(resourcePath);
            if (asset != null)
            {
                text = asset.text;
                return true;
            }

            string filePath = GetTsvFilePath(resourcePath);
            if (File.Exists(filePath))
            {
                text = File.ReadAllText(filePath);
                return true;
            }

            text = string.Empty;
            return false;
        }

        // Resources/Data/dialogue.tsv 같은 기준 파일과 같은 접두사의 분할 TSV를 모두 읽는다.
        public static IReadOnlyList<TsvTextSource> LoadMatchingTexts(string resourcePath)
        {
            resourcePath = NormalizeResourcePath(resourcePath);
            string directory = Path.GetDirectoryName(resourcePath)?.Replace('\\', '/') ?? string.Empty;
            string fileName = Path.GetFileName(resourcePath);
            var sources = new List<TsvTextSource>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            TextAsset[] assets = Resources.LoadAll<TextAsset>(directory);
            for (int i = 0; i < assets.Length; i++)
            {
                TextAsset asset = assets[i];
                if (asset == null || !IsMatchingSplitName(asset.name, fileName))
                {
                    continue;
                }

                string sourceName = string.IsNullOrEmpty(directory)
                    ? asset.name
                    : $"{directory}/{asset.name}";
                if (seen.Add(sourceName))
                {
                    sources.Add(new TsvTextSource(sourceName, asset.text));
                }
            }

            string filePath = GetTsvFilePath(resourcePath);
            string fileDirectory = Path.GetDirectoryName(filePath);
            if (Directory.Exists(fileDirectory))
            {
                string[] files = Directory.GetFiles(fileDirectory, $"{fileName}*.tsv", SearchOption.TopDirectoryOnly);
                for (int i = 0; i < files.Length; i++)
                {
                    string name = Path.GetFileNameWithoutExtension(files[i]);
                    if (!IsMatchingSplitName(name, fileName))
                    {
                        continue;
                    }

                    string sourceName = NormalizeResourcePath(ToResourcePath(files[i]));
                    if (seen.Add(sourceName))
                    {
                        sources.Add(new TsvTextSource(sourceName, File.ReadAllText(files[i])));
                    }
                }
            }

            sources.Sort((left, right) => CompareSplitSources(left.SourceName, right.SourceName, fileName));
            return sources;
        }

        // Resources 기준 경로를 실제 Assets/Resources 안의 .tsv 파일 경로로 바꾼다.
        public static string GetTsvFilePath(string resourcePath)
        {
            return Path.Combine(Application.dataPath, "Resources", $"{resourcePath}.tsv");
        }

        static string ToResourcePath(string filePath)
        {
            string normalizedFile = Path.GetFullPath(filePath).Replace('\\', '/');
            string resourcesRoot = Path.Combine(Application.dataPath, "Resources").Replace('\\', '/').TrimEnd('/') + "/";
            if (normalizedFile.StartsWith(resourcesRoot, StringComparison.OrdinalIgnoreCase))
            {
                normalizedFile = normalizedFile.Substring(resourcesRoot.Length);
            }

            return normalizedFile.EndsWith(".tsv", StringComparison.OrdinalIgnoreCase)
                ? normalizedFile.Substring(0, normalizedFile.Length - ".tsv".Length)
                : normalizedFile;
        }

        static bool IsMatchingSplitName(string candidateName, string baseName)
        {
            return string.Equals(candidateName, baseName, StringComparison.OrdinalIgnoreCase) ||
                   candidateName.StartsWith($"{baseName}_", StringComparison.OrdinalIgnoreCase);
        }

        static int CompareSplitSources(string left, string right, string baseName)
        {
            int leftRank = GetSplitSourceRank(left, baseName);
            int rightRank = GetSplitSourceRank(right, baseName);
            if (leftRank != rightRank)
            {
                return leftRank.CompareTo(rightRank);
            }

            return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        }

        static int GetSplitSourceRank(string sourceName, string baseName)
        {
            string name = Path.GetFileName(sourceName);
            if (string.Equals(name, baseName, StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (string.Equals(name, $"{baseName}_common", StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            if (string.Equals(name, $"{baseName}_bedroom", StringComparison.OrdinalIgnoreCase))
            {
                return 2;
            }

            if (string.Equals(name, $"{baseName}_entrance", StringComparison.OrdinalIgnoreCase))
            {
                return 3;
            }

            if (string.Equals(name, $"{baseName}_kitchen", StringComparison.OrdinalIgnoreCase))
            {
                return 4;
            }

            if (string.Equals(name, $"{baseName}_livingroom", StringComparison.OrdinalIgnoreCase))
            {
                return 5;
            }

            if (string.Equals(name, $"{baseName}_utility", StringComparison.OrdinalIgnoreCase))
            {
                return 6;
            }

            if (string.Equals(name, $"{baseName}_intro", StringComparison.OrdinalIgnoreCase))
            {
                return 20;
            }

            if (string.Equals(name, $"{baseName}_ending", StringComparison.OrdinalIgnoreCase))
            {
                return 21;
            }

            if (string.Equals(name, $"{baseName}_timing_test", StringComparison.OrdinalIgnoreCase))
            {
                return 22;
            }

            return 10;
        }

        // Windows/Mac 줄바꿈 차이를 통일해서 줄 단위로 나눈다.
        public static string[] SplitLines(string text)
        {
            return (text ?? string.Empty)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n')
                .Split('\n');
        }
    }

    readonly struct TsvTextSource
    {
        public TsvTextSource(string sourceName, string text)
        {
            SourceName = sourceName ?? string.Empty;
            Text = text ?? string.Empty;
        }

        public string SourceName { get; }
        public string Text { get; }
    }
}
