using System.Collections.Generic;
using AIAR.LanguageLearning.Domain;

namespace AIAR.LanguageLearning.Application.Ports
{
    public interface ILearningProgressRepository
    {
        IReadOnlyList<WordProgress> LoadAll();

        WordProgress Load(string vocabularyId);

        void Save(WordProgress progress);

        void SaveAll(IEnumerable<WordProgress> progress);
    }
}
