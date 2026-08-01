using AIAR.LanguageLearning.Application.Models;
using AIAR.LanguageLearning.Application.Services;
using AIAR.LanguageLearning.Unity.Bootstrap;
using AIAR.LanguageLearning.Unity.Data;
using AIAR.LanguageLearning.Unity.ObjectDetection;
using AIAR.LanguageLearning.Unity.Presentation;
using NUnit.Framework;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using System.Linq;

namespace AIAR.LanguageLearning.Tests
{
    public sealed class LanguageLearningUnityIntegrationTests
    {
        const string MainScenePath = "Assets/Scenes/Main.unity";
        const string ModelPath = "Assets/_Project/LanguageLearning/ObjectDetection/Models/yolo11n_320.onnx";
        const string LabelsPath = "Assets/_Project/LanguageLearning/ObjectDetection/Models/coco80.txt";
        static readonly string[] LocalizedLabelsPaths =
        {
            "Assets/_Project/LanguageLearning/ObjectDetection/Models/coco80.en.txt",
            "Assets/_Project/LanguageLearning/ObjectDetection/Models/coco80.hi.txt",
            "Assets/_Project/LanguageLearning/ObjectDetection/Models/coco80.ml.txt",
            "Assets/_Project/LanguageLearning/ObjectDetection/Models/coco80.fr.txt"
        };

        [Test]
        public void MainScene_HasValidMvpRuntimeWiring()
        {
            var scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);

            Assert.IsTrue(scene.IsValid());
            Assert.IsNotNull(Object.FindAnyObjectByType<LanguageLearningCompositionRoot>());
            Assert.IsNotNull(Object.FindAnyObjectByType<LearningSessionController>());
            Assert.IsNotNull(Object.FindAnyObjectByType<LearningMvpController>());
            Assert.IsNotNull(Object.FindAnyObjectByType<RealtimeCocoObjectDetector>());
            Assert.IsNotNull(Object.FindAnyObjectByType<PooledDetectionOverlay>(FindObjectsInactive.Include));
            Assert.IsNotNull(Object.FindAnyObjectByType<ARVocabularyOverlayPresenter>());

            var uiRoot = GameObject.Find("LanguageLearning UI");
            Assert.IsNotNull(uiRoot, "The main MVP canvas is missing.");
            Assert.Greater(
                uiRoot.transform.localScale.sqrMagnitude,
                0.0001f,
                "The root canvas must not have the zero scale that previously made the UI invisible.");

            var recognizer = Object.FindAnyObjectByType<RealtimeCocoObjectDetector>();
            var serializedRecognizer = new SerializedObject(recognizer);
            Assert.IsNotNull(
                serializedRecognizer.FindProperty("m_ModelAsset").objectReferenceValue,
                "The verified YOLO11n model is not assigned.");
            Assert.IsNotNull(
                serializedRecognizer.FindProperty("m_CocoLabels").objectReferenceValue,
                "The COCO labels are not assigned.");
            Assert.AreEqual(
                (int)BackendType.GPUCompute,
                serializedRecognizer.FindProperty("m_PreferredBackend").intValue);
        }

        [Test]
        public void MainScene_MenuQuizAndBackButtonsDriveTheMvpFlow()
        {
            EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);

            var mvp = Object.FindAnyObjectByType<LearningMvpController>();
            Assert.IsNotNull(mvp);
            mvp.ShowHome();

            var mainMenu = GameObject.Find("Main Menu").GetComponent<CanvasGroup>();
            var quizButton = GameObject.Find("Quiz Button").GetComponent<Button>();
            Assert.IsTrue(mainMenu.interactable);
            Assert.IsTrue(mainMenu.blocksRaycasts);
            Assert.AreEqual(
                "READY TO EXPLORE",
                GameObject.Find("Exploration Eyebrow").GetComponent<TMPro.TMP_Text>().text);

            quizButton.onClick.Invoke();

            var quizPanel = GameObject.Find("Quiz Panel").GetComponent<CanvasGroup>();
            var languageSelector = GameObject.Find("Quiz Language Selector");
            var nextButton = GameObject.Find("Next Question Button").GetComponent<Button>();
            Assert.IsFalse(mainMenu.interactable);
            Assert.IsTrue(quizPanel.interactable);
            Assert.AreEqual(
                System.Enum.GetValues(typeof(Domain.LanguageCode)).Length - 1,
                languageSelector.transform.childCount,
                "The quiz selector should only list non-English target languages.");
            Assert.IsNull(languageSelector.transform.Find("English Button"));
            Assert.IsFalse(nextButton.interactable, "Next must remain disabled until an answer is chosen.");

            var caption = GameObject.Find("Question Caption").GetComponent<TMPro.TMP_Text>();
            var prompt = GameObject.Find("Quiz Prompt").GetComponent<TMPro.TMP_Text>();
            StringAssert.StartsWith("TRANSLATE THIS ", caption.text);
            StringAssert.EndsWith(" WORD", caption.text);
            Assert.AreEqual(LearningUiTheme.Primary, caption.color);
            Assert.AreEqual(prompt.text.ToUpperInvariant(), prompt.text);
            Assert.AreEqual(TMPro.TextAlignmentOptions.Center, prompt.alignment);
            Assert.AreEqual(TMPro.FontStyles.Bold, prompt.fontStyle);

            var firstPrompt = prompt.text;
            GameObject.Find("Quiz Option 1 Button").GetComponent<Button>().onClick.Invoke();
            Assert.IsTrue(nextButton.interactable);
            nextButton.onClick.Invoke();
            var secondPrompt = GameObject.Find("Quiz Prompt").GetComponent<TMPro.TMP_Text>().text;
            Assert.AreNotEqual(firstPrompt, secondPrompt, "Next must advance to a different question.");

