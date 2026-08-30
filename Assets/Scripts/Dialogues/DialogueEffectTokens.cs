using System;

namespace Escape.Dialogues
{
    // Dialogue effect DSL에서 공유하는 토큰과 타이밍 조합 규칙을 한곳에서 관리한다.
    internal static class DialogueEffectTokens
    {
        public const string RedFadeOn = "빨강ON";
        public const string RedFadeOff = "빨강OFF";
        public const string GreenFadeOn = "초록ON";
        public const string GreenFadeOff = "초록OFF";
        public const string EscapeWillDecreaseOne = "탈출의지감소1";
        public const string EscapeWillIncreaseOne = "탈출의지증가1";
        public const string BeforeSuffix = ":대사전";
        public const string DuringSuffix = ":대사중";
        public const string AfterSuffix = ":대사후";

        // effect를 대사 시작 전에 실행하도록 표시한다.
        public static string Before(string effect)
        {
            return effect + BeforeSuffix;
        }

        // effect를 대사가 끝난 뒤 실행하도록 표시한다.
        public static string After(string effect)
        {
            return effect + AfterSuffix;
        }

        // 여러 effect를 TSV와 같은 실행 순서 문자열로 합친다.
        public static string Sequence(params string[] effects)
        {
            return string.Join("+", effects ?? Array.Empty<string>());
        }
    }
}
