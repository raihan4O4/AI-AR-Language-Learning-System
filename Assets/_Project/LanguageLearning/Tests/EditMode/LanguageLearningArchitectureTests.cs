using System;
using System.Collections.Generic;
using System.Linq;
using AIAR.LanguageLearning.Application.Models;
using AIAR.LanguageLearning.Application.Ports;
using AIAR.LanguageLearning.Application.Services;
using AIAR.LanguageLearning.Domain;
using NUnit.Framework;

namespace AIAR.LanguageLearning.Tests
{
    public sealed class LanguageLearningArchitectureTests
    {
        [Test]
        public void ObjectLabelNormalizer_MatchesSynonymsAndPluralForms()
        {
            var catalog = new[]
            {
                Entry("phone", "phone", "technology", "mobile phone", "cell phone", "smartphone")
            };

            var normalizer = new ObjectLabelNormalizer();

            Assert.IsTrue(normalizer.TryFindBestMatch("cell phones", catalog, out var match));
            Assert.AreEqual("phone", match.Id);
        }

        [Test]
        public void VocabularyLearningService_RecordsObjectViews()
        {
            var catalog = new[]
            {
                Entry("book", "book", "education", "book", "textbook")
            };
            var progressRepository = new InMemoryProgressRepository();
            var service = new VocabularyLearningService(
                new InMemoryVocabularyRepository(catalog),
                progressRepository,
                new ObjectLabelNormalizer(),
                new FixedClock(new DateTime(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc)));

            var lookup = service.FindVocabularyForObject(
                new ObjectRecognitionResult("textbook", 0.92f),
                LanguageCode.Hindi);

            Assert.IsTrue(lookup.Found);
            Assert.AreEqual(LanguageCode.Hindi, lookup.Translation.Language);

            var progress = service.RecordObjectViewed(lookup.Entry, LanguageCode.Hindi);

            Assert.AreEqual(1, progress.TimesSeen);
            Assert.AreEqual(progress, progressRepository.Load("book"));
        }

        [Test]
        public void QuizService_GeneratesSingleCorrectAnswer()
        {
            var catalog = Catalog();
            var quiz = new QuizService().CreateTranslationQuestion(
                catalog[0],
                LanguageCode.French,
                catalog,
                optionCount: 4,
                seed: 123);

            Assert.AreEqual("book", quiz.TargetVocabularyId);
            Assert.AreEqual(4, quiz.Options.Count);
            Assert.AreEqual(1, quiz.Options.Count(option => option.VocabularyId == "book"));
            Assert.IsTrue(quiz.IsCorrect(quiz.CorrectOptionIndex));
        }

        [Test]
        public void AdaptiveRevisionService_PrioritizesNewAndWeakWords()
        {
            var now = new DateTime(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);
            var catalog = Catalog();
            var progress = new[]
            {
                new WordProgress("book", 5, 2, 5, 0, now.AddDays(-1)),
                new WordProgress("bottle", 2, 0, 0, 3, now.AddDays(-8))
            };

            var recommendations = new AdaptiveRevisionService().GetRecommendations(
                catalog,
                progress,
                now,
                maxItems: 3);

            Assert.AreEqual("chair", recommendations[0].Entry.Id);
            Assert.IsTrue(recommendations.Any(item => item.Entry.Id == "bottle"));
            Assert.IsTrue(recommendations[0].PriorityScore > recommendations.Last().PriorityScore);
        }

        [Test]
        public void RetentionAnalysisService_SummarizesVocabularyProgress()
        {
            var now = new DateTime(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);
            var summary = new RetentionAnalysisService().CreateSummary(
                Catalog(),
                new[]
                {
                    new WordProgress("book", 5, 2, 5, 0, now),
                    new WordProgress("bottle", 1, 0, 0, 2, now)
                });

            Assert.AreEqual(4, summary.TotalWords);
            Assert.AreEqual(2, summary.SeenWords);
            Assert.AreEqual(1, summary.MasteredWords);
            Assert.AreEqual(1, summary.StrugglingWords);
            Assert.Greater(summary.RetentionRate, 0d);
        }

        static IReadOnlyList<VocabularyEntry> Catalog()
        {
            return new[]
            {
                Entry("book", "book", "education", "book", "textbook"),
                Entry("bottle", "bottle", "household", "bottle", "water bottle"),
                Entry("chair", "chair", "furniture", "chair", "seat"),
                Entry("phone", "phone", "technology", "phone", "mobile phone")
            };
        }

        static VocabularyEntry Entry(string id, string canonicalLabel, string category, params string[] labels)
        {
            return new VocabularyEntry(
                id,
                canonicalLabel,
                category,
                labels,
                new[]
                {
                    new VocabularyTranslation(LanguageCode.English, canonicalLabel, canonicalLabel, "en-US"),
                    new VocabularyTranslation(LanguageCode.Hindi, $"{canonicalLabel}-hi", $"{canonicalLabel}-hi", "hi-IN"),
                    new VocabularyTranslation(LanguageCode.Malayalam, $"{canonicalLabel}-ml", $"{canonicalLabel}-ml", "ml-IN"),
                    new VocabularyTranslation(LanguageCode.French, $"{canonicalLabel}-fr", $"{canonicalLabel}-fr", "fr-FR")
                });
        }

        sealed class InMemoryVocabularyRepository : IVocabularyRepository
        {
            readonly IReadOnlyList<VocabularyEntry> m_Entries;

            public InMemoryVocabularyRepository(IReadOnlyList<VocabularyEntry> entries)
            {
                m_Entries = entries;
            }

            public IReadOnlyList<VocabularyEntry> GetAll()
            {
                return m_Entries;
            }

            public bool TryGetById(string id, out VocabularyEntry entry)
            {
                entry = m_Entries.FirstOrDefault(item =>
                    string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));

                return entry != null;
            }
        }

        sealed class InMemoryProgressRepository : ILearningProgressRepository
        {
            readonly Dictionary<string, WordProgress> m_ProgressById = new Dictionary<string, WordProgress>(StringComparer.OrdinalIgnoreCase);

            public IReadOnlyList<WordProgress> LoadAll()
            {
                return m_ProgressById.Values.ToArray();
            }

            public WordProgress Load(string vocabularyId)
            {
                return m_ProgressById.TryGetValue(vocabularyId, out var progress) ? progress : null;
            }

            public void Save(WordProgress progress)
            {
                m_ProgressById[progress.VocabularyId] = progress;
            }

            public void SaveAll(IEnumerable<WordProgress> progress)
            {
                m_ProgressById.Clear();
                foreach (var item in progress)
                    Save(item);
            }
        }

        sealed class FixedClock : IClock
        {
            public FixedClock(DateTime utcNow)
            {
                UtcNow = utcNow;
            }

            public DateTime UtcNow { get; }
        }
    }
}
