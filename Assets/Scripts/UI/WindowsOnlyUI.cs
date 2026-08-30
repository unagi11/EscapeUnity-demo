using UnityEngine;

namespace Escape.UI
{
    // 키보드 안내처럼 Windows에서만 필요한 UI를 다른 플랫폼에서 숨긴다.
    public sealed class WindowsOnlyUI : MonoBehaviour
    {
        // Windows Player와 Windows Editor 외의 환경에서는 이 UI를 비활성화한다.
        private void Awake()
        {
#if !UNITY_STANDALONE_WIN && !UNITY_EDITOR_WIN
            gameObject.SetActive(false);
#endif
        }
    }
}
