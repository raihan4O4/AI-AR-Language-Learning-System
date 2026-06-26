using System;
using AIAR.LanguageLearning.Application.Models;
using AIAR.LanguageLearning.Domain;
using AIAR.LanguageLearning.Unity.Bootstrap;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace AIAR.LanguageLearning.Unity.Presentation
{
    public sealed class LearningSessionController : MonoBehaviour
    {
        [SerializeField]
        LanguageLearningCompositionRoot m_CompositionRoot;

        [SerializeField]
        LanguageCode m_LearningLanguage = LanguageCode.Hindi;

        [SerializeField]
        float m_MinimumRecognitionConfidence = 0.35f;

        [SerializeField]
        VocabularyDetectedEvent m_OnVocabularyDetected = new VocabularyDetectedEvent();

        [SerializeField]
        VocabularyMissedEvent m_OnVocabularyMissed = new VocabularyMissedEvent();

        VocabularyLookupResult m_CurrentLookup;
        bool m_IsSubscribed;

        public event Action<RecognitionPresentation> RecognitionPresented;

        public VocabularyDetectedEvent onVocabularyDetected => m_OnVocabularyDetected;

        public VocabularyMissedEvent onVocabularyMissed => m_OnVocabularyMissed;

        void Awake()
        {
            ResolveCompositionRoot();
        }

        void OnEnable()
        {
            SubscribeToRecognizer();
        }

        void Start()
        {
            SubscribeToRecognizer();
            m_CompositionRoot?.ObjectRecognitionService?.StartRecognition();
        }

        void OnDisable()
        {
            UnsubscribeFromRecognizer();
        }

        public void SetLearningLanguage(int languageIndex)
        {
            if (!Enum.IsDefined(typeof(LanguageCode), languageIndex))
                return;

            m_LearningLanguage = (LanguageCode)languageIndex;

            if (m_CurrentLookup?.Entry != null)
                PublishVocabulary(m_CurrentLookup.Entry, m_CurrentLookup.RecognizedLabel, m_CurrentLookup.RecognitionConfidence);
        }

        public void SpeakCurrentWord()
        {
            if (m_CurrentLookup == null || !m_CurrentLookup.Found || m_CompositionRoot?.TextToSpeechService == null)
                return;

            m_CompositionRoot.TextToSpeechService.Speak(m_CurrentLookup.Translation);
            m_CompositionRoot.LearningService.RecordPronunciationAttempt(m_CurrentLookup.Entry, m_LearningLanguage);
        }

        void OnObjectRecognized(ObjectRecognitionResult recognitionResult)
        {
            if (recognitionResult == null || recognitionResult.Confidence < m_MinimumRecognitionConfidence)
                return;

            if (!ResolveCompositionRoot())
                return;

            var lookup = m_CompositionRoot.LearningService.FindVocabularyForObject(recognitionResult, m_LearningLanguage);
            if (!lookup.Found)
            {
                RecognitionPresented?.Invoke(new RecognitionPresentation(
                    recognitionResult.Label,
                    recognitionResult.Label,
                    recognitionResult.Confidence,
                    recognitionResult.BoundingBox,
                    false));
                m_OnVocabularyMissed.Invoke(recognitionResult.Label);
                return;
            }

            m_CurrentLookup = lookup;
            m_CompositionRoot.LearningService.RecordObjectViewed(lookup.Entry, m_LearningLanguage);
            PublishVocabulary(lookup.Entry, lookup.RecognizedLabel, lookup.RecognitionConfidence, recognitionResult.BoundingBox);
        }

        bool ResolveCompositionRoot()
        {
            if (m_CompositionRoot == null)
                m_CompositionRoot = LanguageLearningCompositionRoot.Instance ?? FindAnyObjectByType<LanguageLearningCompositionRoot>();

            if (m_CompositionRoot == null)
            {
                Debug.LogWarning("LearningSessionController could not find LanguageLearningCompositionRoot.");
                return false;
            }

            m_CompositionRoot.EnsureComposed();
            return true;
        }

        void SubscribeToRecognizer()
        {
            if (m_IsSubscribed)
                return;

            if (!ResolveCompositionRoot())
                return;

            if (m_CompositionRoot.ObjectRecognitionService == null)
            {
                Debug.LogWarning("No object recognition service configured for language learning.");
                return;
            }

            m_CompositionRoot.ObjectRecognitionService.ObjectRecognized += OnObjectRecognized;
            m_IsSubscribed = true;
        }

        void UnsubscribeFromRecognizer()
        {
            if (!m_IsSubscribed || m_CompositionRoot?.ObjectRecognitionService == null)
                return;

            m_CompositionRoot.ObjectRecognitionService.ObjectRecognized -= OnObjectRecognized;
            m_IsSubscribed = false;
        }

        void PublishVocabulary(
            VocabularyEntry entry,
            string recognizedLabel,
            float recognitionConfidence,
            NormalizedBoundingBox boundingBox = default)
        {
            if (entry == null)
                return;

            if (!entry.TryGetTranslation(m_LearningLanguage, out var translation))
                return;

            var english = entry.TryGetTranslation(LanguageCode.English, out var englishTranslation)
                ? englishTranslation.Text
                : entry.CanonicalObjectLabel;

            m_CurrentLookup = VocabularyLookupResult.FoundEntry(
                entry,
                translation,
                recognizedLabel,
                recognitionConfidence);

            m_OnVocabularyDetected.Invoke(
                translation.Text,
                english,
                translation.PhoneticHint,
                translation.Language.ToDisplayName());

            RecognitionPresented?.Invoke(new RecognitionPresentation(
                recognizedLabel,
                translation.Text,
                recognitionConfidence,
                boundingBox.IsValid ? boundingBox : NormalizedBoundingBox.Centered(0.5f, 0.35f),
                true));
        }

        [Serializable]
        public sealed class VocabularyDetectedEvent : UnityEvent<string, string, string, string>
        {
        }

        [Serializable]
        public sealed class VocabularyMissedEvent : UnityEvent<string>
        {
        }
    }

    public readonly struct RecognitionPresentation
    {
        public RecognitionPresentation(
            string label,
            string displayText,
            float confidence,
            NormalizedBoundingBox boundingBox,
            bool matchedVocabulary)
        {
            Label = label;
            DisplayText = displayText;
            Confidence = confidence;
            BoundingBox = boundingBox;
            MatchedVocabulary = matchedVocabulary;
        }

        public string Label { get; }

        public string DisplayText { get; }

        public float Confidence { get; }

        public NormalizedBoundingBox BoundingBox { get; }

        public bool MatchedVocabulary { get; }
    }

    public sealed class RecognitionBoundingBoxPresenter : MonoBehaviour
    {
        [SerializeField]
        LearningSessionController m_SessionController;

        [SerializeField]
        RectTransform m_BoxRectTransform;

        [SerializeField]
        TMP_Text m_LabelText;

        [SerializeField]
        CanvasGroup m_CanvasGroup;

        [SerializeField]
        float m_MinimumBoxWidth = 80f;

        [SerializeField]
        float m_MinimumBoxHeight = 64f;

        RectTransform m_ParentRectTransform;

        void Awake()
        {
            if (m_SessionController == null)
                m_SessionController = FindAnyObjectByType<LearningSessionController>();

            if (m_BoxRectTransform == null)
                m_BoxRectTransform = transform as RectTransform;

            if (m_CanvasGroup == null)
                m_CanvasGroup = GetComponent<CanvasGroup>();

            m_ParentRectTransform = m_BoxRectTransform != null
                ? m_BoxRectTransform.parent as RectTransform
                : null;

            SetVisible(false);
        }

        void OnEnable()
        {
            if (m_SessionController != null)
                m_SessionController.RecognitionPresented += OnRecognitionPresented;
        }

        void OnDisable()
        {
            if (m_SessionController != null)
                m_SessionController.RecognitionPresented -= OnRecognitionPresented;
        }

        void OnRecognitionPresented(RecognitionPresentation presentation)
        {
            if (!presentation.BoundingBox.IsValid || m_BoxRectTransform == null || m_ParentRectTransform == null)
            {
                SetVisible(false);
                return;
            }

            var parentSize = m_ParentRectTransform.rect.size;
            var width = Mathf.Max(m_MinimumBoxWidth, parentSize.x * presentation.BoundingBox.Width);
            var height = Mathf.Max(m_MinimumBoxHeight, parentSize.y * presentation.BoundingBox.Height);
            var centerX = (presentation.BoundingBox.X + (presentation.BoundingBox.Width * 0.5f) - 0.5f) * parentSize.x;
            var centerY = (0.5f - presentation.BoundingBox.Y - (presentation.BoundingBox.Height * 0.5f)) * parentSize.y;

            m_BoxRectTransform.anchoredPosition = new Vector2(centerX, centerY);
            m_BoxRectTransform.sizeDelta = new Vector2(width, height);

            if (m_LabelText != null)
            {
                var confidencePercent = Mathf.RoundToInt(Mathf.Clamp01(presentation.Confidence) * 100f);
                m_LabelText.text = $"{presentation.DisplayText}  {confidencePercent}%";
            }

            SetVisible(true);
        }

        void SetVisible(bool visible)
        {
            if (m_CanvasGroup == null)
            {
                gameObject.SetActive(visible);
                return;
            }

            m_CanvasGroup.alpha = visible ? 1f : 0f;
            m_CanvasGroup.blocksRaycasts = false;
            m_CanvasGroup.interactable = false;
        }
    }
}
