using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

namespace Escape.UI
{
    public sealed class ToastPanelUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text toastText;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField, Min(0f)] private float defaultHoldSeconds = 1.4f;

        private CancellationTokenSource showCts;

        private void Awake()
        {
            ResolveReferences();
            SetVisibleImmediate(false);
        }

        private void OnDestroy()
        {
            showCts?.Cancel();
            showCts?.Dispose();
            showCts = null;
        }

        public void Show(string message)
        {
            Show(message, defaultHoldSeconds);
        }

        public void Show(string message, float holdSeconds)
        {
            gameObject.SetActive(true);
            ResolveReferences();

            showCts?.Cancel();
            showCts?.Dispose();
            showCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            ShowAsync(message ?? string.Empty, Mathf.Max(0f, holdSeconds), showCts.Token).Forget();
        }

        private async UniTaskVoid ShowAsync(string message, float holdSeconds, CancellationToken ct)
        {
            gameObject.SetActive(true);

            if (toastText != null)
            {
                toastText.text = message;
            }

            SetVisibleImmediate(true);

            if (holdSeconds > 0f)
            {
                await UniTask.Delay(TimeSpan.FromSeconds(holdSeconds), ignoreTimeScale: true, cancellationToken: ct);
            }

            SetVisibleImmediate(false);
        }

        private void SetVisibleImmediate(bool visible)
        {
            SetAlpha(visible ? 1f : 0f);
            gameObject.SetActive(visible);
        }

        private void SetAlpha(float alpha)
        {
            ResolveReferences();

            if (canvasGroup == null)
            {
                return;
            }

            canvasGroup.alpha = alpha;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        private void ResolveReferences()
        {
            toastText ??= GetComponentInChildren<TMP_Text>(true);
            canvasGroup ??= GetComponent<CanvasGroup>();
            canvasGroup ??= gameObject.AddComponent<CanvasGroup>();
        }
    }
}
