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
            double strugglingThreshold = 0.6d,
            DateTime? nowUtc = null)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));

            var progressById = (progress ?? Array.Empty<WordProgress>())
                .GroupBy(item => item.VocabularyId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

            var trackedProgress = catalog
                .Where(entry => entry != null)
                .GroupBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Select(entry => progressById.TryGetValue(entry.Id, out var wordProgress)
                    ? wordProgress
                    : WordProgress.CreateNew(entry.Id))
                .ToArray();

            if (trackedProgress.Length == 0)
                return new RetentionSummary(0, 0, 0, 0, 0d, 0d, 0, 0, 0d);

            var analysisTime = (nowUtc ?? DateTime.UtcNow).ToUniversalTime();
            var learnedProgress = trackedProgress.Where(item => item.HasLearningEvidence).ToArray();
            var seenWords = learnedProgress.Length;
            var masteredWords = learnedProgress.Count(item =>
                item.CalculateRetentionScore(analysisTime) >= masteredThreshold);
            var strugglingWords = learnedProgress.Count(item =>
                item.NeedsRevision(analysisTime, strugglingThreshold));
            var averageMastery = learnedProgress.Length == 0
                ? 0d
                : learnedProgress.Average(item => item.CalculateRetentionScore(analysisTime));
            var retentionRate = seenWords == 0 ? 0d : masteredWords / (double)seenWords;
            var quizAttempts = learnedProgress.Sum(item => item.QuizAttempts);
            var quizCorrect = learnedProgress.Sum(item => item.QuizCorrect);
            var quizAccuracy = quizAttempts == 0 ? 0d : quizCorrect / (double)quizAttempts;

            return new RetentionSummary(
                trackedProgress.Length,
                seenWords,
                masteredWords,
                strugglingWords,
                averageMastery,
                retentionRate,
                quizAttempts,
                quizCorrect,
                quizAccuracy);
        }
    }
}
