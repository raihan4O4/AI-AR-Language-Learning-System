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
        {
            TotalWords = totalWords;
            SeenWords = seenWords;
            MasteredWords = masteredWords;
            StrugglingWords = strugglingWords;
            AverageMastery = averageMastery;
            RetentionRate = retentionRate;
        }

        public int TotalWords { get; }

        public int SeenWords { get; }

        public int MasteredWords { get; }

        public int StrugglingWords { get; }

        public double AverageMastery { get; }

        public double RetentionRate { get; }
    }
}
