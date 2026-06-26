using System;
using System.Collections.Generic;
using System.Linq;

namespace AIAR.LanguageLearning.Domain
{
    public sealed class QuizQuestion
    {
        public QuizQuestion(
            string targetVocabularyId,
            string prompt,
            LanguageCode language,
            IEnumerable<QuizOption> options,
            int correctOptionIndex)
        {
            if (string.IsNullOrWhiteSpace(targetVocabularyId))
                throw new ArgumentException("Target vocabulary id is required.", nameof(targetVocabularyId));

            if (string.IsNullOrWhiteSpace(prompt))
                throw new ArgumentException("Prompt is required.", nameof(prompt));

            var optionArray = (options ?? Array.Empty<QuizOption>())
                .Where(option => option != null)
                .ToArray();

            if (optionArray.Length < 2)
                throw new ArgumentException("A quiz question needs at least two options.", nameof(options));

            if (correctOptionIndex < 0 || correctOptionIndex >= optionArray.Length)
                throw new ArgumentOutOfRangeException(nameof(correctOptionIndex));

            TargetVocabularyId = targetVocabularyId.Trim();
            Prompt = prompt.Trim();
            Language = language;
            Options = optionArray;
            CorrectOptionIndex = correctOptionIndex;
        }

        public string TargetVocabularyId { get; }

        public string Prompt { get; }

        public LanguageCode Language { get; }

        public IReadOnlyList<QuizOption> Options { get; }

        public int CorrectOptionIndex { get; }

        public bool IsCorrect(int selectedOptionIndex)
        {
            return selectedOptionIndex == CorrectOptionIndex;
        }
    }
}
