using UnityEngine;

namespace Escape.Runtime
{
    // 화면 관련 사용자 설정(창 모드)을 관리하고 PlayerPrefs에 영속화한다.
    // 창 모드는 데스크톱 스탠드얼론에서만 의미가 있으므로 모바일에서는 무시된다.
    public static class ScreenSettings
    {
        private const string PlayerPrefsFullscreenKey = "escape.screen.fullscreen";

        private static readonly Vector2Int BaseResolution = new(256, 192);
        private const int DefaultWindowedScale = 3;

        // 창 모드 컨트롤을 노출할지 여부. 데스크톱 스탠드얼론에서만 true.
        public static bool SupportsDisplayControls =>
            Application.platform == RuntimePlatform.WindowsPlayer ||
            Application.platform == RuntimePlatform.OSXPlayer ||
            Application.platform == RuntimePlatform.LinuxPlayer ||
            Application.isEditor;

        public static bool IsFullScreen => Screen.fullScreenMode != FullScreenMode.Windowed;

        // 실행 시에는 네이티브 스플래시의 화면 상태를 유지하고, 사용자가 설정을 바꿀 때만 적용한다.
        public static void SetFullScreen(bool fullScreen)
        {
            PlayerPrefs.SetInt(PlayerPrefsFullscreenKey, fullScreen ? 1 : 0);
            PlayerPrefs.Save();

            if (SupportsDisplayControls)
            {
                ApplyDisplaySettings(fullScreen);
            }
        }

        private static void ApplyDisplaySettings(bool fullScreen)
        {
            FullScreenMode mode = fullScreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
            Vector2Int targetResolution;

            if (fullScreen)
            {
                // 전체화면은 네이티브 디스플레이 해상도를 사용한다.
                Resolution native = Screen.currentResolution;
                targetResolution = new Vector2Int(native.width, native.height);
            }
            else
            {
                // 네이티브 스플래시와 같은 3배 정수배 크기를 유지해 씬 진입 시 창이 튀지 않게 한다.
                targetResolution = BaseResolution * DefaultWindowedScale;
            }

            if (Screen.fullScreenMode == mode &&
                Screen.width == targetResolution.x &&
                Screen.height == targetResolution.y)
            {
                return;
            }

            Screen.SetResolution(targetResolution.x, targetResolution.y, mode);
        }
    }
}
