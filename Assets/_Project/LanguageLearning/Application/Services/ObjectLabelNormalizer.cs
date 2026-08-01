using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using AIAR.LanguageLearning.Domain;

namespace AIAR.LanguageLearning.Application.Services
{
    public sealed class ObjectLabelNormalizer
    {
        public bool TryFindBestMatch(
            string recognizedLabel,
            IEnumerable<VocabularyEntry> vocabulary,
            out VocabularyEntry entry)
        {
            entry = null;

            if (string.IsNullOrWhiteSpace(recognizedLabel) || vocabulary == null)
                return false;

            var candidates = GetCandidateForms(recognizedLabel).ToArray();
            if (candidates.Length == 0)
                return false;

            // Prefer an entry whose canonical label is the detector label. Some of the
            // original learning words have broad aliases (for example, "bag" matched
            // both COCO's "backpack" and "handbag"). Exact canonical matching keeps
            // every detector class distinct while retaining those legacy aliases.
            foreach (var vocabularyEntry in vocabulary)
            {
                var canonicalLabel = Normalize(vocabularyEntry.CanonicalObjectLabel);
                if (canonicalLabel.Length > 0 &&
                    candidates.Contains(canonicalLabel, StringComparer.OrdinalIgnoreCase))
                {
                    entry = vocabularyEntry;
                    return true;
                }
            }

            foreach (var vocabularyEntry in vocabulary)
            {
                var normalizedLabels = vocabularyEntry.RecognitionLabels
                    .Select(Normalize)
                    .Where(label => label.Length > 0)
                    .ToArray();

                if (normalizedLabels.Any(label => candidates.Contains(label, StringComparer.OrdinalIgnoreCase)))
                {
                    entry = vocabularyEntry;
                    return true;
                }
            }

            foreach (var vocabularyEntry in vocabulary)
            {
                var normalizedLabels = vocabularyEntry.RecognitionLabels
                    .Select(Normalize)
                    .Where(label => label.Length > 2);

                if (normalizedLabels.Any(label => candidates.Any(candidate => ContainsWholePhrase(candidate, label))))
                {
                    entry = vocabularyEntry;
                    return true;
                }
            }

            return false;
        }

        static bool ContainsWholePhrase(string candidate, string expectedPhrase)
        {
            if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(expectedPhrase))
                return false;

            var paddedCandidate = $" {candidate} ";
            var paddedPhrase = $" {expectedPhrase} ";
            return paddedCandidate.IndexOf(paddedPhrase, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public IEnumerable<string> GetCandidateForms(string label)
        {
            var normalized = Normalize(label);
            if (string.IsNullOrEmpty(normalized))
                yield break;

            yield return normalized;

            if (normalized.EndsWith("es", StringComparison.OrdinalIgnoreCase) && normalized.Length > 4)
                yield return normalized.Substring(0, normalized.Length - 2);

            if (normalized.EndsWith("s", StringComparison.OrdinalIgnoreCase) && normalized.Length > 3)
                yield return normalized.Substring(0, normalized.Length - 1);
        }

        public string Normalize(string label)
        {
            if (string.IsNullOrWhiteSpace(label))
                return string.Empty;

            var builder = new StringBuilder(label.Length);
            var lastWasSpace = false;

            foreach (var character in label.Trim())
            {
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(char.ToLowerInvariant(character));
                    lastWasSpace = false;
                    continue;
                }

                if (char.IsWhiteSpace(character) || character == '-' || character == '_')
                {
                    if (!lastWasSpace && builder.Length > 0)
                    {
                        builder.Append(' ');
                        lastWasSpace = true;
                    }
                }
            }

            return builder.ToString().Trim();
        }
    }
}
