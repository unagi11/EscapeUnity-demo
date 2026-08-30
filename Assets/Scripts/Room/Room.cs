using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Serialization;

namespace Escape.Rooms
{
    [DisallowMultipleComponent]
    public sealed class Room : MonoBehaviour
    {
        [Serializable]
        public sealed class EntranceAnimationCandidate
        {
            [SerializeField] private AsepriteRoomAnimator asepriteRoomAnimator;
            [SerializeField] private string animationName = string.Empty;

            public AsepriteRoomAnimator Animator => asepriteRoomAnimator;
            public string AnimationName => animationName;

            // 후보가 실제 재생 가능한 최소 정보를 갖췄는지 반환한다.
            public bool CanPlay()
            {
                return asepriteRoomAnimator != null &&
                       !string.IsNullOrWhiteSpace(animationName);
            }
        }

        [SerializeField] private RoomType roomId = RoomType.None;
        [SerializeField] private InteractionRule[] fallbackInteractions = Array.Empty<InteractionRule>();
        [SerializeField, FormerlySerializedAs("ambientAnimationProbability"), Range(0f, 1f)] private float entranceAnimationProbability = 0.2f;
        [SerializeField, FormerlySerializedAs("ambientAnimationCandidates")] private EntranceAnimationCandidate[] entranceAnimationCandidates = Array.Empty<EntranceAnimationCandidate>();

        private CancellationTokenSource entranceAnimationCts;
        private AsepriteRoomAnimator preparedEntranceAnimator;

        public RoomType RoomId
        {
            get => roomId;
            set => roomId = value;
        }

        public InteractionRule[] FallbackInteractions => fallbackInteractions ?? Array.Empty<InteractionRule>();
        public bool HasFallbackInteractions => fallbackInteractions != null && fallbackInteractions.Length > 0;

        // 방 입장 시 등록된 후보 중 하나를 확률로 골라 재생한다.
        public void PlayEntranceAnimation()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            StopEntranceAnimation();
            if (!TryPickEntranceAnimation(out EntranceAnimationCandidate candidate))
            {
                return;
            }

            entranceAnimationCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            PlayEntranceAnimationAsync(candidate, entranceAnimationCts).Forget();
        }

        // 전환 캡처 전에 입장 연출 첫 프레임을 적용하고 해당 애니메이터만 잠시 멈춘다.
        public bool PrepareEntranceAnimation()
        {
            if (!isActiveAndEnabled)
            {
                return false;
            }

            StopEntranceAnimation();
            if (!TryPickEntranceAnimation(out EntranceAnimationCandidate candidate) ||
                !PlayEntranceAnimationClip(candidate))
            {
                return false;
            }

            preparedEntranceAnimator = candidate.Animator;
            preparedEntranceAnimator.SetPlaybackPaused(true);
            return true;
        }

        public void ResumePreparedEntranceAnimation()
        {
            if (preparedEntranceAnimator == null)
            {
                return;
            }

            preparedEntranceAnimator.SetPlaybackPaused(false);
            preparedEntranceAnimator = null;
        }

        // 비활성화된 방의 예약 연출을 정리한다.
        private void OnDisable()
        {
            StopEntranceAnimation();
        }

        // 인스펙터에서 입장 연출 확률이 유효 범위에 머물도록 보정한다.
        private void OnValidate()
        {
            entranceAnimationProbability = Mathf.Clamp01(entranceAnimationProbability);
        }

        // 진행 중인 입장 연출 루틴을 멈춘다.
        private void StopEntranceAnimation()
        {
            ResumePreparedEntranceAnimation();
            if (entranceAnimationCts == null)
            {
                return;
            }

            entranceAnimationCts.Cancel();
            entranceAnimationCts.Dispose();
            entranceAnimationCts = null;
        }

        // 입장 직후 한 프레임을 넘긴 뒤 후보 목록에서 하나를 확률로 고른다.
        private async UniTaskVoid PlayEntranceAnimationAsync(
            EntranceAnimationCandidate candidate,
            CancellationTokenSource cts)
        {
            CancellationToken ct = cts.Token;
            try
            {
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                if (!PlayEntranceAnimationClip(candidate))
                {
                    return;
                }

                await WaitEntranceAnimation(candidate, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // 방 전환/비활성화로 취소된 입장 연출 대기는 정상 종료로 본다.
            }
            finally
            {
                if (ReferenceEquals(entranceAnimationCts, cts))
                {
                    entranceAnimationCts.Dispose();
                    entranceAnimationCts = null;
                }
            }
        }

        // 방 확률이 성공하면 재생 가능한 후보 중 하나를 균등 랜덤으로 고른다.
        private bool TryPickEntranceAnimation(out EntranceAnimationCandidate selectedCandidate)
        {
            selectedCandidate = null;
            if (entranceAnimationCandidates == null ||
                entranceAnimationCandidates.Length == 0 ||
                UnityEngine.Random.value > entranceAnimationProbability)
            {
                return false;
            }

            int playableCount = 0;
            for (int i = 0; i < entranceAnimationCandidates.Length; i++)
            {
                EntranceAnimationCandidate candidate = entranceAnimationCandidates[i];
                if (candidate != null && candidate.CanPlay())
                {
                    playableCount++;
                }
            }

            if (playableCount <= 0)
            {
                return false;
            }

            int selectedIndex = UnityEngine.Random.Range(0, playableCount);
            for (int i = 0; i < entranceAnimationCandidates.Length; i++)
            {
                EntranceAnimationCandidate candidate = entranceAnimationCandidates[i];
                if (candidate == null || !candidate.CanPlay())
                {
                    continue;
                }

                if (selectedIndex == 0)
                {
                    selectedCandidate = candidate;
                    return true;
                }

                selectedIndex--;
            }

            return false;
        }

        // Aseprite 룸 애니메이터를 Once 모드로 재생한다.
        private static bool PlayEntranceAnimationClip(EntranceAnimationCandidate candidate)
        {
            AsepriteRoomAnimator animator = candidate?.Animator;
            if (animator == null ||
                !animator.isActiveAndEnabled ||
                !animator.TryPlay(candidate.AnimationName, true, AsepriteSpritePlaybackMode.Once, out float _))
            {
                return false;
            }

            return true;
        }

        // Aseprite 룸 애니메이터의 Once 재생이 끝날 때까지 기다린다.
        private static async UniTask WaitEntranceAnimation(
            EntranceAnimationCandidate candidate,
            CancellationToken ct)
        {
            AsepriteRoomAnimator animator = candidate?.Animator;
            while (animator != null && animator.isActiveAndEnabled && animator.IsPlaying)
            {
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
        }
    }
}
