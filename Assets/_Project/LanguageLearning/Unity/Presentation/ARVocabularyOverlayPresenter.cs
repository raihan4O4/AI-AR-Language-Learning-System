using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace AIAR.LanguageLearning.Unity.Presentation
{
    public sealed class ARVocabularyOverlayPresenter : MonoBehaviour
    {
        [SerializeField]
        TMP_Text m_TranslatedWordText;

        [SerializeField]
        TMP_Text m_SourceWordText;

        [SerializeField]
        TMP_Text m_PhoneticHintText;

        [SerializeField]
        TMP_Text m_LanguageText;

        [SerializeField]
        CanvasGroup m_CanvasGroup;

        void Awake()
        {
            ApplyMultilingualFont();
            Hide();
        }

        public void ShowVocabulary(
            string translatedWord,
            string sourceWord,
            string phoneticHint,
            string languageName)
        {
            if (m_TranslatedWordText != null)
                m_TranslatedWordText.text = translatedWord;

            if (m_SourceWordText != null)
                m_SourceWordText.text = sourceWord;

            if (m_PhoneticHintText != null)
                m_PhoneticHintText.text = phoneticHint;

            if (m_LanguageText != null)
                m_LanguageText.text = languageName;

            SetVisible(true);
        }

        public void Hide()
        {
            SetVisible(false);
        }

        void ApplyMultilingualFont()
        {
            var fontAsset = MultilingualFontProvider.GetFontAsset();
            if (fontAsset == null)
                return;

            ApplyFont(m_TranslatedWordText, fontAsset);
            ApplyFont(m_SourceWordText, fontAsset);
            ApplyFont(m_PhoneticHintText, fontAsset);
            ApplyFont(m_LanguageText, fontAsset);
        }

        static void ApplyFont(TMP_Text text, TMP_FontAsset fontAsset)
        {
            if (text == null)
                return;

            text.font = fontAsset;
        }

        static class MultilingualFontProvider
        {
            const string LatinFontResourcePath = "Fonts/NotoSans-Regular";
            const string DevanagariFontResourcePath = "Fonts/NotoSansDevanagari-Regular";
            const string MalayalamFontResourcePath = "Fonts/NotoSansMalayalam-Regular";

            static TMP_FontAsset s_FontAsset;

            public static TMP_FontAsset GetFontAsset()
            {
                if (s_FontAsset != null)
                    return s_FontAsset;

                var latinFont = Resources.Load<Font>(LatinFontResourcePath);
                if (latinFont == null)
                {
                    Debug.LogWarning("NotoSans-Regular was not found in Resources/Fonts. Multilingual UI will use TMP default font.");
                    return null;
                }

                s_FontAsset = CreateDynamicFontAsset(latinFont);
                s_FontAsset.name = "Runtime NotoSans Multilingual";
                AddFallback(s_FontAsset, DevanagariFontResourcePath);
                AddFallback(s_FontAsset, MalayalamFontResourcePath);
                return s_FontAsset;
            }

            static TMP_FontAsset CreateDynamicFontAsset(Font font)
            {
                return TMP_FontAsset.CreateFontAsset(
                    font,
                    90,
                    9,
                    GlyphRenderMode.SDFAA,
                    1024,
                    1024,
                    AtlasPopulationMode.Dynamic,
                    true);
            }

            static void AddFallback(TMP_FontAsset fontAsset, string resourcePath)
            {
                var font = Resources.Load<Font>(resourcePath);
                if (font == null)
                {
                    Debug.LogWarning($"{resourcePath} was not found in Resources. Some glyphs may be missing.");
                    return;
                }

                var fallback = CreateDynamicFontAsset(font);
                fallback.name = $"Runtime {font.name}";
                if (!fontAsset.fallbackFontAssetTable.Contains(fallback))
                    fontAsset.fallbackFontAssetTable.Add(fallback);
            }
        }

        void SetVisible(bool visible)
        {
            if (m_CanvasGroup == null)
            {
                gameObject.SetActive(visible);
                return;
            }

            m_CanvasGroup.alpha = visible ? 1f : 0f;
            m_CanvasGroup.blocksRaycasts = visible;
            m_CanvasGroup.interactable = visible;
        }
    }
}
