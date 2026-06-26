using System;

namespace AIAR.LanguageLearning.Domain
{
    public sealed class QuizOption
    {
        public QuizOption(string vocabularyId, string text)
        {
            if (string.IsNullOrWhiteSpace(vocabularyId))
                throw new ArgumentException("Vocabulary id is required.", nameof(vocabularyId));

            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("Option text is required.", nameof(text));

            VocabularyId = vocabularyId.Trim();
            Text = text.Trim();
        }

        public string VocabularyId { get; }

        public string Text { get; }
    }
}
