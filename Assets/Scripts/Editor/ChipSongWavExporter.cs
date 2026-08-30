using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Escape.Audio;
using UnityEditor;
using UnityEngine;

namespace Escape.EditorTools
{
    // 런타임 칩 신시사이저와 같은 계산식으로 편집용 BGM WAV를 생성한다.
    public static class ChipSongWavExporter
    {
        private const int SampleRate = 48000;
        private const int ChannelCount = 2;
        private const float DefaultBgmVolume = 0.7f;
        private const float OutputGain = 3f;
        private const string OutputDirectory = "Recordings/QA/BGM_Source";

        [MenuItem("Tools/Escape/Audio/Export All BGM WAV")]
        public static void ExportAllFromMenu()
        {
            ExportAll();
            EditorUtility.RevealInFinder(Path.GetFullPath(OutputDirectory));
        }

        // Unity 배치 모드에서도 호출할 수 있도록 공개 진입점을 제공한다.
        public static void ExportAllFromCommandLine()
        {
            ExportAll();
        }

        // 전체 칩송과 언어별 QA 편집 매니페스트를 한 번에 만든다.
        public static void ExportAll()
        {
            ChipSongLibrary.Reload();
            IReadOnlyList<ChipSongEntry> entries = ChipSongLibrary.ListSongs();
            if (entries.Count == 0)
            {
                throw new InvalidOperationException("내보낼 칩 BGM이 없습니다.");
            }

            string root = Path.GetFullPath(OutputDirectory);
            string wavDirectory = Path.Combine(root, "wav");
            string manifestDirectory = Path.Combine(root, "manifests");
            Directory.CreateDirectory(wavDirectory);
            Directory.CreateDirectory(manifestDirectory);

            var rows = new List<ManifestRow>(entries.Count);
            foreach (ChipSongEntry entry in entries.OrderBy(item => item.Order).ThenBy(item => item.Id, StringComparer.Ordinal))
            {
                ChipSong song = entry.Song;
                int loopSampleCount = CalculateLoopSampleCount(song);
                float[] samples = RenderSteadyLoop(song, loopSampleCount);
                string fileName = entry.Id + ".wav";
                WritePcm16StereoWav(Path.Combine(wavDirectory, fileName), samples);

                rows.Add(new ManifestRow(
                    entry.Order,
                    entry.Id,
                    entry.Title,
                    song.Bpm,
                    song.StepsPerBeat,
                    song.StepCount,
                    loopSampleCount / (double)SampleRate,
                    "wav/" + fileName));
            }

            WriteMasterManifest(root, rows);
            WriteLanguageManifests(manifestDirectory, rows);
            WriteReadme(root, rows.Count);
            Debug.Log($"[BGM Export] {rows.Count}개 WAV를 생성했습니다: {root}");
        }

        // 첫 루프의 시작 과도음을 버리고 정상 순환 상태인 두 번째 루프를 반환한다.
        private static float[] RenderSteadyLoop(ChipSong song, int loopSampleCount)
        {
            int totalSampleCount = checked(loopSampleCount * 3);
            var rendered = new float[totalSampleCount];
            var activeNotes = new List<RenderedNote>();
            double sampleDuration = 1.0 / SampleRate;
            double songTime = 0.0;
            double nextStepTime = 0.0;
            int nextStepIndex = 0;

            for (int sampleIndex = 0; sampleIndex < totalSampleCount; sampleIndex++)
            {
                while (songTime + 0.0000001 >= nextStepTime)
                {
                    ScheduleStep(song, nextStepIndex, nextStepTime, songTime, activeNotes);
                    nextStepIndex = (nextStepIndex + 1) % song.StepCount;
                    nextStepTime += song.StepDurationSeconds;
                }

                float mixed = 0f;
                for (int noteIndex = activeNotes.Count - 1; noteIndex >= 0; noteIndex--)
                {
                    RenderedNote note = activeNotes[noteIndex];
                    mixed += note.Next(SampleRate);
                    if (note.Done)
                    {
                        activeNotes.RemoveAt(noteIndex);
                    }
                    else
                    {
                        activeNotes[noteIndex] = note;
                    }
                }

                rendered[sampleIndex] = Mathf.Clamp(
                    mixed * song.MasterGain * OutputGain * DefaultBgmVolume,
                    -1f,
                    1f);
                songTime += sampleDuration;
            }

            var loop = new float[loopSampleCount];
            Array.Copy(rendered, loopSampleCount, loop, 0, loopSampleCount);
            return loop;
        }

