using AIAR.LanguageLearning.Domain;
using TMPro;
using UnityEngine;

namespace AIAR.LanguageLearning.Unity.Presentation
{
    public sealed class ARVocabularyOverlayPresenter : MonoBehaviour
    {
        [SerializeField]
        LearningSessionController m_SessionController;

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
            return MultilingualFontProvider.GetFontAsset(LanguageCode.English);
        }

        void Awake()
        {
            ApplyMultilingualFont();
            Hide();
        }

        void OnEnable()
        {
            SubscribeToSession();
        }

        void OnDisable()
        {
            UnsubscribeFromSession();
        }

        public void ShowVocabulary(
            string translatedWord,
            string sourceWord,
            string phoneticHint,
            string languageName)
        {
            ApplyTranslatedWordFont(languageName);

            if (m_TranslatedWordText != null)
                m_TranslatedWordText.text = translatedWord ?? string.Empty;

            if (m_SourceWordText != null)
                m_SourceWordText.text = sourceWord ?? string.Empty;

            if (m_PhoneticHintText != null)
                m_PhoneticHintText.text = phoneticHint ?? string.Empty;

            if (m_LanguageText != null)
                m_LanguageText.text = languageName ?? string.Empty;

            SetVisible(true);
        }

        public void Hide()
        {
            SetVisible(false);
        }

        void ApplyMultilingualFont()
        {
            var fontAsset = MultilingualFontProvider.GetFontAsset(LanguageCode.English);
            if (fontAsset == null)
                return;

            ApplyFont(m_TranslatedWordText, fontAsset);
            ApplyFont(m_SourceWordText, fontAsset);
            ApplyFont(m_PhoneticHintText, fontAsset);
            ApplyFont(m_LanguageText, fontAsset);
        }

        void ApplyTranslatedWordFont(string languageName)
        {
            var language = LanguageCodeExtensions.TryParse(languageName, out var parsedLanguage)
                ? parsedLanguage
                : LanguageCode.English;
            var fontAsset = MultilingualFontProvider.GetFontAsset(language);
            if (fontAsset != null)
                ApplyFont(m_TranslatedWordText, fontAsset);
        }

        void SubscribeToSession()
        {
            if (m_SessionController == null)
                m_SessionController = FindAnyObjectByType<LearningSessionController>();

            if (m_SessionController != null)
                m_SessionController.VocabularyPresented += ShowVocabulary;
        }

        void UnsubscribeFromSession()
        {
            if (m_SessionController != null)
                m_SessionController.VocabularyPresented -= ShowVocabulary;
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

            static TMP_FontAsset s_LatinFontAsset;
            static TMP_FontAsset s_DevanagariFontAsset;
            static TMP_FontAsset s_MalayalamFontAsset;

            public static TMP_FontAsset GetFontAsset(LanguageCode language)
            {
                EnsureLoaded();
                switch (language)
                {
                    case LanguageCode.Hindi:
                        return s_DevanagariFontAsset ?? s_LatinFontAsset;
                    case LanguageCode.Malayalam:
                        return s_MalayalamFontAsset ?? s_LatinFontAsset;
                    default:
                        return s_LatinFontAsset;
                }
            }

            static void EnsureLoaded()
            {
                if (s_LatinFontAsset != null)
                    return;

                s_LatinFontAsset = LoadFont(LatinFontResourcePath);
                s_DevanagariFontAsset = LoadFont(DevanagariFontResourcePath);
                s_MalayalamFontAsset = LoadFont(MalayalamFontResourcePath);
                if (s_LatinFontAsset == null)
                    return;

                AddFallback(s_LatinFontAsset, s_DevanagariFontAsset);
                AddFallback(s_LatinFontAsset, s_MalayalamFontAsset);
            }

            static TMP_FontAsset LoadFont(string resourcePath)
            {
                var fontAsset = Resources.Load<TMP_FontAsset>(resourcePath);
                if (fontAsset == null)
                {
                    Debug.LogWarning(
                        $"{resourcePath} was not found in Resources. Some glyphs may be missing.");
                }

                return fontAsset;
            }

            static void AddFallback(TMP_FontAsset fontAsset, TMP_FontAsset fallback)
            {
                if (fontAsset == null || fallback == null)
                    return;

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
