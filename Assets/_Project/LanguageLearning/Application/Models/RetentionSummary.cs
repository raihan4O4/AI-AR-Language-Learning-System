namespace AIAR.LanguageLearning.Application.Models
{
    public sealed class RetentionSummary
    {
        public RetentionSummary(
            int totalWords,
            int seenWords,
            int masteredWords,
            int strugglingWords,
            double averageMastery,
            double retentionRate)
            : this(
                totalWords,
                seenWords,
                masteredWords,
                strugglingWords,
                averageMastery,
                retentionRate,
                0,
                0,
                0d)
        {
        }

        public RetentionSummary(
            int totalWords,
            int seenWords,
            int masteredWords,
            int strugglingWords,
            double averageMastery,
            double retentionRate,
            int quizAttempts,
            int quizCorrect,
            double quizAccuracy)
        {
            TotalWords = totalWords;
            SeenWords = seenWords;
            MasteredWords = masteredWords;
            StrugglingWords = strugglingWords;
            AverageMastery = averageMastery;
            RetentionRate = retentionRate;
            QuizAttempts = quizAttempts;
            QuizCorrect = quizCorrect;
            QuizAccuracy = quizAccuracy;
        }

        public int TotalWords { get; }

        public int SeenWords { get; }

        public int MasteredWords { get; }

        public int StrugglingWords { get; }

        public double AverageMastery { get; }

        public double RetentionRate { get; }

        public double VocabularyMasteryRate => RetentionRate;

        public double AverageRetentionScore => AverageMastery;

        public int WordsNeedingRevision => StrugglingWords;

        public int QuizAttempts { get; }

        public int QuizCorrect { get; }

        public double QuizAccuracy { get; }
    }
}
