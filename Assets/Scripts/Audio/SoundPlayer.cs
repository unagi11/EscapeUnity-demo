using UnityEngine;

namespace Escape.Audio
{
    // 상호작용 종류에 맞는 터치 효과음을 지정한다.
    public enum TouchSfxPreset
    {
        Default = 0,
        Silent = 1,
        Click = 2,
        LightSwitch = 3,
        Whistle = 4,
        Keypad1 = 5,
        Keypad2 = 6,
        Keypad3 = 7,
        Keypad4 = 8,
        Keypad5 = 9,
        Keypad6 = 10,
        Keypad7 = 11,
        Keypad8 = 12,
        Keypad9 = 13,
        KeypadStar = 14,
        Keypad0 = 15,
        KeypadHash = 16,
        MouseSqueak = 17
    }

    public static class SoundPlayer
    {
        private const string PlayerPrefsMasterKey = "escape.volume.master";
        private const string PlayerPrefsBgmKey = "escape.volume.bgm";
        private const string PlayerPrefsSfxKey = "escape.volume.sfx";

        private const float DefaultMasterVolume = 1f;
        private const float DefaultBgmVolume = 0.7f;
        private const float DefaultSfxVolume = 1f;

        private static bool hasSceneBgm;
        private static string sceneBgmId = string.Empty;
        private static bool hasStoryBgm;

        public static float MasterVolume => AudioListener.volume;
        public static float BgmVolume => ChipSynthPlayer.Instance != null ? ChipSynthPlayer.Instance.Volume : DefaultBgmVolume;
        public static float SfxVolume => ChipSfxPlayer.Instance != null ? ChipSfxPlayer.Instance.Volume : DefaultSfxVolume;

        // 저장된 볼륨을 실행 시작 시 한 번 불러와 적용한다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void LoadSavedVolumes()
        {
            AudioListener.volume = Mathf.Clamp01(PlayerPrefs.GetFloat(PlayerPrefsMasterKey, DefaultMasterVolume));
            ChipSynthPlayer.Ensure().SetVolume(PlayerPrefs.GetFloat(PlayerPrefsBgmKey, DefaultBgmVolume));
            ChipSfxPlayer.Ensure().SetVolume(PlayerPrefs.GetFloat(PlayerPrefsSfxKey, DefaultSfxVolume));
        }

        public static void SetMasterVolume(float volume)
        {
            AudioListener.volume = Mathf.Clamp01(volume);
            PlayerPrefs.SetFloat(PlayerPrefsMasterKey, AudioListener.volume);
            PlayerPrefs.Save();
        }

        public static bool PlayBgm(ChipSongId songId)
        {
            return PlayBgm(ChipSongLibrary.ToSongId(songId));
        }

        // 바깥 씬 레이어의 BGM을 예약/재생한다. 스토리 BGM이 있으면 저장만 하고 복귀 때 재생한다.
        public static bool PlayBgm(string songIdOrEventId)
        {
            if (!TryNormalizeExistingBgm(songIdOrEventId, out var songId))
            {
                return false;
            }

            hasSceneBgm = true;
            sceneBgmId = songId;
            if (hasStoryBgm)
            {
                return true;
            }

            return PlayNormalizedBgm(songId);
        }

        // 바깥 씬 레이어를 비운다. 스토리 레이어가 재생 중이면 현재 오버라이드는 유지한다.
        public static void StopBgm()
        {
            hasSceneBgm = false;
            sceneBgmId = string.Empty;
            if (!hasStoryBgm)
            {
                ChipSynthPlayer.Instance?.Stop();
            }
        }

        public static bool ReserveSceneBgm(ChipSongId songId)
        {
            return ReserveSceneBgm(ChipSongLibrary.ToSongId(songId));
        }

        // 지금 재생하지 않고 바깥 씬 레이어 복귀곡만 예약한다.
        public static bool ReserveSceneBgm(string songIdOrEventId)
        {
            if (!TryNormalizeExistingBgm(songIdOrEventId, out var songId))
            {
                return false;
            }

            hasSceneBgm = true;
            sceneBgmId = songId;
            return true;
        }

        // 씬/스토리 BGM 레이어를 모두 비우고 현재 재생음을 페이드아웃한다.
        public static void StopAllBgm()
        {
            hasSceneBgm = false;
            sceneBgmId = string.Empty;
            hasStoryBgm = false;
            ChipSynthPlayer.Instance?.Stop();
        }

        public static bool PlayStoryBgm(ChipSongId songId)
        {
            return PlayStoryBgm(ChipSongLibrary.ToSongId(songId));
        }