        // 현재 스텝의 단음과 화음을 런타임과 같은 길이로 예약한다.
        private static void ScheduleStep(
            ChipSong song,
            int stepIndex,
            double startTime,
            double songTime,
            List<RenderedNote> activeNotes)
        {
            foreach (ChipChannel channel in song.Channels)
            {
                if (channel.Notes.Length == 0)
                {
                    continue;
                }

                string token = channel.Notes[stepIndex % channel.Notes.Length];
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                double duration = song.StepDurationSeconds * channel.SustainSteps *
                                  (channel.SustainSteps > 1 ? 1.02 : 0.96);
                foreach (string noteName in token.Split('+'))
                {
                    if (TryNoteToFrequency(noteName, out double frequency))
                    {
                        activeNotes.Add(new RenderedNote(
                            channel.Waveform,
                            frequency,
                            channel.Gain,
                            duration,
                            Math.Max(0, startTime - songTime)));
                    }
                }
            }
        }

        // 음이름을 MIDI 규칙의 주파수로 변환한다.
        private static bool TryNoteToFrequency(string note, out double frequency)
        {
            frequency = 0;
            if (string.IsNullOrWhiteSpace(note))
            {
                return false;
            }

            note = note.Trim();
            int semitone = note[0] switch
            {
                'C' => 0,
                'D' => 2,
                'E' => 4,
                'F' => 5,
                'G' => 7,
                'A' => 9,
                'B' => 11,
                _ => -100,
            };
            if (semitone < 0)
            {
                return false;
            }

            int index = 1;
            if (index < note.Length && note[index] == '#')
            {
                semitone++;
                index++;
            }
            else if (index < note.Length && note[index] == 'b')
            {
                semitone--;
                index++;
            }

            if (!int.TryParse(note.Substring(index), out int octave))
            {
                return false;
            }

            int midi = (octave + 1) * 12 + semitone;
            frequency = 440.0 * Math.Pow(2.0, (midi - 69) / 12.0);
            return true;
        }

        // 한 루프의 정확한 샘플 수를 계산한다.
        private static int CalculateLoopSampleCount(ChipSong song)
        {
            return Math.Max(1, (int)Math.Round(song.StepCount * song.StepDurationSeconds * SampleRate));
        }

