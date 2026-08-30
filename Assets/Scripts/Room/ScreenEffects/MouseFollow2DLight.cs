using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Escape.Rooms
{
    [DisallowMultipleComponent]
    public sealed class MouseFollow2DLight : MonoBehaviour
    {
        [SerializeField] private Camera targetCamera;
        [SerializeField] private bool followPointer;
        [SerializeField] private float worldZ;
        [SerializeField, Min(0f)] private float followSpeed;

        private void Awake()
        {
            if (targetCamera == null)
            {
                targetCamera = Camera.main;
            }
        }

        private void LateUpdate()
        {
            if (!followPointer ||
                targetCamera == null ||
                !TryGetPointerPosition(out var screenPosition) ||
                !Escape.Input.GameScreenInputArea.Contains(screenPosition))
            {
                return;
            }

            var ray = targetCamera.ScreenPointToRay(screenPosition);
            var plane = new Plane(Vector3.forward, new Vector3(0f, 0f, worldZ));
            if (!plane.Raycast(ray, out var distance))
            {
                return;
            }

            var targetPosition = ray.GetPoint(distance);
            targetPosition.z = worldZ;

            transform.position = followSpeed <= 0f
                ? targetPosition
                : Vector3.Lerp(transform.position, targetPosition, 1f - Mathf.Exp(-followSpeed * Time.deltaTime));
        }

        private static bool TryGetPointerPosition(out Vector2 screenPosition)
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                screenPosition = Mouse.current.position.ReadValue();
                return true;
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            screenPosition = Input.mousePosition;
            return true;
#else
            screenPosition = default;
            return false;
#endif
        }
    }
}
