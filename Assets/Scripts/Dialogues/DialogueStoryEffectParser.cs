using System;

namespace Escape.Dialogues
{
    // TSV effect 열에서 스토리 진행에 영향을 주는 토큰 종류를 구분한다.
    public enum DialogueStoryEffect
    {
        None = 0,
        HealthMinusOne = 1,
        HealthPlusOne = 2,
        TimingCheck = 3,
        ReturnTitle = 4,
        EndingCredits = 5,
        EndStory = 6,
        FlashlightOn = 7,
    }

    public static class DialogueStoryEffectParser
    {
        public static DialogueStoryEffect Parse(string token)
        {
            string value = (token ?? string.Empty).Trim();
            value = StripTimingSuffix(value);
            if (string.Equals(value, DialogueEffectTokens.EscapeWillDecreaseOne, StringComparison.Ordinal) ||
                string.Equals(value, "의지감소1", StringComparison.Ordinal) ||
                string.Equals(value, "평정심감소1", StringComparison.Ordinal) ||
                string.Equals(value, "체력감소1", StringComparison.Ordinal))
            {
                return DialogueStoryEffect.HealthMinusOne;
            }

            if (string.Equals(value, "탈출의지회복1", StringComparison.Ordinal) ||
                string.Equals(value, DialogueEffectTokens.EscapeWillIncreaseOne, StringComparison.Ordinal) ||
                string.Equals(value, "의지회복1", StringComparison.Ordinal) ||
                string.Equals(value, "의지증가1", StringComparison.Ordinal) ||
                string.Equals(value, "평정심회복1", StringComparison.Ordinal) ||
                string.Equals(value, "평정심증가1", StringComparison.Ordinal) ||
                string.Equals(value, "체력증가1", StringComparison.Ordinal))
            {
                return DialogueStoryEffect.HealthPlusOne;
            }

            if (string.Equals(value, "타이밍체크", StringComparison.Ordinal))
            {
                return DialogueStoryEffect.TimingCheck;
            }

            if (string.Equals(value, "타이틀로", StringComparison.Ordinal) ||
                string.Equals(value, "RETURN_TITLE", StringComparison.OrdinalIgnoreCase))
            {
                return DialogueStoryEffect.ReturnTitle;
            }

            if (string.Equals(value, "스토리종료", StringComparison.Ordinal) ||
                string.Equals(value, "END_STORY", StringComparison.OrdinalIgnoreCase))
            {
                return DialogueStoryEffect.EndStory;
            }

            if (string.Equals(value, "손전등ON", StringComparison.Ordinal) ||
                string.Equals(value, "FLASHLIGHT_ON", StringComparison.OrdinalIgnoreCase))
            {
                return DialogueStoryEffect.FlashlightOn;
            }

            return string.Equals(value, "엔딩크레딧", StringComparison.Ordinal) ||
                   string.Equals(value, "CREDITS_ROLL", StringComparison.OrdinalIgnoreCase)
                ? DialogueStoryEffect.EndingCredits
                : DialogueStoryEffect.None;
        }

        // 런타임 실행 단계 밖에서도 토큰 종류를 판별할 수 있게 타이밍 접미사를 제거한다.
        private static string StripTimingSuffix(string value)
        {
            string[] suffixes =
            {
                DialogueEffectTokens.BeforeSuffix,
                DialogueEffectTokens.DuringSuffix,
                DialogueEffectTokens.AfterSuffix,
            };
            for (int i = 0; i < suffixes.Length; i++)
            {
                if (value.EndsWith(suffixes[i], StringComparison.Ordinal))
                {
                    return value.Substring(0, value.Length - suffixes[i].Length).Trim();
                }
            }

            return value;
        }
    }
}
