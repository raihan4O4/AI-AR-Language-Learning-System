using System.Collections;
using System.Collections.Generic;
using System.Linq;
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

        TMP_Text m_ConfidenceText;
        Button m_PronunciationButton;
        RectTransform m_CardRect;
        Vector2 m_RestingPosition;
        Coroutine m_ShowAnimation;
        bool m_ModernLayoutApplied;
        bool m_ScanDisplayEnabled;

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
            ApplyModernLayout();
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
            ApplyModernLayout();
            var wasHidden = m_CanvasGroup == null || m_CanvasGroup.alpha < 0.01f;
            var language = LanguageCodeExtensions.TryParse(languageName, out var parsedLanguage)
                ? parsedLanguage
                : LanguageCode.English;
            var translatedValue = translatedWord ?? string.Empty;

            UpdateTranslatedWord(translatedValue, language);

            if (m_SourceWordText != null)
                m_SourceWordText.text = string.IsNullOrWhiteSpace(sourceWord) ? "Detected object" : sourceWord;

            if (m_PhoneticHintText != null)
                m_PhoneticHintText.text = string.IsNullOrWhiteSpace(phoneticHint) ? "Tap the speaker to hear it" : phoneticHint;

            if (m_LanguageText != null)
                m_LanguageText.text = $"{languageName ?? "Language"} translation";

            if (!m_ScanDisplayEnabled)
            {
                SetVisible(false);
                return;
            }

            SetVisible(true);

            if (wasHidden && UnityEngine.Application.isPlaying)
            {
                if (m_ShowAnimation != null)
                    StopCoroutine(m_ShowAnimation);
                m_ShowAnimation = StartCoroutine(AnimateIn());
            }
        }

        public void Hide()
        {
            SetVisible(false);
        }

        public void SetScanDisplayEnabled(bool enabled)
        {
            m_ScanDisplayEnabled = enabled;
            if (!enabled)
                Hide();
        }

        public void ApplyModernLayout()
        {
            if (m_ModernLayoutApplied)
                return;

            m_CardRect = transform as RectTransform;
            if (m_CardRect != null)
            {
                m_CardRect.anchorMin = new Vector2(0f, 1f);
                m_CardRect.anchorMax = new Vector2(1f, 1f);
                m_CardRect.pivot = new Vector2(0.5f, 1f);
                m_CardRect.anchoredPosition = new Vector2(0f, -132f);
                m_CardRect.sizeDelta = new Vector2(-64f, 252f);
                m_RestingPosition = m_CardRect.anchoredPosition;
            }

            var image = GetComponent<Image>();
            if (image != null)
                LearningUiTheme.StyleSurface(image, LearningUiTheme.Glass, LearningUiTheme.RadiusLarge, true, new Vector2(0f, -8f));

            var layout = GetComponent<VerticalLayoutGroup>() ?? gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(30, 112, 20, 18);
            layout.spacing = 2f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            ConfigureText(m_LanguageText, 18f, LearningUiTheme.PrimaryDark, FontStyles.Bold, 28f);
            ConfigureText(m_SourceWordText, 28f, LearningUiTheme.Ink, FontStyles.Bold, 40f);
            ConfigureText(m_TranslatedWordText, 49f, LearningUiTheme.Ink, FontStyles.Bold, 66f);
            ConfigureText(m_PhoneticHintText, 20f, LearningUiTheme.Muted, FontStyles.Italic, 34f);

            if (m_ConfidenceText == null)
            {
                var confidenceObject = new GameObject("Confidence", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
                confidenceObject.transform.SetParent(transform, false);
                m_ConfidenceText = confidenceObject.GetComponent<TextMeshProUGUI>();
                if (m_SourceWordText != null)
                    m_ConfidenceText.font = m_SourceWordText.font;
            }
            ConfigureText(m_ConfidenceText, 18f, LearningUiTheme.Muted, FontStyles.Normal, 30f);
            m_ConfidenceText.text = "Looking for a confident match";

            if (m_PronunciationButton == null)
            {
                var buttonObject = new GameObject("Card Speak Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
                buttonObject.transform.SetParent(transform, false);
                var ignoredLayout = buttonObject.GetComponent<LayoutElement>();
                ignoredLayout.ignoreLayout = true;
                var buttonRect = buttonObject.GetComponent<RectTransform>();
                buttonRect.anchorMin = new Vector2(1f, 1f);
                buttonRect.anchorMax = new Vector2(1f, 1f);
                buttonRect.pivot = new Vector2(1f, 1f);
                buttonRect.anchoredPosition = new Vector2(-24f, -24f);
                buttonRect.sizeDelta = new Vector2(72f, 72f);
                m_PronunciationButton = buttonObject.GetComponent<Button>();
                m_PronunciationButton.targetGraphic = buttonObject.GetComponent<Image>();
                m_PronunciationButton.onClick.AddListener(PlayPronunciation);
                LearningUiTheme.StyleButton(m_PronunciationButton, LearningButtonStyle.Soft, LearningUiTheme.RadiusPill);
                var iconObject = new GameObject("Speak Icon", typeof(RectTransform), typeof(LearningUiIcon));
                iconObject.transform.SetParent(buttonObject.transform, false);
                var iconRect = iconObject.GetComponent<RectTransform>();
                iconRect.anchorMin = new Vector2(0.5f, 0.5f);
                iconRect.anchorMax = new Vector2(0.5f, 0.5f);
                iconRect.pivot = new Vector2(0.5f, 0.5f);
                iconRect.sizeDelta = new Vector2(38f, 38f);
                var icon = iconObject.GetComponent<LearningUiIcon>();
                icon.Icon = LearningUiIconType.Speak;
                icon.color = LearningUiTheme.PrimaryDark;
            }

            m_ModernLayoutApplied = true;
        }

        void PlayPronunciation()
        {
            if (m_SessionController?.CurrentVocabularyEntry == null)
                return;

            m_SessionController.SpeakCurrentWord();
        }

        static void ConfigureText(
            TMP_Text text,
            float fontSize,
            Color color,
            FontStyles fontStyle,
            float preferredHeight)
        {
            if (text == null)
                return;

            text.fontSize = fontSize;
            text.color = color;
            text.fontStyle = fontStyle;
            text.alignment = TextAlignmentOptions.Left;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Truncate;
            var layout = text.GetComponent<LayoutElement>() ?? text.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = preferredHeight;
            layout.preferredHeight = preferredHeight;
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

            if (m_SessionController == null)
                return;

            m_SessionController.VocabularyPresented -= ShowVocabulary;
            m_SessionController.VocabularyPresented += ShowVocabulary;
            m_SessionController.RecognitionsPresented -= OnRecognitionsPresented;
            m_SessionController.RecognitionsPresented += OnRecognitionsPresented;
        }

        void UnsubscribeFromSession()
        {
            if (m_SessionController == null)
                return;

            m_SessionController.VocabularyPresented -= ShowVocabulary;
            m_SessionController.RecognitionsPresented -= OnRecognitionsPresented;
        }

        void OnRecognitionsPresented(IReadOnlyList<RecognitionPresentation> presentations)
        {
            if (m_ConfidenceText == null)
                return;

            var confidence = presentations == null
                ? 0f
                : presentations
                    .Where(item => item.MatchedVocabulary)
                    .Select(item => item.Confidence)
                    .DefaultIfEmpty(0f)
                    .Max();
            m_ConfidenceText.text = confidence > 0f
                ? $"{confidence:P0} confidence"
                : "Looking for a confident match";
        }

        IEnumerator AnimateIn()
        {
            if (m_CanvasGroup == null || m_CardRect == null)
                yield break;

            var elapsed = 0f;
            const float duration = 0.26f;
            m_CanvasGroup.alpha = 0f;
            m_CardRect.anchoredPosition = m_RestingPosition + Vector2.up * 24f;
            m_CardRect.localScale = Vector3.one * 0.98f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                var amount = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                m_CanvasGroup.alpha = amount;
                m_CardRect.anchoredPosition = Vector2.Lerp(m_RestingPosition + Vector2.up * 24f, m_RestingPosition, amount);
                m_CardRect.localScale = Vector3.one * Mathf.Lerp(0.98f, 1f, amount);
                yield return null;
            }

            m_CanvasGroup.alpha = 1f;
            m_CardRect.anchoredPosition = m_RestingPosition;
            m_CardRect.localScale = Vector3.one;
            m_ShowAnimation = null;
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
            const string LatinFontResourcePath = "Fonts/TMP/GoogleSans-Regular SDF";
            const string LatinSourceFontResourcePath = "Fonts/GoogleSans-Regular";
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

                s_LatinFontAsset = LoadFont(LatinFontResourcePath, LatinSourceFontResourcePath);
                s_DevanagariFontAsset = LoadFont(DevanagariFontResourcePath);
                s_MalayalamFontAsset = LoadFont(MalayalamFontResourcePath);
                if (s_LatinFontAsset == null)
                    return;

                AddFallback(s_LatinFontAsset, s_DevanagariFontAsset);
                AddFallback(s_LatinFontAsset, s_MalayalamFontAsset);
            }

            static TMP_FontAsset LoadFont(string resourcePath, string sourceFontResourcePath = null)
            {
                var fontAsset = Resources.Load<TMP_FontAsset>(resourcePath);
                if (fontAsset == null && !string.IsNullOrWhiteSpace(sourceFontResourcePath))
                {
                    var sourceFont = Resources.Load<Font>(sourceFontResourcePath);
                    if (sourceFont != null)
                    {
                        fontAsset = TMP_FontAsset.CreateFontAsset(sourceFont);
                        if (fontAsset != null)
                            fontAsset.name = "GoogleSans-Regular SDF";
                    }
                }

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
