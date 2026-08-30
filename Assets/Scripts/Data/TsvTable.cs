using System;
using System.Collections.Generic;
using System.Reflection;

namespace Escape.Data
{
    // 로드된 데이터 목록을 보관하고, public string id 필드가 있으면 id 검색을 제공한다.
    public sealed class TsvTable<T> where T : class
    {
        readonly Dictionary<string, T> rowsById = new(StringComparer.Ordinal);
        readonly Dictionary<string, IReadOnlyList<T>> rowGroupsById = new(StringComparer.Ordinal);
        readonly FieldInfo idField;

        public IReadOnlyList<T> Rows { get; }

        // 데이터 목록을 저장하고 id 필드가 있으면 검색용 딕셔너리를 만든다.
        public TsvTable(IReadOnlyList<T> rows)
        {
            Rows = rows ?? Array.Empty<T>();
            idField = typeof(T).GetField("id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);

            if (idField == null || idField.FieldType != typeof(string))
            {
                return;
            }

            // id가 비어 있는 row는 직전에 등장한 id의 후속 행으로 묶는다.
            string currentGroupId = null;
            List<T> currentGroup = null;
            for (int i = 0; i < Rows.Count; i++)
            {
                T row = Rows[i];
                string id = (idField.GetValue(row) as string)?.Trim();

                if (!string.IsNullOrEmpty(id))
                {
                    currentGroupId = id;
                    if (!rowGroupsById.TryGetValue(id, out IReadOnlyList<T> existing))
                    {
                        currentGroup = new List<T> { row };
                        rowGroupsById.Add(id, currentGroup);
                    }
                    else
                    {
                        currentGroup = (List<T>)existing;
                        currentGroup.Add(row);
                    }

                    if (!rowsById.ContainsKey(id))
                    {
                        rowsById.Add(id, row);
                    }
                }
                else if (currentGroupId != null && currentGroup != null)
                {
                    currentGroup.Add(row);
                }
            }
        }

        // id 값으로 한 행 객체를 찾는다.
        public bool TryGet(string id, out T row)
        {
            if (!string.IsNullOrWhiteSpace(id) && rowsById.TryGetValue(id.Trim(), out row))
            {
                return true;
            }

            row = null;
            return false;
        }

        // id 값으로 시작하는 행과 그 뒤에 이어진 id 빈 줄들을 묶어서 돌려준다.
        public bool TryGetRows(string id, out IReadOnlyList<T> rows)
        {
            if (!string.IsNullOrWhiteSpace(id) && rowGroupsById.TryGetValue(id.Trim(), out rows))
            {
                return true;
            }

            rows = null;
            return false;
        }
    }
}
