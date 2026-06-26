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