        // 스토리 진행 레이어의 BGM을 즉시 재생해 바깥 씬 레이어를 오버라이드한다.
        public static bool PlayStoryBgm(string songIdOrEventId)
        {
            if (!TryNormalizeExistingBgm(songIdOrEventId, out var songId))
            {
                return false;
            }

            hasStoryBgm = true;
            return PlayNormalizedBgm(songId);
        }

        public static bool PrepareStoryBgmPaused(ChipSongId songId)
        {
            return PrepareStoryBgmPaused(ChipSongLibrary.ToSongId(songId));
        }

        // 스토리 BGM을 재생 위치 0초에 준비하되, ResumeBgm 전까지 소리는 내지 않는다.
        public static bool PrepareStoryBgmPaused(string songIdOrEventId)
        {
            if (!TryNormalizeExistingBgm(songIdOrEventId, out var songId))
            {
                return false;
            }

            hasStoryBgm = true;
            return ChipSynthPlayer.Ensure().PreparePaused(songId);
        }

        // 스토리 레이어를 무음으로 유지해 대사가 끝날 때까지 바깥 씬 BGM 복귀를 막는다.
        public static void SilenceStoryBgm()
        {
            hasStoryBgm = true;
            ChipSynthPlayer.Instance?.Stop();
        }

        // 스토리 BGM 오버라이드를 해제하고 바깥 씬 레이어로 복귀한다.
        public static void StopStoryBgm()
        {
            hasStoryBgm = false;
            RestoreSceneBgm();
        }

        private static bool TryNormalizeExistingBgm(string songIdOrEventId, out string songId)
        {
            songId = ChipSongLibrary.NormalizeSongId(songIdOrEventId);
            if (string.IsNullOrWhiteSpace(songId))
            {
                return false;
            }

            if (ChipSongLibrary.TryGetSong(songId, out _))
            {
                return true;
            }

            Debug.LogWarning($"Chip song not found: {songIdOrEventId}");
            return false;
        }

        private static bool PlayNormalizedBgm(string songId)
        {
            return ChipSynthPlayer.Ensure().Play(songId);
        }

        private static void RestoreSceneBgm()
        {
            if (hasSceneBgm && !string.IsNullOrWhiteSpace(sceneBgmId))
            {
                PlayNormalizedBgm(sceneBgmId);
                return;
            }

            ChipSynthPlayer.Instance?.Stop();
        }

        public static void PauseBgm()
        {
            ChipSynthPlayer.Instance?.SetPaused(true);
        }

        public static void ResumeBgm()
        {
            ChipSynthPlayer.Instance?.SetPaused(false);
        }

        public static void SetBgmVolume(float volume)
        {
            ChipSynthPlayer.Ensure().SetVolume(volume);
            PlayerPrefs.SetFloat(PlayerPrefsBgmKey, Mathf.Clamp01(volume));
            PlayerPrefs.Save();
        }

        // 포스트 이펙트 연출에서 사용할 BGM 피치를 적용한다.
        public static void SetBgmPitch(float pitch)
        {
            ChipSynthPlayer.Ensure().SetPitch(pitch);
        }

        // 현재 BGM의 다음 짧은 구간을 캡처해 반복한다.
        public static bool StartBgmSegmentLoop(int milliseconds = 100)
        {
            return ChipSynthPlayer.Instance != null &&
                ChipSynthPlayer.Instance.StartSegmentLoop(milliseconds);
        }

        // 반복 중인 짧은 BGM 구간을 해제하고 원래 진행을 잇는다.
        public static void StopBgmSegmentLoop()
        {
            ChipSynthPlayer.Instance?.StopSegmentLoop();
        }

        public static void SetSfxVolume(float volume)
        {
            ChipSfxPlayer.Ensure().SetVolume(volume);
            PlayerPrefs.SetFloat(PlayerPrefsSfxKey, Mathf.Clamp01(volume));
            PlayerPrefs.Save();
        }

        // 전체 초기화 직후 기본 볼륨을 런타임과 PlayerPrefs에 함께 반영한다.
        public static void ResetVolumeSettings()
        {
            SetMasterVolume(DefaultMasterVolume);
            SetBgmVolume(DefaultBgmVolume);
            SetSfxVolume(DefaultSfxVolume);
        }

        public static void PlayMoveSfx()
        {
            ChipSfxPlayer.Ensure().PlayMove();
        }

        public static void PlayClickSfx()
        {
            ChipSfxPlayer.Ensure().PlayClick();
        }

        // 벽시계 초침처럼 짧은 째깍째깍 효과음을 재생한다.
        public static void PlayClockTickSfx()
        {
            ChipSfxPlayer.Ensure().PlayClockTick();
        }

