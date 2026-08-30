using System;
using System.Collections.Generic;
using UnityEngine;

namespace Escape.Rooms
{
    // RoomInteractor 후보 수집과 화면 좌표 기반 픽셀 알파 판정을 전담한다.
    internal sealed class RoomHitTester
    {
        private readonly RoomRegistry rooms;
        private readonly GameObject sceneOwner;
        private readonly Func<Camera> getCamera;
        private readonly int alphaHitThreshold;
        private readonly Action<string> log;
        private readonly List<HitCandidate> candidates = new();
        private bool warnedUnreadableTexture;

        private readonly struct HitCandidate
        {
            public readonly RoomInteractor Interactable;
            public readonly SpriteRenderer Renderer;

            // 상호작용 오브젝트와 알파 판정용 렌더러를 묶는다.
            public HitCandidate(RoomInteractor interactable, SpriteRenderer renderer)
            {
                Interactable = interactable;
                Renderer = renderer;
            }
        }

        public int CandidateCount => candidates.Count;

        // Room 목록과 현재 카메라 조회 수단을 연결한다.
        public RoomHitTester(
            RoomRegistry rooms,
            GameObject sceneOwner,
            Func<Camera> getCamera,
            int alphaHitThreshold,
            Action<string> log)
        {
            this.rooms = rooms;
            this.sceneOwner = sceneOwner;
            this.getCamera = getCamera;
            this.alphaHitThreshold = alphaHitThreshold;
            this.log = log;
        }

        // 현재 Room 계층에서 상호작용 가능한 스프라이트 후보를 다시 수집한다.
        public void Refresh()
        {
            candidates.Clear();
            var seen = new HashSet<RoomInteractor>();

            if (rooms != null)
            {
                foreach (Transform roomRoot in rooms.EnumerateRoots())
                {
                    AddCandidates(roomRoot, seen);
                }
            }

            if (seen.Count == 0 && sceneOwner != null)
            {
                var scene = sceneOwner.scene;
                if (scene.IsValid())
                {
                    foreach (GameObject rootObject in scene.GetRootGameObjects())
                    {
                        AddCandidates(rootObject.transform, seen);
                    }
                }
            }

            candidates.Sort(CompareCandidatesForTopFirst);
            log?.Invoke($"Refresh hit candidates. count={candidates.Count}, rooms={rooms?.Count ?? 0}");
        }

        // 화면 좌표를 방 평면의 월드 좌표로 변환한다.
        public bool TryProjectScreenToRoom(Vector2 screenPosition, out Vector2 worldPoint)
        {
            Camera camera = getCamera?.Invoke();
            if (camera == null)
            {
                worldPoint = default;
                return false;
            }

            Ray ray = camera.ScreenPointToRay(screenPosition);
            var plane = new Plane(Vector3.forward, Vector3.zero);
            if (!plane.Raycast(ray, out float distance))
            {
                worldPoint = default;
                return false;
            }

            worldPoint = ray.GetPoint(distance);
            return true;
        }

        // 정렬된 후보 중 지정 월드 좌표에서 불투명한 최상단 상호작용을 찾는다.
        public bool TryFindAlphaHit(Vector2 worldPoint, out RoomInteractor interactable)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                HitCandidate candidate = candidates[i];
                if (!candidate.Interactable.enabled ||
                    !candidate.Interactable.gameObject.activeInHierarchy ||
                    !candidate.Renderer.enabled ||
                    candidate.Renderer.sprite == null)
                {
                    continue;
                }

                if (IsOpaquePixel(candidate.Renderer, worldPoint))
                {
                    interactable = candidate.Interactable;
                    return true;
                }
            }

            interactable = null;
            return false;
        }

        // 지정 계층에서 SpriteRenderer가 있는 상호작용 후보를 중복 없이 추가한다.
        private void AddCandidates(Transform root, ISet<RoomInteractor> seen)
        {
            if (root == null)
            {
                return;
            }

            foreach (RoomInteractor interactable in root.GetComponentsInChildren<RoomInteractor>(true))
            {
                if (!seen.Add(interactable))
                {
                    continue;
                }

                var renderer = interactable.GetComponent<SpriteRenderer>();
                if (renderer == null || renderer.sprite == null)
                {
                    continue;
                }

                candidates.Add(new HitCandidate(interactable, renderer));
            }
        }

        // Sorting Layer, Order, Z 순서로 화면 최상단 후보가 먼저 오게 정렬한다.
        private static int CompareCandidatesForTopFirst(HitCandidate a, HitCandidate b)
        {
            int layerCompare = SortingLayer.GetLayerValueFromID(b.Renderer.sortingLayerID)
                .CompareTo(SortingLayer.GetLayerValueFromID(a.Renderer.sortingLayerID));
            if (layerCompare != 0)
            {
                return layerCompare;
            }

            int orderCompare = b.Renderer.sortingOrder.CompareTo(a.Renderer.sortingOrder);
            if (orderCompare != 0)
            {
                return orderCompare;
            }

            return a.Renderer.transform.position.z.CompareTo(b.Renderer.transform.position.z);
        }

        // Sprite 내부 좌표가 실제 불투명 픽셀인지 확인한다.
        private bool IsOpaquePixel(SpriteRenderer spriteRenderer, Vector2 worldPoint)
        {
            Sprite sprite = spriteRenderer.sprite;
            Vector3 localPoint = spriteRenderer.transform.InverseTransformPoint(worldPoint);
            float pixelX = localPoint.x * sprite.pixelsPerUnit + sprite.pivot.x;
            float pixelY = localPoint.y * sprite.pixelsPerUnit + sprite.pivot.y;
            Rect rect = sprite.rect;

            if (spriteRenderer.flipX)
            {
                pixelX = rect.width - pixelX;
            }

            if (spriteRenderer.flipY)
            {
                pixelY = rect.height - pixelY;
            }

            if (pixelX < 0f || pixelY < 0f || pixelX >= rect.width || pixelY >= rect.height)
            {
                return false;
            }

            int textureX = Mathf.FloorToInt(rect.x + pixelX);
            int textureY = Mathf.FloorToInt(rect.y + pixelY);
            try
            {
                return sprite.texture.GetPixel(textureX, textureY).a * 255f > alphaHitThreshold;
            }
            catch (UnityException)
            {
                if (!warnedUnreadableTexture)
                {
                    Debug.LogWarning(
                        "Sprite texture is not readable. Enable Read/Write on the room atlas texture.");
                    warnedUnreadableTexture = true;
                }

                return false;
            }
        }
    }
}
