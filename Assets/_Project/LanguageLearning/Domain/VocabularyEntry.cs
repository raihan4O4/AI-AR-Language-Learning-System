using System;
using System.Collections.Generic;
using System.Linq;

namespace AIAR.LanguageLearning.Domain
{
    public sealed class VocabularyEntry
    {
        public VocabularyEntry(
            string id,
            string canonicalObjectLabel,
            string category,
            IEnumerable<string> recognitionLabels,
            IEnumerable<VocabularyTranslation> translations)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Vocabulary id is required.", nameof(id));

            if (string.IsNullOrWhiteSpace(canonicalObjectLabel))
                throw new ArgumentException("Canonical object label is required.", nameof(canonicalObjectLabel));

            var translationArray = (translations ?? Array.Empty<VocabularyTranslation>())
                .Where(translation => translation != null)
                .ToArray();

            if (translationArray.Length == 0)
                throw new ArgumentException("At least one translation is required.", nameof(translations));

            Id = id.Trim();
            CanonicalObjectLabel = canonicalObjectLabel.Trim();
            Category = string.IsNullOrWhiteSpace(category) ? "general" : category.Trim();
            RecognitionLabels = (recognitionLabels ?? Array.Empty<string>())
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Select(label => label.Trim())
                .Append(CanonicalObjectLabel)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            Translations = translationArray;
        }

        public string Id { get; }

        public string CanonicalObjectLabel { get; }

        public string Category { get; }

        public IReadOnlyList<string> RecognitionLabels { get; }

        public IReadOnlyList<VocabularyTranslation> Translations { get; }

        public bool TryGetTranslation(LanguageCode language, out VocabularyTranslation translation)
        {
            translation = null;

            foreach (var candidate in Translations)
            {
                if (candidate.Language != language)
                    continue;

                translation = candidate;
                return true;
            }

            return false;
        }

        public VocabularyTranslation GetRequiredTranslation(LanguageCode language)
        {
            if (TryGetTranslation(language, out var translation))
                return translation;

            throw new KeyNotFoundException($"Vocabulary '{Id}' does not contain a {language} translation.");
        }
    }
}
