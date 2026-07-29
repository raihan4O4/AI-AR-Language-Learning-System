using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace AIAR.LanguageLearning.Unity.Presentation
{
    public readonly struct LearningActivitySnapshot
    {
        public LearningActivitySnapshot(int todayXp, int streakDays, int[] weeklyActivity)
        {
            TodayXp = Mathf.Max(0, todayXp);
            StreakDays = Mathf.Max(0, streakDays);
            WeeklyActivity = weeklyActivity ?? new int[7];
        }

        public int TodayXp { get; }

        public int StreakDays { get; }

        public int[] WeeklyActivity { get; }
    }

    public static class LearningActivityTracker
    {
        const string PlayerPrefsKey = "aiar.language-learning.activity.v1";
        const int HistoryDays = 21;

        public static void Record(int xp, int activityUnits = 1)
        {
            var document = Load();
            var todayKey = DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            var entry = document.days.FirstOrDefault(item => item != null && item.date == todayKey);
            if (entry == null)
            {
                entry = new DailyActivityRecord { date = todayKey };
                var updatedDays = document.days.ToList();
                updatedDays.Add(entry);
                document.days = updatedDays.ToArray();
            }

            entry.xp = Mathf.Max(0, entry.xp + Mathf.Max(0, xp));
            entry.activityUnits = Mathf.Max(0, entry.activityUnits + Mathf.Max(0, activityUnits));
            Trim(document);
            Save(document);
        }

        public static LearningActivitySnapshot GetSnapshot()
        {
            var document = Load();
            var activityByDate = document.days
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.date))
                .GroupBy(item => item.date)
                .ToDictionary(
                    group => group.Key,
                    group => new DailyActivityRecord
                    {
                        date = group.Key,
                        xp = group.Sum(item => Mathf.Max(0, item.xp)),
                        activityUnits = group.Sum(item => Mathf.Max(0, item.activityUnits))
                    });

            var today = DateTime.Now.Date;
            var todayKey = today.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            var todayXp = activityByDate.TryGetValue(todayKey, out var todayRecord)
                ? todayRecord.xp
                : 0;

            var weekly = new int[7];
            for (var dayIndex = 0; dayIndex < weekly.Length; dayIndex++)
            {
                var date = today.AddDays(dayIndex - 6);
                var dateKey = date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                weekly[dayIndex] = activityByDate.TryGetValue(dateKey, out var record)
                    ? record.activityUnits
                    : 0;
            }

            return new LearningActivitySnapshot(todayXp, CalculateStreak(today, activityByDate), weekly);
        }

        static int CalculateStreak(
            DateTime today,
            IReadOnlyDictionary<string, DailyActivityRecord> activityByDate)
        {
            var cursor = today;
            var todayKey = cursor.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            if (!activityByDate.TryGetValue(todayKey, out var todayRecord) || todayRecord.activityUnits <= 0)
                cursor = cursor.AddDays(-1);

            var streak = 0;
            for (var dayIndex = 0; dayIndex < HistoryDays; dayIndex++)
            {
                var dateKey = cursor.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                if (!activityByDate.TryGetValue(dateKey, out var record) || record.activityUnits <= 0)
                    break;

                streak++;
                cursor = cursor.AddDays(-1);
            }

            return streak;
        }

        static ActivityDocument Load()
        {
            var json = PlayerPrefs.GetString(PlayerPrefsKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json))
                return new ActivityDocument { schemaVersion = 1, days = Array.Empty<DailyActivityRecord>() };

            var document = JsonUtility.FromJson<ActivityDocument>(json) ?? new ActivityDocument();
            document.schemaVersion = 1;
            if (document.days == null)
                document.days = Array.Empty<DailyActivityRecord>();
            return document;
        }

        static void Trim(ActivityDocument document)
        {
            var earliestDate = DateTime.Now.Date.AddDays(-(HistoryDays - 1));
            document.days = document.days
                .Where(item => item != null && TryParseDate(item.date, out var date) && date >= earliestDate)
                .OrderBy(item => item.date)
                .ToArray();
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
            PlayerPrefs.SetString(PlayerPrefsKey, JsonUtility.ToJson(document));
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
        }
    }
}
