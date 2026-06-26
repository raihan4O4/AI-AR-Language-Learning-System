using System;
using AIAR.LanguageLearning.Application.Models;
using AIAR.LanguageLearning.Application.Ports;
using AIAR.LanguageLearning.Domain;

namespace AIAR.LanguageLearning.Application.Services
{
    public sealed class VocabularyLearningService
    {
        readonly IVocabularyRepository m_VocabularyRepository;
        readonly ILearningProgressRepository m_ProgressRepository;
        readonly ObjectLabelNormalizer m_LabelNormalizer;
        readonly IClock m_Clock;

        public VocabularyLearningService(
            IVocabularyRepository vocabularyRepository,
            ILearningProgressRepository progressRepository,
            ObjectLabelNormalizer labelNormalizer,
            IClock clock)
        {
            m_VocabularyRepository = vocabularyRepository ?? throw new ArgumentNullException(nameof(vocabularyRepository));
            m_ProgressRepository = progressRepository ?? throw new ArgumentNullException(nameof(progressRepository));
            m_LabelNormalizer = labelNormalizer ?? throw new ArgumentNullException(nameof(labelNormalizer));
            m_Clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public VocabularyLookupResult FindVocabularyForObject(
            ObjectRecognitionResult recognitionResult,
            LanguageCode language)
        {
            if (recognitionResult == null)
                throw new ArgumentNullException(nameof(recognitionResult));

            if (!m_LabelNormalizer.TryFindBestMatch(
                    recognitionResult.Label,
                    m_VocabularyRepository.GetAll(),
                    out var entry))
            {
                return VocabularyLookupResult.NotFound(recognitionResult.Label, recognitionResult.Confidence);
            }

            return entry.TryGetTranslation(language, out var translation)
                ? VocabularyLookupResult.FoundEntry(entry, translation, recognitionResult.Label, recognitionResult.Confidence)
                : VocabularyLookupResult.NotFound(recognitionResult.Label, recognitionResult.Confidence);
        }

        public WordProgress RecordObjectViewed(VocabularyEntry entry, LanguageCode language)
        {
            return Record(entry, language, LearningInteractionType.ObjectViewed, null);
        }

        public WordProgress RecordPronunciationAttempt(VocabularyEntry entry, LanguageCode language)
        {
            return Record(entry, language, LearningInteractionType.PronunciationPlayed, null);
        }

        public WordProgress RecordQuizAnswer(VocabularyEntry entry, LanguageCode language, bool wasCorrect)
        {
            return Record(entry, language, LearningInteractionType.QuizAnswered, wasCorrect);
        }

        WordProgress Record(
            VocabularyEntry entry,
            LanguageCode language,
            LearningInteractionType interactionType,
            bool? wasCorrect)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));

            var existingProgress = m_ProgressRepository.Load(entry.Id) ?? WordProgress.CreateNew(entry.Id);
            var interaction = new LearningInteraction(entry.Id, language, interactionType, wasCorrect, m_Clock.UtcNow);
            var updatedProgress = existingProgress.Record(interaction);
            m_ProgressRepository.Save(updatedProgress);
            return updatedProgress;
        }
    }
}
