using UnityEngine;

namespace Escape.MiniGames
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class PickLineColliderSync : MonoBehaviour
    {
        [SerializeField] private LineRenderer sourceLine;
        [SerializeField] private EdgeCollider2D targetCollider;
        [SerializeField] private bool syncInPlayMode = true;

        private void Reset()
        {
            ResolveReferences();
            SyncCollider();
        }

        private void OnValidate()
        {
            ResolveReferences();
            SyncCollider();
        }

        private void Awake()
        {
            ResolveReferences();
            SyncCollider();
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying || syncInPlayMode)
            {
                SyncCollider();
            }
        }

        public void Configure(LineRenderer lineRenderer, EdgeCollider2D edgeCollider)
        {
            sourceLine = lineRenderer;
            targetCollider = edgeCollider;
            SyncCollider();
        }

        public bool SyncCollider()
        {
            if (sourceLine == null || targetCollider == null || sourceLine.positionCount == 0)
            {
                return false;
            }

            int count = sourceLine.positionCount;
            var nextPoints = new Vector2[count];
            Transform lineTransform = sourceLine.transform;
            Transform colliderTransform = targetCollider.transform;

            for (int i = 0; i < count; i++)
            {
                Vector3 sourcePosition = sourceLine.GetPosition(i);
                Vector3 worldPosition = sourceLine.useWorldSpace
                    ? sourcePosition
                    : lineTransform.TransformPoint(sourcePosition);
                Vector3 colliderPosition = colliderTransform.InverseTransformPoint(worldPosition);
                nextPoints[i] = new Vector2(colliderPosition.x, colliderPosition.y);
            }

            Vector2[] currentPoints = targetCollider.points;
            if (PointsMatch(currentPoints, nextPoints))
            {
                return true;
            }

            targetCollider.points = nextPoints;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.EditorUtility.SetDirty(targetCollider);
                UnityEditor.EditorUtility.SetDirty(this);
            }
#endif
            return true;
        }

        private void ResolveReferences()
        {
            if (sourceLine == null)
            {
                sourceLine = GetComponent<LineRenderer>();
            }

            if (targetCollider == null)
            {
                targetCollider = GetComponentInChildren<EdgeCollider2D>(true);
            }
        }

        private static bool PointsMatch(Vector2[] currentPoints, Vector2[] nextPoints)
        {
            if (currentPoints == null || currentPoints.Length != nextPoints.Length)
            {
                return false;
            }

            for (int i = 0; i < currentPoints.Length; i++)
            {
                if ((currentPoints[i] - nextPoints[i]).sqrMagnitude > 0.000001f)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