        public static void PlayTouchSfx(TouchSfxPreset preset)
        {
            switch (preset)
            {
                case TouchSfxPreset.Silent:
                    return;
                case TouchSfxPreset.LightSwitch:
                    PlayLightSwitchSfx();
                    return;
                case TouchSfxPreset.Whistle:
                    PlayWhistleSfx();
                    return;
                case TouchSfxPreset.Keypad1:
                    PlayKeypadSfx('1');
                    return;
                case TouchSfxPreset.Keypad2:
                    PlayKeypadSfx('2');
                    return;
                case TouchSfxPreset.Keypad3:
                    PlayKeypadSfx('3');
                    return;
                case TouchSfxPreset.Keypad4:
                    PlayKeypadSfx('4');
                    return;
                case TouchSfxPreset.Keypad5:
                    PlayKeypadSfx('5');
                    return;
                case TouchSfxPreset.Keypad6:
                    PlayKeypadSfx('6');
                    return;
                case TouchSfxPreset.Keypad7:
                    PlayKeypadSfx('7');
                    return;
                case TouchSfxPreset.Keypad8:
                    PlayKeypadSfx('8');
                    return;
                case TouchSfxPreset.Keypad9:
                    PlayKeypadSfx('9');
                    return;
                case TouchSfxPreset.KeypadStar:
                    PlayKeypadSfx('*');
                    return;
                case TouchSfxPreset.Keypad0:
                    PlayKeypadSfx('0');
                    return;
                case TouchSfxPreset.KeypadHash:
                    PlayKeypadSfx('#');
                    return;
                case TouchSfxPreset.MouseSqueak:
                    PlayMouseSqueakSfx();
                    return;
                default:
                    PlayClickSfx();
                    return;
            }
        }

        // TSV나 이벤트에서 받은 효과음 ID를 실제 칩 효과음으로 재생한다.
        public static bool PlaySfx(string sfxId, bool warnIfUnknown = true)
        {
            string id = (sfxId ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(id) ||
                id == "none" ||
                id == "silent")
            {
                return false;
            }

            if (ChipSfxPlayer.Ensure().PlayById(id))
            {
                return true;
            }

            if (warnIfUnknown)
            {
                Debug.LogWarning($"Unknown sfx id: {sfxId}");
            }

            return false;
        }

        // 미니게임처럼 특정 상황에서만 효과음 크기를 낮춰 재생한다.
        public static bool PlaySfxScaled(string sfxId, float volumeScale, bool warnIfUnknown = true)
        {
            string id = (sfxId ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(id) ||
                id == "none" ||
                id == "silent")
            {
                return false;
            }

            if (ChipSfxPlayer.Ensure().PlayScaled(id, volumeScale))
            {
                return true;
            }

            if (warnIfUnknown)
            {
                Debug.LogWarning($"Unknown sfx id: {sfxId}");
            }

            return false;
        }

        public static void PlayLightSwitchSfx()
        {
            ChipSfxPlayer.Ensure().PlayLightSwitch();
        }

        public static void PlayWhistleSfx()
        {
            ChipSfxPlayer.Ensure().PlayWhistle();
        }

        public static void PlayDialogueTypeSfx()
        {
            ChipSfxPlayer.Ensure().PlayDialogueType();
        }

        public static void PlayYeonDialogueTypeSfx()
        {
            ChipSfxPlayer.Ensure().PlayYeonDialogueType();
        }

        public static void PlayTypewriterTypeSfx()
        {
            ChipSfxPlayer.Ensure().PlayTypewriterType();
        }

        // 리듬 분리수거 입력용 짧은 "착" 효과음을 재생한다.
        public static void PlayRecycleChakSfx()
        {
            ChipSfxPlayer.Ensure().PlayRecycleChak();
        }

        // 리듬 분리수거 정타 판정용 짧은 성공 효과음을 재생한다.
        public static void PlayRythmRecycleHitSfx()
        {
            ChipSfxPlayer.Ensure().PlayRythmRecycleHit();
        }

        // 락픽 미니게임의 픽 들어 올림 효과음을 재생한다.
        public static void PlayLockPickPickUpSfx() => PlaySfx("lockpick_pick_up");

        // 락픽 미니게임의 픽 내려놓기 효과음을 재생한다.
        public static void PlayLockPickPickDownSfx() => PlaySfx("lockpick_pick_down");

        // 락픽 미니게임의 핀 고정 효과음을 재생한다.
        public static void PlayLockPickPinSetSfx() => PlaySfx("lockpick_pin_set");

