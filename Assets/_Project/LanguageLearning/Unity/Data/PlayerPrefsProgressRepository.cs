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

            var document = JsonUtility.FromJson<ProgressDocument>(json);
            if (document == null || document.words == null)
                return Array.Empty<WordProgress>();

            return document.words
                .Where(record => record != null && !string.IsNullOrWhiteSpace(record.vocabularyId))
                .Select(ToDomain)
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
                .ToDictionary(item => item.VocabularyId, item => item, StringComparer.OrdinalIgnoreCase);

            progressById[progress.VocabularyId] = progress;
            SaveAll(progressById.Values);
        }

        public void SaveAll(IEnumerable<WordProgress> progress)
        {
            var document = new ProgressDocument
            {
                schemaVersion = 1,
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

        static WordProgress ToDomain(ProgressRecord record)
        {
            var lastSeenTicks = Math.Max(0L, record.lastSeenAtUtcTicks);
            var lastSeen = lastSeenTicks == 0L
                ? DateTime.MinValue
                : new DateTime(lastSeenTicks, DateTimeKind.Utc);

            return new WordProgress(
                record.vocabularyId,
                record.timesSeen,
                record.pronunciationAttempts,
                record.quizCorrect,
                record.quizIncorrect,
                lastSeen);
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
                lastSeenAtUtcTicks = progress.LastSeenAtUtc == DateTime.MinValue
                    ? 0L
                    : progress.LastSeenAtUtc.Ticks
            };
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
            public long lastSeenAtUtcTicks;
        }
    }
}
