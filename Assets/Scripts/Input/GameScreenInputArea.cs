using UnityEngine;

namespace Escape.Input
{
    // 256x192 기준 게임 화면이 실제 스크린 안에서 차지하는 영역을 계산한다.
    public static class GameScreenInputArea
    {
        public const float ReferenceWidth = 256f;
        public const float ReferenceHeight = 192f;

        // 현재 기기 해상도에서 게임 화면으로 인정되는 중앙 Rect를 반환한다.
        public static Rect GetScreenRect()
        {
            float screenWidth = Mathf.Max(1f, Screen.width);
            float screenHeight = Mathf.Max(1f, Screen.height);
            float scale = Mathf.Min(screenWidth / ReferenceWidth, screenHeight / ReferenceHeight);
            float width = ReferenceWidth * scale;
            float height = ReferenceHeight * scale;
            float x = (screenWidth - width) * 0.5f;
            float y = (screenHeight - height) * 0.5f;

            return new Rect(x, y, width, height);
        }

        // 포인터 좌표가 실제 게임 화면 영역 안에 있는지 확인한다.
        public static bool Contains(Vector2 screenPosition)
        {
            Rect rect = GetScreenRect();
            return screenPosition.x >= rect.xMin &&
                screenPosition.x <= rect.xMax &&
                screenPosition.y >= rect.yMin &&
                screenPosition.y <= rect.yMax;
        }
    }
}
