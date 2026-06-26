using System;

namespace AIAR.LanguageLearning.Domain
{
    public sealed class VocabularyTranslation
    {
        public VocabularyTranslation(
            LanguageCode language,
            string text,
            string phoneticHint,
            string pronunciationLocale)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("Translation text is required.", nameof(text));

            Language = language;
            Text = text.Trim();
            PhoneticHint = string.IsNullOrWhiteSpace(phoneticHint) ? string.Empty : phoneticHint.Trim();
            PronunciationLocale = string.IsNullOrWhiteSpace(pronunciationLocale)
                ? language.ToIsoCode()
                : pronunciationLocale.Trim();
        }

        public LanguageCode Language { get; }

        public string Text { get; }

        public string PhoneticHint { get; }

        public string PronunciationLocale { get; }
    }
}
