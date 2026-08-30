using System;
using System.Collections.Generic;
using UnityEngine;

namespace Escape.Rooms
{
    // Aseprite 룸 애니메이션의 반복 방식을 지정한다.
    public enum AsepriteSpritePlaybackMode
    {
        Once = 0,
        Loop = 1,
    }

    // Aseprite 태그로 등록된 룸 오브젝트 애니메이션을 effect에서 재생한다.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class AsepriteRoomAnimator : MonoBehaviour
    {
        [Serializable]
        public sealed class AnimationDefinition
        {
            [SerializeField] private string animationName = string.Empty;
            [SerializeField] private Sprite[] frames = new Sprite[0];
            [SerializeField] private Vector2[] frameLocalPositions = new Vector2[0];
            [SerializeField, Min(1)] private int frameDurationMs = 100;
            [SerializeField] private AsepriteSpritePlaybackMode playbackMode = AsepriteSpritePlaybackMode.Once;

            public string AnimationName => animationName;
            public IReadOnlyList<Sprite> Frames => frames;
            public IReadOnlyList<Vector2> FrameLocalPositions => frameLocalPositions;
            public int FrameDurationMs => frameDurationMs;
            public AsepriteSpritePlaybackMode PlaybackMode => playbackMode;

            public AnimationDefinition(
                string nextAnimationName,
                IReadOnlyList<Sprite> nextFrames,
                int nextFrameDurationMs,
                AsepriteSpritePlaybackMode nextPlaybackMode)
                : this(nextAnimationName, nextFrames, null, nextFrameDurationMs, nextPlaybackMode)
            {
            }

            public AnimationDefinition(
                string nextAnimationName,
                IReadOnlyList<Sprite> nextFrames,
                IReadOnlyList<Vector2> nextFrameLocalPositions,
                int nextFrameDurationMs,
                AsepriteSpritePlaybackMode nextPlaybackMode)
            {
                animationName = string.IsNullOrWhiteSpace(nextAnimationName) ? "default" : nextAnimationName.Trim();
                frames = AsepriteRoomAnimator.CopySprites(nextFrames);
                frameLocalPositions = AsepriteRoomAnimator.CopyFrameLocalPositions(nextFrameLocalPositions);
                frameDurationMs = Mathf.Max(1, nextFrameDurationMs);
                playbackMode = nextPlaybackMode;
            }
        }

        [SerializeField] private SpriteRenderer targetRenderer;
        [SerializeField] private Sprite defaultSprite;
        [SerializeField] private string selectedAnimationName = string.Empty;
        [SerializeField] private AsepriteSpritePlaybackMode selectedPlaybackMode = AsepriteSpritePlaybackMode.Once;
        [SerializeField] private AnimationDefinition[] animations = new AnimationDefinition[0];

        private AnimationDefinition currentAnimation;
        private AsepriteSpritePlaybackMode currentPlaybackMode;
        private int frameIndex;
        private float frameTimer;
        private bool isPlaying;
        private bool isPlaybackPaused;
        private bool hasAppliedAnimationState;
        private Vector3 defaultLocalPosition;
        private bool hasDefaultLocalPosition;

        public string SelectedAnimationName => selectedAnimationName;
        public AsepriteSpritePlaybackMode SelectedPlaybackMode => selectedPlaybackMode;
        public string CurrentAnimationName => currentAnimation != null ? currentAnimation.AnimationName : string.Empty;
        public AsepriteSpritePlaybackMode CurrentPlaybackMode => currentPlaybackMode;
        public bool HasAppliedAnimationState => hasAppliedAnimationState && currentAnimation != null;
        public bool IsPlaying => isPlaying;

        private void Reset()
        {
            targetRenderer = GetComponent<SpriteRenderer>();
            defaultSprite = targetRenderer != null ? targetRenderer.sprite : null;
        }

        private void OnEnable()
        {
            targetRenderer ??= GetComponent<SpriteRenderer>();
            if (defaultSprite == null && targetRenderer != null)
            {
                defaultSprite = targetRenderer.sprite;
            }

            CaptureDefaultLocalPosition();

            if (hasAppliedAnimationState && currentAnimation != null)
            {
                ApplyPlaybackState(currentAnimation, currentPlaybackMode, currentPlaybackMode == AsepriteSpritePlaybackMode.Once);
                return;
            }

            ApplySelectedAnimationState();
        }

        private void OnValidate()
        {
            targetRenderer ??= GetComponent<SpriteRenderer>();
            if (defaultSprite == null && targetRenderer != null)
            {
                defaultSprite = targetRenderer.sprite;
            }

            CaptureDefaultLocalPosition();

            if (!Application.isPlaying)
            {
                ApplySelectedAnimationState();
            }
        }

        private void Update()
        {
            if (isPlaybackPaused || !isPlaying || targetRenderer == null || currentAnimation == null || currentAnimation.Frames.Count == 0)
            {
                return;
            }

            frameTimer += Time.deltaTime;
            float duration = Mathf.Max(0.001f, currentAnimation.FrameDurationMs / 1000f);
            while (frameTimer >= duration)
            {
                frameTimer -= duration;
                frameIndex++;
                if (frameIndex >= currentAnimation.Frames.Count)
                {
                    if (currentPlaybackMode == AsepriteSpritePlaybackMode.Loop)
                    {
                        frameIndex = 0;
                    }
                    else
                    {
                        frameIndex = currentAnimation.Frames.Count - 1;
                        isPlaying = false;
                    }
                }

                ApplyFrame();
                if (!isPlaying)
                {
                    return;
                }
            }
        }

        // 방 전환 캡처 중에는 첫 프레임을 유지하되 전역 timeScale은 건드리지 않는다.
        public void SetPlaybackPaused(bool paused)
        {
            isPlaybackPaused = paused;
        }

        // 에디터 빌더가 SpriteRenderer와 태그 애니메이션 목록을 주입한다.
        public void Configure(
            SpriteRenderer renderer,
            IReadOnlyList<AnimationDefinition> nextAnimations)
        {
            targetRenderer = renderer != null ? renderer : GetComponent<SpriteRenderer>();
            defaultSprite = targetRenderer != null ? targetRenderer.sprite : defaultSprite;
            defaultLocalPosition = transform.localPosition;
            hasDefaultLocalPosition = true;
            animations = MergeAnimationsPreservingFrameDurations(animations, nextAnimations);
            if (!string.IsNullOrWhiteSpace(selectedAnimationName) && FindAnimation(selectedAnimationName) == null)
            {
                selectedAnimationName = string.Empty;
            }

            if (!Application.isPlaying)
            {
                hasAppliedAnimationState = false;
                ApplySelectedAnimationState();
            }
        }

        // Dialogue effect에서 지정한 이름의 애니메이션을 재생하고 once 재생 길이를 반환한다.
        public bool TryPlay(
            string animationName,
            bool hasPlaybackOverride,
            AsepriteSpritePlaybackMode playbackOverride,
            out float durationSeconds)
        {
            durationSeconds = 0f;
            AnimationDefinition animation = FindAnimation(animationName);
            if (animation == null || animation.Frames.Count == 0)
            {
                return false;
            }

            ApplyPlaybackState(
                animation,
                hasPlaybackOverride ? playbackOverride : animation.PlaybackMode,
                false,
                true);

            if (currentPlaybackMode == AsepriteSpritePlaybackMode.Once)
            {
                durationSeconds = animation.Frames.Count * Mathf.Max(0.001f, animation.FrameDurationMs / 1000f);
            }

            return true;
        }

        // 저장 데이터나 인스펙터 정적 선택을 현재 표시 상태로 적용한다.
        public bool TryApplyAnimationState(string animationName, AsepriteSpritePlaybackMode playbackMode)
        {
            AnimationDefinition animation = FindAnimation(animationName);
            if (animation == null || animation.Frames.Count == 0)
            {
                return false;
            }

            ApplyPlaybackState(animation, playbackMode, playbackMode == AsepriteSpritePlaybackMode.Once);
            return true;
        }

        // 인스펙터에서 고른 정적 애니메이션을 다시 적용한다.
        public bool ApplySelectedAnimationState()
        {
            if (string.IsNullOrWhiteSpace(selectedAnimationName))
            {
                ClearAnimationState();
                return false;
            }

            return TryApplyAnimationState(selectedAnimationName, selectedPlaybackMode);
        }

        // 적용된 태그 애니메이션을 비우고 기본 스프라이트로 되돌린다.
        public void ClearAnimationState()
        {
            currentAnimation = null;
            frameIndex = 0;
            frameTimer = 0f;
            isPlaying = false;
            isPlaybackPaused = false;
            hasAppliedAnimationState = false;
            ApplyDefaultFrame();
        }

        // 저장용으로 현재 적용된 애니메이션 이름과 재생 방식을 반환한다.
        public bool TryGetAnimationState(out string animationName, out AsepriteSpritePlaybackMode playbackMode)
        {
            animationName = CurrentAnimationName;
            playbackMode = currentPlaybackMode;
            return HasAppliedAnimationState && !string.IsNullOrWhiteSpace(animationName);
        }

        private void ApplyPlaybackState(
            AnimationDefinition animation,
            AsepriteSpritePlaybackMode playbackMode,
            bool showLastFrame,
            bool playOnceFromStart = false)
        {
            currentAnimation = animation;
            currentPlaybackMode = playbackMode;
            frameIndex = showLastFrame ? animation.Frames.Count - 1 : 0;
            frameTimer = 0f;
            isPlaying = playbackMode == AsepriteSpritePlaybackMode.Loop ||
                        (playOnceFromStart && playbackMode == AsepriteSpritePlaybackMode.Once);
            hasAppliedAnimationState = true;
            ApplyFrame();
        }

        private void ApplyDefaultFrame()
        {
            if (targetRenderer != null && defaultSprite != null)
            {
                targetRenderer.sprite = defaultSprite;
            }

            if (hasDefaultLocalPosition)
            {
                transform.localPosition = defaultLocalPosition;
            }
        }

        private void ApplyFrame()
        {
            if (targetRenderer == null || currentAnimation == null || currentAnimation.Frames.Count == 0)
            {
                return;
            }

            frameIndex = Mathf.Clamp(frameIndex, 0, currentAnimation.Frames.Count - 1);
            Sprite sprite = currentAnimation.Frames[frameIndex];
            if (sprite != null)
            {
                targetRenderer.sprite = sprite;
            }

            if (TryGetFrameLocalPosition(currentAnimation, frameIndex, out var frameLocalPosition))
            {
                var currentLocalPosition = transform.localPosition;
                transform.localPosition = new Vector3(frameLocalPosition.x, frameLocalPosition.y, currentLocalPosition.z);
            }
        }

        // 기본 스프라이트로 돌아갈 때 쓸 기준 위치를 기록한다.
        private void CaptureDefaultLocalPosition()
        {
            if (hasDefaultLocalPosition)
            {
                return;
            }

            defaultLocalPosition = transform.localPosition;
            hasDefaultLocalPosition = true;
        }

        // 프레임별 trim 보정 위치가 있으면 현재 프레임 위치를 반환한다.
        private static bool TryGetFrameLocalPosition(
            AnimationDefinition animation,
            int index,
            out Vector2 frameLocalPosition)
        {
            frameLocalPosition = default;
            if (animation == null ||
                animation.FrameLocalPositions == null ||
                index < 0 ||
                index >= animation.FrameLocalPositions.Count)
            {
                return false;
            }

            frameLocalPosition = animation.FrameLocalPositions[index];
            return true;
        }

        private AnimationDefinition FindAnimation(string animationName)
        {
            string targetName = (animationName ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(targetName) || animations == null)
            {
                return null;
            }

            for (int i = 0; i < animations.Length; i++)
            {
                AnimationDefinition animation = animations[i];
                if (animation != null &&
                    string.Equals(animation.AnimationName, targetName, StringComparison.OrdinalIgnoreCase))
                {
                    return animation;
                }
            }

            return null;
        }

        // 재빌드 시 같은 이름의 애니메이션은 Inspector에서 조정한 프레임 시간을 유지한다.
        private static AnimationDefinition[] MergeAnimationsPreservingFrameDurations(
            IReadOnlyList<AnimationDefinition> currentAnimations,
            IReadOnlyList<AnimationDefinition> nextAnimations)
        {
            if (nextAnimations == null || nextAnimations.Count == 0)
            {
                return new AnimationDefinition[0];
            }

            var result = new AnimationDefinition[nextAnimations.Count];
            for (int i = 0; i < nextAnimations.Count; i++)
            {
                AnimationDefinition nextAnimation = nextAnimations[i];
                if (nextAnimation == null)
                {
                    continue;
                }

                AnimationDefinition currentAnimation = FindAnimation(currentAnimations, nextAnimation.AnimationName);
                int frameDurationMs = currentAnimation != null
                    ? currentAnimation.FrameDurationMs
                    : nextAnimation.FrameDurationMs;
                result[i] = new AnimationDefinition(
                    nextAnimation.AnimationName,
                    nextAnimation.Frames,
                    nextAnimation.FrameLocalPositions,
                    frameDurationMs,
                    nextAnimation.PlaybackMode);
            }

            return result;
        }

        // 목록에서 이름이 같은 애니메이션 정의를 찾는다.
        private static AnimationDefinition FindAnimation(
            IReadOnlyList<AnimationDefinition> source,
            string animationName)
        {
            if (source == null || string.IsNullOrWhiteSpace(animationName))
            {
                return null;
            }

            for (int i = 0; i < source.Count; i++)
            {
                AnimationDefinition animation = source[i];
                if (animation != null &&
                    string.Equals(animation.AnimationName, animationName, StringComparison.OrdinalIgnoreCase))
                {
                    return animation;
                }
            }

            return null;
        }

        private static Sprite[] CopySprites(IReadOnlyList<Sprite> source)
        {
            if (source == null || source.Count == 0)
            {
                return new Sprite[0];
            }

            var result = new Sprite[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                result[i] = source[i];
            }

            return result;
        }

        // 생성자에 전달된 프레임별 위치 배열을 직렬화 배열로 복사한다.
        private static Vector2[] CopyFrameLocalPositions(IReadOnlyList<Vector2> source)
        {
            if (source == null || source.Count == 0)
            {
                return new Vector2[0];
            }

            var result = new Vector2[source.Count];
            for (int i = 0; i < source.Count; i++)
            {
                result[i] = source[i];
            }

            return result;
        }
    }
}
