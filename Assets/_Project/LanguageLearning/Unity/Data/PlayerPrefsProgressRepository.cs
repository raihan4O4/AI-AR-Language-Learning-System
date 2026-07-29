using System;
using System.Collections.Generic;
using System.Linq;
using AIAR.LanguageLearning.Application.Ports;
using AIAR.LanguageLearning.Domain;
using UnityEngine;

namespace AIAR.LanguageLearning.Unity.Data
{
    public sealed class PlayerPrefsProgressRepository : ILearningProgressRepository
    {
        readonly string m_PlayerPrefsKey;

        public PlayerPrefsProgressRepository(string playerPrefsKey)
        {
            m_PlayerPrefsKey = string.IsNullOrWhiteSpace(playerPrefsKey)
                ? "aiar.language-learning.progress.v1"
                : playerPrefsKey.Trim();
        }

        public IReadOnlyList<WordProgress> LoadAll()
        {
            var json = PlayerPrefs.GetString(m_PlayerPrefsKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json))
                return Array.Empty<WordProgress>();

            ProgressDocument document;
            try
            {
                document = JsonUtility.FromJson<ProgressDocument>(json);
            }
            catch (ArgumentException)
            {
                return Array.Empty<WordProgress>();
            }

            if (document == null || document.words == null)
                return Array.Empty<WordProgress>();

            return document.words
                .Where(record => record != null && !string.IsNullOrWhiteSpace(record.vocabularyId))
                .Select(record => ToDomain(record, document.schemaVersion))
                .ToArray();
        }

        public WordProgress Load(string vocabularyId)
        {
            if (string.IsNullOrWhiteSpace(vocabularyId))
                return null;

            return LoadAll().FirstOrDefault(progress =>
                string.Equals(progress.VocabularyId, vocabularyId, StringComparison.OrdinalIgnoreCase));
        }

        public void Save(WordProgress progress)
        {
            if (progress == null)
                throw new ArgumentNullException(nameof(progress));

            var progressById = LoadAll()
                .GroupBy(item => item.VocabularyId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

            progressById[progress.VocabularyId] = progress;
            SaveAll(progressById.Values);
        }

        public void SaveAll(IEnumerable<WordProgress> progress)
        {
            var document = new ProgressDocument
            {
                schemaVersion = 2,
                words = (progress ?? Array.Empty<WordProgress>())
                    .Where(item => item != null)
                    .Select(ToRecord)
                    .ToArray()
            };

            PlayerPrefs.SetString(m_PlayerPrefsKey, JsonUtility.ToJson(document));
            PlayerPrefs.Save();
        }

        public void Clear()
        {
            PlayerPrefs.DeleteKey(m_PlayerPrefsKey);
            PlayerPrefs.Save();
        }

        static WordProgress ToDomain(ProgressRecord record, int schemaVersion)
        {
            var lastSeen = FromUtcTicks(record.lastSeenAtUtcTicks);

            if (schemaVersion < 2)
            {
                return new WordProgress(
                    record.vocabularyId,
                    record.timesSeen,
                    record.pronunciationAttempts,
                    record.quizCorrect,
                    record.quizIncorrect,
                    lastSeen);
            }

            return new WordProgress(
                record.vocabularyId,
                record.timesSeen,
                record.pronunciationAttempts,
                record.quizCorrect,
                record.quizIncorrect,
                lastSeen,
                record.correctRecallStreak,
                record.spacedCorrectRecalls,
                FromUtcTicks(record.lastQuizAtUtcTicks),
                FromUtcTicks(record.lastCorrectAtUtcTicks),
                FromUtcTicks(record.lastScannedAtUtcTicks));
        }

        static ProgressRecord ToRecord(WordProgress progress)
        {
            return new ProgressRecord
            {
                vocabularyId = progress.VocabularyId,
                timesSeen = progress.TimesSeen,
                pronunciationAttempts = progress.PronunciationAttempts,
                quizCorrect = progress.QuizCorrect,
                quizIncorrect = progress.QuizIncorrect,
                correctRecallStreak = progress.CorrectRecallStreak,
                spacedCorrectRecalls = progress.SpacedCorrectRecalls,
                lastSeenAtUtcTicks = progress.LastSeenAtUtc == DateTime.MinValue
                    ? 0L
                    : progress.LastSeenAtUtc.Ticks,
                lastQuizAtUtcTicks = progress.LastQuizAtUtc == DateTime.MinValue
                    ? 0L
                    : progress.LastQuizAtUtc.Ticks,
                lastCorrectAtUtcTicks = progress.LastCorrectAtUtc == DateTime.MinValue
                    ? 0L
                    : progress.LastCorrectAtUtc.Ticks,
                lastScannedAtUtcTicks = progress.LastScannedAtUtc == DateTime.MinValue
                    ? 0L
                    : progress.LastScannedAtUtc.Ticks
            };
        }

        static DateTime FromUtcTicks(long ticks)
        {
            if (ticks <= 0L)
                return DateTime.MinValue;

            return new DateTime(Math.Min(DateTime.MaxValue.Ticks, ticks), DateTimeKind.Utc);
        }

        [Serializable]
        sealed class ProgressDocument
        {
            public int schemaVersion;
            public ProgressRecord[] words;
        }

        [Serializable]
        sealed class ProgressRecord
        {
            public string vocabularyId;
            public int timesSeen;
            public int pronunciationAttempts;
            public int quizCorrect;
            public int quizIncorrect;
            public int correctRecallStreak;
            public int spacedCorrectRecalls;
            public long lastSeenAtUtcTicks;
            public long lastQuizAtUtcTicks;
            public long lastCorrectAtUtcTicks;
            public long lastScannedAtUtcTicks;
        }
    }
}
