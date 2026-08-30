using System;
using System.Collections.Generic;
using Escape.Data;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;

namespace Escape.Progress
{
    // 정보 소유 상태를 GameSession 위에서 관리한다.
    [MovedFrom(true, "Escape.Managers", null, "InfoManager")]
    public sealed class InfoCollection : MonoBehaviour
    {
        public static InfoCollection Instance { get; private set; }

        public event Action Changed;

        private const string InfoResourcePath = "Data/info";
        private const string DiaryInfoId = "diary_info";
        private const string DiaryInfoAchievementId = "diary_info_acquired";

        [SerializeField] private bool debugLogs = true;

        private readonly List<string> defaultInfoIds = new();
        private GameSession state;
        private bool applyingDefaults;
        private bool defaultsLoaded;
        private const string LogPrefix = "[InfoCollection]";

        public GameSession State => ResolveState();
        public IReadOnlyCollection<string> Infos => ResolveState().Infos;

        // 중복 매니저를 정리하고 기본 정보를 보장한다.
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
            ResolveState();
            EnsureDefaults();
        }

        // 게임 상태 변경을 감시해서 정보 UI를 갱신하게 한다.
        private void OnEnable()
        {
            ResolveState().Changed += HandleStateChanged;
            EnsureDefaults();
        }

        // 비활성화될 때 상태 변경 구독을 해제한다.
        private void OnDisable()
        {
            if (state != null)
            {
                state.Changed -= HandleStateChanged;
            }
        }

        // start_info=true인 기본 정보를 지급한다.
        public void EnsureDefaults()
        {
            if (applyingDefaults)
            {
                return;
            }

            EnsureDefaultInfosLoaded();
            applyingDefaults = true;
            AddDefaultInfos();
            applyingDefaults = false;
            Changed?.Invoke();
        }

        // 지정 정보를 보유 목록에 추가한다.
        public bool AddInfo(string infoId)
        {
            bool changed = ResolveState().AddInfo(infoId);
            if (changed)
            {
                Log($"AddInfo {infoId}");
                if (string.Equals(infoId, DiaryInfoId, StringComparison.Ordinal))
                {
                    AchievementProgress.Unlock(DiaryInfoAchievementId);
                }

                Changed?.Invoke();
            }

            EnsureDefaults();
            return changed;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public bool DevRemoveInfo(string infoId)
        {
            applyingDefaults = true;
            bool changed = ResolveState().RemoveInfo(infoId);
            applyingDefaults = false;

            if (changed)
            {
                Log($"DevRemoveInfo {infoId}");
                Changed?.Invoke();
            }

            return changed;
        }
#endif

        // TSV 순서대로 보유 정보를 정렬하고, 미등록 정보는 뒤에 정렬해 붙인다.
        public IReadOnlyList<string> GetOrderedInfos()
        {
            EnsureDefaultInfosLoaded();
            var ordered = new List<string>();
            GameSession currentState = ResolveState();

            TsvTable<Info> table = new TsvDataLoader<Info>().LoadTable(InfoResourcePath);
            IReadOnlyList<Info> rows = table.Rows;
            for (int i = 0; i < rows.Count; i++)
            {
                string infoId = rows[i]?.id?.Trim();
                if (!string.IsNullOrWhiteSpace(infoId) &&
                    currentState.Infos.Contains(infoId) &&
                    !ordered.Contains(infoId))
                {
                    ordered.Add(infoId);
                }
            }

            var extraInfos = new List<string>();
            foreach (string infoId in currentState.Infos)
            {
                if (!string.IsNullOrWhiteSpace(infoId) && !ordered.Contains(infoId))
                {
                    extraInfos.Add(infoId);
                }
            }

            extraInfos.Sort(StringComparer.Ordinal);
            ordered.AddRange(extraInfos);
            return ordered;
        }

        // GameSession가 없으면 런타임에 하나 만들어 연결한다.
        private GameSession ResolveState()
        {
            if (state != null)
            {
                return state;
            }

            state = GameSession.Instance;
            if (state == null)
            {
                var stateObject = new GameObject("GameSession");
                state = stateObject.AddComponent<GameSession>();
            }

            return state;
        }

        // info.tsv에서 start_info=true인 정보를 TSV 순서대로 불러온다.
        private void EnsureDefaultInfosLoaded()
        {
            if (defaultsLoaded)
            {
                return;
            }

            defaultsLoaded = true;
            defaultInfoIds.Clear();

            TsvTable<Info> table = new TsvDataLoader<Info>().LoadTable(InfoResourcePath);
            IReadOnlyList<Info> rows = table.Rows;
            for (int i = 0; i < rows.Count; i++)
            {
                Info row = rows[i];
                if (row == null ||
                    string.IsNullOrWhiteSpace(row.id) ||
                    !IsTruthy(row.start_info))
                {
                    continue;
                }

                string infoId = row.id.Trim();
                if (!defaultInfoIds.Contains(infoId))
                {
                    defaultInfoIds.Add(infoId);
                }
            }
        }

        private static bool IsTruthy(string value)
        {
            string normalized = (value ?? string.Empty).Trim();
            return string.Equals(normalized, "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "1", StringComparison.Ordinal) ||
                   string.Equals(normalized, "yes", StringComparison.OrdinalIgnoreCase);
        }

        private void AddDefaultInfos()
        {
            GameSession currentState = ResolveState();
            for (int i = 0; i < defaultInfoIds.Count; i++)
            {
                currentState.AddInfo(defaultInfoIds[i]);
            }
        }

        // 외부 변경이 들어와도 기본 정보 상태를 유지한다.
        private void HandleStateChanged()
        {
            if (applyingDefaults)
            {
                return;
            }

            EnsureDefaults();
        }

        private void Log(string message)
        {
            if (debugLogs)
            {
                Debug.Log($"{LogPrefix} {message}", this);
            }
        }
    }
}
