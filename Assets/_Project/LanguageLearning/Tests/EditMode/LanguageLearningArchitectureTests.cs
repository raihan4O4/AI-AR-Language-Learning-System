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
        public void ObjectLabelNormalizer_DoesNotMatchPartialWords()
        {
            var catalog = new[]
            {
                Entry("car", "car", "transport", "car")
            };

            var matched = new ObjectLabelNormalizer().TryFindBestMatch("carrot", catalog, out _);

            Assert.IsFalse(matched);
        }

        [Test]
        public void NormalizedBoundingBox_StaysInsideTheViewport()
        {
            var box = new NormalizedBoundingBox(0.85f, 0.9f, 0.5f, 0.4f);

            Assert.AreEqual(1f, box.X + box.Width, 0.0001f);
            Assert.AreEqual(1f, box.Y + box.Height, 0.0001f);
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
        public void QuizService_CreatesVariedSequenceWithoutConsecutiveRepeats()
        {
            var catalog = Catalog();

            var sequence = new QuizService().CreateQuestionSequence(
                catalog,
                LanguageCode.Malayalam,
                questionCount: 9,
                seed: 456);

            Assert.AreEqual(9, sequence.Count);
            Assert.AreEqual(catalog.Count, sequence.Take(catalog.Count).Select(entry => entry.Id).Distinct().Count());
            Assert.IsTrue(sequence.All(entry => entry.TryGetTranslation(LanguageCode.Malayalam, out _)));
            for (var index = 1; index < sequence.Count; index++)
                Assert.AreNotEqual(sequence[index - 1].Id, sequence[index].Id);
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

            Assert.AreEqual("bottle", recommendations[0].Entry.Id);
            Assert.IsTrue(recommendations.Any(item => item.Entry.Id == "bottle"));
            Assert.IsTrue(recommendations[0].PriorityScore > recommendations.Last().PriorityScore);
        }

        [Test]
        public void AdaptiveRevisionService_QueueContainsOnlyLearnedWordsThatNeedReview()
        {
            var now = new DateTime(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);
            var progress = new[]
            {
                new WordProgress("book", 3, 0, 0, 0, now),
                new WordProgress("bottle", 2, 0, 1, 3, now)
            };

            var queue = new AdaptiveRevisionService().GetRevisionQueue(Catalog(), progress, now, 8);

            CollectionAssert.AreEquivalent(new[] { "book", "bottle" }, queue.Select(item => item.Entry.Id));
            Assert.IsFalse(queue.Any(item => item.Reason == "New word"));
        }

        [Test]
        public void WordProgress_RetentionRequiresSuccessfulRecallAcrossTime()
        {
            var start = new DateTime(2026, 6, 10, 8, 0, 0, DateTimeKind.Utc);
            var progress = WordProgress.CreateNew("book")
                .Record(QuizInteraction("book", true, start));
            var firstRecallScore = progress.CalculateRetentionScore(start);

            progress = progress.Record(QuizInteraction("book", true, start.AddHours(2d)));
            Assert.AreEqual(1, progress.SpacedCorrectRecalls, "Same-session answers must not inflate spaced recall.");

            progress = progress.Record(QuizInteraction("book", true, start.AddHours(23d)));

            Assert.AreEqual(2, progress.SpacedCorrectRecalls);
            Assert.AreEqual(3, progress.CorrectRecallStreak);
            Assert.Greater(progress.CalculateRetentionScore(start.AddHours(23d)), firstRecallScore);
            Assert.Greater(progress.CalculateRetentionScore(start.AddHours(23d)), 0.75d);
        }

        [Test]
        public void WordProgress_IncorrectRecallResetsStreakAndCreatesRevisionNeed()
        {
            var now = new DateTime(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);
            var progress = WordProgress.CreateNew("book")
                .Record(QuizInteraction("book", true, now.AddDays(-1d)))
                .Record(QuizInteraction("book", false, now));

            Assert.Zero(progress.CorrectRecallStreak);
            Assert.AreEqual(0.5d, progress.QuizAccuracy, 0.0001d);
            Assert.IsTrue(progress.NeedsRevision(now));
        }

        [Test]
        public void QuizService_AdaptiveSequenceRepeatsWeakWordsMoreOften()
        {
            var now = new DateTime(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);
            var progress = new[]
            {
                new WordProgress("book", 5, 2, 6, 0, now),
                new WordProgress("bottle", 3, 0, 0, 4, now)
            };

            var sequence = new QuizService().CreateAdaptiveQuestionSequence(
                Catalog(),
                progress,
                LanguageCode.French,
                40,
                now,
                7421);
            var counts = sequence.GroupBy(entry => entry.Id).ToDictionary(group => group.Key, group => group.Count());

            Assert.IsTrue(counts.TryGetValue("book", out var bookCount));
            Assert.Greater(counts["bottle"], bookCount);
            Assert.GreaterOrEqual(counts["bottle"], 15);
            for (var index = 1; index < sequence.Count; index++)
                Assert.AreNotEqual(sequence[index - 1].Id, sequence[index].Id);
        }

        [Test]
        public void RetentionAnalysisService_SummarizesVocabularyProgress()
        {
            var now = new DateTime(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);
            var summary = new RetentionAnalysisService().CreateSummary(
                Catalog(),
                new[]
                {
                    new WordProgress("book", 5, 2, 5, 0, now, 5, 3, now, now, now),
                    new WordProgress("bottle", 1, 0, 0, 2, now)
                },
                nowUtc: now);

            Assert.AreEqual(4, summary.TotalWords);
            Assert.AreEqual(2, summary.SeenWords);
            Assert.AreEqual(1, summary.MasteredWords);
            Assert.AreEqual(1, summary.StrugglingWords);
            Assert.AreEqual(0.5d, summary.RetentionRate, 0.0001d);
            Assert.Greater(summary.RetentionRate, 0d);
            Assert.AreEqual(7, summary.QuizAttempts);
            Assert.AreEqual(5d / 7d, summary.QuizAccuracy, 0.0001d);
            Assert.Greater(summary.AverageMastery, 0d);
        }

        static LearningInteraction QuizInteraction(string vocabularyId, bool wasCorrect, DateTime occurredAtUtc)
        {
            return new LearningInteraction(
                vocabularyId,
                LanguageCode.French,
                LearningInteractionType.QuizAnswered,
                wasCorrect,
                occurredAtUtc);
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
