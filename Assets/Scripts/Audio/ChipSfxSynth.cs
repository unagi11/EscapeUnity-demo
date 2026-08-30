using System;
using UnityEngine;

namespace Escape.Audio
{
    // ChipSfxDefinition(세그먼트 데이터)을 실제 AudioClip으로 합성한다.
    // 파형 프리미티브는 과거 ChipSfxPlayer의 것을 그대로 옮긴 것이라 소리가 동일하다.
    public static class ChipSfxSynth
    {
        // 정의를 받아 샘플 버퍼에 세그먼트를 순서대로 합성한 클립을 만든다.
        public static AudioClip Build(ChipSfxDefinition definition, int sampleRate)
        {
            if (definition == null || sampleRate <= 0)
            {
                return null;
            }

            double duration = definition.durationSeconds;
            if (duration <= 0.0)
            {
                duration = ResolveDuration(definition);
            }

            int sampleCount = Mathf.CeilToInt((float)(duration * sampleRate));
            if (sampleCount <= 0)
            {
                return null;
            }

            var samples = new float[sampleCount];
            ChipSfxSegment[] segments = definition.segments ?? Array.Empty<ChipSfxSegment>();
            for (int i = 0; i < segments.Length; i++)
            {
                ApplySegment(samples, sampleRate, segments[i]);
            }

            string name = string.IsNullOrWhiteSpace(definition.clipName)
                ? $"Sfx_{definition.id}"
                : definition.clipName;
            return CreateClip(name, samples, sampleRate);
        }

        // 세그먼트가 끝나는 가장 늦은 시각을 총 길이로 삼는다.
        private static double ResolveDuration(ChipSfxDefinition definition)
        {
            double max = 0.0;
            ChipSfxSegment[] segments = definition.segments ?? Array.Empty<ChipSfxSegment>();
            for (int i = 0; i < segments.Length; i++)
            {
                ChipSfxSegment segment = segments[i];
                if (segment == null)
                {
                    continue;
                }

                max = Math.Max(max, segment.start + segment.duration);
            }

            return max;
        }

