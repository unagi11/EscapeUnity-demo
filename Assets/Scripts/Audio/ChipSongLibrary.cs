using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Escape.Audio
{
    public static class ChipSongLibrary
    {
        public const string EventPrefix = "event:/chip/";

        private const string SongDirectoryName = "ChipSongs";
        private const string SongResourcePath = SongDirectoryName;

        private static readonly object loadLock = new();
        private static readonly Dictionary<ChipSongId, string> IdsByEnum = new Dictionary<ChipSongId, string>
        {
            { ChipSongId.TitleTheme, "title_theme" },
            { ChipSongId.IntroDream, "intro_dream" },
            { ChipSongId.DinnerTable, "dinner_table" },
            { ChipSongId.NightHouse, "night_house" },
            { ChipSongId.GhostBell, "ghost_bell" },
            { ChipSongId.BasementDread, "basement_dread" },
            { ChipSongId.BloodPast, "blood_past" },
            { ChipSongId.SunnyEveryday, "sunny_everyday" },
            { ChipSongId.RetroProbe000, "retro_probe_000" },
            { ChipSongId.LiebestraumNo3Chip, "liebestraum_no3_chip" },
            { ChipSongId.BrightEveryday, "bright_everyday" },
            { ChipSongId.IntroNeighborDays, "intro_neighbor_days" },
            { ChipSongId.IntroUneasyCurry, "intro_uneasy_curry" },
            { ChipSongId.EndingEscapeRun, "ending_escape_run" },
            { ChipSongId.EndingAfterimage, "ending_afterimage" },
            { ChipSongId.SpaceInvaderMarch, "space_invader_march" },
            { ChipSongId.SpaceShooterBoss, "space_shooter_boss" },
            { ChipSongId.SpaceShooterClear, "space_shooter_clear" },
            { ChipSongId.RythmRecycleKonga, "recycle_sort_stack" },
            { ChipSongId.TrueEndingSky, "true_ending_sky" },
            { ChipSongId.EndingSelfDefense, "ending_self_defense" },
            { ChipSongId.EndingGoodDay, "ending_good_day" },
            { ChipSongId.LockPickPuzzle, "lockpick_puzzle" },
        };

        // 대표 구간 RMS를 기준으로 맞춘 곡별 마스터 보정값이다.
        private static readonly Dictionary<string, float> MasterGainById = new(StringComparer.Ordinal)
        {
            { "basement_dread", 0.80f },
            { "blood_past", 0.96f },
            { "bright_everyday", 1.16f },
            { "dinner_table", 0.72f },
            { "ending_afterimage", 1.31f },
            { "ending_escape_run", 1.21f },
            { "ending_good_day", 1.18f },
            { "ending_self_defense", 0.96f },
            { "entrance_lock_unease", 3.00f },
            { "ghost_bell", 1.19f },
            { "intro_dream", 0.56f },
            { "intro_neighbor_days", 1.30f },
            { "intro_uneasy_curry", 2.31f },
            { "liebestraum_no3_chip", 1.04f },
            { "lockpick_puzzle", 1.55f },
            { "night_house", 0.80f },
            { "recycle_sort_stack", 2.50f },
            { "retro_probe_000", 1.06f },
            { "space_invader_march", 0.96f },
            { "space_shooter_boss", 0.94f },
            { "space_shooter_clear", 0.86f },
            { "sunny_everyday", 1.35f },
            { "title_theme", 0.87f },
            { "true_ending_sky", 1.365f },
        };

        private static ChipSongEntry[] entries = Array.Empty<ChipSongEntry>();
        private static Dictionary<string, ChipSongEntry> entriesById = new(StringComparer.Ordinal);
        private static bool loaded;

        public static string SongDirectoryPath => Path.Combine(Application.streamingAssetsPath, SongDirectoryName);
        public static string ResourceSongDirectoryPath => Path.Combine(Application.dataPath, "Resources", SongDirectoryName);

        public static IReadOnlyList<ChipSongEntry> ListSongs()
        {
            EnsureLoaded();
            return entries;
        }

        public static void Reload()
        {
            lock (loadLock)
            {
                loaded = false;
                LoadSongs();
            }
        }

        // 현재 로드된 곡들을 ChipSongs 폴더에 JSON으로 다시 써서 편집용 파일로 만든다.
        // 반환값은 저장한 파일 수.
        public static int ExportSongs()
        {
            EnsureLoaded();
            Directory.CreateDirectory(SongDirectoryPath);
            Directory.CreateDirectory(ResourceSongDirectoryPath);

            int count = 0;
            for (var i = 0; i < entries.Length; i++)
            {
                ChipSongEntry entry = entries[i];
                ChipSong song = entry.Song;
                var channelFiles = new PackedChipChannelFile[song.Channels.Count];
                for (var c = 0; c < song.Channels.Count; c++)
                {
                    channelFiles[c] = CreateChannelFile(song.Channels[c]);
                }

                var file = new PackedChipSongFile
                {
                    id = entry.Id,
                    title = entry.Title,
                    order = entry.Order,
                    bpm = song.Bpm,
                    stepsPerBeat = song.StepsPerBeat,
                    channels = channelFiles,
                };

                string json = JsonUtility.ToJson(file, false);
                string path = Path.Combine(SongDirectoryPath, $"{entry.Id}.json");
                File.WriteAllText(path, json, Encoding.UTF8);
                File.WriteAllText(Path.Combine(ResourceSongDirectoryPath, $"{entry.Id}.json"), json, Encoding.UTF8);
                count++;
            }

            return count;
        }

        public static bool TryGetEntry(string songIdOrEventId, out ChipSongEntry entry)
        {
            EnsureLoaded();
            return entriesById.TryGetValue(NormalizeSongId(songIdOrEventId), out entry);
        }

        public static bool TryGetEntry(ChipSongId songId, out ChipSongEntry entry)
        {
            EnsureLoaded();
            return entriesById.TryGetValue(ToSongId(songId), out entry);
        }

        public static bool TryGetSong(string songIdOrEventId, out ChipSong song)
        {
            if (TryGetEntry(songIdOrEventId, out var entry))
            {
                song = entry.Song;
                return true;
            }

            song = null;
            return false;
        }

        public static bool TryGetSong(ChipSongId songId, out ChipSong song)
        {
            if (TryGetEntry(songId, out var entry))
            {
                song = entry.Song;
                return true;
            }

            song = null;
            return false;
        }

        public static bool IsChipEvent(string eventId)
        {
            return !string.IsNullOrWhiteSpace(eventId) && eventId.StartsWith(EventPrefix, StringComparison.Ordinal);
        }

        public static string ToSongId(ChipSongId songId)
        {
            return IdsByEnum.TryGetValue(songId, out var id) ? id : "title_theme";
        }

        public static string ToEventId(ChipSongId songId)
        {
            return EventPrefix + ToSongId(songId);
        }

        public static string NormalizeSongId(string songIdOrEventId)
        {
            if (string.IsNullOrWhiteSpace(songIdOrEventId))
            {
                return string.Empty;
            }

            return IsChipEvent(songIdOrEventId) ? songIdOrEventId.Substring(EventPrefix.Length) : songIdOrEventId;
        }

        private static void EnsureLoaded()
        {
            if (loaded)
            {
                return;
            }

            lock (loadLock)
            {
                if (!loaded)
                {
                    LoadSongs();
                }
            }
        }

        private static void LoadSongs()
        {
            var nextEntriesById = new Dictionary<string, ChipSongEntry>(StringComparer.Ordinal);

            LoadResourceSongs(nextEntriesById);
            LoadStreamingAssetSongs(nextEntriesById);

            if (nextEntriesById.Count == 0)
            {
                Debug.LogWarning(
                    $"No chip songs loaded. Resources path='{SongResourcePath}', " +
                    $"StreamingAssets path='{SongDirectoryPath}'");
                entries = Array.Empty<ChipSongEntry>();
            }
            else
            {
                entries = nextEntriesById.Values
                    .OrderBy(entry => entry.Order)
                    .ThenBy(entry => entry.Id, StringComparer.Ordinal)
                    .ToArray();
            }

            entriesById = nextEntriesById;
            loaded = true;
        }

        private static void LoadResourceSongs(Dictionary<string, ChipSongEntry> nextEntriesById)
        {
            TextAsset[] assets = Resources.LoadAll<TextAsset>(SongResourcePath);
            foreach (TextAsset asset in assets.OrderBy(asset => asset.name, StringComparer.Ordinal))
            {
                if (asset == null)
                {
                    continue;
                }

                AddSongJson(nextEntriesById, asset.text, $"{SongResourcePath}/{asset.name}.json");
            }
        }

        private static void LoadStreamingAssetSongs(Dictionary<string, ChipSongEntry> nextEntriesById)
        {
            var directory = SongDirectoryPath;
            if (!Directory.Exists(directory))
            {
                return;
            }

            foreach (var path in Directory.GetFiles(directory, "*.json").OrderBy(path => path, StringComparer.Ordinal))
            {
                try
                {
                    AddSongJson(nextEntriesById, File.ReadAllText(path, Encoding.UTF8), path);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Failed to load chip song json: {path}\n{ex}");
                }
            }
        }

        private static void AddSongJson(Dictionary<string, ChipSongEntry> nextEntriesById, string json, string sourceName)
        {
            try
            {
                var songFile = JsonUtility.FromJson<ChipSongFile>(json);
                if (TryBuildEntry(songFile, sourceName, out var entry))
                {
                    nextEntriesById[entry.Id] = entry;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to load chip song json: {sourceName}\n{ex}");
            }
        }

        // 그리드 에디터에서 편집한 곡을 ChipSongs/<id>.json으로 저장하고 다시 읽는다.
        public static void SaveSongJson(string id, string title, int order, int bpm, int stepsPerBeat, IReadOnlyList<ChipChannel> channels)
        {
            id = NormalizeSongId(id);
            if (string.IsNullOrWhiteSpace(id))
            {
                return;
            }

            channels ??= Array.Empty<ChipChannel>();
            var channelFiles = new PackedChipChannelFile[channels.Count];
            for (var c = 0; c < channels.Count; c++)
            {
                channelFiles[c] = CreateChannelFile(channels[c]);
            }

            var file = new PackedChipSongFile
            {
                id = id,
                title = string.IsNullOrWhiteSpace(title) ? id : title,
                order = order,
                bpm = Math.Max(1, bpm),
                stepsPerBeat = Math.Max(1, stepsPerBeat),
                channels = channelFiles,
            };

            string json = JsonUtility.ToJson(file, false);
            Directory.CreateDirectory(SongDirectoryPath);
            Directory.CreateDirectory(ResourceSongDirectoryPath);
            File.WriteAllText(Path.Combine(SongDirectoryPath, $"{id}.json"), json, Encoding.UTF8);
            File.WriteAllText(Path.Combine(ResourceSongDirectoryPath, $"{id}.json"), json, Encoding.UTF8);
            Reload();
        }

        private static bool TryBuildEntry(ChipSongFile songFile, string sourceName, out ChipSongEntry entry)
        {
            entry = default;
            if (songFile == null)
            {
                Debug.LogWarning($"Chip song json is empty or invalid: {sourceName}");
                return false;
            }

            var id = NormalizeSongId(songFile.id);
            if (string.IsNullOrWhiteSpace(id))
            {
                Debug.LogWarning($"Chip song json has no id: {sourceName}");
                return false;
            }

            var channelFiles = songFile.channels ?? Array.Empty<ChipChannelFile>();
            var channels = new List<ChipChannel>(channelFiles.Length);
            for (var i = 0; i < channelFiles.Length; i++)
            {
                var channelFile = channelFiles[i];
                if (channelFile == null)
                {
                    continue;
                }

                if (!Enum.TryParse(channelFile.waveform, true, out ChipWaveform waveform))
                {
                    Debug.LogWarning($"Chip song has invalid waveform '{channelFile.waveform}': {sourceName}, channel {i}");
                    return false;
                }

                if (!TryResolveNotes(channelFile, sourceName, i, out var notes))
                {
                    return false;
                }

                channels.Add(new ChipChannel(waveform, channelFile.gain, notes, channelFile.sustainSteps));
            }

            if (channels.Count == 0)
            {
                Debug.LogWarning($"Chip song has no channels: {sourceName}");
                return false;
            }

            var title = string.IsNullOrWhiteSpace(songFile.title) ? id : songFile.title;
            float masterGain = MasterGainById.TryGetValue(id, out float configuredGain) ? configuredGain : 1f;
            var song = new ChipSong(id, title, songFile.bpm, songFile.stepsPerBeat, masterGain, channels.ToArray());
            entry = new ChipSongEntry(id, title, song, songFile.order);
            return true;
        }

        // 긴 휴지부가 많은 칩송 채널은 RLE 문자열로 저장해 JSON 크기를 줄인다.
        private static PackedChipChannelFile CreateChannelFile(ChipChannel channel)
        {
            return new PackedChipChannelFile
            {
                waveform = channel.Waveform.ToString(),
                gain = channel.Gain,
                sustainSteps = channel.SustainSteps,
                n = EncodeNotes(channel.Notes),
            };
        }

        private static bool TryResolveNotes(ChipChannelFile channelFile, string sourceName, int channelIndex, out string[] notes)
        {
            if (!string.IsNullOrEmpty(channelFile.n))
            {
                if (TryDecodeNotes(channelFile.n, out notes))
                {
                    return true;
                }

                Debug.LogWarning($"Chip song has invalid packed notes: {sourceName}, channel {channelIndex}");
                return false;
            }

            notes = channelFile.notes ?? Array.Empty<string>();
            return true;
        }

        private static string EncodeNotes(IReadOnlyList<string> notes)
        {
            if (notes == null || notes.Count == 0)
            {
                return string.Empty;
            }

            var builder = new StringBuilder(notes.Count * 3);
            string previous = NormalizeStoredNote(notes[0]);
            var repeatCount = 1;

            for (var i = 1; i < notes.Count; i++)
            {
                string current = NormalizeStoredNote(notes[i]);
                if (string.Equals(previous, current, StringComparison.Ordinal))
                {
                    repeatCount++;
                    continue;
                }

                AppendPackedRun(builder, previous, repeatCount);
                previous = current;
                repeatCount = 1;
            }

            AppendPackedRun(builder, previous, repeatCount);
            return builder.ToString();
        }

        private static string NormalizeStoredNote(string note)
        {
            return string.IsNullOrWhiteSpace(note) ? null : note;
        }

        private static void AppendPackedRun(StringBuilder builder, string note, int count)
        {
            if (builder.Length > 0)
            {
                builder.Append(',');
            }

            if (string.IsNullOrEmpty(note))
            {
                builder.Append('~');
                builder.Append(count);
                return;
            }

            builder.Append(note.Length);
            builder.Append(':');
            builder.Append(note);
            if (count > 1)
            {
                builder.Append('*');
                builder.Append(count);
            }
        }

        private static bool TryDecodeNotes(string packedNotes, out string[] notes)
        {
            var result = new List<string>();
            var index = 0;

            while (index < packedNotes.Length)
            {
                if (packedNotes[index] == '~')
                {
                    index++;
                    if (!TryReadPositiveInt(packedNotes, ref index, out var emptyCount))
                    {
                        notes = Array.Empty<string>();
                        return false;
                    }

                    AddRepeated(result, null, emptyCount);
                }
                else
                {
                    if (!TryReadPositiveInt(packedNotes, ref index, out var tokenLength) ||
                        index >= packedNotes.Length ||
                        packedNotes[index] != ':')
                    {
                        notes = Array.Empty<string>();
                        return false;
                    }

                    index++;
                    if (tokenLength < 0 || index + tokenLength > packedNotes.Length)
                    {
                        notes = Array.Empty<string>();
                        return false;
                    }

                    string token = packedNotes.Substring(index, tokenLength);
                    index += tokenLength;
                    var repeatCount = 1;
                    if (index < packedNotes.Length && packedNotes[index] == '*')
                    {
                        index++;
                        if (!TryReadPositiveInt(packedNotes, ref index, out repeatCount))
                        {
                            notes = Array.Empty<string>();
                            return false;
                        }
                    }

                    AddRepeated(result, token, repeatCount);
                }

                if (index >= packedNotes.Length)
                {
                    break;
                }

                if (packedNotes[index] != ',')
                {
                    notes = Array.Empty<string>();
                    return false;
                }

                index++;
            }

            notes = result.ToArray();
            return true;
        }

        private static bool TryReadPositiveInt(string value, ref int index, out int number)
        {
            number = 0;
            var start = index;
            while (index < value.Length && char.IsDigit(value[index]))
            {
                number = number * 10 + (value[index] - '0');
                index++;
            }

            return index > start && number > 0;
        }

        private static void AddRepeated(List<string> target, string value, int count)
        {
            for (var i = 0; i < count; i++)
            {
                target.Add(value);
            }
        }

        [Serializable]
        private sealed class PackedChipSongFile
        {
            public string id = string.Empty;
            public string title = string.Empty;
            public int order = 0;
            public int bpm = 1;
            public int stepsPerBeat = 1;
            public PackedChipChannelFile[] channels = Array.Empty<PackedChipChannelFile>();
        }

        [Serializable]
        private sealed class PackedChipChannelFile
        {
            public string waveform = nameof(ChipWaveform.Square);
            public float gain = 1f;
            public int sustainSteps = 1;
            public string n = string.Empty;
        }

        [Serializable]
        private sealed class ChipSongFile
        {
            public string id = string.Empty;
            public string title = string.Empty;
            public int order = 0;
            public int bpm = 1;
            public int stepsPerBeat = 1;
            public ChipChannelFile[] channels = Array.Empty<ChipChannelFile>();
        }

        [Serializable]
        private sealed class ChipChannelFile
        {
            public string waveform = nameof(ChipWaveform.Square);
            public float gain = 1f;
            public int sustainSteps = 1;
            public string n = string.Empty;
            public string[] notes = Array.Empty<string>();
        }
    }
}
