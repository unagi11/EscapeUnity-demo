using System;

namespace Escape.Audio
{
    // SFX 한 조각(노트/스윕/노이즈 등)을 나타내는 직렬화용 세그먼트.
    // type에 따라 사용하는 필드가 다르다(아래 주석 참고).
    [Serializable]
    public sealed class ChipSfxSegment
    {
        // square | squareSweep | sweep | bell | electric | piezo | noise | filteredNoise
        public string type = "square";
        public double start;            // 시작 시각(초)
        public double duration;         // 길이(초)
        public double frequency;        // square/bell/electric/piezo 고정 주파수
        public double startFrequency;   // sweep/squareSweep 시작 주파수
        public double endFrequency;     // sweep/squareSweep 끝 주파수
        public float volume = 0.3f;     // 세그먼트 진폭
        public float response = 0.05f;  // filteredNoise 저역 필터 응답(0~1)
        public long seed = 1;           // noise/filteredNoise 난수 시드
    }

    // SFX 하나의 전체 정의: 총 길이 + 세그먼트 목록.
    // JsonUtility로 그대로 직렬화되며, StreamingAssets/ChipSfx/<id>.json 형식이 된다.
    [Serializable]
    public sealed class ChipSfxDefinition
    {
        public string id = string.Empty;
        public string clipName = string.Empty;
        public double durationSeconds;  // 0이면 세그먼트 끝 시각에서 자동 계산
        public ChipSfxSegment[] segments = Array.Empty<ChipSfxSegment>();
    }
}