        // 락픽 미니게임의 해제 성공 효과음을 재생한다.
        public static void PlayLockPickSuccessSfx() => PlaySfx("lockpick_success");

        public static void PlayPortraitBounceSfx()
        {
            ChipSfxPlayer.Ensure().PlayPortraitBounce();
        }

        public static void PlayQuestionBellSfx()
        {
            ChipSfxPlayer.Ensure().PlayQuestionBell();
        }

        // 불길한 단서나 의문을 강조하는 하강 효과음을 재생한다.
        public static void PlayQuestionOminousSfx()
        {
            ChipSfxPlayer.Ensure().PlayQuestionOminous();
        }

        // 현관 초인종처럼 길게 울리는 딩-동 효과음을 재생한다.
        public static void PlayDoorbellLongSfx()
        {
            ChipSfxPlayer.Ensure().PlayDoorbellLong();
        }

        public static void PlayItemAcquireSfx()
        {
            ChipSfxPlayer.Ensure().PlayItemAcquire();
        }

        // 도전과제 달성 알림용 짧은 칩 팡파레를 재생한다.
        public static void PlayAchievementUnlockSfx()
        {
            ChipSfxPlayer.Ensure().PlayAchievementUnlock();
        }

        // 아이템 사용 완료 칩 효과음을 재생한다.
        public static void PlayItemUseSfx()
        {
            ChipSfxPlayer.Ensure().PlayItemUse();
        }

        // 플레이어 피격 칩 효과음을 재생한다.
        public static void PlayHitSfx()
        {
            ChipSfxPlayer.Ensure().PlayHit();
        }

        // 고통 장면의 짧은 칩 비명을 재생한다.
        public static void PlayScreamSfx()
        {
            ChipSfxPlayer.Ensure().PlayScream();
        }

        // 체력/희망이 깎일 때 쓰는 감정 충격음을 재생한다.
        public static void PlayHeartDropSfx()
        {
            ChipSfxPlayer.Ensure().PlayHeartDrop();
        }

        // 엔딩 암전처럼 큰 충격이 필요한 순간의 쾅 효과음을 재생한다.
        public static void PlayBangSfx()
        {
            ChipSfxPlayer.Ensure().PlayBang();
        }

        // 플레이어 사망 칩 효과음을 재생한다.
        public static void PlayDeathSfx()
        {
            ChipSfxPlayer.Ensure().PlayDeath();
        }

        // 플레이어 체력 회복 칩 효과음을 재생한다.
        public static void PlayHealSfx()
        {
            ChipSfxPlayer.Ensure().PlayHeal();
        }

        // Space Shooter 자동 발사음을 재생한다.
        public static void PlaySpaceShooterShootSfx()
        {
            ChipSfxPlayer.Ensure().PlaySpaceShooterShoot();
        }

        // Space Shooter 적 격파음을 재생한다.
        public static void PlaySpaceShooterEnemyHitSfx()
        {
            ChipSfxPlayer.Ensure().PlaySpaceShooterEnemyHit();
        }

        public static void PlayKeypadSfx()
        {
            ChipSfxPlayer.Ensure().PlayKeypad();
        }

        public static void PlayKeypadSfx(char key)
        {
            ChipSfxPlayer.Ensure().PlayKeypad(key);
        }

        public static void PlayKeypadSuccessSfx()
        {
            ChipSfxPlayer.Ensure().PlayKeypadSuccess();
        }

        public static void PlayKeypadFailSfx()
        {
            ChipSfxPlayer.Ensure().PlayKeypadFail();
        }

        public static void PlayKeypadAlarmSfx()
        {
            ChipSfxPlayer.Ensure().PlayKeypadAlarm();
        }

        // 인트로 냄비 끓는 효과음을 재생한다.
        public static void PlayPotSimmerSfx()
        {
            ChipSfxPlayer.Ensure().PlayPotSimmer();
        }

        // 인트로 TV 켜짐 효과음을 재생한다.
        public static void PlayTvOnSfx()
        {
            ChipSfxPlayer.Ensure().PlayTvOn();
        }

        // 인트로 TV 꺼짐 효과음을 재생한다.
        public static void PlayTvOffSfx()
        {
            ChipSfxPlayer.Ensure().PlayTvOff();
        }

        // 인트로 침구 마찰 효과음을 재생한다.
        public static void PlayBeddingRustleSfx()
        {
            ChipSfxPlayer.Ensure().PlayBeddingRustle();
        }

        // 쥐가 짧게 찍찍거리는 칩 효과음을 재생한다.
        public static void PlayMouseSqueakSfx()
        {
            ChipSfxPlayer.Ensure().PlayMouseSqueak();
        }
    }

}
