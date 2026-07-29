using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace AIAR.LanguageLearning.Unity.Presentation
{
    public readonly struct LearningDaySnapshot
    {
        public LearningDaySnapshot(
            DateTime date,
            int xp,
            int objectScans,
            int quizAnswers,
            int quizCorrect,
            int quizzesCompleted,
            int pronunciationPlays,
            int learningSeconds,
            int legacyActivityUnits = 0)
        {
            Date = date.Date;
            Xp = Math.Max(0, xp);
            ObjectScans = Math.Max(0, objectScans);
            QuizAnswers = Math.Max(0, quizAnswers);
            QuizCorrect = Math.Max(0, quizCorrect);
            QuizzesCompleted = Math.Max(0, quizzesCompleted);
            PronunciationPlays = Math.Max(0, pronunciationPlays);
            LearningSeconds = Math.Max(0, learningSeconds);
            LegacyActivityUnits = Math.Max(0, legacyActivityUnits);
        }

        public DateTime Date { get; }

        public int Xp { get; }

        public int ObjectScans { get; }

        public int QuizAnswers { get; }

        public int QuizCorrect { get; }

        public int QuizzesCompleted { get; }

        public int PronunciationPlays { get; }

        public int LearningSeconds { get; }

        public int LegacyActivityUnits { get; }

        public int ActivityUnits => ObjectScans + QuizAnswers + PronunciationPlays + LegacyActivityUnits;

        public bool IsActive => ActivityUnits > 0;
    }

    public readonly struct LearningActivitySnapshot
    {
        public LearningActivitySnapshot(int todayXp, int streakDays, int[] weeklyActivity)
            : this(
                todayXp,
                streakDays,
                CreateLegacyDays(weeklyActivity),
                DateTime.MinValue)
        {
        }

        public LearningActivitySnapshot(
            int todayXp,
            int streakDays,
            LearningDaySnapshot[] weeklyDays,
            DateTime lastActiveDate)
        {
            TodayXp = Math.Max(0, todayXp);
            StreakDays = Math.Max(0, streakDays);
            WeeklyDays = weeklyDays ?? Array.Empty<LearningDaySnapshot>();
            WeeklyActivity = WeeklyDays.Select(day => day.ActivityUnits).ToArray();
            LastActiveDate = lastActiveDate;
            WeeklyObjectScans = WeeklyDays.Sum(day => day.ObjectScans);
            WeeklyQuizAnswers = WeeklyDays.Sum(day => day.QuizAnswers);
            WeeklyQuizCorrect = WeeklyDays.Sum(day => day.QuizCorrect);
            WeeklyQuizzesCompleted = WeeklyDays.Sum(day => day.QuizzesCompleted);
            WeeklyLearningSeconds = WeeklyDays.Sum(day => day.LearningSeconds);
        }

        public int TodayXp { get; }

        public int StreakDays { get; }

        public int[] WeeklyActivity { get; }

        public LearningDaySnapshot[] WeeklyDays { get; }

        public DateTime LastActiveDate { get; }

        public int WeeklyObjectScans { get; }

        public int WeeklyQuizAnswers { get; }

        public int WeeklyQuizCorrect { get; }

        public int WeeklyQuizzesCompleted { get; }

        public int WeeklyLearningSeconds { get; }

        public double WeeklyQuizAccuracy => WeeklyQuizAnswers == 0
            ? 0d
            : WeeklyQuizCorrect / (double)WeeklyQuizAnswers;

        static LearningDaySnapshot[] CreateLegacyDays(int[] weeklyActivity)
        {
            var values = weeklyActivity ?? new int[7];
            return values
                .Select((value, index) => new LearningDaySnapshot(
                    DateTime.Today.AddDays(index - (values.Length - 1)),
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    value))
                .ToArray();
        }
    }

    public static class LearningActivityTracker
    {
        const string DefaultPlayerPrefsKey = "aiar.language-learning.activity.v1";
        const int HistoryDays = 370;
        static string s_PlayerPrefsKey = DefaultPlayerPrefsKey;

        public static void RecordObjectScan(int xp = 5)
        {
            RecordToday(record =>
            {
                record.xp += Math.Max(0, xp);
                record.objectScans++;
            });
        }

        public static void RecordQuizAnswer(bool wasCorrect, int xp)
        {
            RecordToday(record =>
            {
                record.xp += Math.Max(0, xp);
                record.quizAnswers++;
                if (wasCorrect)
                    record.quizCorrect++;
            });
        }

        public static void RecordQuizCompleted()
        {
            RecordToday(record => record.quizzesCompleted++);
        }

        public static void RecordPronunciationPlayed(int xp = 3)
        {
            RecordToday(record =>
            {
                record.xp += Math.Max(0, xp);
                record.pronunciationPlays++;
            });
        }

        public static void RecordLearningSeconds(float seconds)
        {
            seconds = Mathf.Max(0f, seconds);
            if (seconds <= 0f)
                return;

            RecordToday(record => record.learningSeconds += seconds);
        }

        public static LearningActivitySnapshot GetSnapshot()
        {
            return GetSnapshot(DateTime.Now);
        }

        public static LearningActivitySnapshot GetSnapshot(DateTime localNow)
        {
            var document = Load();
            var activityByDate = document.days
                .Where(item => item != null &&
                    !string.IsNullOrWhiteSpace(item.date) &&
                    TryParseDate(item.date, out _))
                .GroupBy(item => item.date)
                .ToDictionary(group => group.Key, Merge);

            var today = localNow.Date;
            var todayKey = ToDateKey(today);
            var todayXp = activityByDate.TryGetValue(todayKey, out var todayRecord)
                ? Math.Max(0, todayRecord.xp)
                : 0;
            var weeklyDays = new LearningDaySnapshot[7];

            for (var dayIndex = 0; dayIndex < weeklyDays.Length; dayIndex++)
            {
                var date = today.AddDays(dayIndex - 6);
                var dateKey = ToDateKey(date);
                weeklyDays[dayIndex] = activityByDate.TryGetValue(dateKey, out var record)
                    ? ToSnapshot(date, record)
                    : new LearningDaySnapshot(date, 0, 0, 0, 0, 0, 0, 0);
            }

            var lastActiveDate = activityByDate.Values
                .Where(HasActivity)
                .Select(record => TryParseDate(record.date, out var date) ? date : DateTime.MinValue)
                .DefaultIfEmpty(DateTime.MinValue)
                .Max();

            return new LearningActivitySnapshot(
                todayXp,
                CalculateStreak(today, activityByDate),
                weeklyDays,
                lastActiveDate);
        }

        public static void Clear()
        {
            PlayerPrefs.DeleteKey(s_PlayerPrefsKey);
            PlayerPrefs.Save();
        }

        public static void SetStorageKeyForTests(string playerPrefsKey)
        {
            s_PlayerPrefsKey = string.IsNullOrWhiteSpace(playerPrefsKey)
                ? DefaultPlayerPrefsKey
                : playerPrefsKey.Trim();
        }

        static void RecordToday(Action<DailyActivityRecord> update)
        {
            var document = Load();
            var todayKey = ToDateKey(DateTime.Now);
            var entry = document.days.FirstOrDefault(item => item != null && item.date == todayKey);
            if (entry == null)
            {
                entry = new DailyActivityRecord { date = todayKey };
                document.days = document.days.Concat(new[] { entry }).ToArray();
            }

            update(entry);
            Normalize(entry);
            Trim(document, DateTime.Now.Date);
            Save(document);
        }

        static int CalculateStreak(
            DateTime today,
            IReadOnlyDictionary<string, DailyActivityRecord> activityByDate)
        {
            var cursor = today;
            if (!activityByDate.TryGetValue(ToDateKey(cursor), out var todayRecord) || !HasActivity(todayRecord))
                cursor = cursor.AddDays(-1);

            var streak = 0;
            for (var dayIndex = 0; dayIndex < HistoryDays; dayIndex++)
            {
                if (!activityByDate.TryGetValue(ToDateKey(cursor), out var record) || !HasActivity(record))
                    break;

                streak++;
                cursor = cursor.AddDays(-1);
            }

            return streak;
        }

        static ActivityDocument Load()
        {
            var json = PlayerPrefs.GetString(s_PlayerPrefsKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json))
                return new ActivityDocument { schemaVersion = 2, days = Array.Empty<DailyActivityRecord>() };

            ActivityDocument document;
            try
            {
                document = JsonUtility.FromJson<ActivityDocument>(json) ?? new ActivityDocument();
            }
            catch (ArgumentException)
            {
                document = new ActivityDocument();
            }

            document.schemaVersion = 2;
            if (document.days == null)
                document.days = Array.Empty<DailyActivityRecord>();
            return document;
        }

        static DailyActivityRecord Merge(IEnumerable<DailyActivityRecord> records)
        {
            var merged = new DailyActivityRecord();
            foreach (var record in records)
            {
                merged.date = record.date;
                merged.xp += Math.Max(0, record.xp);
                merged.activityUnits += Math.Max(0, record.activityUnits);
                merged.objectScans += Math.Max(0, record.objectScans);
                merged.quizAnswers += Math.Max(0, record.quizAnswers);
                merged.quizCorrect += Math.Max(0, record.quizCorrect);
                merged.quizzesCompleted += Math.Max(0, record.quizzesCompleted);
                merged.pronunciationPlays += Math.Max(0, record.pronunciationPlays);
                merged.learningSeconds += Math.Max(0f, record.learningSeconds);
            }

            Normalize(merged);
            return merged;
        }

        static LearningDaySnapshot ToSnapshot(DateTime date, DailyActivityRecord record)
        {
            return new LearningDaySnapshot(
                date,
                record.xp,
                record.objectScans,
                record.quizAnswers,
                record.quizCorrect,
                record.quizzesCompleted,
                record.pronunciationPlays,
                Mathf.RoundToInt(record.learningSeconds),
                record.activityUnits);
        }

        static bool HasActivity(DailyActivityRecord record)
        {
            return record != null &&
                   (record.activityUnits > 0 ||
                    record.objectScans > 0 ||
                    record.quizAnswers > 0 ||
                    record.pronunciationPlays > 0);
        }

        static void Normalize(DailyActivityRecord record)
        {
            record.xp = Math.Max(0, record.xp);
            record.activityUnits = Math.Max(0, record.activityUnits);
            record.objectScans = Math.Max(0, record.objectScans);
            record.quizAnswers = Math.Max(0, record.quizAnswers);
            record.quizCorrect = Math.Max(0, Math.Min(record.quizAnswers, record.quizCorrect));
            record.quizzesCompleted = Math.Max(0, Math.Min(record.quizAnswers, record.quizzesCompleted));
            record.pronunciationPlays = Math.Max(0, record.pronunciationPlays);
            record.learningSeconds = Math.Max(0f, record.learningSeconds);
        }

        static void Trim(ActivityDocument document, DateTime today)
        {
            var earliestDate = today.AddDays(-(HistoryDays - 1));
            document.days = document.days
                .Where(item => item != null && TryParseDate(item.date, out var date) && date >= earliestDate && date <= today)
                .OrderBy(item => item.date)
                .ToArray();
        }

        static string ToDateKey(DateTime date)
        {
            return date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        }

        static bool TryParseDate(string value, out DateTime date)
        {
            return DateTime.TryParseExact(
                value,
                "yyyyMMdd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date);
        }

        static void Save(ActivityDocument document)
        {
            PlayerPrefs.SetString(s_PlayerPrefsKey, JsonUtility.ToJson(document));
            PlayerPrefs.Save();
        }

        [Serializable]
        sealed class ActivityDocument
        {
            public int schemaVersion;
            public DailyActivityRecord[] days;
        }

        [Serializable]
        sealed class DailyActivityRecord
        {
            public string date;
            public int xp;
            public int activityUnits;
            public int objectScans;
            public int quizAnswers;
            public int quizCorrect;
            public int quizzesCompleted;
            public int pronunciationPlays;
            public float learningSeconds;
        }
    }
}
