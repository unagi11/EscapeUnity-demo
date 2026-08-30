using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Escape.Audio
{
    // 칩튠 효과음을 데이터(ChipSfxLibrary 정의)로부터 합성해 재생한다.
    // 각 SFX의 음정/노트는 ChipSfxLibrary(JSON 오버라이드 포함)에서 오고,
    // 아래 볼륨 필드는 사운드별 믹스 레벨로 남는다.
    [RequireComponent(typeof(AudioSource))]
    public sealed class ChipSfxPlayer : MonoBehaviour
    {
        private const int DefaultSampleRate = 48000;
        private const float OutputGain = 1.6f;

        private static ChipSfxPlayer instance;

        [SerializeField, Range(0f, 1f)] private float moveVolume = 0.25f;
        [SerializeField, Range(0f, 1f)] private float clickVolume = 0.18f;
        [SerializeField, Range(0f, 1f)] private float clockTickVolume = 0.07f;
        [SerializeField, Range(0f, 1f)] private float lightSwitchVolume = 0.28f;
        [SerializeField, Range(0f, 1f)] private float whistleVolume = 0.28f;
        [SerializeField, Range(0f, 1f)] private float dialogueTypeVolume = 0.24f;
        [SerializeField, Range(0f, 1f)] private float yeonDialogueTypeVolume = 0.11f;
        [SerializeField, Range(0f, 1f)] private float typewriterTypeVolume = 0.36f;
        [SerializeField, Range(0f, 1f)] private float recycleChakVolume = 0.18f;
        [SerializeField, Range(0f, 1f)] private float rythmRecycleHitVolume = 0.20f;
        [SerializeField, Range(0f, 1f)] private float portraitBounceVolume = 0.22f;
        [SerializeField, Range(0f, 1f)] private float questionBellVolume = 0.24f;
        [SerializeField, Range(0f, 1f)] private float questionOminousVolume = 0.50f;
        [SerializeField, Range(0f, 1f)] private float doorbellVolume = 0.26f;
        [SerializeField, Range(0f, 1f)] private float itemAcquireVolume = 0.09f;
        [SerializeField, Range(0f, 1f)] private float achievementUnlockVolume = 0.20f;
        [SerializeField, Range(0f, 1f)] private float itemUseVolume = 0.16f;
        [SerializeField, Range(0f, 1f)] private float lockPickMotionVolume = 0.24f;
        [SerializeField, Range(0f, 1f)] private float lockPickPinSetVolume = 0.28f;
        [SerializeField, Range(0f, 1f)] private float lockPickSuccessVolume = 0.30f;
        [SerializeField, Range(0f, 1f)] private float keypadVolume = 0.42f;
        [SerializeField, Range(0f, 1f)] private float bangVolume = 0.38f;
        [SerializeField, Range(0f, 1f)] private float hitVolume = 0.30f;
        [SerializeField, Range(0f, 1f)] private float screamVolume = 0.32f;
        [SerializeField, Range(0f, 1f)] private float heartDropVolume = 0.30f;
        [SerializeField, Range(0f, 1f)] private float deathVolume = 0.34f;
        [SerializeField, Range(0f, 1f)] private float healVolume = 0.24f;
        [FormerlySerializedAs("miniShootVolume")]
        [SerializeField, Range(0f, 1f)] private float spaceShooterShootVolume = 0.12f;
        [FormerlySerializedAs("miniEnemyHitVolume")]
        [SerializeField, Range(0f, 1f)] private float spaceShooterEnemyHitVolume = 0.18f;
        [SerializeField, Range(0f, 1f)] private float kitchenVolume = 0.24f;
        [SerializeField, Range(0f, 1f)] private float televisionVolume = 0.24f;
        [SerializeField, Range(0f, 1f)] private float beddingVolume = 0.20f;
        [SerializeField, Range(0f, 1f)] private float volume = 1f;

        private AudioSource audioSource;
        private readonly Dictionary<string, AudioClip> clipCache = new(StringComparer.Ordinal);
        private int clipSampleRate;

        public static ChipSfxPlayer Instance => instance;
        public float Volume => volume;

        public static ChipSfxPlayer Ensure()
        {
            if (instance != null)
            {
                return instance;
            }

            var go = new GameObject("ChipSfxPlayer");
            return go.AddComponent<ChipSfxPlayer>();
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
            audioSource = GetComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 0f;
        }

        public void PlayMove() => Play("move", moveVolume);
        public void PlayClick() => Play("click", clickVolume);
        public void PlayClockTick() => Play("clock_tick", clockTickVolume);
        public void PlayLightSwitch() => Play("light_switch", lightSwitchVolume);
        public void PlayWhistle() => Play("whistle", whistleVolume);
        public void PlayDialogueType() => Play("dialogue_type", dialogueTypeVolume);
        public void PlayYeonDialogueType() => Play("yeon_dialogue_type", yeonDialogueTypeVolume);
        public void PlayTypewriterType() => Play("typewriter_type", typewriterTypeVolume);
        public void PlayRecycleChak() => Play("recycle_chak", recycleChakVolume);
        public void PlayRythmRecycleHit() => Play("rythm_recycle_hit", rythmRecycleHitVolume);
        public void PlayPortraitBounce() => Play("portrait_bounce", portraitBounceVolume);
        public void PlayQuestionBell() => Play("question_bell", questionBellVolume);
        public void PlayQuestionOminous() => Play("question_ominous", questionOminousVolume);
        public void PlayDoorbellLong() => Play("doorbell_long", doorbellVolume);
        public void PlayItemAcquire() => Play("item_acquire", itemAcquireVolume);
        public void PlayAchievementUnlock() => Play("achievement_unlock", achievementUnlockVolume);
        public void PlayItemUse() => Play("item_use", itemUseVolume);
        public void PlayKeypad() => Play("keypad", keypadVolume);
        public void PlayKeypad(char key) => PlayKeypadTone(key);
        public void PlayKeypadSuccess() => Play("keypad_success", keypadVolume);
        public void PlayKeypadFail() => Play("keypad_fail", keypadVolume);
        public void PlayKeypadAlarm() => Play("keypad_alarm", keypadVolume);
        public void PlayBang() => Play("bang", bangVolume);
        public void PlayHit() => Play("hit", hitVolume);
        public void PlayScream() => Play("scream", screamVolume);
        public void PlayHeartDrop() => Play("heart_drop", heartDropVolume);
        public void PlayDeath() => Play("death", deathVolume);
        public void PlayHeal() => Play("heal", healVolume);
        public void PlaySpaceShooterShoot() => Play("space_shoot", spaceShooterShootVolume);
        public void PlaySpaceShooterEnemyHit() => Play("space_enemy_hit", spaceShooterEnemyHitVolume);
        public void PlayPotSimmer() => Play("pot_simmer", kitchenVolume);
        public void PlayTvOn() => Play("tv_on", televisionVolume);
        public void PlayTvOff() => Play("tv_off", televisionVolume);
        public void PlayBeddingRustle() => Play("bedding_rustle", beddingVolume);
        public void PlayMouseSqueak() => Play("mouse_squeak", clickVolume);

        // 문자열 ID는 별칭 없이 내장 SFX의 정식 id와 정확히 일치할 때만 재생한다.
        public bool PlayById(string id)
        {
            string exactId = (id ?? string.Empty).Trim();
            if (!TryGetVolumeForId(exactId, out float perSoundVolume))
            {
                return false;
            }

            Play(exactId, perSoundVolume);
            return true;
        }

        // 특정 게임에서만 효과음 믹스 레벨을 낮춰 재생한다.
        public bool PlayScaled(string id, float volumeScale)
        {
            string exactId = (id ?? string.Empty).Trim();
            if (!TryGetVolumeForId(exactId, out float perSoundVolume))
            {
                return false;
            }

            Play(exactId, perSoundVolume * Mathf.Clamp01(volumeScale));
            return true;
        }

        public void SetVolume(float value)
        {
            volume = Mathf.Clamp01(value);
        }

        // SFX JSON 오버라이드를 다시 읽고 합성 캐시를 비운다(에디터 프리뷰용).
        public void ReloadSfx()
        {
            ChipSfxLibrary.Reload();
            clipCache.Clear();
        }

        // GUI 에디터에서 편집 중인(아직 저장 안 한) 정의를 즉시 합성해 들려준다.
        public void PreviewDefinition(ChipSfxDefinition definition, float perSoundVolume)
        {
            EnsureSampleRate();
            AudioClip clip = ChipSfxSynth.Build(definition, clipSampleRate);
            if (clip != null)
            {
                audioSource.PlayOneShot(clip, ScaledVolume(perSoundVolume));
            }
        }

        // id 정의를 합성해 재생한다(캐시).
        private void Play(string id, float perSoundVolume)
        {
            AudioClip clip = GetClip(id);
            if (clip != null)
            {
                audioSource.PlayOneShot(clip, ScaledVolume(perSoundVolume));
            }
        }

        // 키패드 자리별 톤을 만들어 재생한다.
        private void PlayKeypadTone(char key)
        {
            EnsureSampleRate();
            string id = "keypad_" + key;
            if (!clipCache.TryGetValue(id, out AudioClip clip) || clip == null)
            {
                clip = ChipSfxSynth.Build(ChipSfxLibrary.BuildKeypadTone(key), clipSampleRate);
                clipCache[id] = clip;
            }

            if (clip != null)
            {
                audioSource.PlayOneShot(clip, ScaledVolume(keypadVolume));
            }
        }

        // 정의를 합성한 클립을 얻는다(없으면 만들고 캐시).
        private AudioClip GetClip(string id)
        {
            EnsureSampleRate();
            if (clipCache.TryGetValue(id, out AudioClip clip) && clip != null)
            {
                return clip;
            }

            clip = ChipSfxLibrary.TryGetDefinition(id, out ChipSfxDefinition definition)
                ? ChipSfxSynth.Build(definition, clipSampleRate)
                : null;
            clipCache[id] = clip;
            return clip;
        }

        // 출력 샘플레이트가 바뀌면 캐시를 비워 다시 합성하게 한다.
        private void EnsureSampleRate()
        {
            int sampleRate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : DefaultSampleRate;
            if (clipSampleRate != sampleRate)
            {
                clipSampleRate = sampleRate;
                clipCache.Clear();
            }
        }

        private float ScaledVolume(float value)
        {
            // 개별 SFX 밸런스와 설정 슬라이더 비율은 유지한 채 전체 출력만 보강한다.
            return Mathf.Clamp01(Mathf.Clamp01(value) * volume * OutputGain);
        }

        private bool TryGetVolumeForId(string id, out float perSoundVolume)
        {
            switch (id)
            {
                case "click":
                    perSoundVolume = clickVolume;
                    return true;
                case "clock_tick":
                    perSoundVolume = clockTickVolume;
                    return true;
                case "move":
                    perSoundVolume = moveVolume;
                    return true;
                case "light_switch":
                    perSoundVolume = lightSwitchVolume;
                    return true;
                case "whistle":
                    perSoundVolume = whistleVolume;
                    return true;
                case "dialogue_type":
                    perSoundVolume = dialogueTypeVolume;
                    return true;
                case "yeon_dialogue_type":
                    perSoundVolume = yeonDialogueTypeVolume;
                    return true;
                case "typewriter_type":
                    perSoundVolume = typewriterTypeVolume;
                    return true;
                case "recycle_chak":
                    perSoundVolume = recycleChakVolume;
                    return true;
                case "rythm_recycle_hit":
                    perSoundVolume = rythmRecycleHitVolume;
                    return true;
                case "portrait_bounce":
                    perSoundVolume = portraitBounceVolume;
                    return true;
                case "question_bell":
                    perSoundVolume = questionBellVolume;
                    return true;
                case "question_ominous":
                    perSoundVolume = questionOminousVolume;
                    return true;
                case "doorbell_long":
                    perSoundVolume = doorbellVolume;
                    return true;
                case "item_acquire":
                    perSoundVolume = itemAcquireVolume;
                    return true;
                case "achievement_unlock":
                    perSoundVolume = achievementUnlockVolume;
                    return true;
                case "item_use":
                    perSoundVolume = itemUseVolume;
                    return true;
                case "lockpick_pick_up":
                case "lockpick_pick_down":
                    perSoundVolume = lockPickMotionVolume;
                    return true;
                case "lockpick_pin_set":
                    perSoundVolume = lockPickPinSetVolume;
                    return true;
                case "lockpick_success":
                    perSoundVolume = lockPickSuccessVolume;
                    return true;
                case "keypad":
                case "keypad_success":
                case "keypad_fail":
                case "keypad_alarm":
                    perSoundVolume = keypadVolume;
                    return true;
                case "bang":
                    perSoundVolume = bangVolume;
                    return true;
                case "hit":
                    perSoundVolume = hitVolume;
                    return true;
                case "scream":
                    perSoundVolume = screamVolume;
                    return true;
                case "heart_drop":
                    perSoundVolume = heartDropVolume;
                    return true;
                case "death":
                    perSoundVolume = deathVolume;
                    return true;
                case "heal":
                    perSoundVolume = healVolume;
                    return true;
                case "space_shoot":
                    perSoundVolume = spaceShooterShootVolume;
                    return true;
                case "space_enemy_hit":
                    perSoundVolume = spaceShooterEnemyHitVolume;
                    return true;
                case "pot_simmer":
                    perSoundVolume = kitchenVolume;
                    return true;
                case "tv_on":
                case "tv_off":
                    perSoundVolume = televisionVolume;
                    return true;
                case "bedding_rustle":
                    perSoundVolume = beddingVolume;
                    return true;
                case "mouse_squeak":
                    perSoundVolume = clickVolume;
                    return true;
                default:
                    perSoundVolume = 0f;
                    return false;
            }
        }
    }
}
