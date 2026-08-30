using Escape.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Scripting.APIUpdating;

namespace Escape.UI
{
    [MovedFrom(true, "Escape.Managers", null, "ToastManager")]
    public sealed class ToastPresenter : MonoBehaviour
    {
        private const string ToastCanvasName = "TopUICanvas";

        public static ToastPresenter Instance { get; private set; }

        [SerializeField] private ToastPanelUI toastPanel;

        public static void Show()
        {
            Show("Toast");
        }

        public static void Show(string message)
        {
            Ensure().ShowToast(message);
        }

        public static void Show(string message, float holdSeconds)
        {
            Ensure().ShowToast(message, holdSeconds);
        }

        public static void show(string message)
        {
            Show(message);
        }

        public static void show()
        {
            Show();
        }

        public static void show(string message, float holdSeconds)
        {
            Show(message, holdSeconds);
        }

        public static ToastPresenter Ensure()
        {
            if (Instance != null)
            {
                return Instance;
            }

            ToastPresenter found = FindFirstObjectByType<ToastPresenter>(FindObjectsInactive.Include);
            if (found != null)
            {
                Instance = found;
                return found;
            }

            var presenterObject = new GameObject(nameof(ToastPresenter));
            return presenterObject.AddComponent<ToastPresenter>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public void ShowToast(string message)
        {
            GetToastPanel()?.Show(message);
        }

        public void ShowToast(string message, float holdSeconds)
        {
            GetToastPanel()?.Show(message, holdSeconds);
        }

        private ToastPanelUI GetToastPanel()
        {
            if (toastPanel != null)
            {
                EnsureToastLayer(toastPanel);
                return toastPanel;
            }

            toastPanel = FindFirstObjectByType<ToastPanelUI>(FindObjectsInactive.Include);
            toastPanel ??= FindSceneToastPanel();

            EnsureToastLayer(toastPanel);
            return toastPanel;
        }

        private static ToastPanelUI FindSceneToastPanel()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded)
                {
                    continue;
                }

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    ToastPanelUI panel = FindToastPanelInChildren(root.transform);
                    if (panel != null)
                    {
                        return panel;
                    }
                }
            }

            return null;
        }

        private static ToastPanelUI FindToastPanelInChildren(Transform root)
        {
            if (root.name == "ToastPanelUI")
            {
                return root.GetComponent<ToastPanelUI>() ?? root.gameObject.AddComponent<ToastPanelUI>();
            }

            foreach (Transform child in root)
            {
                ToastPanelUI panel = FindToastPanelInChildren(child);
                if (panel != null)
                {
                    return panel;
                }
            }

            return null;
        }

        private static Transform FindToastCanvasTransform()
        {
            GameObject canvasObject = GameObject.Find(ToastCanvasName);
            if (canvasObject != null)
            {
                return canvasObject.transform;
            }

            return null;
        }

        private static void EnsureToastLayer(ToastPanelUI panel)
        {
            if (panel == null)
            {
                return;
            }

            Transform parent = FindToastCanvasTransform();
            if (parent != null && panel.transform.parent != parent)
            {
                panel.transform.SetParent(parent, false);
            }

            panel.transform.SetAsLastSibling();
        }
    }
}
