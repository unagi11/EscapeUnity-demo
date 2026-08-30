using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Escape.Audio
{
    // 프레임별 포인터 해제 입력을 상호작용 터치 효과음으로 전달한다.
    public sealed class TouchSfxRouter : MonoBehaviour
    {
        private static TouchSfxRouter instance;
        private int pendingTouchFrame = -1;
        private int overrideFrame = -1;
        private TouchSfxPreset overridePreset = TouchSfxPreset.Default;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            Ensure();
        }

        public static void OverrideCurrentTouch(TouchSfxPreset preset)
        {
            TouchSfxRouter router = Ensure();
            router.overrideFrame = Time.frameCount;
            router.overridePreset = preset;
        }

        private static TouchSfxRouter Ensure()
        {
            if (instance != null)
            {
                return instance;
            }

            var routerObject = new GameObject(nameof(TouchSfxRouter));
            return routerObject.AddComponent<TouchSfxRouter>();
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
        }

        private void Update()
        {
            if (WasPrimaryPointerReleased())
            {
                pendingTouchFrame = Time.frameCount;
            }
        }

        private void LateUpdate()
        {
            if (pendingTouchFrame != Time.frameCount)
            {
                return;
            }

            TouchSfxPreset preset = overrideFrame == pendingTouchFrame
                ? overridePreset
                : TouchSfxPreset.Default;
            SoundPlayer.PlayTouchSfx(preset);
            pendingTouchFrame = -1;
            overrideFrame = -1;
            overridePreset = TouchSfxPreset.Default;
        }

        private static bool WasPrimaryPointerReleased()
        {
#if ENABLE_INPUT_SYSTEM
            if (Touchscreen.current != null &&
                Touchscreen.current.primaryTouch.press.wasReleasedThisFrame)
            {
                if (Escape.Input.GameScreenInputArea.Contains(Touchscreen.current.primaryTouch.position.ReadValue()))
                {
                    return true;
                }
            }

            if (Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame)
            {
                if (Escape.Input.GameScreenInputArea.Contains(Mouse.current.position.ReadValue()))
                {
                    return true;
                }
            }
#endif
#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetMouseButtonUp(0))
            {
                return Escape.Input.GameScreenInputArea.Contains(Input.mousePosition);
            }
#endif

            return false;
        }
    }
}
