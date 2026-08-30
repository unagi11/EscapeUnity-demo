using System;
using System.Collections.Generic;

namespace Escape.Audio
{
    // 코드에서 재생할 내장 칩튠 곡을 식별한다.
    public enum ChipSongId
    {
        TitleTheme = 0,
        IntroDream = 2,
        DinnerTable = 3,
        NightHouse = 4,
        GhostBell = 5,
        BasementDread = 6,
        BloodPast = 7,
        SunnyEveryday = 8,
        RetroProbe000 = 9,
        LiebestraumNo3Chip = 10,
        BrightEveryday = 11,
        IntroNeighborDays = 12,
        IntroUneasyCurry = 13,
        EndingEscapeRun = 14,
        EndingAfterimage = 15,
        SpaceInvaderMarch = 16,
        SpaceShooterBoss = 17,
        SpaceShooterClear = 18,
        RythmRecycleKonga = 19,
        TrueEndingSky = 20,
        EndingSelfDefense = 21,
        EndingGoodDay = 22,
        LockPickPuzzle = 23,
    }

    // 칩튠 채널의 기본 파형을 지정한다.
    public enum ChipWaveform
    {
        Square,
        Triangle,
        Bell,
        Sawtooth,
    }

    // 한 칩튠 파형의 음표 배열과 음량을 보관한다.
    [Serializable]
    public sealed class ChipChannel
    {
        public readonly ChipWaveform Waveform;
        public readonly float Gain;
        public readonly string[] Notes;
        public readonly int SustainSteps;

        public ChipChannel(ChipWaveform waveform, float gain, string[] notes, int sustainSteps = 1)
        {
            Waveform = waveform;
            Gain = gain;
            Notes = notes ?? Array.Empty<string>();
            SustainSteps = Math.Max(1, sustainSteps);
        }
    }

    [Serializable]
    public sealed class ChipSong
    {
        public readonly string Id;
        public readonly string Title;
        public readonly int Bpm;
        public readonly int StepsPerBeat;
        public readonly float MasterGain;
        public readonly IReadOnlyList<ChipChannel> Channels;

        // 곡별 마스터 보정을 포함한 재생 데이터를 만든다.
        public ChipSong(string id, string title, int bpm, int stepsPerBeat, float masterGain, params ChipChannel[] channels)
        {
            Id = id;
            Title = title;
            Bpm = Math.Max(1, bpm);
            StepsPerBeat = Math.Max(1, stepsPerBeat);
            MasterGain = Math.Clamp(masterGain, 0.25f, 4f);
            Channels = channels ?? Array.Empty<ChipChannel>();
        }

        public int StepCount
        {
            get
            {
                var count = 0;
                for (var i = 0; i < Channels.Count; i++)
                {
                    count = Math.Max(count, Channels[i].Notes.Length);
                }

                return count;
            }
        }

        public double StepDurationSeconds => 60.0 / Bpm / StepsPerBeat;
    }

    // 곡 라이브러리가 노출하는 정렬 가능한 칩튠 항목이다.
    public readonly struct ChipSongEntry
    {
        public readonly string Id;
        public readonly string Title;
        public readonly ChipSong Song;
        public readonly int Order;

        public ChipSongEntry(string id, string title, ChipSong song, int order)
        {
            Id = id;
            Title = title;
            Song = song;
            Order = order;
        }
    }
}
