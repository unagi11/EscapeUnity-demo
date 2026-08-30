using System;
using System.Collections.Generic;

namespace Escape.Rooms
{
    // 대화 스토리 재생 중 선택된 flag와 게임오버 요청을 보관한다.
    public sealed class DialogueStoryResult
    {
        private readonly HashSet<string> flags = new(StringComparer.Ordinal);

        public string SourceDialogueId { get; }
        public IReadOnlyCollection<string> Flags => flags;
        public bool RequestsGameOver { get; private set; }

        // 결과가 시작된 원본 대사 ID를 기록한다.
        public DialogueStoryResult(string sourceDialogueId)
        {
            SourceDialogueId = sourceDialogueId ?? string.Empty;
        }

        // 선택 결과에 지정 flag가 포함됐는지 확인한다.
        public bool HasFlag(string flag)
        {
            return !string.IsNullOrWhiteSpace(flag) && flags.Contains(flag);
        }

        // 지정 접미사로 끝나는 선택 flag 개수를 반환한다.
        public int CountFlagsEndingWith(string suffix)
        {
            if (string.IsNullOrWhiteSpace(suffix))
            {
                return 0;
            }

            int count = 0;
            foreach (string flag in flags)
            {
                if (!string.IsNullOrWhiteSpace(flag) && flag.EndsWith(suffix, StringComparison.Ordinal))
                {
                    count++;
                }
            }

            return count;
        }

        // DialoguePlayer가 선택된 flag 하나를 결과에 추가한다.
        internal void AddFlag(string flag)
        {
            if (!string.IsNullOrWhiteSpace(flag))
            {
                flags.Add(flag.Trim());
            }
        }

        // 대사 효과가 게임오버 후속 흐름을 요청했음을 기록한다.
        internal void RequestGameOver()
        {
            RequestsGameOver = true;
        }
    }
}
