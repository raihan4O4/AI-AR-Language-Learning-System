using System;

namespace AIAR.LanguageLearning.Domain
{
    public sealed class LearningInteraction
    {
        public LearningInteraction(
            string vocabularyId,
            LanguageCode language,
            LearningInteractionType interactionType,
            bool? wasCorrect,
            DateTime occurredAtUtc)
        {
            if (string.IsNullOrWhiteSpace(vocabularyId))
                throw new ArgumentException("Vocabulary id is required.", nameof(vocabularyId));

            if (!Enum.IsDefined(typeof(LanguageCode), language))
                throw new ArgumentOutOfRangeException(nameof(language));

            if (!Enum.IsDefined(typeof(LearningInteractionType), interactionType))
                throw new ArgumentOutOfRangeException(nameof(interactionType));

            if (occurredAtUtc == default || occurredAtUtc == DateTime.MinValue)
                throw new ArgumentException("Interaction time is required.", nameof(occurredAtUtc));

            if (interactionType == LearningInteractionType.QuizAnswered && !wasCorrect.HasValue)
                throw new ArgumentException("Quiz answers require a correctness result.", nameof(wasCorrect));

            if (interactionType != LearningInteractionType.QuizAnswered && wasCorrect.HasValue)
                throw new ArgumentException("Only quiz answers can contain a correctness result.", nameof(wasCorrect));

            VocabularyId = vocabularyId.Trim();
            Language = language;
            InteractionType = interactionType;
            WasCorrect = wasCorrect;
            OccurredAtUtc = occurredAtUtc.Kind == DateTimeKind.Utc
                ? occurredAtUtc
                : occurredAtUtc.ToUniversalTime();
        }

        public string VocabularyId { get; }

        public LanguageCode Language { get; }

        public LearningInteractionType InteractionType { get; }

        public bool? WasCorrect { get; }

        public DateTime OccurredAtUtc { get; }
    }
}
