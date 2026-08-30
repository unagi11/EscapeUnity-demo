using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Escape.UI
{
    // 공용 확인 팝업의 메시지와 Yes/No 콜백을 관리한다.
    public sealed class YesNoUI : MonoBehaviour
    {
        private const string ObjectName = "YesNoUI";

        private static YesNoUI activeInstance;

        // 확인 팝업이 현재 표시 중인지 여부(ESC 등 다른 입력을 막는 데 사용).
        public static bool IsShowing => activeInstance != null;

        [SerializeField] private TMP_Text messageText;
        [SerializeField] private TMP_Text yesButtonText;
        [SerializeField] private TMP_Text noButtonText;
        [SerializeField] private Button yesButton;
        [SerializeField] private Button noButton;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private Image backgroundImage;
        [SerializeField, Range(0f, 1f)] private float backgroundVisibleAlpha = 0.66f;

        private Action yesAction;
        private Action noAction;

        private void Awake()
        {
            ResolveReferences();
            InitializeHidden();
        }

        public static void Show(string message, Action onYes, Action onNo = null)
        {
            YesNoUI ui = FindOrCreate();
            if (ui == null)
            {
                onYes?.Invoke();
                return;
            }

            ui.ShowInternal(message, onYes, onNo);
        }

        // 씬에서 명시적으로 연결한 확인 팝업 인스턴스를 사용한다.
        public void ShowChoice(string message, Action onYes, Action onNo = null)
        {
            ShowInternal(message, onYes, onNo);
        }

        // 선택지 문구를 버튼에 반영해서 확인 팝업을 표시한다.
        public void ShowChoice(string message, string yesLabel, string noLabel, Action onYes, Action onNo = null)
        {
            ShowInternal(message, onYes, onNo);

            if (yesButtonText != null)
            {
                yesButtonText.text = yesLabel ?? string.Empty;
            }

            if (noButtonText != null)
            {
                noButtonText.text = noLabel ?? string.Empty;
            }

            RefreshLayout();
        }

        private static YesNoUI FindOrCreate()
        {
            YesNoUI found = FindFirstObjectByType<YesNoUI>(FindObjectsInactive.Include);
            if (found != null)
            {
                return found;
            }

            GameObject target = GameObject.Find(ObjectName);
            target ??= FindInactiveSceneObject(ObjectName);
            if (target == null)
            {
                return null;
            }

            return target.GetComponent<YesNoUI>() ?? target.AddComponent<YesNoUI>();
        }

        private static GameObject FindInactiveSceneObject(string objectName)
        {
            foreach (GameObject candidate in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                if (candidate == null ||
                    candidate.name != objectName ||
                    !candidate.scene.IsValid())
                {
                    continue;
                }

                return candidate;
            }

            return null;
        }

        private void ShowInternal(string message, Action onYes, Action onNo)
        {
            ResolveReferences();
            yesAction = onYes;
            noAction = onNo;

            if (messageText != null)
            {
                messageText.text = message ?? string.Empty;
            }

            yesButton?.onClick.RemoveListener(Confirm);
            yesButton?.onClick.AddListener(Confirm);
            noButton?.onClick.RemoveListener(Cancel);
            noButton?.onClick.AddListener(Cancel);

            gameObject.SetActive(true);
            RefreshLayout();
            SetVisible(true);
            transform.SetAsLastSibling();
        }

        private void Confirm()
        {
            Action action = yesAction;
            ClearCallbacks();
            Hide();
            action?.Invoke();
        }

        private void Cancel()
        {
            Action action = noAction;
            ClearCallbacks();
            Hide();
            action?.Invoke();
        }

        private void Hide()
        {
            InitializeHidden();
        }

        private void ClearCallbacks()
        {
            yesAction = null;
            noAction = null;
        }

        // 씬 로드와 닫기 시 오브젝트는 유지하고 입력과 표시만 비활성화한다.
        private void InitializeHidden()
        {
            SetVisible(false);
        }

        private void SetVisible(bool visible)
        {
            ResolveReferences();

            if (visible)
            {
                activeInstance = this;
            }
            else if (activeInstance == this)
            {
                activeInstance = null;
            }

            if (canvasGroup == null)
            {
                return;
            }

            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.interactable = visible;
            canvasGroup.blocksRaycasts = visible;

            if (backgroundImage == null)
            {
                return;
            }

            Color backgroundColor = backgroundImage.color;
            backgroundColor.a = visible ? backgroundVisibleAlpha : 0f;
            backgroundImage.color = backgroundColor;
            backgroundImage.raycastTarget = visible;
        }

        private void RefreshLayout()
        {
            if (messageText == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(messageText.rectTransform);

            if (messageText.transform.parent is RectTransform panel)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
            }

            Canvas.ForceUpdateCanvases();
        }

        private void ResolveReferences()
        {
            canvasGroup ??= GetComponent<CanvasGroup>();
            canvasGroup ??= gameObject.AddComponent<CanvasGroup>();
            backgroundImage ??= GetComponent<Image>();
            messageText ??= FindChild<TMP_Text>("MessageText");
            yesButton ??= FindChild<Button>("YesButton");
            noButton ??= FindChild<Button>("NoButton");
        }

        private T FindChild<T>(string childName) where T : Component
        {
            foreach (Transform child in GetComponentsInChildren<Transform>(true))
            {
                if (child.name == childName)
                {
                    return child.GetComponent<T>();
                }
            }

            return null;
        }
    }
}
