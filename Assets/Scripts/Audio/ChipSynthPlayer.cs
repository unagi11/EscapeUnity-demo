using System;
using Escape.SceneFlow;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Escape.Audio
{
    [RequireComponent(typeof(AudioSource))]
    public sealed class ChipSynthPlayer : MonoBehaviour
    {
        private const string EventPrefix = "event:/chip/";
        private const int StreamSampleRate = 48000;
        private const int StreamLengthSeconds = 1;
        private const float OutputGain = 3f;
        private static ChipSynthPlayer instance;

        [SerializeField, Range(0f, 1f)] private float volume = 0.7f;
        [SerializeField] private bool playOnAwake;
        [SerializeField] private ChipSongId initialSong = ChipSongId.TitleTheme;
        [SerializeField] private bool playSelectedSongInPlayMode = true;
        [Tooltip("BGM 교체/정지 시 페이드 아웃·인 시간(초).")]
        [SerializeField, Min(0f)] private float fadeSeconds = 0.35f;

        private readonly object stateLock = new();
        private readonly List<Note> activeNotes = new();
        private AudioSource audioSource;
        private ChipSong song;
        private ChipSong pendingSong;
        private ChipSongId activeInspectorSong;
        private bool playing;
        private bool paused;
        private bool stopAfterFade;
        private double fadeLevel = 1.0;
        private double fadeTarget = 1.0;
        private int sampleRate = StreamSampleRate;
        private double songTime;
        private int nextStepIndex;
        private double nextStepTime;
        private float[] segmentLoopBuffer = Array.Empty<float>();
        private int segmentLoopCaptureIndex;
        private int segmentLoopPlaybackIndex;
        private bool segmentLoopEnabled;
        private bool segmentLoopCaptured;
        private bool useNonStreamingClipPlayback;
        private int nonStreamingSegmentStartSample;
        private int nonStreamingSegmentEndSample;

        public static ChipSynthPlayer Instance => instance;
        public float Volume => volume;
        public float Pitch => audioSource != null ? audioSource.pitch : 1f;
        public bool IsPlaying => playing && !paused;
        public string CurrentSongId => song?.Id;

        // 그리드 에디터 플레이헤드용: 현재 재생 중인 스텝 인덱스(정지면 -1).
        public int CurrentStepIndex
        {
            get
            {
                lock (stateLock)
                {
                    if (!playing || song == null || song.StepCount <= 0)
                    {
                        return -1;
                    }

                    if (useNonStreamingClipPlayback && audioSource != null && audioSource.clip != null)
                    {
                        int stepIndex = (int)Math.Floor(audioSource.time / song.StepDurationSeconds);
                        return stepIndex % song.StepCount;
                    }

                    int index = nextStepIndex - 1;
                    return index < 0 ? index + song.StepCount : index;
                }
            }
        }

        // 저장 없이 편집 중인 곡을 그대로 미리 재생한다(그리드 에디터용).
        public bool PlayPreview(ChipSong previewSong)
        {
            if (previewSong == null || previewSong.StepCount <= 0)
            {
                return false;
            }

            PlaySong(previewSong);
            return true;
        }

        public static ChipSynthPlayer Ensure()
        {
            if (instance != null)
            {
                return instance;
            }

            var go = new GameObject("ChipSynthPlayer");
            Debug.Log($"[BGM] Create persistent player. activeScene={SceneManager.GetActiveScene().name}");
            return go.AddComponent<ChipSynthPlayer>();
        }

        private void Awake()
        {
            useNonStreamingClipPlayback = Application.platform == RuntimePlatform.WebGLPlayer;
            if (instance != null && instance != this)
            {
                Debug.Log(
                    $"[BGM] Duplicate player detected. scene={gameObject.scene.name}, " +
                    $"playOnAwake={playOnAwake}, initialSong={initialSong}, " +
                    $"persistentSong={instance.CurrentSongId ?? "<none>"}");

                if (playOnAwake && ShouldPlayInitialBgmOnAwake())
                {
                    PlayInitialBgmLayer();
                }

                Destroy(gameObject);
                return;
            }

            instance = this;
            Debug.Log(
                $"[BGM] Initialize player. scene={gameObject.scene.name}, " +
                $"playOnAwake={playOnAwake}, initialSong={initialSong}");
            DontDestroyOnLoad(gameObject);
            sampleRate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : StreamSampleRate;
            activeInspectorSong = initialSong;
            audioSource = GetComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = true;
            audioSource.spatialBlend = 0f;
            audioSource.pitch = 1f;
            audioSource.volume = useNonStreamingClipPlayback ? volume : 1f;
            if (!useNonStreamingClipPlayback)
            {
                audioSource.clip = AudioClip.Create(
                    "ChipSynthStream",
                    sampleRate * StreamLengthSeconds,
                    1,
                    sampleRate,
                    true,
                    OnAudioRead);
            }
        }

        private void Start()
        {
            if (playOnAwake && ShouldPlayInitialBgmOnAwake())
            {
                PlayInitialBgmLayer();
            }
        }

        // Additive 미니게임 BGM은 스토리 레이어로 올려 방 BGM 복귀 지점을 보존한다.
        private void PlayInitialBgmLayer()
        {
            if (IsAdditiveMiniSceneInstance())
            {
                SoundPlayer.PlayStoryBgm(initialSong);
                return;
            }

            SoundPlayer.PlayBgm(initialSong);
        }

        private bool IsAdditiveMiniSceneInstance()
        {
            return string.Equals(gameObject.scene.name, EscapeSceneLoader.SpaceShooterSceneName, StringComparison.Ordinal) &&
                !string.Equals(SceneManager.GetActiveScene().name, EscapeSceneLoader.SpaceShooterSceneName, StringComparison.Ordinal);
        }

        // 신규 룸 인트로 진입 때는 룸 기본곡이 대사 BGM보다 먼저 끼지 않게 한다.
        private bool ShouldSkipPlayOnAwakeForPendingIntro()
        {
            return SceneLoadArgs.PlayIntro &&
                string.Equals(gameObject.scene.name, EscapeSceneLoader.RoomSceneName, StringComparison.Ordinal);
        }

        // 인트로가 먼저 재생될 때는 방 기본곡을 복귀용으로만 예약한다.
        private bool ShouldPlayInitialBgmOnAwake()
        {
            if (ShouldSkipPlayOnAwakeForPendingIntro())
            {
                SoundPlayer.ReserveSceneBgm(initialSong);
                return false;
            }

            return true;
        }

        private void Update()
        {
            if (playSelectedSongInPlayMode && initialSong != activeInspectorSong)
            {
                // 인스펙터 선택 변경만 추적하고, 실제 재생곡 변경과는 분리한다.
                activeInspectorSong = initialSong;
                Play(initialSong);
            }

            UpdateNonStreamingSegmentLoop();
        }

        public bool Play(ChipSongId songId)
        {
            if (!ChipSongLibrary.TryGetSong(songId, out var nextSong))
            {
                Debug.LogWarning($"Chip song not found: {songId}");
                return false;
            }

            PlaySong(nextSong);
            return true;
        }

        public bool Play(string songIdOrEventId)
        {
            var id = NormalizeSongId(songIdOrEventId);
            if (!ChipSongLibrary.TryGetSong(id, out var nextSong))
            {
                Debug.LogWarning($"Chip song not found: {songIdOrEventId}");
                return false;
            }

            PlaySong(nextSong);
            return true;
        }

        // 곡을 0초 위치에 즉시 올려두고, 외부에서 ResumeBgm을 호출할 때까지 무음으로 멈춘다.
        public bool PreparePaused(string songIdOrEventId)
        {
            var id = NormalizeSongId(songIdOrEventId);
            if (!ChipSongLibrary.TryGetSong(id, out var nextSong))
            {
                Debug.LogWarning($"Chip song not found: {songIdOrEventId}");
                return false;
            }

            PrepareSongPaused(nextSong);
            return true;
        }

        private void PlaySong(ChipSong nextSong)
        {
            string previousSongId = song?.Id ?? "<none>";
            Debug.Log(
                $"[BGM] Play song. scene={SceneManager.GetActiveScene().name}, " +
                $"previous={previousSongId}, next={nextSong.Id} ({nextSong.Title})");

            if (useNonStreamingClipPlayback)
            {
                PlayNonStreamingSong(nextSong);
                return;
            }

            lock (stateLock)
            {
                DisableSegmentLoopLocked();
                // 명시적인 재생 요청은 준비 상태의 일시정지를 해제해 곡 교체가 진행되게 한다.
                paused = false;
                bool active = playing && song != null;
                if (active && string.Equals(song.Id, nextSong.Id, StringComparison.Ordinal) &&
                    pendingSong == null && !stopAfterFade)
                {
                    // 같은 곡이 이미 재생 중이면 그대로 둔다(불필요한 페이드 방지).
                }
                else if (active)
                {
                    // 재생 중: 현재 곡을 페이드아웃한 뒤 교체(OnAudioRead에서 스왑).
                    pendingSong = nextSong;
                    stopAfterFade = false;
                    fadeTarget = 0.0;
                }
                else
                {
                    // 정지 상태에서 시작: 페이드인.
                    song = nextSong;
                    playing = true;
                    paused = false;
                    songTime = 0;
                    nextStepIndex = 0;
                    nextStepTime = 0;
                    activeNotes.Clear();
                    pendingSong = null;
                    stopAfterFade = false;
                    fadeLevel = 0.0;
                    fadeTarget = 1.0;
                }
            }

            if (audioSource != null && !audioSource.isPlaying)
            {
                audioSource.Play();
            }
        }

        private void PrepareSongPaused(ChipSong nextSong)
        {
            string previousSongId = song?.Id ?? "<none>";
            Debug.Log(
                $"[BGM] Prepare paused song. scene={SceneManager.GetActiveScene().name}, " +
                $"previous={previousSongId}, next={nextSong.Id} ({nextSong.Title})");

            if (useNonStreamingClipPlayback)
            {
                PrepareNonStreamingSongPaused(nextSong);
                return;
            }

            lock (stateLock)
            {
                DisableSegmentLoopLocked();
                song = nextSong;
                pendingSong = null;
                playing = true;
                paused = true;
                stopAfterFade = false;
                fadeLevel = 1.0;
                fadeTarget = 1.0;
                songTime = 0;
                nextStepIndex = 0;
                nextStepTime = 0;
                activeNotes.Clear();
            }

            if (audioSource != null && !audioSource.isPlaying)
            {
                audioSource.Play();
            }
        }

        public void Stop()
        {
            Debug.Log(
                $"[BGM] Stop song. scene={SceneManager.GetActiveScene().name}, " +
                $"current={song?.Id ?? "<none>"}");

            if (useNonStreamingClipPlayback)
            {
                StopNonStreamingSong();
                return;
            }

            lock (stateLock)
            {
                DisableSegmentLoopLocked();
                if (!playing || song == null)
                {
                    playing = false;
                    paused = false;
                    song = null;
                    pendingSong = null;
                    stopAfterFade = false;
                    fadeLevel = 0.0;
                    fadeTarget = 0.0;
                    activeNotes.Clear();
                    return;
                }

                // 일시정지 중에는 오디오 콜백이 페이드를 진행하지 않으므로 즉시 정리한다.
                if (paused)
                {
                    playing = false;
                    paused = false;
                    song = null;
                    pendingSong = null;
                    stopAfterFade = false;
                    fadeLevel = 0.0;
                    fadeTarget = 0.0;
                    activeNotes.Clear();
                    return;
                }

                // 페이드아웃 후 실제 정지(OnAudioRead에서 처리).
                pendingSong = null;
                stopAfterFade = true;
                fadeTarget = 0.0;
            }
        }

        public void SetPaused(bool value)
        {
            if (useNonStreamingClipPlayback)
            {
                SetNonStreamingPaused(value);
                return;
            }

            lock (stateLock)
            {
                paused = value;
                if (paused)
                {
                    activeNotes.Clear();
                }
            }
        }

        public void SetVolume(float value)
        {
            volume = Mathf.Clamp01(value);
            if (useNonStreamingClipPlayback && audioSource != null)
            {
                audioSource.volume = volume;
            }
        }

        // 현재 BGM 재생 속도와 음높이를 함께 조절한다.
        public void SetPitch(float value)
        {
            if (audioSource != null)
            {
                audioSource.pitch = Mathf.Clamp(value, 0.5f, 1.5f);
            }
        }

        // 현재부터 지정 길이만큼 출력되는 BGM 조각을 잡아 해제할 때까지 반복한다.
        public bool StartSegmentLoop(int milliseconds)
        {
            if (useNonStreamingClipPlayback)
            {
                return StartNonStreamingSegmentLoop(milliseconds);
            }

            lock (stateLock)
            {
                if (!playing || paused || song == null)
                {
                    return false;
                }

                int clampedMilliseconds = Mathf.Clamp(milliseconds, 10, 1000);
                int sampleCount = Math.Max(1, sampleRate * clampedMilliseconds / 1000);
                segmentLoopBuffer = new float[sampleCount];
                segmentLoopCaptureIndex = 0;
                segmentLoopPlaybackIndex = 0;
                segmentLoopEnabled = true;
                segmentLoopCaptured = false;
                return true;
            }
        }

        // 짧은 BGM 구간 반복을 끝내고 멈춰 있던 곡 진행을 이어간다.
        public void StopSegmentLoop()
        {
            lock (stateLock)
            {
                DisableSegmentLoopLocked();
            }
        }

        // 오디오 스레드의 캡처·재생 인덱스를 한 번에 초기화한다.
        private void DisableSegmentLoopLocked()
        {
            segmentLoopBuffer = Array.Empty<float>();
            segmentLoopCaptureIndex = 0;
            segmentLoopPlaybackIndex = 0;
            segmentLoopEnabled = false;
            segmentLoopCaptured = false;
            nonStreamingSegmentStartSample = 0;
            nonStreamingSegmentEndSample = 0;
        }

        // WebGL에서는 실시간 PCM 스트림 대신 한 루프 전체를 비스트리밍 클립으로 합성해 재생한다.
        private void PlayNonStreamingSong(ChipSong nextSong)
        {
            bool reuseCurrentClip;
            lock (stateLock)
            {
                DisableSegmentLoopLocked();
                reuseCurrentClip = playing && song != null && audioSource != null && audioSource.clip != null &&
                    string.Equals(song.Id, nextSong.Id, StringComparison.Ordinal);
                paused = false;
            }

            if (reuseCurrentClip)
            {
                audioSource.UnPause();
                if (!audioSource.isPlaying)
                {
                    audioSource.Play();
                }

                return;
            }

            CreateAndPlayNonStreamingClip(nextSong, false);
        }

        // WebGL 리듬 연동용 곡을 첫 샘플에 준비한 뒤 명시적인 재개 전까지 멈춘다.
        private void PrepareNonStreamingSongPaused(ChipSong nextSong)
        {
            CreateAndPlayNonStreamingClip(nextSong, true);
        }

        // WebGL이 요구하는 완전한 PCM 데이터를 생성하고 AudioSource에 새 루프 클립을 연결한다.
        private void CreateAndPlayNonStreamingClip(ChipSong nextSong, bool startPaused)
        {
            if (audioSource == null)
            {
                return;
            }

            lock (stateLock)
            {
                DisableSegmentLoopLocked();
                song = nextSong;
                pendingSong = null;
                playing = true;
                paused = false;
                stopAfterFade = false;
                fadeLevel = 1.0;
                fadeTarget = 1.0;
                songTime = 0;
                nextStepIndex = 0;
                nextStepTime = 0;
                activeNotes.Clear();
            }

            int sampleCount = CalculateNonStreamingSampleCount(nextSong);
            AudioClip nextClip = AudioClip.Create(
                $"ChipSynth_{nextSong.Id}",
                sampleCount,
                1,
                sampleRate,
                false,
                OnAudioRead);
            AudioClip previousClip = audioSource.clip;
            audioSource.Stop();
            audioSource.clip = nextClip;
            audioSource.loop = true;
            audioSource.volume = volume;
            audioSource.Play();

            lock (stateLock)
            {
                paused = startPaused;
                songTime = 0;
                nextStepIndex = 0;
                nextStepTime = 0;
                activeNotes.Clear();
            }

            if (startPaused)
            {
                audioSource.Pause();
            }

            if (previousClip != null)
            {
                Destroy(previousClip);
            }
        }

        // 곡 길이를 WebGL 비스트리밍 AudioClip의 전체 샘플 수로 환산한다.
        private int CalculateNonStreamingSampleCount(ChipSong nextSong)
        {
            double exactSampleCount = nextSong.StepCount * nextSong.StepDurationSeconds * sampleRate;
            return Math.Max(1, (int)Math.Round(exactSampleCount));
        }

        // WebGL 클립은 PCM 콜백 페이드가 진행되지 않으므로 즉시 정지하고 메모리를 해제한다.
        private void StopNonStreamingSong()
        {
            lock (stateLock)
            {
                DisableSegmentLoopLocked();
                playing = false;
                paused = false;
                song = null;
                pendingSong = null;
                stopAfterFade = false;
                fadeLevel = 0.0;
                fadeTarget = 0.0;
                activeNotes.Clear();
            }

            if (audioSource == null)
            {
                return;
            }

            AudioClip previousClip = audioSource.clip;
            audioSource.Stop();
            audioSource.clip = null;
            if (previousClip != null)
            {
                Destroy(previousClip);
            }
        }

        // WebGL의 정적 루프 클립을 AudioSource 자체의 일시정지 API로 제어한다.
        private void SetNonStreamingPaused(bool value)
        {
            lock (stateLock)
            {
                if (!playing || song == null)
                {
                    return;
                }

                paused = value;
            }

            if (audioSource == null)
            {
                return;
            }

            if (value)
            {
                audioSource.Pause();
                return;
            }

            audioSource.UnPause();
            if (!audioSource.isPlaying)
            {
                audioSource.Play();
            }
        }

        // WebGL 정적 클립의 현재 재생 위치를 짧은 반복 구간으로 지정한다.
        private bool StartNonStreamingSegmentLoop(int milliseconds)
        {
            lock (stateLock)
            {
                if (!playing || paused || song == null || audioSource == null || audioSource.clip == null)
                {
                    return false;
                }

                int sampleCount = Math.Max(1, sampleRate * Mathf.Clamp(milliseconds, 10, 1000) / 1000);
                nonStreamingSegmentStartSample = audioSource.timeSamples;
                nonStreamingSegmentEndSample = Math.Min(
                    audioSource.clip.samples,
                    nonStreamingSegmentStartSample + sampleCount);
                segmentLoopEnabled = nonStreamingSegmentEndSample > nonStreamingSegmentStartSample;
                segmentLoopCaptured = segmentLoopEnabled;
                return segmentLoopEnabled;
            }
        }

        // WebGL 정적 클립이 지정 구간 끝에 도달하면 시작 샘플로 되감는다.
        private void UpdateNonStreamingSegmentLoop()
        {
            if (!useNonStreamingClipPlayback || audioSource == null || audioSource.clip == null)
            {
                return;
            }

            lock (stateLock)
            {
                if (!segmentLoopEnabled || paused || !playing)
                {
                    return;
                }

                int currentSample = audioSource.timeSamples;
                if (currentSample >= nonStreamingSegmentEndSample || currentSample < nonStreamingSegmentStartSample)
                {
                    audioSource.timeSamples = nonStreamingSegmentStartSample;
                }
            }
        }

        private void OnAudioRead(float[] data)
        {
            lock (stateLock)
            {
                Array.Clear(data, 0, data.Length);
                if (!playing || paused)
                {
                    return;
                }

                var sampleDuration = 1.0 / sampleRate;
                double fadeStep = fadeSeconds > 0f ? sampleDuration / fadeSeconds : 1.0;

                for (var i = 0; i < data.Length; i++)
                {
                    if (segmentLoopEnabled && segmentLoopCaptured)
                    {
                        data[i] = Mathf.Clamp(
                            segmentLoopBuffer[segmentLoopPlaybackIndex] * volume,
                            -1f,
                            1f);
                        segmentLoopPlaybackIndex = (segmentLoopPlaybackIndex + 1) % segmentLoopBuffer.Length;
                        continue;
                    }

                    // 페이드 레벨을 목표(0 또는 1)로 이동.
                    if (fadeLevel < fadeTarget)
                    {
                        fadeLevel = Math.Min(fadeTarget, fadeLevel + fadeStep);
                    }
                    else if (fadeLevel > fadeTarget)
                    {
                        fadeLevel = Math.Max(fadeTarget, fadeLevel - fadeStep);
                    }

                    // 페이드아웃 완료 시 대기 곡으로 교체하거나 정지한다.
                    if (fadeLevel <= 0.0 && fadeTarget <= 0.0)
                    {
                        if (pendingSong != null)
                        {
                            song = pendingSong;
                            pendingSong = null;
                            songTime = 0;
                            nextStepIndex = 0;
                            nextStepTime = 0;
                            activeNotes.Clear();
                            fadeTarget = 1.0;
                        }
                        else if (stopAfterFade)
                        {
                            stopAfterFade = false;
                            playing = false;
                            song = null;
                            activeNotes.Clear();
                        }
                    }

                    if (song == null || song.StepCount <= 0)
                    {
                        data[i] = 0f;
                        continue;
                    }

                    while (songTime + 0.0000001 >= nextStepTime)
                    {
                        ScheduleStep(nextStepIndex, nextStepTime);
                        nextStepIndex = (nextStepIndex + 1) % song.StepCount;
                        nextStepTime += song.StepDurationSeconds;
                    }

                    var mixed = 0f;
                    for (var v = activeNotes.Count - 1; v >= 0; v--)
                    {
                        var note = activeNotes[v];
                        mixed += note.Next(sampleRate);
                        if (note.Done)
                        {
                            activeNotes.RemoveAt(v);
                        }
                        else
                        {
                            activeNotes[v] = note;
                        }
                    }

                    // 설정 슬라이더 비율은 유지하면서 칩 BGM의 전체 체감 음량을 넉넉하게 보강한다.
                    float output = mixed * song.MasterGain * OutputGain * (float)fadeLevel;
                    float playbackVolume = useNonStreamingClipPlayback ? 1f : volume;
                    data[i] = Mathf.Clamp(output * playbackVolume, -1f, 1f);
                    if (segmentLoopEnabled)
                    {
                        segmentLoopBuffer[segmentLoopCaptureIndex++] = output;
                        if (segmentLoopCaptureIndex >= segmentLoopBuffer.Length)
                        {
                            segmentLoopCaptured = true;
                            segmentLoopPlaybackIndex = 0;
                        }
                    }

                    songTime += sampleDuration;
                }

                if (song != null && nextStepIndex == 0 && songTime >= nextStepTime)
                {
                    songTime -= nextStepTime;
                    nextStepTime = 0;
                }
            }
        }

        private void ScheduleStep(int stepIndex, double startTime)
        {
            for (var i = 0; i < song.Channels.Count; i++)
            {
                var channel = song.Channels[i];
                if (channel.Notes.Length == 0)
                {
                    continue;
                }

                var token = channel.Notes[stepIndex % channel.Notes.Length];
                if (string.IsNullOrWhiteSpace(token))
                {
                    continue;
                }

                var duration = song.StepDurationSeconds * channel.SustainSteps * (channel.SustainSteps > 1 ? 1.02 : 0.96);
                var parts = token.Split('+');
                for (var n = 0; n < parts.Length; n++)
                {
                    if (TryNoteToFrequency(parts[n], out var frequency))
                    {
                        activeNotes.Add(new Note(channel.Waveform, frequency, channel.Gain, duration, Math.Max(0, startTime - songTime)));
                    }
                }
            }
        }

        private static string NormalizeSongId(string songIdOrEventId)
        {
            if (string.IsNullOrWhiteSpace(songIdOrEventId))
            {
                return string.Empty;
            }

            return songIdOrEventId.StartsWith(EventPrefix, StringComparison.Ordinal)
                ? songIdOrEventId.Substring(EventPrefix.Length)
                : songIdOrEventId;
        }

        private static bool TryNoteToFrequency(string note, out double frequency)
        {
            frequency = 0;
            if (string.IsNullOrWhiteSpace(note))
            {
                return false;
            }

            note = note.Trim();
            var letter = note[0];
            var semitone = letter switch
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

            var index = 1;
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

            if (!int.TryParse(note.Substring(index), out var octave))
            {
                return false;
            }

            var midi = (octave + 1) * 12 + semitone;
            frequency = 440.0 * Math.Pow(2.0, (midi - 69) / 12.0);
            return true;
        }

        private struct Note
        {
            private readonly ChipWaveform waveform;
            private readonly double frequency;
            private readonly float gain;
            private readonly double duration;
            private readonly double delay;
            private double phase;
            private double age;

            public Note(ChipWaveform waveform, double frequency, float gain, double duration, double delay)
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

            public float Next(int sampleRate)
            {
                age += 1.0 / sampleRate;
                if (age < delay || age > delay + duration)
                {
                    return 0f;
                }

                var localAge = age - delay;
                phase += frequency / sampleRate;
                phase -= Math.Floor(phase);
                var envelope = waveform == ChipWaveform.Bell
                    ? BellEnvelope(localAge, duration)
                    : Envelope(localAge, duration);
                return Wave(phase, waveform) * gain * envelope;
            }

            private static float BellEnvelope(double age, double duration)
            {
                const double attack = 0.006;
                if (age < attack)
                {
                    return Mathf.Clamp01((float)(age / attack));
                }

                return (float)Math.Exp(-5.0 * (age - attack) / Math.Max(0.01, duration - attack));
            }

            private static float Envelope(double age, double duration)
            {
                const double attack = 0.018;
                const double floor = 0.0001;
                if (age < attack)
                {
                    return (float)Mathf.Lerp((float)floor, 1f, (float)(age / attack));
                }

                var releaseStart = Math.Max(attack, duration - 0.04);
                if (age > releaseStart)
                {
                    return Mathf.Clamp01((float)((duration - age) / Math.Max(0.001, duration - releaseStart)));
                }

                return 1f;
            }

            private static float Wave(double phase, ChipWaveform waveform)
            {
                return waveform switch
                {
                    ChipWaveform.Square => phase < 0.5 ? 1f : -1f,
                    ChipWaveform.Triangle => (float)(1.0 - 4.0 * Math.Abs(Math.Round(phase - 0.25) - (phase - 0.25))),
                    ChipWaveform.Bell => BellWave(phase),
                    ChipWaveform.Sawtooth => (float)(2.0 * phase - 1.0),
                    _ => 0f,
                };
            }

            private static float BellWave(double phase)
            {
                var angle = phase * Math.PI * 2.0;
                return (float)(
                    Math.Sin(angle) * 0.56 +
                    Math.Sin(angle * 2.0) * 0.26 +
                    Math.Sin(angle * 3.0) * 0.12 +
                    Math.Sin(angle * 4.0) * 0.06);
            }
        }
    }
}
