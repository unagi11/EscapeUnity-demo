using System;
using System.Collections.Generic;
using UnityEngine;

namespace Escape.Data
{
    // TSV 한 줄을 T 객체 하나로 변환해서 목록 또는 테이블로 돌려주는 로더.
    public sealed class TsvDataLoader<T> : ITsvDataLoader<T> where T : class, new()
    {
        // Item이면 Data/item, Speaker면 Data/speaker 같은 기본 경로를 사용한다.
        public IReadOnlyList<T> Load()
        {
            return Load(GetDefaultResourcePath());
        }

        // TSV 텍스트를 읽고, 헤더 컬럼명과 T의 public string 필드를 맞춰 객체 목록을 만든다.
        public IReadOnlyList<T> Load(string resourcePath)
        {
            string normalizedPath = TsvTextLoader.NormalizeResourcePath(resourcePath);
            if (typeof(T) == typeof(Dialogue) &&
                string.Equals(normalizedPath, GetDefaultResourcePath(), StringComparison.OrdinalIgnoreCase))
            {
                return LoadMergedDialogueFiles(normalizedPath);
            }

            if (!TsvTextLoader.TryLoadText(normalizedPath, out string text))
            {
                Debug.LogWarning($"TSV not found at Resources/{normalizedPath}.tsv or {TsvTextLoader.GetTsvFilePath(normalizedPath)}");
                return Array.Empty<T>();
            }

            return TsvParser.Parse<T>(text, normalizedPath);
        }

        // 기본 경로 TSV를 읽어서 id로 찾을 수 있는 테이블을 만든다.
        public TsvTable<T> LoadTable()
        {
            return LoadTable(GetDefaultResourcePath());
        }

        // 지정한 경로 TSV를 읽어서 id로 찾을 수 있는 테이블을 만든다.
        public TsvTable<T> LoadTable(string resourcePath)
        {
            return new TsvTable<T>(Load(resourcePath));
        }

        // 타입 이름을 Resources/Data 아래 TSV 이름으로 변환한다.
        static string GetDefaultResourcePath()
        {
            return $"Data/{typeof(T).Name.ToLowerInvariant()}";
        }

        // Dialogue 기본 로드는 Data/dialogue.tsv와 Data/dialogue_*.tsv를 한 테이블처럼 병합한다.
        IReadOnlyList<T> LoadMergedDialogueFiles(string normalizedPath)
        {
            IReadOnlyList<TsvTextSource> sources = TsvTextLoader.LoadMatchingTexts(normalizedPath);
            if (sources.Count == 0)
            {
                Debug.LogWarning($"TSV not found at Resources/{normalizedPath}.tsv or {TsvTextLoader.GetTsvFilePath(normalizedPath)}");
                return Array.Empty<T>();
            }

            var rows = new List<T>();
            for (int i = 0; i < sources.Count; i++)
            {
                IReadOnlyList<T> parsed = TsvParser.Parse<T>(sources[i].Text, sources[i].SourceName);
                rows.AddRange(parsed);
            }

            return rows;
        }
    }

}