        // 편집 호환성이 높은 48kHz 16-bit 스테레오 WAV를 기록한다.
        private static void WritePcm16StereoWav(string path, IReadOnlyList<float> monoSamples)
        {
            int dataLength = checked(monoSamples.Count * ChannelCount * sizeof(short));
            using var stream = File.Create(path);
            using var writer = new BinaryWriter(stream, Encoding.ASCII, false);
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataLength);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)ChannelCount);
            writer.Write(SampleRate);
            writer.Write(SampleRate * ChannelCount * sizeof(short));
            writer.Write((short)(ChannelCount * sizeof(short)));
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataLength);

            foreach (float sample in monoSamples)
            {
                short pcm = (short)Math.Round(Mathf.Clamp(sample, -1f, 1f) * short.MaxValue);
                writer.Write(pcm);
                writer.Write(pcm);
            }
        }

        // 공용 전체 목록을 CSV로 기록한다.
        private static void WriteMasterManifest(string root, IReadOnlyList<ManifestRow> rows)
        {
            WriteManifest(Path.Combine(root, "bgm_master.csv"), rows, "common");
        }

        // 자동 QA의 세 언어 렌더가 같은 공용 WAV를 참조하도록 목록을 분리한다.
        private static void WriteLanguageManifests(string directory, IReadOnlyList<ManifestRow> rows)
        {
            foreach (string language in new[] { "ko", "en", "ja" })
            {
                WriteManifest(Path.Combine(directory, $"bgm_{language}.csv"), rows, language);
            }
        }

        // 언어 코드와 곡 메타데이터를 안정적인 CSV 형식으로 저장한다.
        private static void WriteManifest(string path, IReadOnlyList<ManifestRow> rows, string language)
        {
            var builder = new StringBuilder();
            builder.AppendLine("language,order,id,title,bpm,steps_per_beat,step_count,duration_seconds,wav_path");
            foreach (ManifestRow row in rows)
            {
                builder.Append(language).Append(',')
                    .Append(row.Order).Append(',')
                    .Append(EscapeCsv(row.Id)).Append(',')
                    .Append(EscapeCsv(row.Title)).Append(',')
                    .Append(row.Bpm).Append(',')
                    .Append(row.StepsPerBeat).Append(',')
                    .Append(row.StepCount).Append(',')
                    .Append(row.DurationSeconds.ToString("0.000000", CultureInfo.InvariantCulture)).Append(',')
                    .Append(EscapeCsv(row.WavPath)).AppendLine();
            }

            File.WriteAllText(path, builder.ToString(), new UTF8Encoding(true));
        }

        // CSV의 쉼표와 따옴표를 안전하게 이스케이프한다.
        private static string EscapeCsv(string value)
        {
            value ??= string.Empty;
            return value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0
                ? '"' + value.Replace("\"", "\"\"") + '"'
                : value;
        }

        // 산출물의 용도와 언어 공용 관계를 설명한다.
        private static void WriteReadme(string root, int songCount)
        {
            string text =
                "# EscapeUnity BGM Source\n\n" +
                $"- 트랙 수: {songCount}\n" +
                "- 형식: PCM WAV, 48 kHz, 16-bit, stereo\n" +
                "- 음량: 인게임 기본 BGM 볼륨 0.7 적용\n" +
                "- 루프: 시작 과도음을 제외한 정상 순환 상태의 1회 루프\n" +
                "- 언어: BGM 합성 데이터는 ko/en/ja 공용이며 manifests 폴더의 언어별 CSV가 같은 WAV를 참조함\n" +
                "- 원본 데이터: Assets/Resources/ChipSongs/*.json\n";
            File.WriteAllText(Path.Combine(root, "README.md"), text, new UTF8Encoding(true));
        }

        private readonly struct ManifestRow
        {
            public readonly int Order;
            public readonly string Id;
            public readonly string Title;
            public readonly int Bpm;
            public readonly int StepsPerBeat;
            public readonly int StepCount;
            public readonly double DurationSeconds;
            public readonly string WavPath;

            // 내보낸 곡의 편집용 메타데이터를 한 행으로 묶는다.
            public ManifestRow(
                int order,
                string id,
                string title,
                int bpm,
                int stepsPerBeat,
                int stepCount,
                double durationSeconds,
                string wavPath)
            {
                Order = order;
                Id = id;
                Title = title;
                Bpm = bpm;
                StepsPerBeat = stepsPerBeat;
                StepCount = stepCount;
                DurationSeconds = durationSeconds;
                WavPath = wavPath;
            }
        }

        // 오프라인 합성 중인 단음의 파형과 엔벌로프 상태를 보관한다.
        private struct RenderedNote
        {
            private readonly ChipWaveform waveform;
            private readonly double frequency;
            private readonly float gain;
            private readonly double duration;
            private readonly double delay;
            private double phase;
            private double age;

            public RenderedNote(ChipWaveform waveform, double frequency, float gain, double duration, double delay)
            {
                this.waveform = waveform;
                this.frequency = frequency;
                this.gain = gain;
                this.duration = Math.Max(0.01, duration);
                this.delay = delay;
                phase = 0;
                age = 0;
            }

            public bool Done => age >= delay + duration + 0.02;

            // 다음 샘플의 파형과 엔벌로프를 계산한다.
            public float Next(int sampleRate)
            {
                age += 1.0 / sampleRate;
                if (age < delay || age > delay + duration)
                {
                    return 0f;
                }

                double localAge = age - delay;
                phase += frequency / sampleRate;
                phase -= Math.Floor(phase);
                float envelope = waveform == ChipWaveform.Bell
                    ? BellEnvelope(localAge, duration)
                    : Envelope(localAge, duration);
                return Wave(phase, waveform) * gain * envelope;
            }

            // 벨 파형의 빠른 어택과 지수 감쇠를 계산한다.
            private static float BellEnvelope(double currentAge, double noteDuration)
            {
                const double attack = 0.006;
                if (currentAge < attack)
                {
                    return Mathf.Clamp01((float)(currentAge / attack));
                }

                return (float)Math.Exp(-5.0 * (currentAge - attack) / Math.Max(0.01, noteDuration - attack));
            }

            // 사각·삼각·톱니 파형의 어택과 릴리스를 계산한다.
            private static float Envelope(double currentAge, double noteDuration)
            {
                const double attack = 0.018;
                const double floor = 0.0001;
                if (currentAge < attack)
                {
                    return Mathf.Lerp((float)floor, 1f, (float)(currentAge / attack));
                }

                double releaseStart = Math.Max(attack, noteDuration - 0.04);
                return currentAge > releaseStart
                    ? Mathf.Clamp01((float)((noteDuration - currentAge) / Math.Max(0.001, noteDuration - releaseStart)))
                    : 1f;
            }

            // 런타임과 동일한 칩 파형 값을 반환한다.
            private static float Wave(double currentPhase, ChipWaveform currentWaveform)
            {
                return currentWaveform switch
                {
                    ChipWaveform.Square => currentPhase < 0.5 ? 1f : -1f,
                    ChipWaveform.Triangle => (float)(1.0 - 4.0 * Math.Abs(Math.Round(currentPhase - 0.25) - (currentPhase - 0.25))),
                    ChipWaveform.Bell => BellWave(currentPhase),
                    ChipWaveform.Sawtooth => (float)(2.0 * currentPhase - 1.0),
                    _ => 0f,
                };
            }

            // 기본음과 세 배음을 섞어 벨 음색을 만든다.
            private static float BellWave(double currentPhase)
            {
                double angle = currentPhase * Math.PI * 2.0;
                return (float)(
                    Math.Sin(angle) * 0.56 +
                    Math.Sin(angle * 2.0) * 0.26 +
                    Math.Sin(angle * 3.0) * 0.12 +
                    Math.Sin(angle * 4.0) * 0.06);
            }
        }
    }
}
