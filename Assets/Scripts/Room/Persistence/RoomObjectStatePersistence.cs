using System;
using Escape.Progress;
using System.Collections.Generic;
using UnityEngine;

namespace Escape.Rooms
{
    // Room 계층의 활성 상태와 애니메이션 상태를 저장 형식으로 변환하고 복원한다.
    internal sealed class RoomObjectStatePersistence
    {
        private readonly RoomRegistry rooms;
        private readonly Func<GameSession> getState;
        private readonly Action<string> log;
        private readonly Dictionary<string, bool> initialVisibility = new(StringComparer.Ordinal);

        // 고정된 Room 목록과 현재 게임 상태 조회 수단을 연결한다.
        public RoomObjectStatePersistence(
            RoomRegistry rooms,
            Func<GameSession> getState,
            Action<string> log)
        {
            this.rooms = rooms;
            this.getState = getState;
            this.log = log;
        }

        // 씬 로드 직후 모든 Room GameObject의 초기 activeSelf 상태를 보관한다.
        public void CaptureInitialVisibility()
        {
            initialVisibility.Clear();
            foreach (Transform roomRoot in rooms.EnumerateRoots())
            {
                Transform[] transforms = roomRoot.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < transforms.Length; i++)
                {
                    string path = GetScenePath(transforms[i]);
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        initialVisibility[path] = transforms[i].gameObject.activeSelf;
                    }
                }
            }
        }

        // 기존 상태가 없으면 씬 초기값을 등록하고 있으면 저장된 visible 상태를 적용한다.
        public void InitializeVisibility()
        {
            GameSession state = getState();
            if (state == null)
            {
                return;
            }

            if (state.RoomObjectVisibility.Count == 0)
            {
                state.SetRoomObjectVisibility(initialVisibility);
            }

            ApplyVisibility(state.RoomObjectVisibility);
        }

        // Room 하위 GameObject 변경을 GameSession visible 상태에 기록한다.
        public void RecordVisibility(Transform target, bool visible)
        {
            GameSession state = getState();
            if (state != null && TryGetRoomObjectPath(target, out string path))
            {
                state.SetRoomObjectVisible(path, visible);
            }
        }

        // 현재 activeSelf 상태 중 씬 초기값과 달라진 항목만 저장 문자열로 만든다.
        public List<string> CaptureActiveStates()
        {
            Dictionary<string, bool> visibility = CaptureCurrentVisibility();
            GameSession state = getState();
            state?.SetRoomObjectVisibility(visibility);

            IReadOnlyDictionary<string, bool> source = state != null
                ? state.RoomObjectVisibility
                : visibility;
            var states = new List<string>();
            foreach (KeyValuePair<string, bool> pair in source)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    continue;
                }

                if (initialVisibility.TryGetValue(pair.Key, out bool initialActive) &&
                    initialActive == pair.Value)
                {
                    continue;
                }

                states.Add($"{pair.Key}:{(pair.Value ? "1" : "0")}");
            }

            states.Sort(StringComparer.Ordinal);
            return states;
        }

        // 초기 상태 위에 저장된 activeSelf 변경분을 덮어쓰고 씬과 게임 상태에 반영한다.
        public void RestoreActiveStates(IReadOnlyList<string> states)
        {
            var visibility = new Dictionary<string, bool>(initialVisibility, StringComparer.Ordinal);
            if (states != null)
            {
                for (int i = 0; i < states.Count; i++)
                {
                    if (TryParseActiveState(states[i], out string path, out bool active))
                    {
                        visibility[path] = active;
                    }
                }
            }

            foreach (Transform roomRoot in rooms.EnumerateRoots())
            {
                string path = GetScenePath(roomRoot);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    visibility[path] = roomRoot.gameObject.activeSelf;
                }
            }

            getState()?.SetRoomObjectVisibility(visibility);
            ApplyVisibility(visibility);
        }

        // 태그 애니메이터가 붙은 Room 오브젝트의 현재 표시 상태를 저장 문자열로 만든다.
        public List<string> CaptureAnimationStates()
        {
            var states = new List<string>();
            foreach (Transform roomRoot in rooms.EnumerateRoots())
            {
                var animators = roomRoot.GetComponentsInChildren<AsepriteRoomAnimator>(true);
                for (int i = 0; i < animators.Length; i++)
                {
                    AsepriteRoomAnimator animator = animators[i];
                    if (animator == null ||
                        !animator.TryGetAnimationState(
                            out string animationName,
                            out AsepriteSpritePlaybackMode playbackMode))
                    {
                        continue;
                    }

                    string path = GetScenePath(animator.transform);
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        states.Add($"{path}:{animationName}:{FormatPlaybackMode(playbackMode)}");
                    }
                }
            }

            states.Sort(StringComparer.Ordinal);
            return states;
        }

        // 저장된 애니메이션 상태를 현재 Room 계층의 애니메이터에 적용한다.
        public void RestoreAnimationStates(IReadOnlyList<string> states)
        {
            if (states == null || states.Count == 0)
            {
                return;
            }

            Dictionary<string, Transform> transformsByPath = BuildTransformPathMap();
            for (int i = 0; i < states.Count; i++)
            {
                if (!TryParseAnimationState(
                        states[i],
                        out string path,
                        out string animationName,
                        out AsepriteSpritePlaybackMode playbackMode))
                {
                    continue;
                }

                if (!transformsByPath.TryGetValue(path, out Transform target) || target == null)
                {
                    Debug.LogWarning($"Saved room animation object not found: {path}");
                    continue;
                }

                var animator = target.GetComponent<AsepriteRoomAnimator>();
                if (animator == null)
                {
                    Debug.LogWarning($"Saved room animation target has no animator: {path}");
                    continue;
                }

                if (!animator.TryApplyAnimationState(animationName, playbackMode))
                {
                    Debug.LogWarning($"Saved room animation not found: {path}:{animationName}");
                }
            }
        }

        // 현재 모든 Room GameObject의 activeSelf 상태를 경로별로 수집한다.
        private Dictionary<string, bool> CaptureCurrentVisibility()
        {
            var visibility = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (Transform roomRoot in rooms.EnumerateRoots())
            {
                Transform[] transforms = roomRoot.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < transforms.Length; i++)
                {
                    string path = GetScenePath(transforms[i]);
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        visibility[path] = transforms[i].gameObject.activeSelf;
                    }
                }
            }

            return visibility;
        }

        // 지정 Transform이 Room 계층에 속하면 저장 경로를 반환한다.
        private bool TryGetRoomObjectPath(Transform target, out string path)
        {
            path = string.Empty;
            if (target == null)
            {
                return false;
            }

            foreach (Transform roomRoot in rooms.EnumerateRoots())
            {
                if (target == roomRoot || target.IsChildOf(roomRoot))
                {
                    path = GetScenePath(target);
                    return !string.IsNullOrWhiteSpace(path);
                }
            }

            return false;
        }

        // 경로별 visible 상태를 현재 Room 계층에 적용한다.
        private void ApplyVisibility(IReadOnlyDictionary<string, bool> visibility)
        {
            if (visibility == null)
            {
                return;
            }

            Dictionary<string, Transform> transformsByPath = BuildTransformPathMap();
            foreach (KeyValuePair<string, bool> pair in visibility)
            {
                if (transformsByPath.TryGetValue(pair.Key, out Transform target) && target != null)
                {
                    target.gameObject.SetActive(pair.Value);
                    log?.Invoke($"{target.name}.SetActive({pair.Value}) state");
                }
                else
                {
                    Debug.LogWarning($"Saved room object not found: {pair.Key}");
                }
            }
        }

        // 현재 Room 계층을 저장 경로로 조회할 수 있는 사전으로 만든다.
        private Dictionary<string, Transform> BuildTransformPathMap()
        {
            var map = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (Transform roomRoot in rooms.EnumerateRoots())
            {
                Transform[] transforms = roomRoot.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < transforms.Length; i++)
                {
                    Transform transform = transforms[i];
                    string path = GetScenePath(transform);
                    if (!string.IsNullOrWhiteSpace(path) && !map.ContainsKey(path))
                    {
                        map.Add(path, transform);
                    }
                }
            }

            return map;
        }

        // "경로:1" 또는 "경로:0" 문자열을 경로와 active 상태로 분해한다.
        private static bool TryParseActiveState(string entry, out string path, out bool active)
        {
            path = string.Empty;
            active = false;
            if (string.IsNullOrWhiteSpace(entry))
            {
                return false;
            }

            int separator = entry.LastIndexOf(':');
            if (separator <= 0 || separator >= entry.Length - 1)
            {
                return false;
            }

            path = entry.Substring(0, separator);
            string flag = entry.Substring(separator + 1);
            active = flag == "1" || string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase);
            return !string.IsNullOrWhiteSpace(path);
        }

        // "경로:태그명:once|loop" 문자열을 애니메이션 상태로 분해한다.
        private static bool TryParseAnimationState(
            string entry,
            out string path,
            out string animationName,
            out AsepriteSpritePlaybackMode playbackMode)
        {
            path = string.Empty;
            animationName = string.Empty;
            playbackMode = AsepriteSpritePlaybackMode.Once;
            if (string.IsNullOrWhiteSpace(entry))
            {
                return false;
            }

            int modeSeparator = entry.LastIndexOf(':');
            if (modeSeparator <= 0 || modeSeparator >= entry.Length - 1)
            {
                return false;
            }

            int nameSeparator = entry.LastIndexOf(':', modeSeparator - 1);
            if (nameSeparator <= 0 || nameSeparator >= modeSeparator - 1)
            {
                return false;
            }

            path = entry.Substring(0, nameSeparator);
            animationName = entry.Substring(nameSeparator + 1, modeSeparator - nameSeparator - 1);
            playbackMode = ParsePlaybackMode(entry.Substring(modeSeparator + 1));
            return !string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(animationName);
        }

        // 재생 모드를 저장 문자열로 변환한다.
        private static string FormatPlaybackMode(AsepriteSpritePlaybackMode playbackMode)
        {
            return playbackMode == AsepriteSpritePlaybackMode.Loop ? "loop" : "once";
        }

        // 저장 문자열을 재생 모드로 변환한다.
        private static AsepriteSpritePlaybackMode ParsePlaybackMode(string value)
        {
            return string.Equals((value ?? string.Empty).Trim(), "loop", StringComparison.OrdinalIgnoreCase)
                ? AsepriteSpritePlaybackMode.Loop
                : AsepriteSpritePlaybackMode.Once;
        }

        // Transform의 전체 씬 계층 경로를 만든다.
        private static string GetScenePath(Transform transform)
        {
            if (transform == null)
            {
                return string.Empty;
            }

            var names = new Stack<string>();
            for (Transform current = transform; current != null; current = current.parent)
            {
                names.Push(current.name);
            }

            return string.Join("/", names);
        }
    }
}
