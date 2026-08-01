using System;
using AIAR.LanguageLearning.Application.Ports;
using AIAR.LanguageLearning.Application.Services;
using AIAR.LanguageLearning.Unity.Data;
using UnityEngine;

namespace AIAR.LanguageLearning.Unity.Bootstrap
{
    [DefaultExecutionOrder(-1000)]
    public sealed class LanguageLearningCompositionRoot : MonoBehaviour
    {
        [SerializeField]
        ResourcesVocabularyRepository m_VocabularyRepository;

        [SerializeField]
        MonoBehaviour m_ObjectRecognitionService;

        [SerializeField]
        MonoBehaviour m_TextToSpeechService;

        [SerializeField]
        string m_PlayerPrefsProgressKey = "aiar.language-learning.progress.v1";

        public static LanguageLearningCompositionRoot Instance { get; private set; }

        public VocabularyLearningService LearningService { get; private set; }

        public QuizService QuizService { get; private set; }

        public AdaptiveRevisionService AdaptiveRevisionService { get; private set; }

        public RetentionAnalysisService RetentionAnalysisService { get; private set; }

        public IVocabularyRepository VocabularyRepository { get; private set; }

        public ILearningProgressRepository ProgressRepository { get; private set; }

        public IObjectRecognitionService ObjectRecognitionService { get; private set; }

        public ITextToSpeechService TextToSpeechService { get; private set; }

        bool m_Composed;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            EnsureComposed();
        }

        public void EnsureComposed()
        {
            if (m_Composed)
                return;

            Compose();
            m_Composed = true;
        }

        void Compose()
        {
            if (m_VocabularyRepository == null)
                m_VocabularyRepository = GetComponentInChildren<ResourcesVocabularyRepository>();

            if (m_VocabularyRepository == null)
                throw new InvalidOperationException("ResourcesVocabularyRepository is required.");

            VocabularyRepository = m_VocabularyRepository;
            ProgressRepository = new PlayerPrefsProgressRepository(m_PlayerPrefsProgressKey);
            ObjectRecognitionService = m_ObjectRecognitionService as IObjectRecognitionService;
            TextToSpeechService = m_TextToSpeechService as ITextToSpeechService;

            if (m_ObjectRecognitionService != null && ObjectRecognitionService == null)
                throw new InvalidOperationException($"{m_ObjectRecognitionService.name} must implement IObjectRecognitionService.");

            if (m_TextToSpeechService != null && TextToSpeechService == null)
                throw new InvalidOperationException($"{m_TextToSpeechService.name} must implement ITextToSpeechService.");

            var labelNormalizer = new ObjectLabelNormalizer();
            var clock = new SystemClock();

            LearningService = new VocabularyLearningService(
                VocabularyRepository,
                ProgressRepository,
                labelNormalizer,
                clock);
            QuizService = new QuizService();
            AdaptiveRevisionService = new AdaptiveRevisionService();
            RetentionAnalysisService = new RetentionAnalysisService();
        }
    }
}
