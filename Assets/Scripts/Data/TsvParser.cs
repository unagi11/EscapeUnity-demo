using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Escape.Data
{
    // TSV 문자열을 실제 데이터 객체 목록으로 변환하는 내부 파서.
    static class TsvParser
    {
        // 첫 번째 유효 줄을 헤더로 보고, 이후 줄들을 T 객체로 변환한다.
        public static IReadOnlyList<T> Parse<T>(string tsv, string sourceName) where T : class, new()
        {
            string[] lines = TsvTextLoader.SplitLines(tsv);
            var rows = new List<T>();
            Dictionary<string, FieldInfo> fields = GetStringFields<T>();
            string[] columns = null;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                string[] values = line.Split('\t');
                if (columns == null)
                {
                    columns = TrimValues(values);
                    continue;
                }

                T row = new T();
                for (int columnIndex = 0; columnIndex < columns.Length; columnIndex++)
                {
                    string columnName = columns[columnIndex];
                    if (fields.TryGetValue(columnName, out FieldInfo field))
                    {
                        string value = columnIndex < values.Length ? values[columnIndex] ?? string.Empty : string.Empty;
                        field.SetValue(row, value);
                    }
                }

                rows.Add(row);
            }

            if (columns == null)
            {
                Debug.LogWarning($"Skipping empty TSV: {sourceName}");
            }

            return rows;
        }

        // T 타입에서 TSV 컬럼과 매칭할 public string 필드들을 모은다.
        static Dictionary<string, FieldInfo> GetStringFields<T>()
        {
            FieldInfo[] fields = typeof(T).GetFields(BindingFlags.Instance | BindingFlags.Public);
            var fieldsByName = new Dictionary<string, FieldInfo>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (field.FieldType == typeof(string) && !fieldsByName.ContainsKey(field.Name))
                {
                    fieldsByName.Add(field.Name, field);
                }
            }

            return fieldsByName;
        }

        // 헤더 컬럼명의 앞뒤 공백을 정리한다.
        static string[] TrimValues(string[] values)
        {
            var trimmed = new string[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                trimmed[i] = (values[i] ?? string.Empty).Trim();
            }

            return trimmed;
        }
    }
}
