using System;
using System.Collections.Generic;
using System.Linq;
using AIAR.LanguageLearning.Application.Models;
using AIAR.LanguageLearning.Domain;

namespace AIAR.LanguageLearning.Application.Services
{
    public sealed class RetentionAnalysisService
    {
        public RetentionSummary CreateSummary(
            IReadOnlyList<VocabularyEntry> catalog,
            IReadOnlyList<WordProgress> progress,
            double masteredThreshold = 0.75d,
            double strugglingThreshold = 0.45d)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));

            var progressById = (progress ?? Array.Empty<WordProgress>())
                .GroupBy(item => item.VocabularyId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            var trackedProgress = catalog
                .Where(entry => entry != null)
                .Select(entry => progressById.TryGetValue(entry.Id, out var wordProgress)
                    ? wordProgress
                    : WordProgress.CreateNew(entry.Id))
                .ToArray();

            if (trackedProgress.Length == 0)
                return new RetentionSummary(0, 0, 0, 0, 0d, 0d);

            var seenWords = trackedProgress.Count(item => item.TimesSeen > 0);
            var masteredWords = trackedProgress.Count(item => item.MasteryScore >= masteredThreshold);
            var strugglingWords = trackedProgress.Count(item => item.TimesSeen > 0 && item.MasteryScore < strugglingThreshold);
            var averageMastery = trackedProgress.Average(item => item.MasteryScore);
            var retentionRate = masteredWords / (double)trackedProgress.Length;

            return new RetentionSummary(
                trackedProgress.Length,
                seenWords,
                masteredWords,
                strugglingWords,
                averageMastery,
                retentionRate);
        }
    }
}
