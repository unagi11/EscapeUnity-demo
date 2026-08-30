using System.Collections.Generic;
using UnityEngine;

namespace Escape.Rooms
{
    // Aseprite에서 추출한 Sprite 프레임을 SpriteRenderer에 직접 루프 재생한다.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class AsepriteSpriteLooper : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer targetRenderer;
        [SerializeField] private Sprite[] frames = new Sprite[0];
        [SerializeField, Min(1)] private int frameDurationMs = 100;
        [SerializeField] private bool playOnEnable = true;

        private int frameIndex;
        private float frameTimer;
        private bool isPlaying;

        private void Reset()
        {
            targetRenderer = GetComponent<SpriteRenderer>();
        }

        private void OnEnable()
        {
            if (targetRenderer == null)
            {
                targetRenderer = GetComponent<SpriteRenderer>();
            }

            frameIndex = 0;
            frameTimer = 0f;
            isPlaying = playOnEnable;
            ApplyFrame();
        }

        private void Update()
        {
            if (!isPlaying || targetRenderer == null || frames == null || frames.Length <= 1)
            {
                return;
            }

            frameTimer += Time.deltaTime;
            var duration = Mathf.Max(0.001f, frameDurationMs / 1000f);
            while (frameTimer >= duration)
            {
                frameTimer -= duration;
                frameIndex = (frameIndex + 1) % frames.Length;
                ApplyFrame();
            }
        }

        // 에디터 빌더가 SpriteRenderer와 프레임 데이터를 한 번에 주입한다.
        public void Configure(
            SpriteRenderer renderer,
            IReadOnlyList<Sprite> nextFrames,
            int nextFrameDurationMs)
        {
            targetRenderer = renderer != null ? renderer : GetComponent<SpriteRenderer>();
            frames = CopySprites(nextFrames);
            frameDurationMs = Mathf.Max(1, nextFrameDurationMs);
            playOnEnable = true;
            frameIndex = 0;
            frameTimer = 0f;
            isPlaying = enabled && gameObject.activeInHierarchy;
            ApplyFrame();
        }

        private void ApplyFrame()
        {
            if (targetRenderer == null || frames == null || frames.Length == 0)
            {
                return;
            }

            frameIndex = Mathf.Clamp(frameIndex, 0, frames.Length - 1);
            if (frames[frameIndex] != null)
            {
                targetRenderer.sprite = frames[frameIndex];
            }
        }

        private static Sprite[] CopySprites(IReadOnlyList<Sprite> source)
        {
            if (source == null || source.Count == 0)
            {
                return new Sprite[0];
            }

            var result = new Sprite[source.Count];
            for (var i = 0; i < source.Count; i++)
            {
                result[i] = source[i];
            }

            return result;
        }
    }
}
