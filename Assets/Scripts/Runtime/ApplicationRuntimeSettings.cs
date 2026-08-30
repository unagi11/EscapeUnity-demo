using UnityEngine;

namespace Escape.Runtime
{
    // 게임 실행 환경을 첫 씬보다 먼저 설정한다.
    public static class ApplicationRuntimeSettings
    {
        private const int TargetFrameRate = 60;

        // 플랫폼과 모니터 주사율에 관계없이 60FPS를 목표로 사용한다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Apply()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = TargetFrameRate;
        }
    }
}
