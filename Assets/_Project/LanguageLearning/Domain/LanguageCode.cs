using System;

namespace AIAR.LanguageLearning.Domain
{
    public enum LanguageCode
    {
        English,
        Hindi,
        Malayalam,
        French
    }

    public static class LanguageCodeExtensions
    {
        public static string ToIsoCode(this LanguageCode language)
        {
            switch (language)
            {
                case LanguageCode.English:
                    return "en";
                case LanguageCode.Hindi:
                    return "hi";
                case LanguageCode.Malayalam:
                    return "ml";
                case LanguageCode.French:
                    return "fr";
                default:
                    throw new ArgumentOutOfRangeException(nameof(language), language, null);
            }
        }

        public static string ToDisplayName(this LanguageCode language)
        {
            switch (language)
            {
                case LanguageCode.English:
                    return "English";
                case LanguageCode.Hindi:
                    return "Hindi";
                case LanguageCode.Malayalam:
                    return "Malayalam";
                case LanguageCode.French:
                    return "French";
                default:
                    throw new ArgumentOutOfRangeException(nameof(language), language, null);
            }
        }

        public static bool TryParse(string value, out LanguageCode language)
        {
            language = LanguageCode.English;

            if (string.IsNullOrWhiteSpace(value))
                return false;

            var normalized = value.Trim();
            if (string.Equals(normalized, "en", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "eng", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "english", StringComparison.OrdinalIgnoreCase))
            {
                language = LanguageCode.English;
                return true;
            }

            if (string.Equals(normalized, "hi", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "hin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "hindi", StringComparison.OrdinalIgnoreCase))
            {
                language = LanguageCode.Hindi;
                return true;
            }

            if (string.Equals(normalized, "ml", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "mal", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "malayalam", StringComparison.OrdinalIgnoreCase))
            {
                language = LanguageCode.Malayalam;
                return true;
            }

            if (string.Equals(normalized, "fr", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "fre", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "fra", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(normalized, "french", StringComparison.OrdinalIgnoreCase))
            {
                language = LanguageCode.French;
                return true;
            }

            return false;
        }
    }
}
