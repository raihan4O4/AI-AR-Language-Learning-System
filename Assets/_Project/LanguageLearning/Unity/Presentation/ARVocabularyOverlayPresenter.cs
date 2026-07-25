using TMPro;
using UnityEngine;

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

        public static TMP_FontAsset GetMultilingualFontAsset()
        {
            return MultilingualFontProvider.GetFontAsset();
        }

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
            const string LatinFontResourcePath = "Fonts/TMP/NotoSans-Regular SDF";
            const string DevanagariFontResourcePath = "Fonts/TMP/NotoSansDevanagari-Regular SDF";
            const string MalayalamFontResourcePath = "Fonts/TMP/NotoSansMalayalam-Regular SDF";

            static TMP_FontAsset s_FontAsset;

            public static TMP_FontAsset GetFontAsset()
            {
                if (s_FontAsset != null)
                    return s_FontAsset;

                s_FontAsset = Resources.Load<TMP_FontAsset>(LatinFontResourcePath);
                if (s_FontAsset == null)
                {
                    Debug.LogWarning(
                        "NotoSans-Regular SDF was not found in Resources/Fonts/TMP. " +
                        "Multilingual UI will use the TMP default font.");
                    return null;
                }

                AddFallback(s_FontAsset, DevanagariFontResourcePath);
                AddFallback(s_FontAsset, MalayalamFontResourcePath);
                return s_FontAsset;
            }

            static void AddFallback(TMP_FontAsset fontAsset, string resourcePath)
            {
                var fallback = Resources.Load<TMP_FontAsset>(resourcePath);
                if (fallback == null)
                {
                    Debug.LogWarning(
                        $"{resourcePath} was not found in Resources. Some glyphs may be missing.");
                    return;
                }

                if (fontAsset.fallbackFontAssetTable == null)
                    fontAsset.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset>();

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
