using AIAR.LanguageLearning.Domain;

namespace AIAR.LanguageLearning.Application.Ports
{
    public interface ITextToSpeechService
    {
        void Speak(VocabularyTranslation translation);
    }
}
