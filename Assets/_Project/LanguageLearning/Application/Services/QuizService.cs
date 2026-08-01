using System;
using System.Collections.Generic;
using System.Linq;
using AIAR.LanguageLearning.Domain;

namespace AIAR.LanguageLearning.Application.Services
{
    public sealed class QuizService
    {
        public IReadOnlyList<VocabularyEntry> CreateQuestionSequence(
            IReadOnlyList<VocabularyEntry> catalog,
            LanguageCode language,
            int questionCount,
            int? seed = null)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));

            if (questionCount < 1)
                throw new ArgumentOutOfRangeException(nameof(questionCount));

            var candidates = catalog
                .Where(entry => entry != null && entry.TryGetTranslation(language, out _))
                .GroupBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            if (candidates.Count < 2)
                throw new InvalidOperationException(
                    $"At least two vocabulary entries with {language} translations are required.");

            var random = seed.HasValue ? new Random(seed.Value) : new Random();
            var sequence = new List<VocabularyEntry>(questionCount);
            while (sequence.Count < questionCount)
            {
                Shuffle(candidates, random);
                if (sequence.Count > 0 &&
                    string.Equals(
                        sequence[sequence.Count - 1].Id,
                        candidates[0].Id,
                        StringComparison.OrdinalIgnoreCase))
                {
                    var swapIndex = candidates.Count > 1 ? 1 : 0;
                    var first = candidates[0];
                    candidates[0] = candidates[swapIndex];
                    candidates[swapIndex] = first;
                }

                var remaining = questionCount - sequence.Count;
                sequence.AddRange(candidates.Take(Math.Min(remaining, candidates.Count)));
            }

            return sequence;
        }

        public IReadOnlyList<VocabularyEntry> CreateAdaptiveQuestionSequence(
            IReadOnlyList<VocabularyEntry> catalog,
            IReadOnlyList<WordProgress> progress,
            LanguageCode language,
            int questionCount,
            DateTime nowUtc,
            int? seed = null)
        {
            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));

            if (questionCount < 1)
                throw new ArgumentOutOfRangeException(nameof(questionCount));

            var candidates = catalog
                .Where(entry => entry != null && entry.TryGetTranslation(language, out _))
                .GroupBy(entry => entry.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            if (candidates.Count < 2)
                throw new InvalidOperationException(
                    $"At least two vocabulary entries with {language} translations are required.");

            var progressById = (progress ?? Array.Empty<WordProgress>())
                .GroupBy(item => item.VocabularyId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            var priorities = new AdaptiveRevisionService()
                .GetRecommendations(candidates, progress, nowUtc, candidates.Count)
                .ToDictionary(item => item.Entry.Id, item => item.PriorityScore, StringComparer.OrdinalIgnoreCase);
            var random = seed.HasValue ? new Random(seed.Value) : new Random();
            var sequence = new List<VocabularyEntry>(questionCount);

            while (sequence.Count < questionCount)
            {
                var previousId = sequence.Count == 0 ? null : sequence[sequence.Count - 1].Id;
                var eligible = candidates
                    .Where(entry => !string.Equals(entry.Id, previousId, StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                var weights = eligible
                    .Select(entry => CalculateAdaptiveWeight(entry, progressById, priorities))
                    .ToArray();
                var totalWeight = weights.Sum();
                var selection = random.NextDouble() * totalWeight;
                var selectedIndex = weights.Length - 1;

                for (var index = 0; index < weights.Length; index++)
                {
                    selection -= weights[index];
                    if (selection > 0d)
                        continue;

                    selectedIndex = index;
                    break;
                }

                sequence.Add(eligible[selectedIndex]);
            }

            return sequence;
        }

        public QuizQuestion CreateTranslationQuestion(
            VocabularyEntry target,
            LanguageCode language,
            IReadOnlyList<VocabularyEntry> catalog,
            int optionCount = 4,
            int? seed = null)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));

            if (catalog == null)
                throw new ArgumentNullException(nameof(catalog));

            if (!target.TryGetTranslation(language, out var correctTranslation))
                throw new InvalidOperationException($"Vocabulary '{target.Id}' does not have a {language} translation.");

            var candidates = catalog
                .Where(entry => entry != null && !string.Equals(entry.Id, target.Id, StringComparison.OrdinalIgnoreCase))
                .Where(entry => entry.TryGetTranslation(language, out _))
                .ToList();

            if (candidates.Count < 1)
                throw new InvalidOperationException("At least one distractor is required to create a quiz question.");

            var random = seed.HasValue ? new Random(seed.Value) : new Random();
            Shuffle(candidates, random);

            var selectedDistractors = candidates
                .Take(Math.Max(1, optionCount - 1))
                .Select(entry => new QuizOption(entry.Id, entry.GetRequiredTranslation(language).Text))
                .ToList();

            var options = selectedDistractors;
            options.Add(new QuizOption(target.Id, correctTranslation.Text));
            Shuffle(options, random);

            var correctIndex = options.FindIndex(option =>
                string.Equals(option.VocabularyId, target.Id, StringComparison.OrdinalIgnoreCase));

            var prompt = $"What is the {language.ToDisplayName()} word for {target.CanonicalObjectLabel}?";
            return new QuizQuestion(target.Id, prompt, language, options, correctIndex);
        }

        static void Shuffle<T>(IList<T> items, Random random)
        {
            for (var index = items.Count - 1; index > 0; index--)
            {
                var swapIndex = random.Next(index + 1);
                var current = items[index];
                items[index] = items[swapIndex];
                items[swapIndex] = current;
            }
        }

        static double CalculateAdaptiveWeight(
            VocabularyEntry entry,
            IReadOnlyDictionary<string, WordProgress> progressById,
            IReadOnlyDictionary<string, double> priorities)
        {
            if (!progressById.TryGetValue(entry.Id, out var wordProgress) || !wordProgress.HasLearningEvidence)
                return 0.65d;

            var priority = priorities.TryGetValue(entry.Id, out var score) ? score : 0d;
            return 1d + (Math.Min(2d, Math.Max(0d, priority)) * 2.5d);
        }
    }
}
