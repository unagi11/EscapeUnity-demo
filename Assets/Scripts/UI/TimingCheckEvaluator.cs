namespace Escape.UI
{
    // 슬라이더 위치가 타이밍 체크의 안전 구간 안에 있는지 판정한다.
    public static class TimingCheckEvaluator
    {
        public static bool IsSuccess(float value, float safeWindowStart, float safeWindowEnd)
        {
            float minimum = safeWindowStart <= safeWindowEnd ? safeWindowStart : safeWindowEnd;
            float maximum = safeWindowStart <= safeWindowEnd ? safeWindowEnd : safeWindowStart;
            return value >= minimum && value <= maximum;
        }

        // 지정 범위와 창 폭 안에서 난수 비율에 맞는 안전 구간 시작점을 계산한다.
        public static float GetRandomizedSafeWindowStart(
            float randomValue,
            float minimumStart,
            float maximumStart,
            float windowWidth)
        {
            float normalizedRandom = Clamp01(randomValue);
            float width = Clamp01(windowWidth);
            float lowerBound = Clamp01(minimumStart);
            float requestedUpperBound = Clamp01(maximumStart);
            float upperBound = requestedUpperBound < lowerBound ? lowerBound : requestedUpperBound;
            upperBound = upperBound > 1f - width ? 1f - width : upperBound;
            lowerBound = lowerBound > upperBound ? upperBound : lowerBound;
            return lowerBound + ((upperBound - lowerBound) * normalizedRandom);
        }

        private static float Clamp01(float value)
        {
            if (value <= 0f)
            {
                return 0f;
            }

            return value >= 1f ? 1f : value;
        }
    }
}
