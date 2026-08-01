using System.Collections.Generic;
using AIAR.LanguageLearning.Domain;

namespace AIAR.LanguageLearning.Application.Ports
{
    public interface IVocabularyRepository
    {
        IReadOnlyList<VocabularyEntry> GetAll();

        bool TryGetById(string id, out VocabularyEntry entry);
    }
}
