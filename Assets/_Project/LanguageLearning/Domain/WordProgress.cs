using System;

namespace AIAR.LanguageLearning.Domain
{
    public sealed class WordProgress
    {
        public static readonly TimeSpan MinimumSpacedRecallInterval = TimeSpan.FromHours(20d);

        public WordProgress(
            string vocabularyId,
            int timesSeen,
            int pronunciationAttempts,
            int quizCorrect,
            int quizIncorrect,
            DateTime lastSeenAtUtc)
            : this(
                vocabularyId,
                timesSeen,
                pronunciationAttempts,
                quizCorrect,
                quizIncorrect,
                lastSeenAtUtc,
                0,
                quizCorrect > 0 ? 1 : 0,
                quizCorrect + quizIncorrect > 0 ? lastSeenAtUtc : DateTime.MinValue,
                quizCorrect > 0 ? lastSeenAtUtc : DateTime.MinValue,
                timesSeen > 0 ? lastSeenAtUtc : DateTime.MinValue)
        {
        }

        public WordProgress(
            string vocabularyId,
            int timesSeen,
            int pronunciationAttempts,
            int quizCorrect,
            int quizIncorrect,
            DateTime lastSeenAtUtc,
            int correctRecallStreak,
            int spacedCorrectRecalls,
            DateTime lastQuizAtUtc,
            DateTime lastCorrectAtUtc,
            DateTime lastScannedAtUtc)
        {
            if (string.IsNullOrWhiteSpace(vocabularyId))
                throw new ArgumentException("Vocabulary id is required.", nameof(vocabularyId));

            VocabularyId = vocabularyId.Trim();
            TimesSeen = Math.Max(0, timesSeen);
            PronunciationAttempts = Math.Max(0, pronunciationAttempts);
            QuizCorrect = Math.Max(0, quizCorrect);
            QuizIncorrect = Math.Max(0, quizIncorrect);
            LastSeenAtUtc = AsUtc(lastSeenAtUtc);
            CorrectRecallStreak = Math.Max(0, correctRecallStreak);
            SpacedCorrectRecalls = Math.Max(0, spacedCorrectRecalls);
            LastQuizAtUtc = AsUtc(lastQuizAtUtc);
            LastCorrectAtUtc = AsUtc(lastCorrectAtUtc);
            LastScannedAtUtc = AsUtc(lastScannedAtUtc);
        }

        public string VocabularyId { get; }

        public int TimesSeen { get; }

        public int PronunciationAttempts { get; }

        public int QuizCorrect { get; }

        public int QuizIncorrect { get; }

        public DateTime LastSeenAtUtc { get; }

        public int CorrectRecallStreak { get; }

        public int SpacedCorrectRecalls { get; }

        public DateTime LastQuizAtUtc { get; }

        public DateTime LastCorrectAtUtc { get; }

        public DateTime LastScannedAtUtc { get; }

        public int QuizAttempts => QuizCorrect + QuizIncorrect;

        public double QuizAccuracy => QuizAttempts == 0 ? 0d : QuizCorrect / (double)QuizAttempts;

        public double ExposureScore => Math.Min(1d, TimesSeen / 5d);

        public bool HasLearningEvidence => TimesSeen > 0 || QuizAttempts > 0;

        public double MasteryScore => CalculateRetentionScore(DateTime.UtcNow);

        public double CalculateRetentionScore(DateTime nowUtc)
        {
            if (QuizAttempts == 0)
                return 0d;

            var evidence = Math.Min(1d, QuizAttempts / 3d);
            var accuracyEvidence = QuizAccuracy * evidence;
            var repeatedRecallEvidence = Math.Min(1d, SpacedCorrectRecalls / 3d);
            var recency = CalculateRecallRecency(nowUtc);

            return Clamp01(
                (accuracyEvidence * 0.5d) +
                (repeatedRecallEvidence * 0.4d) +
                (recency * evidence * 0.1d));
        }

        public bool NeedsRevision(DateTime nowUtc, double retentionThreshold = 0.6d)
        {
            if (!HasLearningEvidence)
                return false;

            if (QuizAttempts == 0 || LastCorrectAtUtc == DateTime.MinValue)
                return true;

            if (CalculateRetentionScore(nowUtc) < retentionThreshold)
                return true;

            return Math.Max(0d, (AsUtc(nowUtc) - LastCorrectAtUtc).TotalDays) >= 7d;
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
            var correctRecallStreak = CorrectRecallStreak;
            var spacedCorrectRecalls = SpacedCorrectRecalls;
            var lastQuizAtUtc = LastQuizAtUtc;
            var lastCorrectAtUtc = LastCorrectAtUtc;
            var lastScannedAtUtc = LastScannedAtUtc;

            switch (interaction.InteractionType)
            {
                case LearningInteractionType.ObjectViewed:
                    timesSeen++;
                    lastScannedAtUtc = Max(lastScannedAtUtc, interaction.OccurredAtUtc);
                    break;
                case LearningInteractionType.PronunciationPlayed:
                    pronunciationAttempts++;
                    break;
                case LearningInteractionType.QuizAnswered:
                    lastQuizAtUtc = Max(lastQuizAtUtc, interaction.OccurredAtUtc);
                    if (interaction.WasCorrect == true)
                    {
                        quizCorrect++;
                        correctRecallStreak++;
                        if (lastCorrectAtUtc == DateTime.MinValue ||
                            interaction.OccurredAtUtc - lastCorrectAtUtc >= MinimumSpacedRecallInterval)
                        {
                            spacedCorrectRecalls++;
                        }

                        lastCorrectAtUtc = Max(lastCorrectAtUtc, interaction.OccurredAtUtc);
                    }
                    else
                    {
                        quizIncorrect++;
                        correctRecallStreak = 0;
                    }
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
                Max(LastSeenAtUtc, interaction.OccurredAtUtc),
                correctRecallStreak,
                spacedCorrectRecalls,
                lastQuizAtUtc,
                lastCorrectAtUtc,
                lastScannedAtUtc);
        }

        double CalculateRecallRecency(DateTime nowUtc)
        {
            if (LastCorrectAtUtc == DateTime.MinValue)
                return 0d;

            var elapsedDays = Math.Max(0d, (AsUtc(nowUtc) - LastCorrectAtUtc).TotalDays);
            return Clamp01(1d - (elapsedDays / 30d));
        }

        static DateTime AsUtc(DateTime value)
        {
            if (value == default || value == DateTime.MinValue)
                return DateTime.MinValue;

            return value.Kind == DateTimeKind.Utc
                ? value
                : value.ToUniversalTime();
        }

        static DateTime Max(DateTime left, DateTime right)
        {
            left = AsUtc(left);
            right = AsUtc(right);
            return left >= right ? left : right;
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
