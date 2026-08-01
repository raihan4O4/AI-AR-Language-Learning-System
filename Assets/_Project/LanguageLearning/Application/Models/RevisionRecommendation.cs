using AIAR.LanguageLearning.Domain;

namespace AIAR.LanguageLearning.Application.Models
{
    public sealed class RevisionRecommendation
    {
        public RevisionRecommendation(
            VocabularyEntry entry,
            WordProgress progress,
            double priorityScore,
            string reason)
        {
            Entry = entry;
            Progress = progress;
            PriorityScore = priorityScore;
            Reason = reason;
        }

        public VocabularyEntry Entry { get; }

        public WordProgress Progress { get; }

        public double PriorityScore { get; }

        public string Reason { get; }
    }
}
