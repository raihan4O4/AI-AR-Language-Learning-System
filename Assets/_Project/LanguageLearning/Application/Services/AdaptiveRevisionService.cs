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

            var progressById = (progress ?? Array.Empty<WordProgress>())
                .GroupBy(item => item.VocabularyId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            return catalog
                .Where(entry => entry != null)
                .Select(entry =>
                {
                    var wordProgress = progressById.TryGetValue(entry.Id, out var existingProgress)
                        ? existingProgress
                        : WordProgress.CreateNew(entry.Id);

                    var priority = CalculatePriority(wordProgress, nowUtc, out var reason);
                    return new RevisionRecommendation(entry, wordProgress, priority, reason);
                })
                .OrderByDescending(recommendation => recommendation.PriorityScore)
                .ThenBy(recommendation => recommendation.Progress.TimesSeen)
                .ThenBy(recommendation => recommendation.Entry.CanonicalObjectLabel, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(1, maxItems))
                .ToArray();
        }

        static double CalculatePriority(WordProgress progress, DateTime nowUtc, out string reason)
        {
            if (progress.TimesSeen == 0)
            {
                reason = "New word";
                return 1.25d;
            }

            var masteryRisk = 1d - progress.MasteryScore;
            var daysSinceSeen = progress.LastSeenAtUtc == DateTime.MinValue
                ? 30d
                : Math.Max(0d, (nowUtc - progress.LastSeenAtUtc).TotalDays);
            var staleBoost = Math.Min(0.35d, daysSinceSeen / 30d);

            if (progress.QuizAttempts > 0 && progress.QuizAccuracy < 0.7d)
                reason = "Low quiz accuracy";
            else if (daysSinceSeen >= 3d)
                reason = "Due for revision";
            else
                reason = "Building mastery";

            return masteryRisk + staleBoost;
        }
    }
}
