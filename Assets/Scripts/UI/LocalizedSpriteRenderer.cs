using UnityEngine;
using Escape.Localization;

namespace Escape.UI
{
    // 현재 언어에 대응하는 스프라이트를 SpriteRenderer에 표시한다.
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class LocalizedSpriteRenderer : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer targetRenderer;
        [SerializeField] private Sprite koreanSprite;
        [SerializeField] private Sprite englishSprite;
        [SerializeField] private Sprite japaneseSprite;

        // 같은 오브젝트의 렌더러 참조를 준비한다.
        private void Awake()
        {
            targetRenderer ??= GetComponent<SpriteRenderer>();
        }

        // 언어 변경을 구독하고 현재 이미지를 즉시 표시한다.
        private void OnEnable()
        {
            LocalizationService.Ensure().LanguageChanged += Refresh;
            Refresh();
        }

        // 비활성화된 오브젝트가 언어 변경 이벤트를 받지 않게 한다.
        private void OnDisable()
        {
            if (LocalizationService.Instance != null)
            {
                LocalizationService.Instance.LanguageChanged -= Refresh;
            }
        }

        // 현재 언어 코드에 맞는 타이틀 스프라이트로 교체한다.
        public void Refresh()
        {
            targetRenderer ??= GetComponent<SpriteRenderer>();
            targetRenderer.sprite = LocalizationService.Ensure().CurrentLanguage switch
            {
                "en" => englishSprite != null ? englishSprite : koreanSprite,
                "ja" => japaneseSprite != null ? japaneseSprite : koreanSprite,
                _ => koreanSprite
            };
        }
    }
}