        // 세그먼트 type에 맞는 파형 프리미티브를 호출한다.
        private static void ApplySegment(float[] samples, int sampleRate, ChipSfxSegment segment)
        {
            if (segment == null)
            {
                return;
            }

            switch ((segment.type ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "square":
                    WriteSquareNote(samples, sampleRate, segment.start, segment.duration, segment.frequency, segment.volume);
                    break;
                case "squaresweep":
                    WriteSquareSweep(samples, sampleRate, segment.start, segment.duration, segment.startFrequency, segment.endFrequency, segment.volume);
                    break;
                case "sweep":
                    WriteBeepSweep(samples, sampleRate, segment.start, segment.duration, segment.startFrequency, segment.endFrequency, segment.volume);
                    break;
                case "bell":
                    WriteBellNote(samples, sampleRate, segment.start, segment.duration, segment.frequency, segment.volume);
                    break;
                case "electric":
                    WriteElectricBeep(samples, sampleRate, segment.start, segment.duration, segment.frequency, segment.volume);
                    break;
                case "piezo":
                    WritePiezoTone(samples, sampleRate, segment.start, segment.duration, segment.frequency, segment.volume);
                    break;
                case "noise":
                    WriteNoiseBurst(samples, sampleRate, segment.start, segment.duration, segment.volume, (uint)segment.seed);
                    break;
                case "filterednoise":
                    WriteFilteredNoise(samples, sampleRate, segment.start, segment.duration, segment.volume, segment.response, (uint)segment.seed);
                    break;
                default:
                    Debug.LogWarning($"Unknown chip sfx segment type: {segment.type}");
                    break;
            }
        }

        private static AudioClip CreateClip(string clipName, float[] samples, int sampleRate)
        {
            var clip = AudioClip.Create(clipName, samples.Length, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static void WriteSquareNote(float[] samples, int sampleRate, double startTime, double duration, double frequency, float volume)
        {
            int startSample = Mathf.RoundToInt((float)(startTime * sampleRate));
            int endSample = Mathf.Min(samples.Length, startSample + Mathf.RoundToInt((float)(duration * sampleRate)));

            for (var sample = startSample; sample < endSample; sample++)
            {
                double localTime = (sample - startSample) / (double)sampleRate;
                double phase = localTime * frequency;
                float wave = phase - Math.Floor(phase) < 0.5 ? 1f : -1f;
                samples[sample] += wave * Envelope(localTime, duration) * volume;
            }
        }

        // 주파수가 선형 변하는 사각파(포트레이트 바운스 등).
        private static void WriteSquareSweep(float[] samples, int sampleRate, double startTime, double duration, double startFrequency, double endFrequency, float volume)
        {
            int startSample = Mathf.RoundToInt((float)(startTime * sampleRate));
            int endSample = Mathf.Min(samples.Length, startSample + Mathf.RoundToInt((float)(duration * sampleRate)));
            double phase = 0.0;

            for (var sample = startSample; sample < endSample; sample++)
            {
                double localTime = (sample - startSample) / (double)sampleRate;
                double t = Mathf.Clamp01((float)(localTime / duration));
                double frequency = Mathf.Lerp((float)startFrequency, (float)endFrequency, (float)t);
                phase += frequency / sampleRate;
                float wave = phase - Math.Floor(phase) < 0.5 ? 1f : -1f;
                samples[sample] += wave * Envelope(localTime, duration) * volume;
            }
        }

        private static void WriteBeepSweep(float[] samples, int sampleRate, double startTime, double duration, double startFrequency, double endFrequency, float volume)
        {
            int startSample = Mathf.RoundToInt((float)(startTime * sampleRate));
            int endSample = Mathf.Min(samples.Length, startSample + Mathf.RoundToInt((float)(duration * sampleRate)));
            double phase = 0.0;

            for (var sample = startSample; sample < endSample; sample++)
            {
                double localTime = (sample - startSample) / (double)sampleRate;
                double t = Mathf.Clamp01((float)(localTime / duration));
                double frequency = Mathf.Lerp((float)startFrequency, (float)endFrequency, (float)t);
                phase += frequency / sampleRate;
                float wave = Mathf.Sin((float)(phase * Math.PI * 2.0));
                samples[sample] += wave * Envelope(localTime, duration) * volume;
            }
        }

        private static void WriteBellNote(float[] samples, int sampleRate, double startTime, double duration, double frequency, float volume)
        {
            int startSample = Mathf.RoundToInt((float)(startTime * sampleRate));
            int endSample = Mathf.Min(samples.Length, startSample + Mathf.RoundToInt((float)(duration * sampleRate)));

            for (var sample = startSample; sample < endSample; sample++)
            {
                double localTime = (sample - startSample) / (double)sampleRate;
                double phase = localTime * frequency * Math.PI * 2.0;
                float decay = Mathf.Exp((float)(-6.0 * localTime / duration));
                float wave = Mathf.Sin((float)phase) * 0.78f +
                             Mathf.Sin((float)(phase * 2.01)) * 0.16f +
                             Mathf.Sin((float)(phase * 3.98)) * 0.06f;
                samples[sample] += wave * decay * volume;
            }
        }

        private static void WriteElectricBeep(float[] samples, int sampleRate, double startTime, double duration, double frequency, float volume)
        {
            int startSample = Mathf.RoundToInt((float)(startTime * sampleRate));
            int endSample = Mathf.Min(samples.Length, startSample + Mathf.RoundToInt((float)(duration * sampleRate)));

            for (var sample = startSample; sample < endSample; sample++)
            {
                double localTime = (sample - startSample) / (double)sampleRate;
                double phase = localTime * frequency;
                float square = phase - Math.Floor(phase) < 0.5 ? 1f : -1f;
                float overtone = Mathf.Sin((float)(phase * Math.PI * 4.0)) * 0.28f;
                samples[sample] += (square * 0.72f + overtone) * Envelope(localTime, duration) * volume;
            }
        }

        private static void WritePiezoTone(float[] samples, int sampleRate, double startTime, double duration, double frequency, float volume)
        {
            int startSample = Mathf.RoundToInt((float)(startTime * sampleRate));
            int endSample = Mathf.Min(samples.Length, startSample + Mathf.RoundToInt((float)(duration * sampleRate)));

            for (var sample = startSample; sample < endSample; sample++)
            {
                double localTime = (sample - startSample) / (double)sampleRate;
                double phase = localTime * frequency * Math.PI * 2.0;
                float main = Mathf.Sin((float)phase);
                float shimmer = Mathf.Sin((float)(phase * 2.0)) * 0.22f;
                samples[sample] += (main + shimmer) * Envelope(localTime, duration) * volume;
            }
        }

        private static void WriteNoiseBurst(float[] samples, int sampleRate, double startTime, double duration, float volume, uint seed)
        {
            int startSample = Mathf.RoundToInt((float)(startTime * sampleRate));
            int endSample = Mathf.Min(samples.Length, startSample + Mathf.RoundToInt((float)(duration * sampleRate)));
            uint state = seed == 0 ? 1u : seed;

            for (var sample = startSample; sample < endSample; sample++)
            {
                double localTime = (sample - startSample) / (double)sampleRate;
                state = state * 1664525u + 1013904223u;
                float noise = ((state >> 8) / 16777215f) * 2f - 1f;
                samples[sample] += noise * Envelope(localTime, duration) * volume;
            }
        }

        // 난수를 1차 필터로 부드럽게 만들어 물·천·저음 마찰에 사용한다.
        private static void WriteFilteredNoise(float[] samples, int sampleRate, double startTime, double duration, float volume, float response, uint seed)
        {
            int startSample = Mathf.RoundToInt((float)(startTime * sampleRate));
            int endSample = Mathf.Min(samples.Length, startSample + Mathf.RoundToInt((float)(duration * sampleRate)));
            uint state = seed == 0 ? 1u : seed;
            float filtered = 0f;
            float blend = Mathf.Clamp(response, 0.001f, 1f);

            for (var sample = startSample; sample < endSample; sample++)
            {
                double localTime = (sample - startSample) / (double)sampleRate;
                state = state * 1664525u + 1013904223u;
                float noise = ((state >> 8) / 16777215f) * 2f - 1f;
                filtered += (noise - filtered) * blend;
                samples[sample] += filtered * Envelope(localTime, duration) * volume;
            }
        }

        private static float Envelope(double time, double duration)
        {
            const double attack = 0.006;
            const double release = 0.018;

            if (time < attack)
            {
                return Mathf.Clamp01((float)(time / attack));
            }

            double releaseStart = Math.Max(attack, duration - release);
            if (time > releaseStart)
            {
                return Mathf.Clamp01((float)((duration - time) / Math.Max(0.001, duration - releaseStart)));
            }

            return 1f;
        }
    }
}
