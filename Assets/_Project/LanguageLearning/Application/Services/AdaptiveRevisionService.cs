using System;
using System.Collections.Generic;
using System.Linq;
using AIAR.LanguageLearning.Application.Models;
using AIAR.LanguageLearning.Domain;

namespace AIAR.LanguageLearning.Application.Services
{
    public sealed class AdaptiveRevisionService
    {
        public IReadOnlyList<RevisionRecommendation> GetRecommendations(
            IReadOnlyList<VocabularyEntry> catalog,
            IReadOnlyList<WordProgress> progress,
            DateTime nowUtc,
            int maxItems = 8)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));

            return Rank(catalog, progress, nowUtc, false)
                .Take(Math.Max(1, maxItems))
                .ToArray();
        }

        public IReadOnlyList<RevisionRecommendation> GetRevisionQueue(
            IReadOnlyList<VocabularyEntry> catalog,
            IReadOnlyList<WordProgress> progress,
            DateTime nowUtc,
            int maxItems = 8)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));

            return Rank(catalog, progress, nowUtc, true)
                .Take(Math.Max(1, maxItems))
                .ToArray();
        }

        static IEnumerable<RevisionRecommendation> Rank(
            IReadOnlyList<VocabularyEntry> catalog,
            IReadOnlyList<WordProgress> progress,
            DateTime nowUtc,
            bool revisionOnly)
        {
            nowUtc = nowUtc.Kind == DateTimeKind.Utc ? nowUtc : nowUtc.ToUniversalTime();

            var progressById = (progress ?? Array.Empty<WordProgress>())
                .GroupBy(item => item.VocabularyId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            return catalog
                .Where(entry => entry != null)
                .GroupBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Select(entry =>
                {
                    var wordProgress = progressById.TryGetValue(entry.Id, out var existingProgress)
                        ? existingProgress
                        : WordProgress.CreateNew(entry.Id);

                    var priority = CalculatePriority(wordProgress, nowUtc, out var reason);
                    return new RevisionRecommendation(entry, wordProgress, priority, reason);
                })
                .Where(recommendation => !revisionOnly ||
                    recommendation.Progress.NeedsRevision(nowUtc))
                .OrderByDescending(recommendation => recommendation.PriorityScore)
                .ThenBy(recommendation => recommendation.Progress.TimesSeen)
                .ThenBy(recommendation => recommendation.Entry.CanonicalObjectLabel, StringComparer.OrdinalIgnoreCase);
        }

        static double CalculatePriority(WordProgress progress, DateTime nowUtc, out string reason)
        {
            if (!progress.HasLearningEvidence)
            {
                reason = "New word";
                return 1.25d;
            }

            var retentionRisk = 1d - progress.CalculateRetentionScore(nowUtc);
            var wrongAnswerRate = progress.QuizAttempts == 0
                ? 0d
                : progress.QuizIncorrect / (double)progress.QuizAttempts;
            var lastRecallAt = progress.LastCorrectAtUtc != DateTime.MinValue
                ? progress.LastCorrectAtUtc
                : progress.LastSeenAtUtc;
            var daysSinceRecall = lastRecallAt == DateTime.MinValue
                ? 30d
                : Math.Max(0d, (nowUtc - lastRecallAt).TotalDays);
            var overdueBoost = Math.Min(0.35d, daysSinceRecall / 30d);
            var unassessedBoost = progress.QuizAttempts == 0 ? 0.35d : 0d;

            if (progress.QuizAttempts > 0 && progress.QuizAccuracy < 0.7d)
                reason = $"Missed {progress.QuizIncorrect} of {progress.QuizAttempts} quiz answers";
            else if (progress.QuizAttempts == 0)
                reason = "Not tested in a quiz yet";
            else if (daysSinceRecall >= 7d)
                reason = "Recall is due for a refresh";
            else
                reason = "Keep strengthening recall";

            return retentionRisk + (wrongAnswerRate * 0.35d) + overdueBoost + unassessedBoost;
        }
    }
}
