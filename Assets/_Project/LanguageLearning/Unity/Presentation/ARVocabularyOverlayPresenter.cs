using AIAR.LanguageLearning.Domain;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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

        string m_LastTranslatedWord;
        LanguageCode? m_LastTranslatedLanguage;
        bool m_TranslatedWordLayoutConfigured;

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
            var language = LanguageCodeExtensions.TryParse(languageName, out var parsedLanguage)
                ? parsedLanguage
                : LanguageCode.English;
            var translatedValue = translatedWord ?? string.Empty;

            SetVisible(true);
            UpdateTranslatedWord(translatedValue, language);

            if (m_SourceWordText != null)
                m_SourceWordText.text = sourceWord ?? string.Empty;

            if (m_PhoneticHintText != null)
                m_PhoneticHintText.text = phoneticHint ?? string.Empty;

            if (m_LanguageText != null)
                m_LanguageText.text = languageName ?? string.Empty;
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

        void UpdateTranslatedWord(string translatedWord, LanguageCode language)
        {
            if (m_TranslatedWordText == null)
                return;

            ConfigureTranslatedWordLayout();

            var fontAsset = MultilingualFontProvider.GetFontAsset(language);
            if (m_LastTranslatedLanguage == language &&
                string.Equals(m_LastTranslatedWord, translatedWord, System.StringComparison.Ordinal) &&
                m_TranslatedWordText.font == fontAsset &&
                string.Equals(m_TranslatedWordText.text, translatedWord, System.StringComparison.Ordinal))
            {
                return;
            }

            if (fontAsset != null && !string.IsNullOrEmpty(translatedWord) && !fontAsset.HasCharacters(translatedWord))
            {
                fontAsset.TryAddCharacters(translatedWord, out var missingCharacters);
                if (!string.IsNullOrEmpty(missingCharacters))
                {
                    Debug.LogWarning(
                        $"Font '{fontAsset.name}' is missing glyphs for '{missingCharacters}' " +
                        $"while displaying {language}.");
                }
            }

            ApplyFont(m_TranslatedWordText, fontAsset);
            m_TranslatedWordText.enabled = true;
            m_TranslatedWordText.SetText(translatedWord);
            if (m_TranslatedWordText.rectTransform.parent is RectTransform layoutRoot)
                LayoutRebuilder.ForceRebuildLayoutImmediate(layoutRoot);
            m_TranslatedWordText.SetAllDirty();
            m_TranslatedWordText.ForceMeshUpdate(true, true);

            m_LastTranslatedWord = translatedWord;
            m_LastTranslatedLanguage = language;
        }

        void ConfigureTranslatedWordLayout()
        {
            if (m_TranslatedWordLayoutConfigured || m_TranslatedWordText == null)
                return;

            m_TranslatedWordText.textWrappingMode = TextWrappingModes.NoWrap;
            m_TranslatedWordText.overflowMode = TextOverflowModes.Overflow;
            m_TranslatedWordText.enableAutoSizing = true;
            m_TranslatedWordText.fontSizeMin = 30f;
            m_TranslatedWordText.fontSizeMax = Mathf.Max(68f, m_TranslatedWordText.fontSize);

            var layoutElement = m_TranslatedWordText.GetComponent<LayoutElement>();
            if (layoutElement != null)
            {
                layoutElement.minHeight = Mathf.Max(84f, layoutElement.minHeight);
                layoutElement.preferredHeight = Mathf.Max(96f, layoutElement.preferredHeight);
            }

            m_TranslatedWordLayoutConfigured = true;
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
            if (text == null || fontAsset == null)
                return;

            text.font = fontAsset;
            if (fontAsset.material != null && text.fontSharedMaterial != fontAsset.material)
                text.fontSharedMaterial = fontAsset.material;
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
