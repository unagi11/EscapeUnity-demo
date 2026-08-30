using UnityEngine;

namespace Escape.SceneFlow
{
    // 인트로를 끝까지 감상한 적 있는지 기기 단위로 저장한다.
    public static class IntroPlaybackState
    {
        private const string PlayerPrefsCompletedIntroKey = "escape.intro.completed";

        // 인트로 완료 이력이 있으면 이후 새 게임에서 감상 여부를 물어볼 수 있다.
        public static bool HasCompletedIntro =>
            PlayerPrefs.GetInt(PlayerPrefsCompletedIntroKey, 0) == 1;

        // 인트로 대사가 정상 종료된 뒤 완료 이력을 저장한다.
        public static void MarkCompletedIntro()
        {
            if (HasCompletedIntro)
            {
                return;
            }

            PlayerPrefs.SetInt(PlayerPrefsCompletedIntroKey, 1);
            PlayerPrefs.Save();
        }
    }
}
