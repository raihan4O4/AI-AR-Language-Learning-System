using System;

namespace AIAR.LanguageLearning.Domain
{
    public sealed class WordProgress
    {
        public WordProgress(
            string vocabularyId,
            int timesSeen,
            int pronunciationAttempts,
            int quizCorrect,
            int quizIncorrect,
            DateTime lastSeenAtUtc)
        {
            if (string.IsNullOrWhiteSpace(vocabularyId))
                throw new ArgumentException("Vocabulary id is required.", nameof(vocabularyId));

            VocabularyId = vocabularyId.Trim();
            TimesSeen = Math.Max(0, timesSeen);
            PronunciationAttempts = Math.Max(0, pronunciationAttempts);
            QuizCorrect = Math.Max(0, quizCorrect);
            QuizIncorrect = Math.Max(0, quizIncorrect);
            LastSeenAtUtc = lastSeenAtUtc == default
                ? DateTime.MinValue
                : DateTime.SpecifyKind(lastSeenAtUtc, DateTimeKind.Utc);
        }

        public string VocabularyId { get; }

        public int TimesSeen { get; }

        public int PronunciationAttempts { get; }

        public int QuizCorrect { get; }

        public int QuizIncorrect { get; }

        public DateTime LastSeenAtUtc { get; }

        public int QuizAttempts => QuizCorrect + QuizIncorrect;

        public double QuizAccuracy => QuizAttempts == 0 ? 0d : QuizCorrect / (double)QuizAttempts;

        public double ExposureScore => Math.Min(1d, TimesSeen / 5d);

        public double MasteryScore
        {
            get
            {
                var quizScore = QuizAttempts == 0 ? 0d : QuizAccuracy;
                var pronunciationScore = Math.Min(1d, PronunciationAttempts / 2d);
                return Clamp01((ExposureScore * 0.35d) + (quizScore * 0.5d) + (pronunciationScore * 0.15d));
            }
        }

        public static WordProgress CreateNew(string vocabularyId)
        {
            return new WordProgress(vocabularyId, 0, 0, 0, 0, DateTime.MinValue);
        }

        public WordProgress Record(LearningInteraction interaction)
        {
            if (interaction == null)
                throw new ArgumentNullException(nameof(interaction));

            if (!string.Equals(VocabularyId, interaction.VocabularyId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Cannot apply progress interaction for a different vocabulary item.");

            var timesSeen = TimesSeen;
            var pronunciationAttempts = PronunciationAttempts;
            var quizCorrect = QuizCorrect;
            var quizIncorrect = QuizIncorrect;

            switch (interaction.InteractionType)
            {
                case LearningInteractionType.ObjectViewed:
                    timesSeen++;
                    break;
                case LearningInteractionType.PronunciationPlayed:
                    pronunciationAttempts++;
                    break;
                case LearningInteractionType.QuizAnswered:
                    if (interaction.WasCorrect == true)
                        quizCorrect++;
                    else
                        quizIncorrect++;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(interaction.InteractionType));
            }

            return new WordProgress(
                VocabularyId,
                timesSeen,
                pronunciationAttempts,
                quizCorrect,
                quizIncorrect,
                interaction.OccurredAtUtc);
        }

        static double Clamp01(double value)
        {
            if (value < 0d)
                return 0d;

            if (value > 1d)
                return 1d;

            return value;
        }
    }
}
