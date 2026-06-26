using AIAR.LanguageLearning.Domain;

namespace AIAR.LanguageLearning.Application.Models
{
    public sealed class VocabularyLookupResult
    {
        VocabularyLookupResult(
            VocabularyEntry entry,
            VocabularyTranslation translation,
            string recognizedLabel,
            float recognitionConfidence)
        {
            Entry = entry;
            Translation = translation;
            RecognizedLabel = recognizedLabel;
            RecognitionConfidence = recognitionConfidence;
        }

        public VocabularyEntry Entry { get; }

        public VocabularyTranslation Translation { get; }

        public string RecognizedLabel { get; }

        public float RecognitionConfidence { get; }

        public bool Found => Entry != null && Translation != null;

        public static VocabularyLookupResult FoundEntry(
            VocabularyEntry entry,
            VocabularyTranslation translation,
            string recognizedLabel,
            float recognitionConfidence)
        {
            return new VocabularyLookupResult(entry, translation, recognizedLabel, recognitionConfidence);
        }

        public static VocabularyLookupResult NotFound(string recognizedLabel, float recognitionConfidence)
        {
            return new VocabularyLookupResult(null, null, recognizedLabel, recognitionConfidence);
        }
    }
}
