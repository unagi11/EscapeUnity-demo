using Escape.Data;
using Escape.Localization;
using Escape.Rooms;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Escape.UI
{
    // TMP 텍스트를 text.tsv의 tid에 연결해 언어 변경 시 자동 갱신하는 컴포넌트.
    [ExecuteAlways]
    public sealed class LocalizedTextUI : MonoBehaviour
    {
        private const string UiTextResourcePath = "Data/text";

        [SerializeField, TsvId("Assets/Resources/Data/text.tsv")] private string tid;
        [SerializeField] private TMP_Text tmpText;
        [SerializeField, TextArea] private string fallbackText;
        [SerializeField] private bool useCurrentTextAsFallback = true;

        public string Tid
        {
            get => tid;
            set
            {
                tid = value;
                Refresh();
            }
        }

        private void Awake()
        {
            ResolveReferences();
            if (Application.isPlaying)
            {
                CaptureFallbackText();
            }
        }

        private void OnEnable()
        {
            if (Application.isPlaying)
            {
                LocalizationService.Ensure().LanguageChanged += Refresh;
            }

            Refresh();
        }

        private void OnDisable()
        {
            if (LocalizationService.Instance != null)
            {
                LocalizationService.Instance.LanguageChanged -= Refresh;
            }
        }

#if UNITY_EDITOR
        // Inspector에서 tid가 바뀐 즉시 text.tsv의 한국어 미리보기를 반영한다.
        private void OnValidate()
        {
            if (!Application.isPlaying)
            {
                Refresh();
            }
        }
#endif

        public void Refresh()
        {
            ResolveReferences();

            string text = Application.isPlaying
                ? LocalizationService.Text(tid, fallbackText)
                : GetKoreanEditorPreview();
            if (tmpText != null)
            {
                tmpText.text = text;
            }

        }

        // 편집 모드에서 사용할 text.tsv의 한국어 문구를 돌려준다.
        private string GetKoreanEditorPreview()
        {
            if (string.IsNullOrWhiteSpace(tid))
            {
                return fallbackText;
            }

            TsvTable<UiText> table = new TsvDataLoader<UiText>().LoadTable(UiTextResourcePath);
            if (table.TryGet(tid, out UiText row) && !string.IsNullOrWhiteSpace(row.text_ko))
            {
                return row.text_ko;
            }

            return fallbackText;
        }

        public void SetTid(string nextTid)
        {
            Tid = nextTid;
        }

        private void ResolveReferences()
        {
            tmpText ??= GetComponent<TMP_Text>();
        }

        private void CaptureFallbackText()
        {
            if (!useCurrentTextAsFallback || !string.IsNullOrEmpty(fallbackText))
            {
                return;
            }

            if (tmpText != null)
            {
                fallbackText = tmpText.text;
            }
        }
    }
}
