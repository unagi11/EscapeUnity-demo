using Escape.Progress;
using Escape.Audio;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Escape.Rooms
{
    // 체력에 따른 심도 프로필을 RoomImage 런타임 Material에 부드럽게 적용한다.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RawImage))]
    public sealed class RoomPostEffectController : MonoBehaviour
    {
        private static readonly int VectorBlurDirectionId = Shader.PropertyToID("_VectorBlurDirection");
        private static readonly int VectorBlurIntensityId = Shader.PropertyToID("_VectorBlurIntensity");
        private static readonly int VectorBlurLengthId = Shader.PropertyToID("_VectorBlurLength");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int FlickerIntensityId = Shader.PropertyToID("_FlickerIntensity");
        private static readonly int PaletteIntensityId = Shader.PropertyToID("_PaletteIntensity");
        private static readonly int HueShiftId = Shader.PropertyToID("_HueShift");
        private static readonly int VignetteIntensityId = Shader.PropertyToID("_VignetteIntensity");
        private const float VectorBlurLength = 6f;

        public enum PostEffectState
        {
            Default,
            Warn,
            Danger,
            Drunken,
            RythmComboFire,
            RythmComboRainbow
        }

        [SerializeField] private RawImage roomImage;
        [SerializeField] private RoomPostEffectSettings defaultProfile;
        [FormerlySerializedAs("warningProfile")]
        [FormerlySerializedAs("hp60Profile")]
        [FormerlySerializedAs("depth2Profile")]
        [FormerlySerializedAs("hp50Profile")]
        [SerializeField] private RoomPostEffectSettings warnProfile;
        [FormerlySerializedAs("hp20Profile")]
        [FormerlySerializedAs("depth3Profile")]
        [SerializeField] private RoomPostEffectSettings dangerProfile;
        [FormerlySerializedAs("depth4Profile")]
        [SerializeField] private RoomPostEffectSettings drunkenProfile;
        [SerializeField] private RoomPostEffectSettings rythmComboFireProfile;
        [SerializeField] private RoomPostEffectSettings rythmComboRainbowProfile;
        [SerializeField, Min(0f)] private float transitionDuration = 0.5f;
        [SerializeField] private bool followHealth = true;

        private GameSession state;
        private Material sourceMaterial;
        private Material runtimeMaterial;
        private RoomPostEffectSettings currentProfile;
        private RoomPostEffectSettings runtimeRythmComboFireProfile;
        private RoomPostEffectSettings runtimeRythmComboRainbowProfile;
        private RoomPostEffectSettings transitionFrom;
        private RoomPostEffectSettings transitionTo;
        private float transitionElapsed;
        private float activeTransitionDuration;
        private bool isTransitioning;
        private bool transitionAppliesBgmPitch = true;
        private bool started;
        private bool blurOverride;
        private Vector2 vectorBlurDirection;
        private bool drunkenOverride;
        private PostEffectState? rythmComboOverrideState;
        private PostEffectState? storyHealthOverrideState;

        public Material RuntimeMaterial => runtimeMaterial;
        public bool IsTransitioning => isTransitioning;
        public PostEffectState CurrentState { get; private set; } = PostEffectState.Default;

        // RoomImage 전용 런타임 Material을 만들고 기본 프로필을 적용한다.
        private void Awake()
        {
            if (roomImage == null || roomImage.material == null || defaultProfile == null)
            {
                Debug.LogError("RoomPostEffectController requires RoomImage, its Material, and Default profile.", this);
                enabled = false;
                return;
            }

            sourceMaterial = roomImage.material;
            runtimeMaterial = new Material(sourceMaterial)
            {
                name = "RoomImagePostEffectRuntime"
            };
            roomImage.material = runtimeMaterial;
            defaultProfile.ApplyTo(runtimeMaterial);
            SoundPlayer.SetBgmPitch(defaultProfile.BgmPitch);
            currentProfile = defaultProfile;
        }

        // GameSession가 준비된 뒤 현재 체력에 맞는 첫 심도를 적용한다.
        private void Start()
        {
            started = true;
            BindState();
            SetState(ResolveHealthState(), true);
        }

        // 비활성화 후 다시 켜질 때 상태 변경 구독을 복구한다.
        private void OnEnable()
        {
            if (started)
            {
                BindState();
            }
        }

        // 비활성화 중에는 게임 상태 변경을 받지 않는다.
        private void OnDisable()
        {
            UnbindState();
        }

        // 런타임 Material을 정리하고 원본 Material을 복구한다.
        private void OnDestroy()
        {
            UnbindState();
            if (roomImage != null && roomImage.material == runtimeMaterial)
            {
                roomImage.material = sourceMaterial;
            }

            if (runtimeMaterial != null)
            {
                Destroy(runtimeMaterial);
                runtimeMaterial = null;
            }

            ChipSynthPlayer.Instance?.SetPitch(1f);
        }

        // 진행 중인 심도 전환을 프레임마다 보간한다.
        private void Update()
        {
            if (followHealth && state == null)
            {
                BindState();
                if (state != null)
                {
                    SetState(ResolveHealthState(), true);
                }
            }

            if (!isTransitioning || runtimeMaterial == null)
            {
                return;
            }

            transitionElapsed += Time.deltaTime;
            float duration = activeTransitionDuration > 0f ? activeTransitionDuration : transitionDuration;
            float t = duration <= 0f
                ? 1f
                : Mathf.Clamp01(transitionElapsed / duration);
            float smoothT = t * t * (3f - 2f * t);
            RoomPostEffectSettings.ApplyBlend(runtimeMaterial, transitionFrom, transitionTo, smoothT);
            if (transitionAppliesBgmPitch)
            {
                SoundPlayer.SetBgmPitch(RoomPostEffectSettings.LerpBgmPitch(transitionFrom, transitionTo, smoothT));
            }
            ApplyBlurOverride();

            if (t >= 1f)
            {
                isTransitioning = false;
                currentProfile = transitionTo;
            }
        }

        // 외부 연출에서도 사용할 수 있도록 지정 상태로 전환한다.
        public void SetState(
            PostEffectState nextState,
            bool immediate = false,
            bool applyProfilePitch = true,
            float transitionDurationOverride = -1f)
        {
            RoomPostEffectSettings nextProfile = GetProfile(nextState);
            if (runtimeMaterial == null || nextProfile == null || (!immediate && nextState == CurrentState))
            {
                return;
            }

            CurrentState = nextState;
            float duration = transitionDurationOverride >= 0f ? transitionDurationOverride : transitionDuration;
            if (immediate || duration <= 0f || currentProfile == null)
            {
                nextProfile.ApplyTo(runtimeMaterial);
                if (applyProfilePitch)
                {
                    SoundPlayer.SetBgmPitch(nextProfile.BgmPitch);
                }
                currentProfile = nextProfile;
                isTransitioning = false;
                ApplyBlurOverride();
                return;
            }

            transitionFrom = currentProfile;
            transitionTo = nextProfile;
            transitionElapsed = 0f;
            activeTransitionDuration = duration;
            isTransitioning = true;
            transitionAppliesBgmPitch = applyProfilePitch;
        }

        // 스토리 연출에서 Drunken 프로필을 켠다.
        public void EnterDrunken()
        {
            drunkenOverride = true;
            SetState(PostEffectState.Drunken);
        }

        // Drunken 프로필을 끄고 현재 체력 상태로 돌아간다.
        public void ExitDrunken()
        {
            drunkenOverride = false;
            SetState(ResolveHealthState());
        }

        // 스토리의 중간 어지럼 연출 동안 주의 단계 프로필을 사용한다.
        public void SetWarnOverride(bool enabled)
        {
            SetStoryHealthOverride(PostEffectState.Warn, enabled);
        }

        // 스토리 충격 연출 동안 실제 체력과 무관하게 위험 단계 프로필을 유지한다.
        public void SetDangerOverride(bool enabled)
        {
            SetStoryHealthOverride(PostEffectState.Danger, enabled);
        }

        // 기존 TSV 토큰/코드 경로가 주의 단계 프로필을 계속 사용할 수 있게 유지한다.
        public void SetWarningOverride(bool enabled)
        {
            SetWarnOverride(enabled);
        }

        // 대사 shader 컬럼의 프로필 이름을 현재 스토리 override로 적용한다.
        public bool SetStoryProfile(string profileName)
        {
            if (!TryResolveProfileName(profileName, out PostEffectState effectState))
            {
                return false;
            }

            storyHealthOverrideState = effectState;
            SetState(ResolveHealthState());
            return true;
        }

        // 요청한 단계만 켜거나 해제해 다른 스토리 체력 연출을 실수로 지우지 않는다.
        public void SetRythmComboOverride(
            PostEffectState? effectState,
            bool immediate = false,
            float transitionDurationOverride = -1f)
        {
            if (effectState.HasValue &&
                effectState.Value != PostEffectState.RythmComboFire &&
                effectState.Value != PostEffectState.RythmComboRainbow)
            {
                return;
            }

            rythmComboOverrideState = effectState;
            PostEffectState nextState = ResolveHealthState();
            if (!effectState.HasValue &&
                (nextState == PostEffectState.RythmComboFire || nextState == PostEffectState.RythmComboRainbow))
            {
                nextState = PostEffectState.Default;
            }

            SetState(nextState, immediate, false, transitionDurationOverride);
        }

        public void SetRythmComboBeatPulse(float pulse, bool rainbow)
        {
            if (runtimeMaterial == null)
            {
                return;
            }

            pulse = Mathf.Clamp01(pulse);
            SetMaterialFloat(IntensityId, 0f);
            SetMaterialFloat(FlickerIntensityId, Mathf.Lerp(0.08f, 0.20f, pulse));
            SetMaterialFloat(PaletteIntensityId, rainbow
                ? Mathf.Lerp(0.12f, 0.26f, pulse)
                : Mathf.Lerp(0.16f, 0.34f, pulse));
            SetMaterialFloat(VignetteIntensityId, Mathf.Lerp(0.02f, 0.08f, pulse));
            SetMaterialFloat(HueShiftId, rainbow
                ? Mathf.Lerp(0.015f, 0.045f, pulse)
                : 0.01f);
        }

        private void SetStoryHealthOverride(PostEffectState effectState, bool enabled)
        {
            if (enabled)
            {
                storyHealthOverrideState = effectState;
            }
            else if (storyHealthOverrideState == effectState)
            {
                storyHealthOverrideState = null;
            }

            SetState(ResolveHealthState());
        }

        // 대사 연출이 요청한 블러를 심도 보간보다 우선 적용한다.
        public void SetBlurOverride(bool enabled)
        {
            blurOverride = enabled;
            vectorBlurDirection = Vector2.zero;
            if (runtimeMaterial == null)
            {
                return;
            }

            if (!enabled)
            {
                if (isTransitioning)
                {
                    float duration = activeTransitionDuration > 0f ? activeTransitionDuration : transitionDuration;
                    float t = duration <= 0f
                        ? 1f
                        : Mathf.Clamp01(transitionElapsed / duration);
                    float smoothT = t * t * (3f - 2f * t);
                    RoomPostEffectSettings.ApplyBlend(runtimeMaterial, transitionFrom, transitionTo, smoothT);
                }
                else
                {
                    currentProfile?.ApplyTo(runtimeMaterial);
                }
            }

            ApplyBlurOverride();
        }

        // 대사 연출이 요청한 방향으로 이동 잔상을 남기는 블러를 적용한다.
        public void SetVectorBlurOverride(Vector2 direction)
        {
            if (direction.sqrMagnitude <= 0.0001f)
            {
                SetBlurOverride(false);
                return;
            }

            blurOverride = true;
            vectorBlurDirection = direction.normalized;
            ApplyBlurOverride();
        }

        // 현재 체력을 Default, Warn, Danger 상태로 변환한다. 최대 체력에서 두 번 감소하면 Warn이 된다.
        private PostEffectState ResolveHealthState()
        {
            if (drunkenOverride)
            {
                return PostEffectState.Drunken;
            }

            if (storyHealthOverrideState.HasValue)
            {
                return storyHealthOverrideState.Value;
            }

            if (rythmComboOverrideState.HasValue)
            {
                return rythmComboOverrideState.Value;
            }

            if (!followHealth || state == null)
            {
                return CurrentState;
            }

            int health = Mathf.Clamp(state.CurrentHealth, 0, state.MaxHealth);
            int warnHealth = Mathf.Max(0, state.MaxHealth - 2);
            return health switch
            {
                <= 0 => PostEffectState.Danger,
                _ when health <= warnHealth => PostEffectState.Warn,
                _ => PostEffectState.Default
            };
        }

        // 상태에 연결된 프로필을 반환한다.
        private RoomPostEffectSettings GetProfile(PostEffectState effectState)
        {
            return effectState switch
            {
                PostEffectState.Warn => warnProfile != null ? warnProfile : defaultProfile,
                PostEffectState.Danger => dangerProfile != null ? dangerProfile : defaultProfile,
                PostEffectState.Drunken => drunkenProfile != null ? drunkenProfile : defaultProfile,
                PostEffectState.RythmComboFire => rythmComboFireProfile != null
                    ? rythmComboFireProfile
                    : GetRuntimeRythmComboFireProfile(),
                PostEffectState.RythmComboRainbow => rythmComboRainbowProfile != null
                    ? rythmComboRainbowProfile
                    : GetRuntimeRythmComboRainbowProfile(),
                _ => defaultProfile
            };
        }

        // 리듬 콤보 프로필 참조가 비어 있어도 기본 효과를 적용할 수 있게 한다.
        private RoomPostEffectSettings GetRuntimeRythmComboFireProfile()
        {
            runtimeRythmComboFireProfile ??= RoomPostEffectSettings.CreateRythmComboFireRuntimeProfile();
            return runtimeRythmComboFireProfile;
        }

        private RoomPostEffectSettings GetRuntimeRythmComboRainbowProfile()
        {
            runtimeRythmComboRainbowProfile ??= RoomPostEffectSettings.CreateRythmComboRainbowRuntimeProfile();
            return runtimeRythmComboRainbowProfile;
        }

        // TSV shader 컬럼에서 들어온 asset 이름을 내부 상태로 바꾼다.
        private static bool TryResolveProfileName(string profileName, out PostEffectState effectState)
        {
            string normalized = (profileName ?? string.Empty)
                .Trim()
                .Replace(" ", string.Empty)
                .Replace("_", string.Empty)
                .Replace("-", string.Empty)
                .ToUpperInvariant();

            switch (normalized)
            {
                case "DEFAULT":
                    effectState = PostEffectState.Default;
                    return true;
                case "WARN":
                case "WARNING":
                    effectState = PostEffectState.Warn;
                    return true;
                case "DANGER":
                case "DARNGER":
                    effectState = PostEffectState.Danger;
                    return true;
                case "DRUNKEN":
                    effectState = PostEffectState.Drunken;
                    return true;
                case "RYTHMCOMBOFIRE":
                case "RHYTHMCOMBOFIRE":
                case "COMBOFIRE":
                    effectState = PostEffectState.RythmComboFire;
                    return true;
                case "RYTHMCOMBORAINBOW":
                case "RHYTHMCOMBORAINBOW":
                case "COMBORAINBOW":
                    effectState = PostEffectState.RythmComboRainbow;
                    return true;
                default:
                    effectState = PostEffectState.Default;
                    return false;
            }
        }

        // GameSession 상태 변경을 구독한다.
        private void BindState()
        {
            GameSession nextState = GameSession.Instance;
            if (state == nextState)
            {
                return;
            }

            UnbindState();
            state = nextState;
            if (state != null)
            {
                state.Changed += HandleStateChanged;
            }
        }

        // GameSession 상태 변경 구독을 해제한다.
        private void UnbindState()
        {
            if (state != null)
            {
                state.Changed -= HandleStateChanged;
                state = null;
            }
        }

        // 체력 구간이 바뀌면 다음 심도 프로필로 전환한다.
        private void HandleStateChanged()
        {
            if (!drunkenOverride)
            {
                SetState(ResolveHealthState(), false, !rythmComboOverrideState.HasValue);
            }
        }

        // Inspector의 체력 임계값 순서를 유효하게 유지한다.
        private void OnValidate()
        {
            transitionDuration = Mathf.Max(0f, transitionDuration);
        }

        // 블러 override가 켜져 있으면 최종 강도를 고정하고 방향 잔상을 덮어쓴다.
        private void ApplyBlurOverride()
        {
            if (runtimeMaterial == null)
            {
                return;
            }

            bool useVectorBlur = blurOverride && vectorBlurDirection.sqrMagnitude > 0.0001f;
            if (runtimeMaterial.HasProperty(VectorBlurDirectionId))
            {
                runtimeMaterial.SetVector(
                    VectorBlurDirectionId,
                    useVectorBlur ? new Vector4(vectorBlurDirection.x, vectorBlurDirection.y, 0f, 0f) : Vector4.zero);
            }

            if (runtimeMaterial.HasProperty(VectorBlurIntensityId))
            {
                runtimeMaterial.SetFloat(VectorBlurIntensityId, useVectorBlur ? 1f : 0f);
            }

            if (runtimeMaterial.HasProperty(VectorBlurLengthId))
            {
                runtimeMaterial.SetFloat(VectorBlurLengthId, useVectorBlur ? VectorBlurLength : 1f);
            }

            if (blurOverride && runtimeMaterial.HasProperty("_Intensity"))
            {
                runtimeMaterial.SetFloat(IntensityId, 1f);
            }
        }

        private void SetMaterialFloat(int propertyId, float value)
        {
            if (runtimeMaterial != null && runtimeMaterial.HasProperty(propertyId))
            {
                runtimeMaterial.SetFloat(propertyId, value);
            }
        }
    }
}
