using System.Threading;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Escape.UI
{
    public abstract class PopupUIBase : MonoBehaviour
    {
        private static readonly List<PopupUIBase> visiblePopups = new();
        private CancellationTokenSource popupFadeCts;

        protected abstract GameObject PopupRoot { get; set; }
        protected abstract Button PopupOpenButton { get; set; }
        protected abstract Button PopupCloseButton { get; set; }
        protected abstract bool PopupHideOnAwake { get; }
        protected abstract float PopupFadeDuration { get; }
        protected abstract CanvasGroup PopupCanvasGroup { get; set; }
        protected virtual bool CanCloseTopmost => true;

        public static int LastClosedFrame { get; private set; } = -1;
        public static bool IsAnyOpen
        {
            get
            {
                RemoveClosedPopups();
                return visiblePopups.Count > 0;
            }
        }

        // 열려 있는 모든 팝업을 닫는다(저장 데이터 불러오기 등에서 사용).
        public static void CloseAll()
        {
            RemoveClosedPopups();
            if (visiblePopups.Count == 0)
            {
                return;
            }

            PopupUIBase[] snapshot = visiblePopups.ToArray();
            for (int i = snapshot.Length - 1; i >= 0; i--)
            {
                if (snapshot[i] != null)
                {
                    snapshot[i].Close();
                }
            }

            RemoveClosedPopups();
        }

        // 가장 최근에 열린 팝업을 닫고 닫을 대상이 있었는지 반환한다.
        public static bool CloseTopmost()
        {
            RemoveClosedPopups();
            if (visiblePopups.Count == 0)
            {
                return false;
            }

            PopupUIBase popup = visiblePopups[^1];
            if (!popup.CanCloseTopmost)
            {
                return true;
            }

            popup.Close();
            return true;
        }

        protected bool IsPopupVisible => (PopupRoot != null ? PopupRoot : gameObject).activeSelf;

        protected void InitializePopupChrome()
        {
            ResolvePopupChromeReferences();
            ResetPopupPosition();
            EnsurePopupCanvasGroup();
            BindPopupChromeControls();

            SetPopupVisibleImmediate(!PopupHideOnAwake);
        }

        public void Open()
        {
            OnBeforeOpen();
            SetPopupVisibleImmediate(true);
            OnAfterOpen();
        }

        public void Close()
        {
            LastClosedFrame = Time.frameCount;
            OnBeforeClose();
            SetPopupVisibleImmediate(false);
            OnAfterClose();
        }

        protected virtual void OnBeforeOpen()
        {
        }

        protected virtual void OnAfterOpen()
        {
        }

        protected virtual void OnBeforeClose()
        {
        }

        protected virtual void OnAfterClose()
        {
        }

        protected void SetPopupVisibleImmediate(bool visible)
        {
            EnsurePopupCanvasGroup();

            popupFadeCts?.Cancel();
            popupFadeCts?.Dispose();
            popupFadeCts = null;

            GameObject target = PopupRoot != null ? PopupRoot : gameObject;
            target.SetActive(visible);
            RegisterPopupVisibility(visible);

            if (PopupCanvasGroup != null)
            {
                PopupCanvasGroup.alpha = visible ? 1f : 0f;
                PopupCanvasGroup.blocksRaycasts = visible;
                PopupCanvasGroup.interactable = visible;
            }
        }

        protected void FadePopupVisible(bool visible)
        {
            EnsurePopupCanvasGroup();

            if (PopupFadeDuration <= 0f || PopupCanvasGroup == null)
            {
                SetPopupVisibleImmediate(visible);
                return;
            }

            GameObject target = PopupRoot != null ? PopupRoot : gameObject;
            if (visible)
            {
                target.SetActive(true);
            }

            RegisterPopupVisibility(visible);

            popupFadeCts?.Cancel();
            popupFadeCts?.Dispose();
            popupFadeCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            FadePopupRoutine(visible, popupFadeCts.Token).Forget();
        }

        protected virtual void ResolvePopupChromeReferences()
        {
            PopupRoot ??= gameObject;
            PopupCanvasGroup ??= PopupRoot.GetComponent<CanvasGroup>();
        }

        protected void EnsurePopupCanvasGroup()
        {
            PopupRoot ??= gameObject;
            PopupCanvasGroup ??= PopupRoot.GetComponent<CanvasGroup>();
            PopupCanvasGroup ??= PopupRoot.AddComponent<CanvasGroup>();
        }

        protected void ResetPopupPosition()
        {
            PopupRoot ??= gameObject;
            if (PopupRoot.transform is RectTransform rectTransform)
            {
                rectTransform.anchoredPosition = Vector2.zero;
            }
        }

        // 팝업을 다시 열 때 스크롤 관성을 멈추고 첫 항목부터 보이게 한다.
        protected static void ResetScrollToTop(ScrollRect scrollRect)
        {
            if (scrollRect == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            scrollRect.StopMovement();
            scrollRect.horizontalNormalizedPosition = 0f;
            scrollRect.verticalNormalizedPosition = 1f;
        }

        protected Transform FindPopupChild(string childName)
        {
            foreach (Transform child in GetComponentsInChildren<Transform>(true))
            {
                if (child.name == childName)
                {
                    return child;
                }
            }

            return null;
        }

        protected Transform FindScenePopupChild(string childName)
        {
            var scene = gameObject.scene;
            if (!scene.IsValid())
            {
                return null;
            }

            foreach (GameObject rootObject in scene.GetRootGameObjects())
            {
                foreach (Transform child in rootObject.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name == childName)
                    {
                        return child;
                    }
                }
            }

            return null;
        }

        private void BindPopupChromeControls()
        {
            PopupOpenButton?.onClick.AddListener(Open);
            PopupCloseButton?.onClick.AddListener(Close);
        }

        private async UniTaskVoid FadePopupRoutine(bool visible, CancellationToken ct)
        {
            GameObject target = PopupRoot != null ? PopupRoot : gameObject;
            target.SetActive(true);

            PopupCanvasGroup.interactable = false;
            PopupCanvasGroup.blocksRaycasts = visible;

            float from = PopupCanvasGroup.alpha;
            float to = visible ? 1f : 0f;
            float elapsed = 0f;

            while (elapsed < PopupFadeDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / PopupFadeDuration);
                t = t * t * (3f - 2f * t);
                PopupCanvasGroup.alpha = Mathf.Lerp(from, to, t);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            PopupCanvasGroup.alpha = to;
            PopupCanvasGroup.interactable = visible;
            PopupCanvasGroup.blocksRaycasts = visible;

            if (!visible)
            {
                target.SetActive(false);
            }
        }

        // 열린 팝업 수를 추적해 게임 입력과 추격을 안전하게 일시정지한다.
        private void RegisterPopupVisibility(bool visible)
        {
            visiblePopups.Remove(this);
            if (visible)
            {
                visiblePopups.Add(this);
            }
        }

        // 파괴됐거나 외부에서 비활성화된 팝업을 열린 순서 목록에서 제거한다.
        private static void RemoveClosedPopups()
        {
            visiblePopups.RemoveAll(popup => popup == null || !popup.IsPopupVisible);
        }
    }
}