            GameObject.Find("Back Button").GetComponent<Button>().onClick.Invoke();
            Assert.IsTrue(mainMenu.interactable);
            Assert.IsFalse(quizPanel.interactable);
        }

        [Test]
        public void MainScene_ProgressDashboardContainsEvidenceBasedAnalyticsSections()
        {
            const string key = "aiar.language-learning.tests.activity-dashboard";
            var today = new System.DateTime(2026, 7, 23, 12, 0, 0);
            LearningActivityTracker.SetStorageKeyForTests(key);
            LearningActivityTracker.SetLocalNowForTests(today.AddDays(-6));
            LearningActivityTracker.Clear();
            try
            {
                LearningActivityTracker.RecordObjectScan();
                LearningActivityTracker.SetLocalNowForTests(today.AddDays(-3));
                LearningActivityTracker.RecordQuizAnswer(true, 15);
                LearningActivityTracker.RecordQuizAnswer(false, 3);
                LearningActivityTracker.RecordQuizCompleted();
                LearningActivityTracker.SetLocalNowForTests(today);
                for (var scanIndex = 0; scanIndex < 4; scanIndex++)
                    LearningActivityTracker.RecordObjectScan();

                EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);

                var mvp = Object.FindAnyObjectByType<LearningMvpController>();
                Assert.IsNotNull(mvp);
                mvp.ShowProgress();

                Assert.IsNotNull(GameObject.Find("Vocabulary mastery Value"));
                Assert.IsNotNull(GameObject.Find("Quiz accuracy Value"));
                Assert.IsNotNull(GameObject.Find("Retention score Value"));
                Assert.IsNotNull(GameObject.Find("Needs revision Value"));
                Assert.IsNotNull(GameObject.Find("Weekly Metric Row"));
                Assert.IsNotNull(GameObject.Find("Weekly Quiz Quality Card"));
                Assert.IsNotNull(GameObject.Find("Weekly Chart Caption"));
                Assert.IsNotNull(GameObject.Find("Recent Learning Activity Card"));
                Assert.IsNotNull(GameObject.Find("Today Activity Metrics"));
                Assert.IsNotNull(GameObject.Find("Overall Vocabulary Snapshot"));
                Assert.IsNull(GameObject.Find("Progress Summary"));
                var practiceCard = GameObject.Find("Practice Card");
                Assert.IsNotNull(practiceCard);
                var revisionQueue = practiceCard.transform.Find("Revision Queue Items");
                var revisionEmptyState = practiceCard.transform.Find("Revision Queue Empty State");
                Assert.IsNotNull(revisionQueue);
                Assert.IsNotNull(revisionEmptyState);
                Assert.IsNotNull(revisionQueue.Find("Revision Item 1/Revision Copy 1/Word"));
                Assert.IsNotNull(revisionQueue.Find("Revision Item 1/Revision Copy 1/Reason"));
                Assert.IsNotNull(revisionQueue.Find("Revision Item 1/Revision Evidence 1/Evidence"));

                var firstFill = GameObject.Find("Activity Fill 1").GetComponent<Image>();
                var emptyFill = GameObject.Find("Activity Fill 2").GetComponent<Image>();
                var currentFill = GameObject.Find("Activity Fill 7").GetComponent<Image>();
                Assert.AreEqual(0.25f, firstFill.rectTransform.anchorMax.y, 0.0001f);
                Assert.IsTrue(firstFill.enabled);
                Assert.AreEqual(0f, emptyFill.rectTransform.anchorMax.y, 0.0001f);
                Assert.IsFalse(emptyFill.enabled, "Zero-activity days must not render a visible fill.");
                Assert.AreEqual(1f, currentFill.rectTransform.anchorMax.y, 0.0001f);
                Assert.AreEqual("1", GameObject.Find("Activity Count 1").GetComponent<TMPro.TMP_Text>().text);
                Assert.AreEqual("0", GameObject.Find("Activity Count 2").GetComponent<TMPro.TMP_Text>().text);
                Assert.AreEqual("4", GameObject.Find("Activity Count 7").GetComponent<TMPro.TMP_Text>().text);
                Assert.AreEqual(
                    today.AddDays(-6).ToString("ddd"),
                    GameObject.Find("Activity Day Label 1").GetComponent<TMPro.TMP_Text>().text);
                Assert.AreEqual("5", GameObject.Find("Weekly Scans Value").GetComponent<TMPro.TMP_Text>().text);
                Assert.AreEqual("1", GameObject.Find("Weekly Quizzes Value").GetComponent<TMPro.TMP_Text>().text);
                Assert.AreEqual("0", GameObject.Find("Weekly Focused min Value").GetComponent<TMPro.TMP_Text>().text);
                Assert.AreEqual("50%", GameObject.Find("Weekly Quiz Accuracy Value").GetComponent<TMPro.TMP_Text>().text);
                Assert.AreEqual("TODAY", GameObject.Find("Recent Activity Period Label").GetComponent<TMPro.TMP_Text>().text);
                Assert.AreEqual("4", GameObject.Find("Today Scans Value").GetComponent<TMPro.TMP_Text>().text);
                Assert.AreEqual("0", GameObject.Find("Today Quizzes Value").GetComponent<TMPro.TMP_Text>().text);
                StringAssert.Contains(
                    "/",
                    GameObject.Find("Overall Explored Value").GetComponent<TMPro.TMP_Text>().text);
                Assert.IsNotNull(GameObject.Find("Overall Mastered Value"));
                StringAssert.Contains(
                    "1 correct • 1 to review",
                    GameObject.Find("Weekly Quiz Answer Detail").GetComponent<TMPro.TMP_Text>().text);
                StringAssert.Contains(
                    "object scans and completed quizzes",
                    GameObject.Find("Weekly Chart Caption").GetComponent<TMPro.TMP_Text>().text);
            }
            finally
            {
                LearningActivityTracker.Clear();
                LearningActivityTracker.SetLocalNowForTests(null);
                LearningActivityTracker.SetStorageKeyForTests(null);
            }
        }

        [Test]
        public void LearningActivityTracker_PersistsTypedDailyMetrics()
        {
            LearningActivityTracker.SetStorageKeyForTests("aiar.language-learning.tests.activity-retention");
            LearningActivityTracker.SetLocalNowForTests(new System.DateTime(2026, 7, 23, 12, 0, 0));
            LearningActivityTracker.Clear();
            try
            {
                LearningActivityTracker.RecordObjectScan();
                LearningActivityTracker.RecordQuizAnswer(true, 15);
                LearningActivityTracker.RecordQuizAnswer(false, 3);
                LearningActivityTracker.RecordQuizCompleted();
                LearningActivityTracker.RecordPronunciationPlayed();
                LearningActivityTracker.RecordLearningSeconds(125f);

                var snapshot = LearningActivityTracker.GetSnapshot();
                var today = snapshot.WeeklyDays[snapshot.WeeklyDays.Length - 1];

                Assert.AreEqual(1, today.ObjectScans);
                Assert.AreEqual(2, today.QuizAnswers);
                Assert.AreEqual(1, today.QuizCorrect);
                Assert.AreEqual(1, today.QuizzesCompleted);
                Assert.AreEqual(1, today.PronunciationPlays);
                Assert.AreEqual(125, today.LearningSeconds);
                Assert.AreEqual(2, today.LearningActionCount);
                Assert.AreEqual(2, snapshot.WeeklyLearningActions[snapshot.WeeklyLearningActions.Length - 1]);
                Assert.AreEqual(0.5d, snapshot.WeeklyQuizAccuracy, 0.0001d);
                Assert.AreEqual(1, snapshot.StreakDays);
            }
            finally
            {
                LearningActivityTracker.Clear();
                LearningActivityTracker.SetLocalNowForTests(null);
                LearningActivityTracker.SetStorageKeyForTests(null);
            }
        }

        [Test]
        public void LearningActivityTracker_AggregatesExactlySevenLocalCalendarDays()
        {
            const string key = "aiar.language-learning.tests.activity-seven-days";
            var today = new System.DateTime(2026, 7, 23, 12, 0, 0);
            LearningActivityTracker.SetStorageKeyForTests(key);
            LearningActivityTracker.SetLocalNowForTests(today.AddDays(-7));
            LearningActivityTracker.Clear();
            try
            {
                LearningActivityTracker.RecordObjectScan();

                LearningActivityTracker.SetLocalNowForTests(today.AddDays(-6));
                LearningActivityTracker.RecordObjectScan();
                LearningActivityTracker.RecordLearningSeconds(60f);

                LearningActivityTracker.SetLocalNowForTests(today.AddDays(-5));
                LearningActivityTracker.RecordObjectScan();
                LearningActivityTracker.RecordObjectScan();

                LearningActivityTracker.SetLocalNowForTests(today.AddDays(-4));
                LearningActivityTracker.RecordQuizAnswer(true, 15);
                LearningActivityTracker.RecordQuizAnswer(false, 3);

                LearningActivityTracker.SetLocalNowForTests(today.AddDays(-3));
                LearningActivityTracker.RecordQuizAnswer(true, 15);
                LearningActivityTracker.RecordQuizAnswer(false, 3);
                LearningActivityTracker.RecordQuizCompleted();

                LearningActivityTracker.SetLocalNowForTests(today.AddDays(-2));
                LearningActivityTracker.RecordPronunciationPlayed();

                LearningActivityTracker.SetLocalNowForTests(today);
                LearningActivityTracker.RecordObjectScan();
                LearningActivityTracker.RecordQuizAnswer(true, 15);
                LearningActivityTracker.RecordQuizCompleted();
                LearningActivityTracker.RecordLearningSeconds(65f);

                var snapshot = LearningActivityTracker.GetSnapshot();

                Assert.AreEqual(7, snapshot.WeeklyDays.Length);
                Assert.AreEqual(today.AddDays(-6).Date, snapshot.WeeklyDays[0].Date);
                Assert.AreEqual(today.Date, snapshot.WeeklyDays[6].Date);
                CollectionAssert.AreEqual(new[] { 1, 2, 0, 1, 0, 0, 2 }, snapshot.WeeklyLearningActions);
                Assert.AreEqual(4, snapshot.WeeklyObjectScans, "The scan from eight calendar dates ago must be excluded.");
                Assert.AreEqual(5, snapshot.WeeklyQuizAnswers);
                Assert.AreEqual(3, snapshot.WeeklyQuizCorrect);
                Assert.AreEqual(2, snapshot.WeeklyQuizzesCompleted);
                Assert.AreEqual(125, snapshot.WeeklyLearningSeconds);
                Assert.AreEqual(0.6d, snapshot.WeeklyQuizAccuracy, 0.0001d);
            }
            finally
            {
                LearningActivityTracker.Clear();
                LearningActivityTracker.SetLocalNowForTests(null);
                LearningActivityTracker.SetStorageKeyForTests(null);
            }
        }

        [Test]
        public void LearningActivityTracker_StreakAllowsInactiveTodayButStopsAtAGap()
        {
            const string key = "aiar.language-learning.tests.activity-streak";
            var today = new System.DateTime(2026, 7, 23, 12, 0, 0);
            LearningActivityTracker.SetStorageKeyForTests(key);
            LearningActivityTracker.SetLocalNowForTests(today.AddDays(-3));
            LearningActivityTracker.Clear();
            try
            {
                LearningActivityTracker.RecordObjectScan();
                LearningActivityTracker.SetLocalNowForTests(today.AddDays(-2));
                LearningActivityTracker.RecordQuizAnswer(false, 3);
                LearningActivityTracker.SetLocalNowForTests(today.AddDays(-1));
                LearningActivityTracker.RecordPronunciationPlayed();
                LearningActivityTracker.SetLocalNowForTests(today);

                Assert.AreEqual(3, LearningActivityTracker.GetSnapshot().StreakDays);

                LearningActivityTracker.RecordObjectScan();
                Assert.AreEqual(4, LearningActivityTracker.GetSnapshot().StreakDays);

                LearningActivityTracker.SetLocalNowForTests(today.AddDays(2));
                Assert.AreEqual(0, LearningActivityTracker.GetSnapshot().StreakDays);
            }
            finally
            {
                LearningActivityTracker.Clear();
                LearningActivityTracker.SetLocalNowForTests(null);
                LearningActivityTracker.SetStorageKeyForTests(null);
            }
        }

        [Test]
        public void LearningActivityTracker_LegacyActivityAffectsStreakButNotTypedChart()
        {
            const string key = "aiar.language-learning.tests.activity-legacy";
            var today = new System.DateTime(2026, 7, 23, 12, 0, 0);
            LearningActivityTracker.SetStorageKeyForTests(key);
            LearningActivityTracker.SetLocalNowForTests(today);
            LearningActivityTracker.Clear();
            try
            {
                PlayerPrefs.SetString(
                    key,
                    $"{{\"schemaVersion\":1,\"days\":[{{\"date\":\"{today:yyyyMMdd}\",\"xp\":7,\"activityUnits\":3}}]}}");
                PlayerPrefs.Save();

                var snapshot = LearningActivityTracker.GetSnapshot();

                Assert.AreEqual(7, snapshot.TodayXp);
                Assert.AreEqual(1, snapshot.StreakDays);
                Assert.AreEqual(3, snapshot.WeeklyLegacyActivityUnits);
                Assert.AreEqual(0, snapshot.WeeklyObjectScans);
                Assert.AreEqual(0, snapshot.WeeklyQuizzesCompleted);
                Assert.AreEqual(0, snapshot.WeeklyLearningActions[snapshot.WeeklyLearningActions.Length - 1]);
            }
            finally
            {
                LearningActivityTracker.Clear();
                LearningActivityTracker.SetLocalNowForTests(null);
                LearningActivityTracker.SetStorageKeyForTests(null);
            }
        }

        [Test]
        public void LearningSessionController_RecordsEachVocabularyOncePerScanSession()
        {
            EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            var session = Object.FindAnyObjectByType<LearningSessionController>();
            var method = typeof(LearningSessionController).GetMethod(
                "TryMarkObjectViewedThisScanSession",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var entry = new Domain.VocabularyEntry(
                "test-book",
                "book",
                "education",
                new[] { "book" },
                new[] { new Domain.VocabularyTranslation(Domain.LanguageCode.English, "book", "book", "en-US") });

            Assert.IsNotNull(session);
            Assert.IsNotNull(method);
            session.ResetObjectDisplayState();
            Assert.IsTrue((bool)method.Invoke(session, new object[] { entry }));
            Assert.IsFalse((bool)method.Invoke(session, new object[] { entry }));

            session.ResetObjectDisplayState();
            Assert.IsTrue((bool)method.Invoke(session, new object[] { entry }));
        }

        [Test]
        public void PlayerPrefsProgressRepository_RoundTripsRecallEvidence()
        {
            const string key = "aiar.language-learning.tests.progress-retention";
            var repository = new PlayerPrefsProgressRepository(key);
            repository.Clear();
            try
            {
                var now = new System.DateTime(2026, 6, 15, 8, 0, 0, System.DateTimeKind.Utc);
                var progress = new Domain.WordProgress(
                    "book",
                    4,
                    1,
                    3,
                    1,
                    now,
                    2,
                    2,
                    now,
                    now.AddDays(-1),
                    now.AddDays(-2));

                repository.Save(progress);
                var loaded = repository.Load("book");

                Assert.IsNotNull(loaded);
                Assert.AreEqual(2, loaded.CorrectRecallStreak);
                Assert.AreEqual(2, loaded.SpacedCorrectRecalls);
                Assert.AreEqual(now, loaded.LastQuizAtUtc);
                Assert.AreEqual(now.AddDays(-1), loaded.LastCorrectAtUtc);
                Assert.AreEqual(now.AddDays(-2), loaded.LastScannedAtUtc);
            }
            finally
            {
                repository.Clear();
            }
        }

        [Test]
        public void MainScene_ScanHasRealtimeBudgetAndLanguageSpeechControls()
        {
            EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);

            var mvp = Object.FindAnyObjectByType<LearningMvpController>();
            var session = Object.FindAnyObjectByType<LearningSessionController>();
            var recognizer = Object.FindAnyObjectByType<RealtimeCocoObjectDetector>();
            Assert.IsNotNull(mvp);
            Assert.IsNotNull(session);
            Assert.IsNotNull(recognizer);
            mvp.ShowHome();

            var scanControls = GameObject.Find("Scan Controls");
            var languageSelector = GameObject.Find("Scan Language Selector");
            var scanActions = GameObject.Find("Scan Actions");
            Assert.IsNotNull(scanControls, "Scan controls must be available before the first detection.");
            Assert.IsNotNull(languageSelector);
            Assert.IsNotNull(scanActions);
            Assert.AreEqual(
                System.Enum.GetValues(typeof(Domain.LanguageCode)).Length,
                languageSelector.GetComponentsInChildren<Button>(true).Length);

            var speakButton = System.Array.Find(
                scanActions.GetComponentsInChildren<Button>(true),
                button => button.name == "Speak Button");
            var backButton = System.Array.Find(
                scanActions.GetComponentsInChildren<Button>(true),
                button => button.name == "Back Button");
            Assert.IsNotNull(speakButton);
            Assert.IsNotNull(backButton);
            Assert.IsFalse(speakButton.interactable, "Speak must wait until a vocabulary object is detected.");

            var malayalamButton = System.Array.Find(
                languageSelector.GetComponentsInChildren<Button>(true),
                button => button.name == "ML Button");
            Assert.IsNotNull(malayalamButton);
            malayalamButton.onClick.Invoke();
            Assert.AreEqual(Domain.LanguageCode.Malayalam, session.LearningLanguage);

            foreach (Domain.LanguageCode language in System.Enum.GetValues(typeof(Domain.LanguageCode)))
            {
                var languageButtonName =
                    $"{Domain.LanguageCodeExtensions.ToIsoCode(language).ToUpperInvariant()} Button";
                var languageButton = System.Array.Find(
                    languageSelector.GetComponentsInChildren<Button>(true),
                    button => button.name == languageButtonName);
                Assert.IsNotNull(languageButton, $"The scan selector is missing {languageButtonName}.");
                languageButton.onClick.Invoke();
                Assert.AreEqual(
                    language,
                    session.LearningLanguage,
                    $"The scan selector did not select {language}.");
            }

            var serializedRecognizer = new SerializedObject(recognizer);
            Assert.LessOrEqual(
                serializedRecognizer.FindProperty("m_LayersPerFrame").intValue,
                32,
                "YOLO scheduling must be divided across rendered frames.");
            Assert.Greater(
                serializedRecognizer.FindProperty("m_LayersPerFrame").intValue,
                0);
            Assert.LessOrEqual(
                serializedRecognizer.FindProperty("m_MinimumInferenceInterval").floatValue,
                0.1f,
                "Completed inference should promptly accept a fresh camera frame.");
            Assert.IsFalse(
                serializedRecognizer.FindProperty("m_VerboseLogging").boolValue,
                "Per-frame detector logging must be disabled in the device build.");

            var serializedSession = new SerializedObject(session);
            Assert.AreEqual(
                1,
                serializedSession.FindProperty("m_MinimumStableFrames").intValue,
                "A valid first detection should be visible immediately.");
            Assert.GreaterOrEqual(
                serializedSession.FindProperty("m_DetectionHoldSeconds").floatValue,
                1f,
                "The overlay must survive the interval between mobile inference results.");
        }

        [Test]
        public void MainScene_BottomTitleUpdatesForEverySupportedLanguage()
        {
            EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);

            var session = Object.FindAnyObjectByType<LearningSessionController>();
            var repository = Object.FindAnyObjectByType<ResourcesVocabularyRepository>();
            var vocabularyPanel = GameObject.Find("Vocabulary Panel");
            var presenter = vocabularyPanel != null
                ? vocabularyPanel.GetComponent<ARVocabularyOverlayPresenter>()
                : null;
            var mvp = Object.FindAnyObjectByType<LearningMvpController>();
            Assert.IsNotNull(session);
            Assert.IsNotNull(repository);
            Assert.IsNotNull(presenter);
            Assert.IsNotNull(mvp);

            var presenterObject = new SerializedObject(presenter);
            Assert.AreEqual(
                session,
                presenterObject.FindProperty("m_SessionController").objectReferenceValue,
                "The bottom title must subscribe directly to the active learning session.");
            var translatedWordText = (TMPro.TMP_Text)presenterObject
                .FindProperty("m_TranslatedWordText")
                .objectReferenceValue;
            Assert.IsNotNull(translatedWordText);

            typeof(ARVocabularyOverlayPresenter)
                .GetMethod("SubscribeToSession", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.Invoke(presenter, null);

            Assert.IsTrue(repository.TryGetById("book", out var entry));
            var publishVocabulary = typeof(LearningSessionController).GetMethod(
                "PublishVocabulary",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(publishVocabulary);
            publishVocabulary.Invoke(
                session,
                new object[]
                {
                    entry,
                    "book",
                    0.9f,
                    NormalizedBoundingBox.Centered(0.5f, 0.5f),
                    false
                });

            foreach (Domain.LanguageCode language in System.Enum.GetValues(typeof(Domain.LanguageCode)))
            {
                mvp.SelectScanLanguage((int)language);
                var expected = entry.GetRequiredTranslation(language);

                Assert.AreEqual(
                    language,
                    session.LearningLanguage,
                    $"The scan selector did not select {language}.");
                Assert.AreEqual(
                    expected.Text,
                    translatedWordText.text,
                    $"The bottom title did not refresh for {language}.");
                Assert.IsFalse(
                    string.IsNullOrWhiteSpace(translatedWordText.text),
                    $"The bottom title is blank for {language}.");
                Assert.IsNotNull(translatedWordText.font, $"The bottom title has no font for {language}.");

                var expectedFontName = language switch
                {
                    Domain.LanguageCode.Hindi => "NotoSansDevanagari-Regular SDF",
                    Domain.LanguageCode.Malayalam => "NotoSansMalayalam-Regular SDF",
                    _ => "GoogleSans-Regular SDF"
                };
                Assert.AreEqual(expectedFontName, translatedWordText.font.name);

                translatedWordText.ForceMeshUpdate(true, true);
                Assert.Greater(
                    translatedWordText.textInfo.characterCount,
                    0,
                    $"TextMesh Pro generated no characters for {language}.");
                Assert.IsTrue(
                    System.Array.Exists(
                        translatedWordText.textInfo.characterInfo,
                        character => character.isVisible),
                    $"TextMesh Pro generated no visible glyphs for {language}.");
                Assert.AreEqual(
                    translatedWordText.font.atlasTexture,
                    translatedWordText.fontSharedMaterial.mainTexture,
                    $"The font atlas and material texture do not match for {language}.");
            }
        }

        [Test]
        public void ScanObjectDisplay_ResetClearsTransientPresentationWithoutChangingLanguage()
        {
            EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);

            var session = Object.FindAnyObjectByType<LearningSessionController>();
            var repository = Object.FindAnyObjectByType<ResourcesVocabularyRepository>();
            var presenter = Object.FindAnyObjectByType<ARVocabularyOverlayPresenter>();
            var mvp = Object.FindAnyObjectByType<LearningMvpController>();
            Assert.IsNotNull(session);
            Assert.IsNotNull(repository);
            Assert.IsNotNull(presenter);
            Assert.IsNotNull(mvp);

            typeof(ARVocabularyOverlayPresenter)
                .GetMethod("SubscribeToSession", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.Invoke(presenter, null);
            presenter.SetScanDisplayEnabled(true);

            Assert.IsTrue(repository.TryGetById("book", out var entry));
            var publishVocabulary = typeof(LearningSessionController).GetMethod(
                "PublishVocabulary",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(publishVocabulary);
            publishVocabulary.Invoke(
                session,
                new object[]
                {
                    entry,
                    "book",
                    0.9f,
                    NormalizedBoundingBox.Centered(0.5f, 0.5f),
                    false
                });

            Assert.IsNotNull(session.CurrentVocabularyEntry);
            var selectedLanguage = session.LearningLanguage;

            mvp.ShowScan();

            Assert.IsNull(session.CurrentVocabularyEntry);
            Assert.AreEqual(selectedLanguage, session.LearningLanguage);
            var presenterObject = new SerializedObject(presenter);
            foreach (var propertyName in new[]
                     {
                         "m_TranslatedWordText",
                         "m_SourceWordText",
                         "m_PhoneticHintText",
                         "m_LanguageText"
                     })
            {
                var text = (TMPro.TMP_Text)presenterObject.FindProperty(propertyName).objectReferenceValue;
                Assert.IsNotNull(text);
                Assert.IsEmpty(text.text, $"{propertyName} retained the previous object display.");
            }

            var canvasGroup = (CanvasGroup)presenterObject.FindProperty("m_CanvasGroup").objectReferenceValue;
            Assert.IsNotNull(canvasGroup);
            Assert.AreEqual(0f, canvasGroup.alpha);
        }

        [Test]
        public void NativeIndicText_UsesHarfBuzzGraphicOnlyForIndicScripts()
        {
            Assert.IsTrue(NativeIndicText.RequiresShaping("\u0915\u093F\u0924\u093E\u092C"));
            Assert.IsTrue(NativeIndicText.RequiresShaping("\u0D2A\u0D41\u0D38\u0D4D\u0D24\u0D15\u0D02"));
            Assert.IsTrue(NativeIndicText.RequiresShaping("Answer: \u0915\u0941\u0930\u094D\u0938\u0940"));
            Assert.IsFalse(NativeIndicText.RequiresShaping("book"));
            Assert.IsFalse(NativeIndicText.RequiresShaping("t\u00E9l\u00E9phone"));
            Assert.IsFalse(NativeIndicText.RequiresShaping(string.Empty));
            Assert.IsTrue(typeof(MaskableGraphic).IsAssignableFrom(NativeIndicText.NativeGraphicType));
        }

        [Test]
        public void NativeIndicText_HasGeneratedDevanagariAndMalayalamResources()
        {
            const string fontRoot = "Assets/_Project/LanguageLearning/Content/Resources/Fonts/UniText/";
            foreach (var assetName in new[]
                     {
                         "NotoSans-Regular.asset",
                         "NotoSansDevanagari-Regular.asset",
                         "NotoSansMalayalam-Regular.asset",
                         "IndicFontStack.asset",
                         "IndicAppearance.asset"
                     })
            {
                Assert.IsNotNull(
                    AssetDatabase.LoadMainAssetAtPath(fontRoot + assetName),
                    $"Missing generated native text asset: {assetName}");
            }

            Assert.IsNotNull(Resources.Load("Fonts/UniText/IndicFontStack"));
            Assert.IsNotNull(Resources.Load("Fonts/UniText/IndicAppearance"));
            Assert.IsTrue(
                NativeIndicText.UsesFaceOnlyAppearance(),
                "Indic text should use one face-only rendering pass without outline or underlay.");
        }

        [Test]
        public void NativeIndicText_RemainsInSourceHierarchyAndRestoresTmpForLatin()
        {
            var root = new GameObject("Native text test root", typeof(RectTransform));
            var sourceObject = new GameObject(
                "Answer label",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TMPro.TextMeshProUGUI));
            sourceObject.transform.SetParent(root.transform, false);

            try
            {
                var source = sourceObject.GetComponent<TMPro.TextMeshProUGUI>();
                source.text = "\u0D2A\u0D41\u0D38\u0D4D\u0D24\u0D15\u0D02";
                source.fontSize = 32f;
                source.alignment = TMPro.TextAlignmentOptions.Center;

                var adapter = sourceObject.AddComponent<NativeIndicTextAdapter>();
                Assert.DoesNotThrow(
                    () => adapter.Configure(source),
                    "The first Indic option must configure completely in a single pass.");

                var nativeChild = sourceObject.transform.Find("Native Indic Text");
                Assert.IsNotNull(nativeChild, "The shaped graphic was not created as a UGUI child.");
                Assert.AreSame(sourceObject.transform, nativeChild.parent);
                var nativeGraphic = (Graphic)nativeChild.GetComponent(NativeIndicText.NativeGraphicType);
                Assert.IsNotNull(nativeGraphic);
                Assert.IsFalse(nativeGraphic.raycastTarget, "The native label must not intercept button clicks.");
                Assert.IsFalse(nativeChild.GetComponent<CanvasGroup>().blocksRaycasts);
                Assert.IsNull(sourceObject.GetComponent<RectMask2D>());
                Assert.IsFalse(source.enabled, "TMP should be suppressed while the native graphic is active.");

                var nativeRect = (RectTransform)nativeChild;
                Assert.AreEqual(Vector2.zero, nativeRect.anchorMin);
                Assert.AreEqual(Vector2.one, nativeRect.anchorMax);
                Assert.AreEqual(Vector2.zero, nativeRect.offsetMin);
                Assert.AreEqual(Vector2.zero, nativeRect.offsetMax);

                source.enabled = true;
                adapter.Configure(source);
                Assert.IsFalse(source.enabled, "External TMP updates must not reveal old glyphs behind Indic text.");

                source.text = "book";
                adapter.Configure(source);

                Assert.IsTrue(source.enabled, "TMP should be restored when the label returns to Latin text.");
                Assert.IsFalse(nativeChild.gameObject.activeSelf);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void NativeIndicText_FirstIndicUpdateReplacesPlaceholderAndPreviousLatinText()
        {
            var root = new GameObject("Native text replacement root", typeof(RectTransform));
            var sourceObject = new GameObject(
                "Quiz option label",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TMPro.TextMeshProUGUI));
            sourceObject.transform.SetParent(root.transform, false);

            try
            {
                var source = sourceObject.GetComponent<TMPro.TextMeshProUGUI>();
                var adapter = sourceObject.AddComponent<NativeIndicTextAdapter>();
                source.text = "Quiz Option 1";
                adapter.Configure(source);

                const string malayalamOption = "\u0D2A\u0D41\u0D38\u0D4D\u0D24\u0D15\u0D02";
                source.text = malayalamOption;
                adapter.Configure(source);

                var nativeChild = sourceObject.transform.Find("Native Indic Text");
                Assert.IsNotNull(nativeChild);
                var nativeGraphic = nativeChild.GetComponent(NativeIndicText.NativeGraphicType);
                Assert.IsNotNull(nativeGraphic);
                var textProperty = nativeGraphic.GetType().GetProperty("Text");
                Assert.IsNotNull(textProperty);
                Assert.AreEqual(malayalamOption, textProperty?.GetValue(nativeGraphic) as string);
                Assert.IsFalse(source.enabled);

                source.text = "chaise";
                adapter.Configure(source);
                Assert.IsTrue(source.enabled);

                const string hindiOption = "\u0915\u0941\u0930\u094D\u0938\u0940";
                source.text = hindiOption;
                adapter.Configure(source);
                Assert.AreEqual(hindiOption, textProperty?.GetValue(nativeGraphic) as string);
                Assert.IsFalse(source.enabled);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void NativeIndicText_InactiveQuizLabelDefersShapingUntilActivated()
        {
            var root = new GameObject("Inactive quiz option root", typeof(RectTransform));
            var sourceObject = new GameObject(
                "Inactive quiz option label",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TMPro.TextMeshProUGUI));
            sourceObject.transform.SetParent(root.transform, false);
            sourceObject.SetActive(false);

            try
            {
                var source = sourceObject.GetComponent<TMPro.TextMeshProUGUI>();
                source.text = "\u0915\u093F\u0924\u093E\u092C";
                var adapter = sourceObject.AddComponent<NativeIndicTextAdapter>();

                Assert.DoesNotThrow(() => adapter.Configure(source));
                Assert.IsNull(
                    sourceObject.transform.Find("Native Indic Text"),
                    "Inactive labels should not initialize UniText before its OnEnable lifecycle.");

                sourceObject.SetActive(true);
                adapter.Configure(source);

                Assert.IsNotNull(sourceObject.transform.Find("Native Indic Text"));
                Assert.IsFalse(source.enabled);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void MainScene_DetectionBoxesUseOutlineOnlyGraphics()
        {
            EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);

            var session = Object.FindAnyObjectByType<LearningSessionController>();
            var overlay = Object.FindAnyObjectByType<PooledDetectionOverlay>(FindObjectsInactive.Include);
            Assert.IsNotNull(session);
            Assert.IsNotNull(overlay);

            var serializedOverlay = new SerializedObject(overlay);
            var boxRect = (RectTransform)serializedOverlay.FindProperty("m_BoxRectTransform").objectReferenceValue;
            var labelText = (TMPro.TMP_Text)serializedOverlay.FindProperty("m_LabelText").objectReferenceValue;
            var canvasGroup = (CanvasGroup)serializedOverlay.FindProperty("m_CanvasGroup").objectReferenceValue;
            overlay.Configure(session, boxRect, labelText, canvasGroup);

            var background = boxRect.GetComponent<Image>();
            Assert.IsNotNull(background);
            Assert.IsFalse(background.enabled, "The detection box background graphic must stay disabled.");
            Assert.AreEqual(0f, background.color.a, 0.0001f, "The detection box interior must be transparent.");

            var legacyOutline = boxRect.GetComponent<Outline>();
            Assert.IsTrue(
                legacyOutline == null || !legacyOutline.enabled,
                "The filled UI Outline effect must not render the detection box.");

            var border = boxRect.Find("Detection Border");
            Assert.IsNotNull(border, "The four-edge detection border was not created.");
            Assert.AreEqual(4, border.childCount);
            foreach (var edgeName in new[] { "Top", "Bottom", "Left", "Right" })
            {
                var edge = border.Find(edgeName);
                Assert.IsNotNull(edge, $"The {edgeName} border edge is missing.");
                var edgeImage = edge.GetComponent<Image>();
                Assert.IsNotNull(edgeImage);
                Assert.Greater(edgeImage.color.a, 0.99f);
                Assert.IsFalse(edgeImage.raycastTarget);
            }
        }

        [Test]
        public void MainScene_HasNoMissingMonoBehaviourScripts()
        {
            var scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    Assert.Zero(
                        GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject),
                        $"Missing script found on {GetHierarchyPath(transform)}.");
                }
            }
        }

        [Test]
        public void MainScene_DefersArCameraUntilScanAndAutoLoadsAndroidProvider()
        {
            EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);

            var arSession = FindSceneBehaviour("UnityEngine.XR.ARFoundation.ARSession");
            var cameraManager = FindSceneBehaviour("UnityEngine.XR.ARFoundation.ARCameraManager");
            var cameraBackground = FindSceneBehaviour("UnityEngine.XR.ARFoundation.ARCameraBackground");
            Assert.IsNotNull(arSession);
            Assert.IsNotNull(cameraManager);
            Assert.IsNotNull(cameraBackground);
            Assert.IsFalse(arSession.enabled, "ARSession must remain off on the main menu.");
            Assert.IsFalse(cameraManager.enabled, "The camera must remain off until Scan is selected.");
            Assert.IsFalse(cameraBackground.enabled, "The camera background must remain off until Scan is selected.");

            var xrSettingsAssets = AssetDatabase.LoadAllAssetsAtPath("Assets/XR/XRGeneralSettings.asset");
            const string providerName = "Android Providers";
            var provider = System.Array.Find(xrSettingsAssets, asset => asset != null && asset.name == providerName);
            Assert.IsNotNull(provider, $"{providerName} settings are missing.");
            var serializedProvider = new SerializedObject(provider);
            Assert.IsTrue(
                serializedProvider.FindProperty("m_AutomaticLoading").boolValue,
                $"{providerName} must initialize its XR loader automatically.");
            Assert.IsTrue(
                serializedProvider.FindProperty("m_AutomaticRunning").boolValue,
                $"{providerName} must start its XR loader automatically.");
        }

        static Behaviour FindSceneBehaviour(string fullTypeName)
        {
            foreach (var behaviour in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            {
                if (behaviour != null &&
                    behaviour.gameObject.scene.IsValid() &&
                    behaviour.GetType().FullName == fullTypeName)
                {
                    return behaviour;
                }
            }

            return null;
        }

        static string GetHierarchyPath(Transform transform)
        {
            var path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = $"{transform.name}/{path}";
            }

            return path;
        }

        [Test]
        public void VocabularyCatalog_CoversEveryCocoClassForDetectionAndQuiz()
        {
            EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
            var repository = Object.FindAnyObjectByType<ResourcesVocabularyRepository>();
            Assert.IsNotNull(repository);

            var entries = repository.GetAll();
            var labelAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(LabelsPath);
            var cocoLabels = Yolo11Contract.ParseAndValidateLabels(labelAsset);
            var normalizer = new ObjectLabelNormalizer();
            var quizService = new QuizService();

            Assert.AreEqual(80, cocoLabels.Length);
            Assert.GreaterOrEqual(entries.Count, cocoLabels.Length);
            foreach (var cocoLabel in cocoLabels)
            {
                var exactEntries = entries
                    .Where(entry => string.Equals(
                        entry.CanonicalObjectLabel,
                        cocoLabel,
                        System.StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                Assert.AreEqual(1, exactEntries.Length, $"COCO class '{cocoLabel}' needs one canonical vocabulary entry.");

                var entry = exactEntries[0];
                Assert.IsTrue(normalizer.TryFindBestMatch(cocoLabel, entries, out var match));
                Assert.AreSame(entry, match, $"Detection label '{cocoLabel}' resolved to the wrong vocabulary entry.");

                foreach (Domain.LanguageCode language in System.Enum.GetValues(typeof(Domain.LanguageCode)))
                {
                    Assert.IsTrue(entry.TryGetTranslation(language, out var translation),
                        $"{entry.Id} is missing {language}.");
                    Assert.IsFalse(string.IsNullOrWhiteSpace(translation.Text),
                        $"{entry.Id} has an empty {language} translation.");
                    Assert.IsFalse(string.IsNullOrWhiteSpace(translation.PhoneticHint),
                        $"{entry.Id} has an empty {language} phonetic hint.");
                    Assert.IsFalse(string.IsNullOrWhiteSpace(translation.PronunciationLocale),
                        $"{entry.Id} has an empty {language} pronunciation locale.");

                    var question = quizService.CreateTranslationQuestion(
                        entry,
                        language,
                        entries,
                        optionCount: 4,
                        seed: 1000 + System.Array.IndexOf(cocoLabels, cocoLabel));
                    Assert.AreEqual(entry.Id, question.TargetVocabularyId);
                    Assert.AreEqual(translation.Text, question.Options[question.CorrectOptionIndex].Text);
                }
            }

            var languages = (Domain.LanguageCode[])System.Enum.GetValues(typeof(Domain.LanguageCode));
            Assert.AreEqual(languages.Length, LocalizedLabelsPaths.Length);
            for (var languageIndex = 0; languageIndex < languages.Length; languageIndex++)
            {
                var localizedAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(LocalizedLabelsPaths[languageIndex]);
                Assert.IsNotNull(localizedAsset, $"Missing COCO localization file for {languages[languageIndex]}.");
                var localizedLabels = localizedAsset.text
                    .Split(new[] { '\r', '\n' }, System.StringSplitOptions.RemoveEmptyEntries)
                    .Select(label => label.Trim())
                    .ToArray();
                Assert.AreEqual(cocoLabels.Length, localizedLabels.Length,
                    $"The {languages[languageIndex]} COCO localization file is not aligned with coco80.txt.");

                for (var classIndex = 0; classIndex < cocoLabels.Length; classIndex++)
                {
                    var entry = entries.Single(item => string.Equals(
                        item.CanonicalObjectLabel,
                        cocoLabels[classIndex],
                        System.StringComparison.OrdinalIgnoreCase));
                    Assert.AreEqual(
                        entry.GetRequiredTranslation(languages[languageIndex]).Text,
                        localizedLabels[classIndex],
                        $"Localized COCO class {classIndex} is out of sync for {languages[languageIndex]}.");
                }
            }
        }

        [Test]
        public void YoloModel_ImportsWithExpectedInputAndOutputContract()
        {
            var modelAsset = AssetDatabase.LoadAssetAtPath<ModelAsset>(ModelPath);
            Assert.IsNotNull(modelAsset, "The MVP YOLO model did not import as a Unity ModelAsset.");

            var model = ModelLoader.Load(modelAsset);

            Assert.AreEqual(1, model.inputs.Count, "The detector must have one image input.");
            Assert.IsTrue(model.inputs[0].shape.IsStatic());
            Assert.AreEqual(
                new TensorShape(1, 3, Yolo11Contract.InputHeight, Yolo11Contract.InputWidth),
                model.inputs[0].shape.ToTensorShape());
            Assert.AreEqual(DataType.Float, model.inputs[0].dataType);
            Assert.AreEqual(1, model.outputs.Count, "The detector must expose one raw prediction output.");

            var labels = AssetDatabase.LoadAssetAtPath<TextAsset>(LabelsPath);
            Assert.AreEqual(Yolo11Contract.ClassCount, Yolo11Contract.ParseAndValidateLabels(labels).Length);
        }

        [Test]
        public void YoloModel_CpuSmokeInferenceProducesVerifiedRuntimeShape()
        {
            var modelAsset = AssetDatabase.LoadAssetAtPath<ModelAsset>(ModelPath);
            Assert.IsNotNull(modelAsset);

            var model = ModelLoader.Load(modelAsset);
            using var worker = new Worker(model, BackendType.CPU);
            using var input = new Tensor<float>(
                new TensorShape(1, 3, Yolo11Contract.InputHeight, Yolo11Contract.InputWidth));

            worker.Schedule(input);
            var output = worker.PeekOutput() as Tensor<float>;
            Assert.IsNotNull(output);
            using var readable = output.ReadbackAndClone();
            Assert.AreEqual(
                new TensorShape(1, Yolo11Contract.OutputChannels, Yolo11Contract.PredictionCount),
                readable.shape);
        }

        [Test]
        public void RuntimeFont_ProvidesLatinDevanagariAndMalayalamAtlases()
        {
            var font = ARVocabularyOverlayPresenter.GetMultilingualFontAsset();

            Assert.IsNotNull(font);
            Assert.IsNotNull(font.atlasTextures);
            Assert.Greater(font.atlasTextures.Length, 0);
            Assert.IsNotNull(font.atlasTextures[0]);
            Assert.GreaterOrEqual(font.fallbackFontAssetTable.Count, 2);
            foreach (var fallback in font.fallbackFontAssetTable)
            {
                Assert.IsNotNull(fallback);
                Assert.IsNotNull(fallback.atlasTextures);
                Assert.Greater(fallback.atlasTextures.Length, 0);
                Assert.IsNotNull(fallback.atlasTextures[0]);
            }
        }

        [Test]
        public void AllTmpFontAssets_HaveAValidAtlasReference()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:TMP_FontAsset"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(path);
                if (font == null)
                    continue;

                Assert.IsNotNull(font.atlasTextures, $"{path} has no atlas collection.");
                Assert.Greater(font.atlasTextures.Length, 0, $"{path} has an empty atlas collection.");
                Assert.IsNotNull(
                    font.atlasTextures[0],
                    $"{path} contains a null atlas reference and will break TextMesh Pro's build preprocessor.");
            }
        }
    }
}
