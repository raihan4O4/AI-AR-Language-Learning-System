using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AIAR.LanguageLearning.Application.Models;
using AIAR.LanguageLearning.Application.Ports;
using AIAR.LanguageLearning.Domain;
using AIAR.LanguageLearning.Unity.Bootstrap;
using TMPro;
using Unity.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace AIAR.LanguageLearning.Unity.Presentation
{
    /// <summary>
    /// Owns the learner-facing MVP flow. It intentionally builds the small dashboard at runtime so
    /// an invalid or stale scene serialization cannot make the learning, quiz, or progress flows
    /// inaccessible.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LearningMvpController : MonoBehaviour
    {
        enum ExperienceScreen
        {
            Home,
            Scan,
            Quiz,
            Progress
        }

        const int QuizOptionCount = 4;
        const string TotalLearningSecondsKey = "aiar.language-learning.focused-seconds.v2";

        static readonly LanguageCode[] QuizLanguages =
        {
            LanguageCode.Hindi,
            LanguageCode.Malayalam,
            LanguageCode.French
        };

        [SerializeField]
        LearningSessionController m_SessionController;

        [SerializeField]
        LanguageLearningCompositionRoot m_CompositionRoot;

        [SerializeField, Min(1)]
        int m_QuizLength = 5;

        Canvas m_Canvas;
        CanvasGroup m_HomeCanvasGroup;
        CanvasGroup m_ScanCanvasGroup;
        CanvasGroup m_VocabularyCanvasGroup;
        ARVocabularyOverlayPresenter m_VocabularyPresenter;
        CanvasGroup m_QuizCanvasGroup;
        CanvasGroup m_ProgressCanvasGroup;
        TMP_Text m_StatusText;
        TMP_Text m_QuizCounterText;
        TMP_Text m_QuizQuestionCaptionText;
        TMP_Text m_QuizPromptText;
        TMP_Text m_QuizFeedbackText;
        TMP_Text m_TodayScansValueText;
        TMP_Text m_TodayQuizzesValueText;
        TMP_Text m_ExploredWordsValueText;
        TMP_Text m_MasteredWordsValueText;
        TMP_Text m_WeeklyScansValueText;
        TMP_Text m_WeeklyQuizzesValueText;
        TMP_Text m_WeeklyFocusedMinutesValueText;
        TMP_Text m_WeeklyQuizAccuracyText;
        TMP_Text m_WeeklyQuizAnswerDetailText;
        TMP_Text m_WeeklyChartCaptionText;
        Image m_WeeklyQuizAccuracyBadge;
        GameObject m_RevisionCard;
        GameObject m_RevisionQueueContainer;
        GameObject m_RevisionEmptyState;
        GameObject[] m_RevisionItems = Array.Empty<GameObject>();
        TMP_Text[] m_RevisionWordTexts = Array.Empty<TMP_Text>();
        TMP_Text[] m_RevisionReasonTexts = Array.Empty<TMP_Text>();
        TMP_Text[] m_RevisionEvidenceTexts = Array.Empty<TMP_Text>();
        Image[] m_RevisionEvidenceBadges = Array.Empty<Image>();
        Button[] m_QuizOptionButtons = Array.Empty<Button>();
        TMP_Text[] m_QuizOptionLabels = Array.Empty<TMP_Text>();
        Button[] m_QuizLanguageButtons = Array.Empty<Button>();
        Button[] m_ScanLanguageButtons = Array.Empty<Button>();
        Button m_ScanSpeakButton;
        Button m_NextQuestionButton;
        TMP_Text m_NextQuestionButtonLabel;
        QuizQuestion m_CurrentQuestion;
        VocabularyEntry m_CurrentQuizEntry;
        IReadOnlyList<VocabularyEntry> m_QuizEntries = Array.Empty<VocabularyEntry>();
        ExperienceScreen m_CurrentScreen;
        int m_QuestionsAnswered;
        int m_CorrectAnswers;
        bool m_QuestionAnswered;
        bool m_Built;
        IObjectRecognitionService m_SubscribedRecognitionService;
        Coroutine m_ScanStartupCoroutine;
        float m_SessionSegmentStartedAt;
        float m_NextSessionSaveAt;
        RectTransform m_SafeAreaRoot;
        Image m_AppBackground;
        CanvasGroup m_StatusCanvasGroup;
        CanvasGroup m_ScanLanguageCanvasGroup;
        RectTransform m_QuizQuestionCard;
        RectTransform m_XpBurstRect;
        TMP_Text m_XpBurstText;
        TMP_Text m_HomeTodayXpText;
        TMP_Text m_HomeStreakText;
        TMP_Text m_HomeLevelText;
        TMP_Text m_QuizXpText;
        LearningPillGraphic m_QuizProgressFill;
        TMP_Text m_LevelText;
        TMP_Text m_XpProgressText;
        LearningPillGraphic m_LevelProgressFill;
        TMP_Text m_VocabularyMasteryText;
        TMP_Text m_ObjectsScannedText;
        TMP_Text m_QuizAccuracyValueText;
        TMP_Text m_RetentionScoreText;
        TMP_Text m_RevisionCountText;
        TMP_Text m_StreakValueText;
        TMP_Text m_LearningTimeText;
        Image[] m_WeeklyBars = Array.Empty<Image>();
        TMP_Text[] m_WeeklyCountLabels = Array.Empty<TMP_Text>();
        TMP_Text[] m_WeeklyDayLabels = Array.Empty<TMP_Text>();
        ScrollRect m_HomeScrollRect;
        ScrollRect m_QuizScrollRect;
        ScrollRect m_ProgressScrollRect;
        string m_LastTrackedScanVocabularyId;

        public string CurrentScreenName => m_CurrentScreen.ToString();

        void Awake()
        {
            LearningPortraitBootstrap.Apply();
            ResolveDependencies();
            EnsureUi();
            m_SessionSegmentStartedAt = -1f;
            m_NextSessionSaveAt = Time.realtimeSinceStartup + 30f;
        }

        void OnEnable()
        {
            ResolveDependencies();
            if (m_SessionController != null)
                m_SessionController.RecognitionsPresented += OnRecognitionsPresented;

            SubscribeToRecognitionStatus();
        }

        void Start()
        {
            ShowHome();
        }

        void OnDisable()
        {
            if (m_SessionController != null)
                m_SessionController.RecognitionsPresented -= OnRecognitionsPresented;

            if (m_SubscribedRecognitionService != null)
                m_SubscribedRecognitionService.StatusChanged -= OnRecognitionStatusChanged;
            m_SubscribedRecognitionService = null;

            DeactivateScanRuntime();
            SaveSessionDuration(false);
        }

        void Update()
        {
            if (m_SessionSegmentStartedAt < 0f)
                return;

            if (Time.realtimeSinceStartup < m_NextSessionSaveAt)
                return;

            SaveSessionDuration(true);
            m_NextSessionSaveAt = Time.realtimeSinceStartup + 30f;
        }

        void OnApplicationPause(bool paused)
        {
            if (paused)
                SaveSessionDuration(false);
            else if (IsLearningScreen(m_CurrentScreen))
                m_SessionSegmentStartedAt = Time.realtimeSinceStartup;
        }

        public void Configure(
            LearningSessionController sessionController,
            LanguageLearningCompositionRoot compositionRoot)
        {
            m_SessionController = sessionController;
            m_CompositionRoot = compositionRoot;
            ResolveDependencies();
            EnsureUi();
        }

        public void ShowScan()
        {
            EnsureUi();
            if (!ResolveDependencies())
                return;

            TransitionToScreen(ExperienceScreen.Scan);
            m_SessionController.ResetObjectDisplayState();
            SetAppBackgroundVisible(false);
            SetVisible(m_HomeCanvasGroup, false);
            SetVisible(m_QuizCanvasGroup, false);
            SetVisible(m_ProgressCanvasGroup, false);
            SetVisible(m_ScanCanvasGroup, true);
            m_VocabularyPresenter?.SetScanDisplayEnabled(true);
            m_VocabularyPresenter?.ResetObjectDisplay();
            SetVisible(m_VocabularyCanvasGroup, false);
            SetVisible(m_StatusCanvasGroup, false);
            SetScanSpeakAvailable(false);
            RefreshLanguageButtonStyles();
            SetStatus("Point your camera at an everyday object");
            AnimateScreenIn(m_ScanCanvasGroup, 28f);

            if (!UnityEngine.Application.isPlaying)
                return;

            if (m_ScanStartupCoroutine != null)
                StopCoroutine(m_ScanStartupCoroutine);
            m_ScanStartupCoroutine = StartCoroutine(ActivateScanRuntime());
        }

        public void ShowHome()
        {
            EnsureUi();
            TransitionToScreen(ExperienceScreen.Home);
            DeactivateScanRuntime();
            SetAppBackgroundVisible(true);
            SetVisible(m_HomeCanvasGroup, true);
            SetVisible(m_ScanCanvasGroup, false);
            SetVisible(m_QuizCanvasGroup, false);
            SetVisible(m_ProgressCanvasGroup, false);
            m_VocabularyPresenter?.SetScanDisplayEnabled(false);
            SetVisible(m_StatusCanvasGroup, false);
            RefreshHomeSummary();
            ResetScrollToTop(m_HomeScrollRect);
            AnimateScreenIn(m_HomeCanvasGroup, 22f);
        }

        public void ShowQuiz()
        {
            EnsureUi();
            if (!ResolveDependencies())
                return;

            TransitionToScreen(ExperienceScreen.Quiz);
            DeactivateScanRuntime();
            m_SessionController.PauseRecognition();
            SetAppBackgroundVisible(true);
            SetVisible(m_HomeCanvasGroup, false);
            SetVisible(m_ScanCanvasGroup, false);
            m_VocabularyPresenter?.SetScanDisplayEnabled(false);
            SetVisible(m_ProgressCanvasGroup, false);
            SetVisible(m_StatusCanvasGroup, false);
            SetVisible(m_QuizCanvasGroup, true);
            StartQuiz();
            ResetScrollToTop(m_QuizScrollRect);
            AnimateScreenIn(m_QuizCanvasGroup, 20f);
        }

        public void ShowProgress()
        {
            EnsureUi();
            if (!ResolveDependencies())
                return;

            TransitionToScreen(ExperienceScreen.Progress);
            DeactivateScanRuntime();
            m_SessionController.PauseRecognition();
            SetAppBackgroundVisible(true);
            SetVisible(m_HomeCanvasGroup, false);
            SetVisible(m_ScanCanvasGroup, false);
            m_VocabularyPresenter?.SetScanDisplayEnabled(false);
            SetVisible(m_QuizCanvasGroup, false);
            SetVisible(m_StatusCanvasGroup, false);
            SetVisible(m_ProgressCanvasGroup, true);
            RefreshProgress();
            ResetScrollToTop(m_ProgressScrollRect);
            AnimateScreenIn(m_ProgressCanvasGroup, 20f);
        }

        public void AnswerQuizOption(int selectedOptionIndex)
        {
            if (m_CurrentQuestion == null ||
                m_CurrentQuizEntry == null ||
                m_QuestionAnswered ||
                selectedOptionIndex < 0 ||
                selectedOptionIndex >= m_CurrentQuestion.Options.Count)
            {
                return;
            }

            m_QuestionAnswered = true;
            var wasCorrect = m_CurrentQuestion.IsCorrect(selectedOptionIndex);
            m_CompositionRoot.LearningService.RecordQuizAnswer(
                m_CurrentQuizEntry,
                m_CurrentQuestion.Language,
                wasCorrect);

            m_QuestionsAnswered++;
            if (wasCorrect)
                m_CorrectAnswers++;

            var earnedXp = wasCorrect ? 15 : 3;
            if (UnityEngine.Application.isPlaying)
                LearningActivityTracker.RecordQuizAnswer(wasCorrect, earnedXp);

            for (var index = 0; index < m_QuizOptionButtons.Length; index++)
            {
                var button = m_QuizOptionButtons[index];
                if (index == m_CurrentQuestion.CorrectOptionIndex)
                    SetQuizButtonState(button, LearningUiTheme.Success, Color.white);
                else if (index == selectedOptionIndex)
                    SetQuizButtonState(button, LearningUiTheme.Error, Color.white);
                else
                    SetQuizButtonState(button, LearningUiTheme.Card, LearningUiTheme.Muted);

                button.interactable = false;
            }

            var selectedButton = m_QuizOptionButtons[selectedOptionIndex];
            if (UnityEngine.Application.isPlaying)
                StartCoroutine(AnimateQuizResult(selectedButton.transform as RectTransform, wasCorrect));
            ShowXpBurst(earnedXp);
            AnimateFill(m_QuizProgressFill, m_QuestionsAnswered / (float)m_QuizLength);

            m_QuizFeedbackText.color = wasCorrect ? LearningUiTheme.Success : LearningUiTheme.Error;
            m_QuizFeedbackText.text = wasCorrect
                ? "Excellent! You remembered it."
                : $"Almost! The answer is {m_CurrentQuestion.Options[m_CurrentQuestion.CorrectOptionIndex].Text}.";
            m_QuizCounterText.text = $"Question {m_QuestionsAnswered} of {m_QuizLength}";
            if (m_QuizXpText != null)
                m_QuizXpText.text = $"{(m_CorrectAnswers * 15) + ((m_QuestionsAnswered - m_CorrectAnswers) * 3)} XP";

            if (m_QuestionsAnswered >= m_QuizLength)
            {
                if (UnityEngine.Application.isPlaying)
                    LearningActivityTracker.RecordQuizCompleted();
                m_QuizFeedbackText.text += $"  Final score: {m_CorrectAnswers}/{m_QuestionsAnswered}.";
                SetNextQuestionButton(true, "Finish");
                return;
            }

            SetNextQuestionButton(true, "Next question");
        }

        public void RestartQuiz()
        {
            StartQuiz();
        }

        public void NextQuizQuestion()
        {
            if (!m_QuestionAnswered)
                return;

            if (m_QuestionsAnswered >= m_QuizLength)
            {
                ShowHome();
                return;
            }

            ShowNextQuestion();
        }

        public void SelectQuizLanguage(int languageIndex)
        {
            if (!Enum.IsDefined(typeof(LanguageCode), languageIndex) || m_SessionController == null)
                return;

            m_SessionController.SetLearningLanguage(languageIndex);
            RefreshLanguageButtonStyles();
            if (m_CurrentScreen == ExperienceScreen.Quiz)
                StartQuiz();
        }

        public void SelectScanLanguage(int languageIndex)
        {
            if (!Enum.IsDefined(typeof(LanguageCode), languageIndex) || m_SessionController == null)
                return;

            m_SessionController.SetLearningLanguage(languageIndex);
            RefreshLanguageButtonStyles();
            SetVisible(m_ScanLanguageCanvasGroup, false);
            SetStatus($"Learning in {m_SessionController.LearningLanguage.ToDisplayName()}");
        }

        public void SpeakCurrentScanWord()
        {
            if (m_SessionController?.CurrentVocabularyEntry == null)
                return;

            m_SessionController.SpeakCurrentWord();
            SetStatus("Pronunciation played");
        }

        public void CaptureScan()
        {
            if (m_CurrentScreen != ExperienceScreen.Scan)
                return;

            m_SessionController?.ResumeRecognition();
            SetStatus("Scanning for a clear object…");
        }

        public void ToggleScanLanguageSelector()
        {
            if (m_ScanLanguageCanvasGroup == null)
                return;

            var show = m_ScanLanguageCanvasGroup.alpha < 0.5f;
            SetVisible(m_ScanLanguageCanvasGroup, show);
            if (show)
                AnimateScreenIn(m_ScanLanguageCanvasGroup, 14f);
        }

        public void SaveCurrentScanWord()
        {
            var entry = m_SessionController?.CurrentVocabularyEntry;
            if (entry == null)
            {
                SetStatus("Scan an object before saving");
                return;
            }

            SetStatus($"{entry.CanonicalObjectLabel} is already saved to your progress");
            RefreshHomeSummary();
        }

        public void OpenScanSettings()
        {
            ToggleScanLanguageSelector();
            SetStatus("Choose your learning language");
        }

        void StartQuiz()
        {
            m_QuestionsAnswered = 0;
            m_CorrectAnswers = 0;
            m_QuestionAnswered = false;
            m_CurrentQuestion = null;
            m_CurrentQuizEntry = null;
            ClearQuizOptionDisplay();
            m_QuizFeedbackText.text = string.Empty;
            m_QuizFeedbackText.color = LearningUiTheme.Muted;
            SetRoundedFillAmount(m_QuizProgressFill, 0f);
            if (m_QuizXpText != null)
                m_QuizXpText.text = "0 XP";
            SetNextQuestionButton(false, "Next question");

            var catalog = m_CompositionRoot?.VocabularyRepository?.GetAll();
            if (catalog == null || catalog.Count < 2)
            {
                ShowQuizConfigurationError("At least two vocabulary entries are required to start a quiz.");
                return;
            }

            try
            {
                m_QuizEntries = m_CompositionRoot.QuizService.CreateAdaptiveQuestionSequence(
                    catalog,
                    m_CompositionRoot.ProgressRepository.LoadAll(),
                    m_SessionController.LearningLanguage,
                    m_QuizLength,
                    DateTime.UtcNow,
                    Environment.TickCount);
            }
            catch (Exception exception)
            {
                ShowQuizConfigurationError(exception.Message);
                return;
            }

            RefreshLanguageButtonStyles();
            ShowNextQuestion();
        }

        void ShowNextQuestion()
        {
            if (m_CurrentScreen != ExperienceScreen.Quiz || !ResolveDependencies())
                return;

            var catalog = m_CompositionRoot.VocabularyRepository.GetAll();
            if (catalog == null ||
                m_QuizEntries == null ||
                m_QuizEntries.Count <= m_QuestionsAnswered)
            {
                ShowQuizConfigurationError("The quiz question sequence is unavailable. Restart the quiz.");
                return;
            }

            m_CurrentQuizEntry = m_QuizEntries[m_QuestionsAnswered];
            m_CurrentQuestion = m_CompositionRoot.QuizService.CreateTranslationQuestion(
                m_CurrentQuizEntry,
                m_SessionController.LearningLanguage,
                catalog,
                Math.Min(QuizOptionCount, catalog.Count),
                Environment.TickCount ^ m_QuestionsAnswered);
            m_QuestionAnswered = false;

            m_QuizCounterText.text = $"Question {m_QuestionsAnswered + 1} of {m_QuizLength}";
            m_QuizQuestionCaptionText.text =
                $"TRANSLATE THIS {m_CurrentQuestion.Language.ToDisplayName().ToUpperInvariant()} WORD";
            m_QuizPromptText.text = m_CurrentQuizEntry.CanonicalObjectLabel.ToUpperInvariant();
            m_QuizFeedbackText.text = "Choose the best translation.";
            m_QuizFeedbackText.color = LearningUiTheme.Muted;
            SetNextQuestionButton(false, "Next question");

            for (var index = 0; index < m_QuizOptionButtons.Length; index++)
            {
                var hasOption = index < m_CurrentQuestion.Options.Count;
                var button = m_QuizOptionButtons[index];
                if (!hasOption)
                {
                    NativeIndicText.SetText(m_QuizOptionLabels[index], string.Empty);
                    button.interactable = false;
                    button.gameObject.SetActive(false);
                    continue;
                }

                button.gameObject.SetActive(true);
                button.interactable = false;
                SetQuizButtonState(button, LearningUiTheme.Card, LearningUiTheme.Ink);
                NativeIndicText.SetText(m_QuizOptionLabels[index], m_CurrentQuestion.Options[index].Text);
                button.interactable = true;
            }

            // UniText rebuilds through Canvas render callbacks. Force that pass before this newly
            // activated question can be drawn, so no cleared/previous option survives one frame.
            if (UnityEngine.Application.isPlaying &&
                m_CurrentQuestion.Options.Any(option => NativeIndicText.RequiresShaping(option.Text)))
            {
                Canvas.ForceUpdateCanvases();
            }

            if (UnityEngine.Application.isPlaying && m_QuizQuestionCard != null)
                StartCoroutine(AnimatePopIn(m_QuizQuestionCard, 0.96f, 0.22f));
        }

        void ShowQuizConfigurationError(string message)
        {
            if (m_QuizQuestionCaptionText != null)
                m_QuizQuestionCaptionText.text = "QUIZ UNAVAILABLE";
            m_QuizPromptText.text = message;
            m_QuizFeedbackText.text = "Choose another language or return to the main menu.";
            m_QuizEntries = Array.Empty<VocabularyEntry>();
            SetQuizOptionsVisible(false);
            SetNextQuestionButton(false, "Next Question");
        }

        void ClearQuizOptionDisplay()
        {
            for (var index = 0; index < m_QuizOptionButtons.Length; index++)
            {
                NativeIndicText.SetText(m_QuizOptionLabels[index], string.Empty);
                m_QuizOptionButtons[index].interactable = false;
                m_QuizOptionButtons[index].gameObject.SetActive(false);
            }
        }

        void RefreshProgress()
        {
            var catalog = m_CompositionRoot.VocabularyRepository.GetAll();
            var progress = m_CompositionRoot.ProgressRepository.LoadAll();
            var nowUtc = DateTime.UtcNow;
            var summary = m_CompositionRoot.RetentionAnalysisService.CreateSummary(
                catalog,
                progress,
                nowUtc: nowUtc);
            var objectScans = progress.Sum(item => item.TimesSeen);
            var totalXp = CalculateTotalXp(progress);
            var level = Mathf.Max(1, (totalXp / 250) + 1);
            var xpInsideLevel = totalXp % 250;
            var totalLearningTime = TimeSpan.FromSeconds(GetTotalLearningSeconds());
            var activity = LearningActivityTracker.GetSnapshot();

            SetMetricText(m_LevelText, $"Level {level}", Color.white);
            SetMetricText(m_XpProgressText, $"{xpInsideLevel} / 250 XP", Color.white);
            AnimateFill(m_LevelProgressFill, xpInsideLevel / 250f);
            SetMetricText(m_VocabularyMasteryText, $"{summary.VocabularyMasteryRate:P0}", LearningUiTheme.Ink);
            SetMetricText(m_ObjectsScannedText, objectScans.ToString(), LearningUiTheme.Ink);
            SetMetricText(m_QuizAccuracyValueText, $"{summary.QuizAccuracy:P0}", LearningUiTheme.Ink);
            SetMetricText(m_RetentionScoreText, $"{summary.AverageRetentionScore:P0}", LearningUiTheme.Ink);
            SetMetricText(m_RevisionCountText, summary.WordsNeedingRevision.ToString(), LearningUiTheme.Ink);
            SetMetricText(m_StreakValueText, activity.StreakDays.ToString(), LearningUiTheme.Ink);
            SetMetricText(
                m_LearningTimeText,
                $"{(int)totalLearningTime.TotalHours:00}:{totalLearningTime.Minutes:00} focused learning",
                LearningUiTheme.WithAlpha(Color.white, 0.9f),
                FontStyles.Normal);

            var maximumDailyActivity = Math.Max(1, activity.WeeklyLearningActions.DefaultIfEmpty(0).Max());
            for (var index = 0; index < m_WeeklyBars.Length && index < activity.WeeklyLearningActions.Length; index++)
            {
                var isCurrentDay = index == m_WeeklyBars.Length - 1;
                m_WeeklyBars[index].color = LearningUiTheme.WithAlpha(
                    LearningUiTheme.Primary,
                    isCurrentDay ? 1f : 0.55f);
                var activityAmount = activity.WeeklyLearningActions[index];
                var normalizedHeight = CalculateActivityBarFill(activityAmount, maximumDailyActivity);
                m_WeeklyBars[index].enabled = activityAmount > 0;
                var barRect = m_WeeklyBars[index].rectTransform;
                barRect.anchorMax = new Vector2(1f, normalizedHeight);
                barRect.offsetMin = new Vector2(8f, 0f);
                barRect.offsetMax = new Vector2(-8f, 0f);
                if (index < m_WeeklyCountLabels.Length)
                    m_WeeklyCountLabels[index].text = activityAmount.ToString();
                if (index < m_WeeklyDayLabels.Length)
                {
                    m_WeeklyDayLabels[index].text = index < activity.WeeklyDays.Length
                        ? activity.WeeklyDays[index].Date.ToString("ddd")
                        : string.Empty;
                    m_WeeklyDayLabels[index].color = isCurrentDay
                        ? LearningUiTheme.PrimaryDark
                        : LearningUiTheme.Muted;
                }
            }

            if (m_WeeklyScansValueText != null)
            {
                var weeklyMinutes = activity.WeeklyLearningSeconds / 60f;
                var incorrectAnswers = Math.Max(0, activity.WeeklyQuizAnswers - activity.WeeklyQuizCorrect);
                SetMetricText(m_WeeklyScansValueText, activity.WeeklyObjectScans.ToString(), LearningUiTheme.Ink);
                SetMetricText(m_WeeklyQuizzesValueText, activity.WeeklyQuizzesCompleted.ToString(), LearningUiTheme.Ink);
                SetMetricText(m_WeeklyFocusedMinutesValueText, $"{weeklyMinutes:0.#}", LearningUiTheme.Ink);

                if (activity.WeeklyQuizAnswers == 0)
                {
                    SetMetricText(m_WeeklyQuizAccuracyText, "—", LearningUiTheme.Warning);
                    m_WeeklyQuizAnswerDetailText.text = "Complete a quiz to unlock answer quality";
                    LearningUiTheme.StyleSurface(m_WeeklyQuizAccuracyBadge, LearningUiTheme.AccentSoft, LearningUiTheme.RadiusPill);
                }
                else
                {
                    var accuracyColor = activity.WeeklyQuizAccuracy >= 0.7d
                        ? LearningUiTheme.PrimaryDark
                        : LearningUiTheme.Error;
                    var accuracySurface = activity.WeeklyQuizAccuracy >= 0.7d
                        ? LearningUiTheme.PrimarySoft
                        : LearningUiTheme.ErrorSoft;
                    SetMetricText(m_WeeklyQuizAccuracyText, $"{activity.WeeklyQuizAccuracy:P0}", accuracyColor);
                    m_WeeklyQuizAnswerDetailText.text =
                        $"{activity.WeeklyQuizCorrect} correct • {incorrectAnswers} to review";
                    LearningUiTheme.StyleSurface(m_WeeklyQuizAccuracyBadge, accuracySurface, LearningUiTheme.RadiusPill);
                }

                m_WeeklyChartCaptionText.text = activity.WeeklyLegacyActivityUnits > 0
                    ? $"Bars combine object scans and completed quizzes. {activity.WeeklyLegacyActivityUnits} older actions only affect streak."
                    : "Bars combine object scans and completed quizzes.";
            }

            if (m_TodayScansValueText != null)
            {
                var today = activity.WeeklyDays.Length == 0
                    ? default
                    : activity.WeeklyDays[activity.WeeklyDays.Length - 1];
                SetMetricText(m_TodayScansValueText, today.ObjectScans.ToString(), LearningUiTheme.Ink);
                SetMetricText(m_TodayQuizzesValueText, today.QuizzesCompleted.ToString(), LearningUiTheme.Ink);
                SetMetricText(m_ExploredWordsValueText, $"{summary.SeenWords} / {summary.TotalWords}", LearningUiTheme.PrimaryDark);
                SetMetricText(m_MasteredWordsValueText, summary.MasteredWords.ToString(), LearningUiTheme.Lavender);
            }

            var recommendations = m_CompositionRoot.AdaptiveRevisionService.GetRevisionQueue(
                catalog,
                progress,
                nowUtc,
                4);
            if (m_RevisionQueueContainer != null)
            {
                var visibleItems = Math.Min(recommendations.Count, m_RevisionItems.Length);
                m_RevisionQueueContainer.SetActive(visibleItems > 0);
                m_RevisionEmptyState?.SetActive(visibleItems == 0);

                for (var index = 0; index < m_RevisionItems.Length; index++)
                {
                    var isVisible = index < visibleItems;
                    m_RevisionItems[index].SetActive(isVisible);
                    if (!isVisible)
                        continue;

                    var recommendation = recommendations[index];
                    m_RevisionWordTexts[index].text = FormatWordLabel(recommendation.Entry.CanonicalObjectLabel);
                    m_RevisionReasonTexts[index].text = recommendation.Reason;

                    var hasQuizEvidence = recommendation.Progress.QuizAttempts > 0;
                    var accuracy = recommendation.Progress.QuizAccuracy;
                    m_RevisionEvidenceTexts[index].text = hasQuizEvidence
                        ? $"{accuracy:P0} ACCURACY"
                        : "NOT TESTED";

                    var evidenceColor = !hasQuizEvidence
                        ? LearningUiTheme.Warning
                        : accuracy < 0.7d
                            ? LearningUiTheme.Error
                            : LearningUiTheme.PrimaryDark;
                    var evidenceSurface = !hasQuizEvidence
                        ? LearningUiTheme.AccentSoft
                        : accuracy < 0.7d
                            ? LearningUiTheme.ErrorSoft
                            : LearningUiTheme.PrimarySoft;
                    m_RevisionEvidenceTexts[index].color = evidenceColor;
                    LearningUiTheme.StyleSurface(
                        m_RevisionEvidenceBadges[index],
                        evidenceSurface,
                        LearningUiTheme.RadiusPill);
                }

                var queueHeight = visibleItems > 0
                    ? (visibleItems * 96f) + (Math.Max(0, visibleItems - 1) * 10f)
                    : 124f;
                SetPreferredHeight(m_RevisionQueueContainer, queueHeight);
                SetPreferredHeight(m_RevisionCard, 140f + queueHeight);
            }
        }

        void OnRecognitionsPresented(IReadOnlyList<RecognitionPresentation> presentations)
        {
            if (m_CurrentScreen != ExperienceScreen.Scan)
                return;

            if (presentations == null || presentations.Count == 0)
            {
                SetScanSpeakAvailable(false);
                SetStatus("Point your camera at an everyday object");
                return;
            }

            var matchedCount = presentations.Count(item => item.MatchedVocabulary);
            SetScanSpeakAvailable(matchedCount > 0);
            if (matchedCount <= 0)
            {
                SetStatus("Object found — try another learning object");
                return;
            }

            var currentEntry = m_SessionController?.CurrentVocabularyEntry;
            if (currentEntry != null &&
                !string.Equals(m_LastTrackedScanVocabularyId, currentEntry.Id, StringComparison.OrdinalIgnoreCase))
            {
                m_LastTrackedScanVocabularyId = currentEntry.Id;
                RefreshHomeSummary();
            }

            var bestConfidence = presentations
                .Where(item => item.MatchedVocabulary)
                .Select(item => item.Confidence)
                .DefaultIfEmpty(0f)
                .Max();
            SetStatus($"Object found • {bestConfidence:P0} confidence");
        }

        bool ResolveDependencies()
        {
            if (m_SessionController == null)
                m_SessionController = FindAnyObjectByType<LearningSessionController>();

            if (m_CompositionRoot == null)
                m_CompositionRoot = LanguageLearningCompositionRoot.Instance ??
                                    FindAnyObjectByType<LanguageLearningCompositionRoot>();

            if (m_CompositionRoot != null)
                m_CompositionRoot.EnsureComposed();

            SubscribeToRecognitionStatus();
            return m_SessionController != null && m_CompositionRoot != null;
        }

        void SubscribeToRecognitionStatus()
        {
            var recognitionService = m_CompositionRoot?.ObjectRecognitionService;
            if (recognitionService == null || ReferenceEquals(recognitionService, m_SubscribedRecognitionService))
                return;

            if (m_SubscribedRecognitionService != null)
                m_SubscribedRecognitionService.StatusChanged -= OnRecognitionStatusChanged;

            m_SubscribedRecognitionService = recognitionService;
            m_SubscribedRecognitionService.StatusChanged += OnRecognitionStatusChanged;
        }

        void OnRecognitionStatusChanged(RecognitionServiceStatus status)
        {
            if (m_CurrentScreen != ExperienceScreen.Scan)
                return;

            SetStatus(status.Message);
        }

        IEnumerator ActivateScanRuntime()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                    UnityEngine.Android.Permission.Camera))
            {
                var permissionCompleted = false;
                var permissionGranted = false;
                var callbacks = new UnityEngine.Android.PermissionCallbacks();
                callbacks.PermissionGranted += _ =>
                {
                    permissionGranted = true;
                    permissionCompleted = true;
                };
                callbacks.PermissionDenied += _ => permissionCompleted = true;
                callbacks.PermissionDeniedAndDontAskAgain += _ => permissionCompleted = true;
                UnityEngine.Android.Permission.RequestUserPermission(
                    UnityEngine.Android.Permission.Camera,
                    callbacks);

                SetStatus("Camera permission is required to scan objects.");
                while (!permissionCompleted && m_CurrentScreen == ExperienceScreen.Scan)
                    yield return null;

                if (!permissionGranted)
                {
                    SetStatus("Camera permission was denied. Enable it in Android settings and try again.");
                    m_ScanStartupCoroutine = null;
                    yield break;
                }
            }
#endif

            var arSession = FindAnyObjectByType<ARSession>(FindObjectsInactive.Include);
            var cameraManager = FindAnyObjectByType<ARCameraManager>(FindObjectsInactive.Include);
            var cameraBackground = FindAnyObjectByType<ARCameraBackground>(FindObjectsInactive.Include);
            if (arSession == null || cameraManager == null || cameraBackground == null)
            {
                SetStatus("The AR camera is not configured. Rebuild the Main scene setup.");
                m_ScanStartupCoroutine = null;
                yield break;
            }

            SetStatus("Checking AR camera availability...");
            if (ARSession.state == ARSessionState.None ||
                ARSession.state == ARSessionState.CheckingAvailability)
            {
                yield return ARSession.CheckAvailability();
            }

            if (m_CurrentScreen != ExperienceScreen.Scan)
                yield break;

            if (ARSession.state == ARSessionState.NeedsInstall)
                yield return ARSession.Install();

            if (ARSession.state == ARSessionState.Unsupported)
            {
                SetStatus("AR is not supported on this device.");
                m_ScanStartupCoroutine = null;
                yield break;
            }

            arSession.enabled = true;
            cameraManager.enabled = true;
            cameraBackground.enabled = true;

            var startupDeadline = Time.realtimeSinceStartup + 12f;
            while (ARSession.state != ARSessionState.SessionInitializing &&
                   ARSession.state != ARSessionState.SessionTracking)
            {
                if (m_CurrentScreen != ExperienceScreen.Scan)
                    yield break;

                if (ARSession.state == ARSessionState.Unsupported)
                {
                    SetStatus("AR is not supported on this device.");
                    m_ScanStartupCoroutine = null;
                    yield break;
                }

                if (Time.realtimeSinceStartup >= startupDeadline)
                {
                    SetStatus("The camera did not start. Check camera permission and AR Services.");
                    m_ScanStartupCoroutine = null;
                    yield break;
                }

                yield return null;
            }

            ConfigureCameraForRealtimeScanning(cameraManager);
            m_SessionController.ResumeRecognition();
            SetStatus("Scanning for supported objects...");
            m_ScanStartupCoroutine = null;
        }

        static void ConfigureCameraForRealtimeScanning(ARCameraManager cameraManager)
        {
            if (cameraManager == null ||
                cameraManager.subsystem == null ||
                !cameraManager.subsystem.running)
            {
                return;
            }

            try
            {
                using var configurations = cameraManager.GetConfigurations(Allocator.Temp);
                if (!configurations.IsCreated || configurations.Length == 0)
                    return;

                const long targetPixels = 1280L * 720L;
                var bestConfiguration = configurations[0];
                var bestScore = CameraConfigurationScore(bestConfiguration, targetPixels);
                for (var index = 1; index < configurations.Length; index++)
                {
                    var candidate = configurations[index];
                    var score = CameraConfigurationScore(candidate, targetPixels);
                    if (score >= bestScore)
                        continue;

                    bestConfiguration = candidate;
                    bestScore = score;
                }

                if (cameraManager.currentConfiguration != bestConfiguration)
                    cameraManager.currentConfiguration = bestConfiguration;
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    $"Could not select a performance-friendly AR camera configuration: {exception.Message}");
            }
        }

        static long CameraConfigurationScore(XRCameraConfiguration configuration, long targetPixels)
        {
            var pixels = Math.Max(1L, (long)configuration.width * configuration.height);
            var resolutionDistance = Math.Abs(pixels - targetPixels);
            var oversizedPenalty = pixels > targetPixels ? resolutionDistance : 0L;
            var frameRate = configuration.framerate ?? 30;
            var slowFrameRatePenalty = frameRate < 24 ? targetPixels * 4L : 0L;
            var frameRateDistance = Math.Abs(frameRate - 30) * 10_000L;
            return resolutionDistance + oversizedPenalty + slowFrameRatePenalty + frameRateDistance;
        }

        void DeactivateScanRuntime()
        {
            if (m_ScanStartupCoroutine != null)
            {
                StopCoroutine(m_ScanStartupCoroutine);
                m_ScanStartupCoroutine = null;
            }

            m_SessionController?.PauseRecognition();

            var cameraBackground = FindAnyObjectByType<ARCameraBackground>(FindObjectsInactive.Include);
            if (cameraBackground != null)
                cameraBackground.enabled = false;

            var cameraManager = FindAnyObjectByType<ARCameraManager>(FindObjectsInactive.Include);
            if (cameraManager != null)
                cameraManager.enabled = false;

            var arSession = FindAnyObjectByType<ARSession>(FindObjectsInactive.Include);
            if (arSession != null)
                arSession.enabled = false;
        }

        void SetNextQuestionButton(bool interactable, string label)
        {
            if (m_NextQuestionButton != null)
                m_NextQuestionButton.interactable = interactable;

            if (m_NextQuestionButtonLabel != null)
                m_NextQuestionButtonLabel.text = label;
        }

        void RefreshLanguageButtonStyles()
        {
            if (m_SessionController == null)
                return;

            var languages = (LanguageCode[])Enum.GetValues(typeof(LanguageCode));
            RefreshLanguageButtonStyles(m_QuizLanguageButtons, QuizLanguages);
            RefreshLanguageButtonStyles(m_ScanLanguageButtons, languages);
        }

        void RefreshLanguageButtonStyles(Button[] buttons, LanguageCode[] languages)
        {
            if (buttons == null)
                return;

            for (var index = 0; index < buttons.Length && index < languages.Length; index++)
            {
                var button = buttons[index];
                if (button == null)
                    continue;

                var selected = languages[index] == m_SessionController.LearningLanguage;
                var background = selected ? LearningUiTheme.Primary : LearningUiTheme.PrimarySoft;
                var foreground = selected ? Color.white : LearningUiTheme.PrimaryDark;
                LearningUiTheme.SetButtonColor(button, background, selected ? LearningUiTheme.PrimaryDark : LearningUiTheme.Hex("D5F0DD"));
                var colors = button.colors;
                colors.disabledColor = background;
                button.colors = colors;
                var label = button.GetComponentInChildren<TMP_Text>();
                if (label != null)
                    label.color = foreground;
            }
        }

        void SetScanSpeakAvailable(bool available)
        {
            if (m_ScanSpeakButton != null)
                m_ScanSpeakButton.interactable = available;
        }

        void EnsureUi()
        {
            if (m_Built)
                return;

            m_Canvas = FindAnyObjectByType<Canvas>();
            if (m_Canvas == null)
                m_Canvas = CreateCanvas();

            ConfigureCanvas(m_Canvas);
            RepairCanvasTransform(m_Canvas);
            m_SafeAreaRoot = CreateSafeAreaRoot(m_Canvas.transform);
            m_AppBackground = CreatePanel(m_SafeAreaRoot, "App Background", LearningUiTheme.Background, 0f)
                .GetComponent<Image>();
            Stretch(m_AppBackground.rectTransform, 0f);
            m_AppBackground.raycastTarget = false;
            m_AppBackground.transform.SetAsFirstSibling();

            var vocabularyPresenter = FindAnyObjectByType<ARVocabularyOverlayPresenter>();
            if (vocabularyPresenter != null)
            {
                m_VocabularyPresenter = vocabularyPresenter;
                vocabularyPresenter.transform.SetParent(m_SafeAreaRoot, false);
                vocabularyPresenter.ApplyModernLayout();
                vocabularyPresenter.SetScanDisplayEnabled(false);
                m_VocabularyCanvasGroup = vocabularyPresenter.GetComponent<CanvasGroup>();
            }

            var font = ARVocabularyOverlayPresenter.GetMultilingualFontAsset() ??
                       FindAnyObjectByType<TMP_Text>()?.font;
            CreateNavigation(font);
            CreateScanControls(font);
            CreateQuizPanel(font);
            CreateProgressPanel(font);
            m_Built = true;
        }

        Canvas CreateCanvas()
        {
            var canvasObject = new GameObject(
                "LanguageLearning UI",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            var canvas = canvasObject.GetComponent<Canvas>();
            return canvas;
        }

        static void ConfigureCanvas(Canvas canvas)
        {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvas.GetComponent<CanvasScaler>() ?? canvas.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1440f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            scaler.referencePixelsPerUnit = 100f;
        }

        static void RepairCanvasTransform(Canvas canvas)
        {
            var rect = canvas.transform as RectTransform;
            if (rect == null)
                return;

            rect.localScale = Vector3.one;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        void CreateStatus(TMP_FontAsset font)
        {
            var statusObject = CreatePanel(
                m_SafeAreaRoot,
                "Learning Status",
                LearningUiTheme.WithAlpha(LearningUiTheme.Ink, 0.82f),
                LearningUiTheme.RadiusPill);
            m_StatusCanvasGroup = statusObject.GetComponent<CanvasGroup>();
            var rect = statusObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -424f);
            rect.sizeDelta = new Vector2(-80f, 64f);
            m_StatusText = CreateText(statusObject.transform, "Status", "Point your camera at an object", 22f, font, LearningUiTheme.Card, TextAlignmentOptions.Center);
            Stretch(m_StatusText.rectTransform, 18f);
            SetVisible(m_StatusCanvasGroup, false);
        }

        void CreateNavigation(TMP_FontAsset font)
        {
            var navigation = CreateScreenRoot("Main Menu");
            m_HomeCanvasGroup = navigation.GetComponent<CanvasGroup>();

            var content = CreateScrollContent(navigation.transform, "Home Content");
            m_HomeScrollRect = navigation.GetComponent<ScrollRect>();
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(36, 36, 64, 48);
            layout.spacing = 20f;

            var homeHeader = CreateVerticalGroup(content.transform, "Home Header", 6f, 164f);
            var eyebrow = CreateText(
                homeHeader.transform,
                "Exploration Eyebrow",
                "READY TO EXPLORE",
                18f,
                font,
                LearningUiTheme.PrimaryDark);
            eyebrow.fontStyle = FontStyles.Bold;
            eyebrow.fontWeight = FontWeight.Bold;
            eyebrow.characterSpacing = 3f;
            SetPreferredHeight(eyebrow.gameObject, 30f);

            var greeting = CreateText(homeHeader.transform, "Greeting", "Hi, Explorer!", 48f, font, LearningUiTheme.Ink);
            greeting.fontStyle = FontStyles.Bold;
            greeting.fontWeight = FontWeight.Bold;
            greeting.overflowMode = TextOverflowModes.Overflow;
            SetPreferredHeight(greeting.gameObject, 62f);
            var subtitle = CreateText(
                homeHeader.transform,
                "Motivation",
                "Learn languages by exploring the world.",
                27f,
                font,
                LearningUiTheme.Muted);
            subtitle.textWrappingMode = TextWrappingModes.Normal;
            SetPreferredHeight(subtitle.gameObject, 50f);

            var todayCard = CreatePanel(content.transform, "Today Card", LearningUiTheme.PrimarySoft, LearningUiTheme.RadiusLarge, 146f);
            var todayLayout = todayCard.AddComponent<HorizontalLayoutGroup>();
            todayLayout.padding = new RectOffset(28, 28, 14, 14);
            todayLayout.spacing = 16f;
            todayLayout.childControlWidth = true;
            todayLayout.childControlHeight = true;
            todayLayout.childForceExpandWidth = true;
            todayLayout.childForceExpandHeight = false;
            CreateHomeStat(todayCard.transform, "Streak", LearningUiIconType.Flame, LearningUiTheme.Warning, out m_HomeStreakText, font);
            CreateHomeStat(todayCard.transform, "Today", LearningUiIconType.Star, LearningUiTheme.Accent, out m_HomeTodayXpText, font);
            CreateHomeStat(todayCard.transform, "Level", LearningUiIconType.Trophy, LearningUiTheme.Primary, out m_HomeLevelText, font);

            CreateFeatureButton(
                content.transform,
                "Scan",
                "Discover words in the world around you",
                LearningUiIconType.Camera,
                LearningUiTheme.Primary,
                LearningUiTheme.PrimarySoft,
                font,
                ShowScan);
            CreateFeatureButton(
                content.transform,
                "Quiz",
                "Build recall with a quick challenge",
                LearningUiIconType.Quiz,
                LearningUiTheme.Secondary,
                LearningUiTheme.SecondarySoft,
                font,
                ShowQuiz);
            CreateFeatureButton(
                content.transform,
                "Progress",
                "See your streak, XP, and achievements",
                LearningUiIconType.Chart,
                LearningUiTheme.Lavender,
                LearningUiTheme.LavenderSoft,
                font,
                ShowProgress);

            var encouragement = CreateText(
                content.transform,
                "Home Encouragement",
                "Small discoveries add up. Keep exploring!",
                22f,
                font,
                LearningUiTheme.Muted,
                TextAlignmentOptions.Center);
            SetPreferredHeight(encouragement.gameObject, 44f);
        }

        void CreateScanControls(TMP_FontAsset font)
        {
            var controls = CreateScreenRoot("Scan Controls");
            m_ScanCanvasGroup = controls.GetComponent<CanvasGroup>();

            var scanActions = new GameObject("Scan Actions", typeof(RectTransform));
            scanActions.transform.SetParent(controls.transform, false);
            Stretch(scanActions.GetComponent<RectTransform>(), 0f);

            var backButton = CreateIconButton(
                scanActions.transform,
                "Back",
                LearningUiIconType.Back,
                LearningUiTheme.Card,
                LearningUiTheme.Ink,
                ShowHome,
                86f);
            var backRect = backButton.GetComponent<RectTransform>();
            backRect.anchorMin = new Vector2(0f, 1f);
            backRect.anchorMax = new Vector2(0f, 1f);
            backRect.pivot = new Vector2(0f, 1f);
            backRect.anchoredPosition = new Vector2(32f, -28f);
            LearningUiTheme.StyleSurface(backButton.GetComponent<Image>(), LearningUiTheme.Glass, LearningUiTheme.RadiusPill, true, new Vector2(0f, -5f));

            var languageSelector = CreatePanel(
                scanActions.transform,
                "Scan Language Selector",
                LearningUiTheme.Glass,
                LearningUiTheme.RadiusLarge);
            m_ScanLanguageCanvasGroup = languageSelector.GetComponent<CanvasGroup>();
            var selectorRect = languageSelector.GetComponent<RectTransform>();
            selectorRect.anchorMin = new Vector2(0f, 0f);
            selectorRect.anchorMax = new Vector2(1f, 0f);
            selectorRect.pivot = new Vector2(0.5f, 0f);
            selectorRect.anchoredPosition = new Vector2(0f, 174f);
            selectorRect.sizeDelta = new Vector2(-120f, 96f);
            var selectorLayout = languageSelector.AddComponent<HorizontalLayoutGroup>();
            selectorLayout.padding = new RectOffset(12, 12, 10, 10);
            selectorLayout.spacing = 10f;
            selectorLayout.childControlWidth = true;
            selectorLayout.childControlHeight = true;
            selectorLayout.childForceExpandWidth = true;
            selectorLayout.childForceExpandHeight = false;

            var languages = (LanguageCode[])Enum.GetValues(typeof(LanguageCode));
            m_ScanLanguageButtons = new Button[languages.Length];
            for (var index = 0; index < languages.Length; index++)
            {
                var language = languages[index];
                var capturedLanguageIndex = (int)language;
                m_ScanLanguageButtons[index] = CreateButton(
                    languageSelector.transform,
                    language.ToIsoCode().ToUpperInvariant(),
                    font,
                    () => SelectScanLanguage(capturedLanguageIndex),
                    74f,
                    LearningButtonStyle.Soft,
                    18f);
            }
            SetVisible(m_ScanLanguageCanvasGroup, false);

            var actionBar = CreatePanel(
                scanActions.transform,
                "Scan Action Bar",
                LearningUiTheme.Glass,
                LearningUiTheme.RadiusLarge);
            var actionRect = actionBar.GetComponent<RectTransform>();
            actionRect.anchorMin = new Vector2(0.5f, 0f);
            actionRect.anchorMax = new Vector2(0.5f, 0f);
            actionRect.pivot = new Vector2(0.5f, 0f);
            actionRect.anchoredPosition = new Vector2(0f, 24f);
            actionRect.sizeDelta = new Vector2(620f, 132f);
            LearningUiTheme.StyleSurface(actionBar.GetComponent<Image>(), LearningUiTheme.Glass, LearningUiTheme.RadiusLarge, true, new Vector2(0f, -8f));
            var actionLayout = actionBar.AddComponent<HorizontalLayoutGroup>();
            actionLayout.padding = new RectOffset(16, 16, 14, 14);
            actionLayout.spacing = 12f;
            actionLayout.childControlWidth = true;
            actionLayout.childControlHeight = true;
            actionLayout.childForceExpandWidth = true;
            actionLayout.childForceExpandHeight = false;

            CreateActionButton(actionBar.transform, "Translate", LearningUiIconType.Translate, LearningUiTheme.Secondary, font, ToggleScanLanguageSelector);
            m_ScanSpeakButton = CreateActionButton(actionBar.transform, "Speak", LearningUiIconType.Speak, LearningUiTheme.Lavender, font, SpeakCurrentScanWord);

            SetScanSpeakAvailable(false);
            RefreshLanguageButtonStyles();
            SetVisible(m_ScanCanvasGroup, false);
        }

        void CreateQuizPanel(TMP_FontAsset font)
        {
            var panel = CreateScreenRoot("Quiz Panel");
            m_QuizCanvasGroup = panel.GetComponent<CanvasGroup>();
            var content = CreateScrollContent(panel.transform, "Quiz Content");
            m_QuizScrollRect = panel.GetComponent<ScrollRect>();
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(36, 36, 55, 44);
            layout.spacing = 16f;

            var header = CreateHorizontalGroup(content.transform, "Quiz Header", 96f, 16f);
            var backButton = CreateIconButton(header.transform, "Back", LearningUiIconType.Back, LearningUiTheme.Card, LearningUiTheme.Ink, ShowHome, 88f);
            SetPreferredWidth(backButton.gameObject, 88f);
            var headerText = CreateVerticalGroup(header.transform, "Quiz Header Text", 0f);
            SetFlexibleWidth(headerText, 1f);
            SetPreferredHeight(headerText, 80f);
            var title = CreateText(headerText.transform, "Quiz Title", "Quick quiz", 34f, font, LearningUiTheme.Ink);
            title.fontStyle = FontStyles.Bold;
            SetPreferredHeight(title.gameObject, 44f);
            m_QuizCounterText = CreateText(headerText.transform, "Quiz Counter", "Question 1 of 5", 21f, font, LearningUiTheme.Muted);
            SetPreferredHeight(m_QuizCounterText.gameObject, 30f);
            var xpPill = CreatePanel(header.transform, "Quiz XP", LearningUiTheme.AccentSoft, LearningUiTheme.RadiusPill);
            SetPreferredSize(xpPill, 132f, 68f);
            m_QuizXpText = CreateText(xpPill.transform, "Quiz XP Label", "0 XP", 22f, font, LearningUiTheme.Ink, TextAlignmentOptions.Center);
            m_QuizXpText.fontStyle = FontStyles.Bold;
            Stretch(m_QuizXpText.rectTransform, 8f);

            var progressTrack = CreatePill(content.transform, "Quiz Progress", LearningUiTheme.Line, 18f);
            m_QuizProgressFill = CreatePill(progressTrack.transform, "Quiz Progress Fill", LearningUiTheme.Primary)
                .GetComponent<LearningPillGraphic>();
            SetRoundedFillAmount(m_QuizProgressFill, 0f);

            CreateQuizLanguageSelector(content.transform, font);

            var questionArea = CreateVerticalGroup(content.transform, "Question Area", 0f);
            var questionAreaLayout = questionArea.GetComponent<VerticalLayoutGroup>();
            questionAreaLayout.padding = new RectOffset(0, 0, 10, 0);

            var questionCard = CreatePanel(questionArea.transform, "Question Card", LearningUiTheme.Card, LearningUiTheme.RadiusLarge, 270f);
            m_QuizQuestionCard = questionCard.GetComponent<RectTransform>();
            LearningUiTheme.StyleSurface(questionCard.GetComponent<Image>(), LearningUiTheme.Card, LearningUiTheme.RadiusLarge, true, new Vector2(0f, -7f));
            var questionLayout = questionCard.AddComponent<VerticalLayoutGroup>();
            questionLayout.padding = new RectOffset(34, 34, 28, 28);
            questionLayout.spacing = 10f;
            questionLayout.childControlWidth = true;
            questionLayout.childControlHeight = true;
            questionLayout.childForceExpandWidth = true;
            questionLayout.childForceExpandHeight = false;
            m_QuizQuestionCaptionText = CreateText(
                questionCard.transform,
                "Question Caption",
                "TRANSLATE THIS LANGUAGE WORD",
                20f,
                font,
                LearningUiTheme.Primary,
                TextAlignmentOptions.Center);
            m_QuizQuestionCaptionText.fontStyle = FontStyles.Bold;
            m_QuizQuestionCaptionText.fontWeight = FontWeight.Bold;
            m_QuizQuestionCaptionText.characterSpacing = 1.5f;
            SetPreferredHeight(m_QuizQuestionCaptionText.gameObject, 38f);
            m_QuizPromptText = CreateText(questionCard.transform, "Quiz Prompt", "PREPARING QUESTION…", 60f, font, LearningUiTheme.Ink, TextAlignmentOptions.Center);
            m_QuizPromptText.fontStyle = FontStyles.Bold;
            m_QuizPromptText.fontWeight = FontWeight.Bold;
            m_QuizPromptText.textWrappingMode = TextWrappingModes.Normal;
            m_QuizPromptText.enableAutoSizing = true;
            m_QuizPromptText.fontSizeMin = 38f;
            m_QuizPromptText.fontSizeMax = 62f;
            SetPreferredHeight(m_QuizPromptText.gameObject, 150f);

            m_QuizOptionButtons = new Button[QuizOptionCount];
            m_QuizOptionLabels = new TMP_Text[QuizOptionCount];
            for (var index = 0; index < QuizOptionCount; index++)
            {
                var capturedIndex = index;
                m_QuizOptionButtons[index] = CreateButton(
                    content.transform,
                    string.Empty,
                    font,
                    () => AnswerQuizOption(capturedIndex),
                    84f,
                    LearningButtonStyle.Surface,
                    LearningUiTheme.RadiusMedium);
                m_QuizOptionButtons[index].gameObject.name = $"Quiz Option {index + 1} Button";
                m_QuizOptionLabels[index] = m_QuizOptionButtons[index].GetComponentInChildren<TMP_Text>();
                m_QuizOptionLabels[index].alignment = TextAlignmentOptions.Center;
                m_QuizOptionLabels[index].fontSize = 27f;
                LearningUiTheme.StyleSurface(m_QuizOptionButtons[index].GetComponent<Image>(), LearningUiTheme.Card, LearningUiTheme.RadiusMedium, true, new Vector2(0f, -4f));
                m_QuizOptionButtons[index].gameObject.SetActive(false);
            }

            m_QuizFeedbackText = CreateText(content.transform, "Quiz Feedback", "Choose the best translation.", 23f, font, LearningUiTheme.Muted, TextAlignmentOptions.Center);
            m_QuizFeedbackText.textWrappingMode = TextWrappingModes.Normal;
            SetPreferredHeight(m_QuizFeedbackText.gameObject, 58f);

            m_NextQuestionButton = CreateButton(content.transform, "Next Question", font, NextQuizQuestion, 86f, LearningButtonStyle.Primary, LearningUiTheme.RadiusLarge);
            m_NextQuestionButtonLabel = m_NextQuestionButton.GetComponentInChildren<TMP_Text>();
            m_NextQuestionButtonLabel.color = Color.white;
            m_NextQuestionButtonLabel.fontStyle = FontStyles.Bold;
            SetNextQuestionButton(false, "Next question");

            var xpBurst = new GameObject("XP Burst", typeof(RectTransform), typeof(CanvasGroup), typeof(LayoutElement));
            xpBurst.transform.SetParent(panel.transform, false);
            xpBurst.GetComponent<LayoutElement>().ignoreLayout = true;
            m_XpBurstRect = xpBurst.GetComponent<RectTransform>();
            m_XpBurstRect.anchorMin = new Vector2(0.5f, 0.5f);
            m_XpBurstRect.anchorMax = new Vector2(0.5f, 0.5f);
            m_XpBurstRect.sizeDelta = new Vector2(220f, 88f);
            m_XpBurstText = CreateText(xpBurst.transform, "XP Burst Label", "+15 XP", 34f, font, LearningUiTheme.Warning, TextAlignmentOptions.Center);
            m_XpBurstText.fontStyle = FontStyles.Bold;
            Stretch(m_XpBurstText.rectTransform, 0f);
            xpBurst.GetComponent<CanvasGroup>().alpha = 0f;

            SetVisible(m_QuizCanvasGroup, false);
        }

        void CreateQuizLanguageSelector(Transform parent, TMP_FontAsset font)
        {
            var selector = CreateHorizontalGroup(parent, "Quiz Language Selector", 64f, 10f, true);
            m_QuizLanguageButtons = new Button[QuizLanguages.Length];
            for (var index = 0; index < QuizLanguages.Length; index++)
            {
                var language = QuizLanguages[index];
                var capturedLanguageIndex = (int)language;
                m_QuizLanguageButtons[index] = CreateButton(
                    selector.transform,
                    language.ToDisplayName(),
                    font,
                    () => SelectQuizLanguage(capturedLanguageIndex),
                    64f,
                    LearningButtonStyle.Soft,
                    18f);
                var label = m_QuizLanguageButtons[index].GetComponentInChildren<TMP_Text>();
                label.fontSize = 19f;
            }
        }

        void CreateProgressPanel(TMP_FontAsset font)
        {
            var panel = CreateScreenRoot("Progress Panel");
            m_ProgressCanvasGroup = panel.GetComponent<CanvasGroup>();
            var content = CreateScrollContent(panel.transform, "Progress Content");
            m_ProgressScrollRect = panel.GetComponent<ScrollRect>();
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(36, 36, 42, 52);
            layout.spacing = 20f;

            var header = CreateHorizontalGroup(content.transform, "Progress Header", 96f, 16f);
            var backButton = CreateIconButton(header.transform, "Back", LearningUiIconType.Back, LearningUiTheme.Card, LearningUiTheme.Ink, ShowHome, 88f);
            SetPreferredWidth(backButton.gameObject, 88f);
            var headerCopy = CreateVerticalGroup(header.transform, "Progress Header Copy", 0f);
            SetFlexibleWidth(headerCopy, 1f);
            SetPreferredHeight(headerCopy, 80f);
            var title = CreateText(headerCopy.transform, "Progress Title", "Your progress", 38f, font, LearningUiTheme.Ink);
            title.fontStyle = FontStyles.Bold;
            SetPreferredHeight(title.gameObject, 46f);
            var subtitle = CreateText(headerCopy.transform, "Progress Subtitle", "Your learning evidence, clearly explained", 21f, font, LearningUiTheme.Muted);
            SetPreferredHeight(subtitle.gameObject, 30f);

            var levelCard = CreatePanel(content.transform, "Level Card", LearningUiTheme.Primary, LearningUiTheme.RadiusLarge, 238f);
            var levelLayout = levelCard.AddComponent<VerticalLayoutGroup>();
            levelLayout.padding = new RectOffset(32, 32, 26, 26);
            levelLayout.spacing = 10f;
            levelLayout.childControlWidth = true;
            levelLayout.childControlHeight = true;
            levelLayout.childForceExpandWidth = true;
            levelLayout.childForceExpandHeight = false;
            var levelTop = CreateHorizontalGroup(levelCard.transform, "Level Header", 82f, 16f);
            var levelIconBubble = CreatePanel(levelTop.transform, "Level Icon", LearningUiTheme.WithAlpha(Color.white, 0.2f), LearningUiTheme.RadiusPill);
            SetPreferredSize(levelIconBubble, 72f, 72f);
            CreateIcon(levelIconBubble.transform, LearningUiIconType.Trophy, Color.white, 44f);
            var levelCopy = CreateVerticalGroup(levelTop.transform, "Level Copy", 0f);
            SetFlexibleWidth(levelCopy, 1f);
            SetPreferredHeight(levelCopy, 82f);
            m_LevelText = CreateText(levelCopy.transform, "Level Value", "Level 1", 38f, font, Color.white);
            m_LevelText.fontStyle = FontStyles.Bold;
            SetPreferredHeight(m_LevelText.gameObject, 46f);
            m_LearningTimeText = CreateText(levelCopy.transform, "Learning Time", "00:00 focused learning", 20f, font, LearningUiTheme.WithAlpha(Color.white, 0.85f));
            SetPreferredHeight(m_LearningTimeText.gameObject, 30f);
            m_XpProgressText = CreateText(levelCard.transform, "XP Progress", "0 / 250 XP", 21f, font, Color.white);
            m_XpProgressText.fontStyle = FontStyles.Bold;
            SetPreferredHeight(m_XpProgressText.gameObject, 34f);
            var levelTrack = CreatePill(levelCard.transform, "Level Progress", LearningUiTheme.WithAlpha(Color.white, 0.28f), 22f);
            m_LevelProgressFill = CreatePill(levelTrack.transform, "Level Progress Fill", Color.white)
                .GetComponent<LearningPillGraphic>();
            SetRoundedFillAmount(m_LevelProgressFill, 0f);

            var statsGrid = new GameObject("Progress Stats", typeof(RectTransform), typeof(LayoutElement), typeof(GridLayoutGroup));
            statsGrid.transform.SetParent(content.transform, false);
            SetPreferredHeight(statsGrid, 546f);
            var grid = statsGrid.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(260f, 170f);
            grid.spacing = new Vector2(18f, 18f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            grid.childAlignment = TextAnchor.UpperCenter;
            statsGrid.AddComponent<LearningResponsiveGrid>().Configure(2, 18f, 170f);
            CreateStatCard(statsGrid.transform, "Vocabulary mastery", LearningUiIconType.Star, LearningUiTheme.Primary, LearningUiTheme.PrimarySoft, out m_VocabularyMasteryText, font);
            CreateStatCard(statsGrid.transform, "Quiz accuracy", LearningUiIconType.Quiz, LearningUiTheme.Secondary, LearningUiTheme.SecondarySoft, out m_QuizAccuracyValueText, font);
            CreateStatCard(statsGrid.transform, "Retention score", LearningUiIconType.Chart, LearningUiTheme.Lavender, LearningUiTheme.LavenderSoft, out m_RetentionScoreText, font);
            CreateStatCard(statsGrid.transform, "Needs revision", LearningUiIconType.Refresh, LearningUiTheme.Error, LearningUiTheme.ErrorSoft, out m_RevisionCountText, font);
            CreateStatCard(statsGrid.transform, "Objects scanned", LearningUiIconType.Camera, LearningUiTheme.Secondary, LearningUiTheme.SecondarySoft, out m_ObjectsScannedText, font);
            CreateStatCard(statsGrid.transform, "Current streak", LearningUiIconType.Flame, LearningUiTheme.Warning, LearningUiTheme.AccentSoft, out m_StreakValueText, font);

            var weeklyCard = CreatePanel(content.transform, "Weekly Activity Card", LearningUiTheme.Card, LearningUiTheme.RadiusLarge, 584f);
            LearningUiTheme.StyleSurface(weeklyCard.GetComponent<Image>(), LearningUiTheme.Card, LearningUiTheme.RadiusLarge, true, new Vector2(0f, -6f));
            var weeklyLayout = weeklyCard.AddComponent<VerticalLayoutGroup>();
            weeklyLayout.padding = new RectOffset(28, 28, 24, 22);
            weeklyLayout.spacing = 14f;
            weeklyLayout.childControlWidth = true;
            weeklyLayout.childControlHeight = true;
            weeklyLayout.childForceExpandWidth = true;
            weeklyLayout.childForceExpandHeight = false;
            var weeklyTitle = CreateText(weeklyCard.transform, "Weekly Activity Title", "Weekly activity", 28f, font, LearningUiTheme.Ink);
            weeklyTitle.fontStyle = FontStyles.Bold;
            SetPreferredHeight(weeklyTitle.gameObject, 42f);
            var chart = CreateHorizontalGroup(weeklyCard.transform, "Weekly Activity Graph", 212f, 12f, true);
            m_WeeklyBars = new Image[7];
            m_WeeklyCountLabels = new TMP_Text[7];
            m_WeeklyDayLabels = new TMP_Text[7];
            for (var index = 0; index < 7; index++)
                CreateActivityBar(
                    chart.transform,
                    index,
                    font,
                    out m_WeeklyBars[index],
                    out m_WeeklyCountLabels[index],
                    out m_WeeklyDayLabels[index]);

            var weeklyMetrics = CreateHorizontalGroup(
                weeklyCard.transform,
                "Weekly Metric Row",
                92f,
                12f,
                true);
            CreateMetricChip(
                weeklyMetrics.transform,
                "Weekly",
                "Scans",
                LearningUiIconType.Camera,
                LearningUiTheme.PrimaryDark,
                LearningUiTheme.PrimarySoft,
                out m_WeeklyScansValueText,
                font);
            CreateMetricChip(
                weeklyMetrics.transform,
                "Weekly",
                "Quizzes",
                LearningUiIconType.Quiz,
                LearningUiTheme.SecondaryDark,
                LearningUiTheme.SecondarySoft,
                out m_WeeklyQuizzesValueText,
                font);
            CreateMetricChip(
                weeklyMetrics.transform,
                "Weekly",
                "Focused min",
                LearningUiIconType.Clock,
                LearningUiTheme.Lavender,
                LearningUiTheme.LavenderSoft,
                out m_WeeklyFocusedMinutesValueText,
                font);
            CreateWeeklyQuizQualityCard(weeklyCard.transform, font);
            m_WeeklyChartCaptionText = CreateText(
                weeklyCard.transform,
                "Weekly Chart Caption",
                "Bars combine object scans and completed quizzes.",
                17f,
                font,
                LearningUiTheme.Muted,
                TextAlignmentOptions.Center);
            m_WeeklyChartCaptionText.textWrappingMode = TextWrappingModes.Normal;
            SetPreferredHeight(m_WeeklyChartCaptionText.gameObject, 34f);

            var recentActivityCard = CreatePanel(
                content.transform,
                "Recent Learning Activity Card",
                LearningUiTheme.Card,
                LearningUiTheme.RadiusLarge,
                306f);
            LearningUiTheme.StyleSurface(
                recentActivityCard.GetComponent<Image>(),
                LearningUiTheme.Card,
                LearningUiTheme.RadiusLarge,
                true,
                new Vector2(0f, -5f));
            var recentActivityLayout = recentActivityCard.AddComponent<VerticalLayoutGroup>();
            recentActivityLayout.padding = new RectOffset(28, 28, 24, 24);
            recentActivityLayout.spacing = 14f;
            recentActivityLayout.childControlWidth = true;
            recentActivityLayout.childControlHeight = true;
            recentActivityLayout.childForceExpandWidth = true;
            recentActivityLayout.childForceExpandHeight = false;

            var recentHeader = CreateHorizontalGroup(
                recentActivityCard.transform,
                "Recent Activity Header",
                54f,
                12f);
            var recentIconBubble = CreatePanel(
                recentHeader.transform,
                "Recent Activity Icon",
                LearningUiTheme.AccentSoft,
                LearningUiTheme.RadiusPill);
            SetPreferredSize(recentIconBubble, 48f, 48f);
            CreateIcon(recentIconBubble.transform, LearningUiIconType.Clock, LearningUiTheme.Warning, 28f);
            var recentTitle = CreateText(
                recentHeader.transform,
                "Recent Activity Title",
                "Recent learning activity",
                25f,
                font,
                LearningUiTheme.Ink);
            recentTitle.fontStyle = FontStyles.Bold;
            SetFlexibleWidth(recentTitle.gameObject, 1f);
            SetPreferredHeight(recentTitle.gameObject, 48f);
            var periodPill = CreatePanel(
                recentHeader.transform,
                "Recent Activity Period",
                LearningUiTheme.PrimarySoft,
                LearningUiTheme.RadiusPill);
            SetPreferredSize(periodPill, 112f, 42f);
            var periodText = CreateText(
                periodPill.transform,
                "Recent Activity Period Label",
                "TODAY",
                17f,
                font,
                LearningUiTheme.PrimaryDark,
                TextAlignmentOptions.Center);
            periodText.fontStyle = FontStyles.Bold;
            Stretch(periodText.rectTransform, 6f);

            var todayMetrics = CreateHorizontalGroup(
                recentActivityCard.transform,
                "Today Activity Metrics",
                92f,
                12f,
                true);
            CreateMetricChip(
                todayMetrics.transform,
                "Today",
                "Scans",
                LearningUiIconType.Camera,
                LearningUiTheme.PrimaryDark,
                LearningUiTheme.PrimarySoft,
                out m_TodayScansValueText,
                font);
            CreateMetricChip(
                todayMetrics.transform,
                "Today",
                "Quizzes",
                LearningUiIconType.Quiz,
                LearningUiTheme.SecondaryDark,
                LearningUiTheme.SecondarySoft,
                out m_TodayQuizzesValueText,
                font);

            var vocabularySnapshot = CreateHorizontalGroup(
                recentActivityCard.transform,
                "Overall Vocabulary Snapshot",
                68f,
                10f);
            var vocabularyLabel = CreateText(
                vocabularySnapshot.transform,
                "Overall Vocabulary Label",
                "Overall vocabulary",
                17f,
                font,
                LearningUiTheme.Muted);
            vocabularyLabel.fontStyle = FontStyles.Bold;
            SetFlexibleWidth(vocabularyLabel.gameObject, 1f);
            SetPreferredHeight(vocabularyLabel.gameObject, 54f);
            CreateInlineMetricCounter(
                vocabularySnapshot.transform,
                "Explored",
                LearningUiTheme.PrimarySoft,
                LearningUiTheme.PrimaryDark,
                out m_ExploredWordsValueText,
                font);
            CreateInlineMetricCounter(
                vocabularySnapshot.transform,
                "Mastered",
                LearningUiTheme.LavenderSoft,
                LearningUiTheme.Lavender,
                out m_MasteredWordsValueText,
                font);

            m_RevisionCard = CreatePanel(content.transform, "Practice Card", LearningUiTheme.Card, LearningUiTheme.RadiusLarge, 264f);
            LearningUiTheme.StyleSurface(m_RevisionCard.GetComponent<Image>(), LearningUiTheme.Card, LearningUiTheme.RadiusLarge, true, new Vector2(0f, -5f));
            var practiceLayout = m_RevisionCard.AddComponent<VerticalLayoutGroup>();
            practiceLayout.padding = new RectOffset(28, 28, 24, 24);
            practiceLayout.spacing = 10f;
            practiceLayout.childControlWidth = true;
            practiceLayout.childControlHeight = true;
            practiceLayout.childForceExpandWidth = true;
            practiceLayout.childForceExpandHeight = false;
            var practiceTitle = CreateText(m_RevisionCard.transform, "Practice Title", "Adaptive revision queue", 27f, font, LearningUiTheme.Ink);
            practiceTitle.fontStyle = FontStyles.Bold;
            SetPreferredHeight(practiceTitle.gameObject, 42f);
            var practiceSubtitle = CreateText(
                m_RevisionCard.transform,
                "Revision Queue Subtitle",
                "Highest-priority words appear first.",
                18f,
                font,
                LearningUiTheme.Muted);
            SetPreferredHeight(practiceSubtitle.gameObject, 30f);

            m_RevisionQueueContainer = CreateVerticalGroup(
                m_RevisionCard.transform,
                "Revision Queue Items",
                10f,
                414f);
            m_RevisionItems = new GameObject[4];
            m_RevisionWordTexts = new TMP_Text[4];
            m_RevisionReasonTexts = new TMP_Text[4];
            m_RevisionEvidenceTexts = new TMP_Text[4];
            m_RevisionEvidenceBadges = new Image[4];
            for (var index = 0; index < m_RevisionItems.Length; index++)
            {
                CreateRevisionQueueItem(
                    m_RevisionQueueContainer.transform,
                    index,
                    font,
                    out m_RevisionItems[index],
                    out m_RevisionWordTexts[index],
                    out m_RevisionReasonTexts[index],
                    out m_RevisionEvidenceTexts[index],
                    out m_RevisionEvidenceBadges[index]);
            }

            m_RevisionEmptyState = CreateRevisionEmptyState(m_RevisionCard.transform, font);
            m_RevisionQueueContainer.SetActive(false);

            SetVisible(m_ProgressCanvasGroup, false);
        }

        static GameObject CreateHorizontalGroup(
            Transform parent,
            string name,
            float height,
            float spacing,
            bool forceExpandWidth = false,
            bool forceExpandHeight = false)
        {
            var group = new GameObject(name, typeof(RectTransform), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
            group.transform.SetParent(parent, false);
            SetPreferredHeight(group, height);
            var layout = group.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = forceExpandWidth;
            layout.childForceExpandHeight = forceExpandHeight;
            return group;
        }

        static GameObject CreateVerticalGroup(
            Transform parent,
            string name,
            float spacing,
            float height = -1f)
        {
            var group = new GameObject(name, typeof(RectTransform), typeof(LayoutElement), typeof(VerticalLayoutGroup));
            group.transform.SetParent(parent, false);
            if (height > 0f)
                SetPreferredHeight(group, height);
            var layout = group.GetComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return group;
        }

        GameObject CreateScreenRoot(string name)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
            root.transform.SetParent(m_SafeAreaRoot, false);
            Stretch(root.GetComponent<RectTransform>(), 0f);
            return root;
        }

        static RectTransform CreateSafeAreaRoot(Transform parent)
        {
            var existing = parent.Find("Safe Area") as RectTransform;
            if (existing != null)
                return existing;

            var safeArea = new GameObject("Safe Area", typeof(RectTransform), typeof(LearningSafeAreaFitter));
            safeArea.transform.SetParent(parent, false);
            var rect = safeArea.GetComponent<RectTransform>();
            Stretch(rect, 0f);
            return rect;
        }

        static Transform CreateScrollContent(Transform parent, string name)
        {
            var scrollRect = parent.gameObject.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Elastic;
            scrollRect.elasticity = 0.08f;
            scrollRect.decelerationRate = 0.16f;
            scrollRect.scrollSensitivity = 38f;

            var viewport = new GameObject($"{name} Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viewport.transform.SetParent(parent, false);
            Stretch(viewport.GetComponent<RectTransform>(), 0f);
            viewport.GetComponent<Image>().color = Color.clear;
            viewport.GetComponent<Image>().raycastTarget = true;

            var content = new GameObject(name, typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;
            var layout = content.GetComponent<VerticalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = content.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.viewport = viewport.GetComponent<RectTransform>();
            scrollRect.content = contentRect;
            return content.transform;
        }

        static Button CreateFeatureButton(
            Transform parent,
            string title,
            string subtitle,
            LearningUiIconType iconType,
            Color accentColor,
            Color accentSurface,
            TMP_FontAsset font,
            UnityEngine.Events.UnityAction action)
        {
            var buttonObject = new GameObject($"{title} Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
            buttonObject.transform.SetParent(parent, false);
            SetPreferredHeight(buttonObject, 164f);
            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = buttonObject.GetComponent<Image>();
            button.onClick.AddListener(action);
            LearningUiTheme.StyleButton(button, LearningButtonStyle.Surface, LearningUiTheme.RadiusLarge);
            LearningUiTheme.StyleSurface(buttonObject.GetComponent<Image>(), LearningUiTheme.Card, LearningUiTheme.RadiusLarge, true, new Vector2(0f, -6f));

            var layout = buttonObject.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(26, 24, 24, 24);
            layout.spacing = 22f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var iconBubble = CreatePanel(buttonObject.transform, $"{title} Icon", accentSurface, LearningUiTheme.RadiusLarge);
            SetPreferredSize(iconBubble, 108f, 108f);
            CreateIcon(iconBubble.transform, iconType, accentColor, 52f);

            var copy = CreateVerticalGroup(buttonObject.transform, $"{title} Copy", 4f);
            SetFlexibleWidth(copy, 1f);
            var titleText = CreateText(copy.transform, $"{title} Title", title, 34f, font, LearningUiTheme.Ink);
            titleText.fontStyle = FontStyles.Bold;
            SetPreferredHeight(titleText.gameObject, 52f);
            var subtitleText = CreateText(copy.transform, $"{title} Subtitle", subtitle, 22f, font, LearningUiTheme.Muted);
            subtitleText.textWrappingMode = TextWrappingModes.Normal;
            SetPreferredHeight(subtitleText.gameObject, 62f);

            var chevron = CreateIcon(buttonObject.transform, LearningUiIconType.ChevronRight, accentColor, 34f);
            SetPreferredWidth(chevron.gameObject, 42f);
            return button;
        }

        static void CreateHomeStat(
            Transform parent,
            string label,
            LearningUiIconType iconType,
            Color iconColor,
            out TMP_Text valueText,
            TMP_FontAsset font)
        {
            var column = CreateVerticalGroup(parent, $"Home {label}", 2f);
            var icon = CreateIcon(column.transform, iconType, iconColor, 34f);
            SetPreferredHeight(icon.gameObject, 38f);
            valueText = CreateText(column.transform, $"Home {label} Value", "0", 29f, font, LearningUiTheme.Ink, TextAlignmentOptions.Center);
            valueText.fontStyle = FontStyles.Bold;
            SetPreferredHeight(valueText.gameObject, 42f);
            var caption = CreateText(column.transform, $"Home {label} Label", label, 17f, font, LearningUiTheme.Muted, TextAlignmentOptions.Center);
            SetPreferredHeight(caption.gameObject, 28f);
        }

        static Button CreateIconButton(
            Transform parent,
            string name,
            LearningUiIconType iconType,
            Color backgroundColor,
            Color iconColor,
            UnityEngine.Events.UnityAction action,
            float size)
        {
            var buttonObject = new GameObject($"{name} Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonObject.transform.SetParent(parent, false);
            var layout = buttonObject.GetComponent<LayoutElement>();
            layout.minWidth = size;
            layout.preferredWidth = size;
            layout.minHeight = size;
            layout.preferredHeight = size;
            layout.flexibleWidth = 0f;
            layout.flexibleHeight = 0f;
            var rect = buttonObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(size, size);
            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = buttonObject.GetComponent<Image>();
            button.onClick.AddListener(action);
            LearningUiTheme.StyleButton(button, LearningButtonStyle.Surface, LearningUiTheme.RadiusPill);
            LearningUiTheme.StyleSurface(buttonObject.GetComponent<Image>(), backgroundColor, LearningUiTheme.RadiusPill);
            CreateIcon(buttonObject.transform, iconType, iconColor, size * 0.48f);
            return button;
        }

        static Button CreateActionButton(
            Transform parent,
            string label,
            LearningUiIconType iconType,
            Color iconColor,
            TMP_FontAsset font,
            UnityEngine.Events.UnityAction action)
        {
            var buttonObject = new GameObject($"{label} Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement), typeof(VerticalLayoutGroup));
            buttonObject.transform.SetParent(parent, false);
            SetPreferredHeight(buttonObject, 104f);
            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = buttonObject.GetComponent<Image>();
            button.onClick.AddListener(action);
            LearningUiTheme.StyleButton(button, LearningButtonStyle.Surface, 18f);
            LearningUiTheme.StyleSurface(buttonObject.GetComponent<Image>(), LearningUiTheme.Card, 18f);
            var layout = buttonObject.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 9, 8);
            layout.spacing = 3f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var icon = CreateIcon(buttonObject.transform, iconType, iconColor, 42f);
            SetPreferredHeight(icon.gameObject, 46f);
            var text = CreateText(buttonObject.transform, "Label", label, 17f, font, LearningUiTheme.Ink, TextAlignmentOptions.Center);
            text.fontStyle = FontStyles.Bold;
            SetPreferredHeight(text.gameObject, 28f);
            return button;
        }

        static LearningUiIcon CreateIcon(Transform parent, LearningUiIconType iconType, Color color, float size)
        {
            var iconObject = new GameObject(
                $"{iconType} Icon",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(LearningUiIcon),
                typeof(LayoutElement));
            iconObject.transform.SetParent(parent, false);
            var rect = iconObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(size, size);
            var layout = iconObject.GetComponent<LayoutElement>();
            layout.minWidth = size;
            layout.preferredWidth = size;
            layout.minHeight = size;
            layout.preferredHeight = size;
            layout.flexibleWidth = 0f;
            layout.flexibleHeight = 0f;
            var icon = iconObject.GetComponent<LearningUiIcon>();
            icon.Icon = iconType;
            icon.color = color;
            return icon;
        }

        static void CreateStatCard(
            Transform parent,
            string label,
            LearningUiIconType iconType,
            Color iconColor,
            Color iconSurface,
            out TMP_Text valueText,
            TMP_FontAsset font)
        {
            var card = CreatePanel(parent, $"{label} Card", LearningUiTheme.Card, LearningUiTheme.RadiusLarge);
            LearningUiTheme.StyleSurface(card.GetComponent<Image>(), LearningUiTheme.Card, LearningUiTheme.RadiusLarge, true, new Vector2(0f, -5f));
            var layout = card.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(22, 22, 18, 18);
            layout.spacing = 5f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var icon = CreateIcon(card.transform, iconType, iconColor, 38f);
            icon.GetComponent<LayoutElement>().preferredHeight = 42f;
            valueText = CreateText(card.transform, $"{label} Value", "0", 36f, font, LearningUiTheme.Ink, TextAlignmentOptions.Center);
            valueText.fontStyle = FontStyles.Bold;
            valueText.color = LearningUiTheme.Ink;
            valueText.alpha = 1f;
            valueText.enabled = true;
            valueText.overflowMode = TextOverflowModes.Overflow;
            SetPreferredHeight(valueText.gameObject, 48f);
            var caption = CreateText(card.transform, $"{label} Label", label, 18f, font, LearningUiTheme.Muted, TextAlignmentOptions.Center);
            SetPreferredHeight(caption.gameObject, 32f);
        }

        static void CreateMetricChip(
            Transform parent,
            string scope,
            string label,
            LearningUiIconType iconType,
            Color iconColor,
            Color surfaceColor,
            out TMP_Text valueText,
            TMP_FontAsset font)
        {
            var chip = CreatePanel(
                parent,
                $"{scope} {label} Metric",
                surfaceColor,
                LearningUiTheme.RadiusMedium);
            var layout = chip.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 12, 12);
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var icon = CreateIcon(chip.transform, iconType, iconColor, 32f);
            SetPreferredSize(icon.gameObject, 34f, 34f);
            var copy = CreateVerticalGroup(chip.transform, $"{scope} {label} Copy", 0f, 66f);
            SetFlexibleWidth(copy, 1f);
            valueText = CreateText(
                copy.transform,
                $"{scope} {label} Value",
                "0",
                27f,
                font,
                LearningUiTheme.Ink);
            valueText.fontStyle = FontStyles.Bold;
            SetPreferredHeight(valueText.gameObject, 36f);
            var caption = CreateText(
                copy.transform,
                $"{scope} {label} Label",
                label,
                15f,
                font,
                LearningUiTheme.Muted);
            SetPreferredHeight(caption.gameObject, 26f);
        }

        static void CreateInlineMetricCounter(
            Transform parent,
            string label,
            Color surfaceColor,
            Color valueColor,
            out TMP_Text valueText,
            TMP_FontAsset font)
        {
            var counter = CreatePanel(
                parent,
                $"Overall {label} Metric",
                surfaceColor,
                LearningUiTheme.RadiusPill);
            SetPreferredSize(counter, 194f, 54f);
            var layout = counter.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(14, 14, 8, 8);
            layout.spacing = 7f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            valueText = CreateText(
                counter.transform,
                $"Overall {label} Value",
                "0",
                20f,
                font,
                valueColor,
                TextAlignmentOptions.Center);
            valueText.fontStyle = FontStyles.Bold;
            SetPreferredWidth(valueText.gameObject, 72f);
            SetPreferredHeight(valueText.gameObject, 36f);
            var caption = CreateText(
                counter.transform,
                $"Overall {label} Label",
                label.ToLowerInvariant(),
                15f,
                font,
                LearningUiTheme.Muted,
                TextAlignmentOptions.Center);
            SetFlexibleWidth(caption.gameObject, 1f);
            SetPreferredHeight(caption.gameObject, 34f);
        }

        void CreateWeeklyQuizQualityCard(Transform parent, TMP_FontAsset font)
        {
            var card = CreatePanel(
                parent,
                "Weekly Quiz Quality Card",
                LearningUiTheme.Background,
                LearningUiTheme.RadiusMedium,
                82f);
            var layout = card.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 12, 12);
            layout.spacing = 14f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var iconBubble = CreatePanel(
                card.transform,
                "Weekly Quiz Quality Icon",
                LearningUiTheme.SecondarySoft,
                LearningUiTheme.RadiusPill);
            SetPreferredSize(iconBubble, 54f, 54f);
            CreateIcon(iconBubble.transform, LearningUiIconType.Quiz, LearningUiTheme.SecondaryDark, 30f);

            var copy = CreateVerticalGroup(card.transform, "Weekly Quiz Quality Copy", 1f, 58f);
            SetFlexibleWidth(copy, 1f);
            var title = CreateText(
                copy.transform,
                "Weekly Quiz Quality Title",
                "Quiz answer quality",
                18f,
                font,
                LearningUiTheme.Ink);
            title.fontStyle = FontStyles.Bold;
            SetPreferredHeight(title.gameObject, 28f);
            m_WeeklyQuizAnswerDetailText = CreateText(
                copy.transform,
                "Weekly Quiz Answer Detail",
                "Complete a quiz to unlock answer quality",
                17f,
                font,
                LearningUiTheme.Muted);
            SetPreferredHeight(m_WeeklyQuizAnswerDetailText.gameObject, 28f);

            var accuracyBadge = CreatePanel(
                card.transform,
                "Weekly Quiz Accuracy Badge",
                LearningUiTheme.AccentSoft,
                LearningUiTheme.RadiusPill);
            SetPreferredSize(accuracyBadge, 132f, 50f);
            m_WeeklyQuizAccuracyBadge = accuracyBadge.GetComponent<Image>();
            m_WeeklyQuizAccuracyText = CreateText(
                accuracyBadge.transform,
                "Weekly Quiz Accuracy Value",
                "—",
                22f,
                font,
                LearningUiTheme.Warning,
                TextAlignmentOptions.Center);
            m_WeeklyQuizAccuracyText.fontStyle = FontStyles.Bold;
            Stretch(m_WeeklyQuizAccuracyText.rectTransform, 6f);
        }

        static void CreateRevisionQueueItem(
            Transform parent,
            int index,
            TMP_FontAsset font,
            out GameObject item,
            out TMP_Text wordText,
            out TMP_Text reasonText,
            out TMP_Text evidenceText,
            out Image evidenceBadge)
        {
            item = CreatePanel(
                parent,
                $"Revision Item {index + 1}",
                LearningUiTheme.Background,
                LearningUiTheme.RadiusMedium,
                96f);
            var layout = item.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 12, 12);
            layout.spacing = 14f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var rankBadge = CreatePanel(
                item.transform,
                $"Revision Rank {index + 1}",
                LearningUiTheme.PrimarySoft,
                LearningUiTheme.RadiusPill);
            SetPreferredSize(rankBadge, 52f, 52f);
            var rankText = CreateText(
                rankBadge.transform,
                "Rank",
                (index + 1).ToString(),
                22f,
                font,
                LearningUiTheme.PrimaryDark,
                TextAlignmentOptions.Center);
            rankText.fontStyle = FontStyles.Bold;
            Stretch(rankText.rectTransform, 4f);

            var copy = CreateVerticalGroup(item.transform, $"Revision Copy {index + 1}", 1f, 70f);
            SetFlexibleWidth(copy, 1f);
            wordText = CreateText(
                copy.transform,
                "Word",
                "Word",
                25f,
                font,
                LearningUiTheme.Ink);
            wordText.fontStyle = FontStyles.Bold;
            SetPreferredHeight(wordText.gameObject, 36f);
            reasonText = CreateText(
                copy.transform,
                "Reason",
                "Practice recommended",
                17f,
                font,
                LearningUiTheme.Muted);
            reasonText.textWrappingMode = TextWrappingModes.Normal;
            SetPreferredHeight(reasonText.gameObject, 32f);

            var evidence = CreatePanel(
                item.transform,
                $"Revision Evidence {index + 1}",
                LearningUiTheme.AccentSoft,
                LearningUiTheme.RadiusPill);
            SetPreferredSize(evidence, 178f, 48f);
            evidenceBadge = evidence.GetComponent<Image>();
            evidenceText = CreateText(
                evidence.transform,
                "Evidence",
                "NOT TESTED",
                15f,
                font,
                LearningUiTheme.Warning,
                TextAlignmentOptions.Center);
            evidenceText.fontStyle = FontStyles.Bold;
            Stretch(evidenceText.rectTransform, 8f);
        }

        static GameObject CreateRevisionEmptyState(Transform parent, TMP_FontAsset font)
        {
            var emptyState = CreatePanel(
                parent,
                "Revision Queue Empty State",
                LearningUiTheme.PrimarySoft,
                LearningUiTheme.RadiusMedium,
                124f);
            var layout = emptyState.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(20, 20, 18, 18);
            layout.spacing = 16f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var iconBubble = CreatePanel(
                emptyState.transform,
                "Revision Queue Clear Icon",
                LearningUiTheme.Primary,
                LearningUiTheme.RadiusPill);
            SetPreferredSize(iconBubble, 64f, 64f);
            CreateIcon(iconBubble.transform, LearningUiIconType.Refresh, Color.white, 34f);

            var copy = CreateVerticalGroup(emptyState.transform, "Revision Queue Empty Copy", 2f, 82f);
            SetFlexibleWidth(copy, 1f);
            var title = CreateText(
                copy.transform,
                "Revision Queue Empty Title",
                "Queue is clear",
                23f,
                font,
                LearningUiTheme.PrimaryDark);
            title.fontStyle = FontStyles.Bold;
            SetPreferredHeight(title.gameObject, 34f);
            var body = CreateText(
                copy.transform,
                "Revision Queue Empty Body",
                "Scan more objects or complete a quiz to build your next review set.",
                17f,
                font,
                LearningUiTheme.Muted);
            body.textWrappingMode = TextWrappingModes.Normal;
            SetPreferredHeight(body.gameObject, 46f);
            return emptyState;
        }

        static string FormatWordLabel(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Word";

            var trimmed = value.Trim();
            return char.ToUpperInvariant(trimmed[0]) + trimmed.Substring(1);
        }

        static void CreateActivityBar(
            Transform parent,
            int index,
            TMP_FontAsset font,
            out Image fill,
            out TMP_Text countLabel,
            out TMP_Text dayLabel)
        {
            var column = CreateVerticalGroup(parent, $"Activity Day {index + 1}", 6f);
            countLabel = CreateText(column.transform, $"Activity Count {index + 1}", "0", 16f, font, LearningUiTheme.Muted, TextAlignmentOptions.Center);
            countLabel.fontStyle = FontStyles.Bold;
            SetPreferredHeight(countLabel.gameObject, 24f);
            var track = CreatePanel(column.transform, $"Activity Track {index + 1}", LearningUiTheme.PrimarySoft, 14f, 140f);
            var fillObject = CreatePanel(track.transform, $"Activity Fill {index + 1}", LearningUiTheme.Primary, 12f);
            var fillRect = fillObject.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = new Vector2(1f, 0f);
            fillRect.pivot = new Vector2(0.5f, 0f);
            fillRect.offsetMin = new Vector2(8f, 0f);
            fillRect.offsetMax = new Vector2(-8f, 0f);
            fill = fillObject.GetComponent<Image>();
            fill.enabled = false;
            dayLabel = CreateText(column.transform, $"Activity Day Label {index + 1}", "Mon", 16f, font, LearningUiTheme.Muted, TextAlignmentOptions.Center);
            dayLabel.fontStyle = FontStyles.Bold;
            SetPreferredHeight(dayLabel.gameObject, 26f);
        }

        static float CalculateActivityBarFill(int activityAmount, int maximumDailyActivity)
        {
            if (activityAmount <= 0 || maximumDailyActivity <= 0)
                return 0f;

            return Mathf.Clamp01(activityAmount / (float)maximumDailyActivity);
        }

        static GameObject CreatePanel(
            Transform parent,
            string name,
            Color color,
            float radius = LearningUiTheme.RadiusLarge,
            float height = -1f)
        {
            var panel = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            panel.transform.SetParent(parent, false);
            LearningUiTheme.StyleSurface(panel.GetComponent<Image>(), color, radius);
            if (height > 0f)
                SetPreferredHeight(panel, height);
            return panel;
        }

        static GameObject CreatePill(
            Transform parent,
            string name,
            Color color,
            float height = -1f)
        {
            var pill = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasGroup),
                typeof(CanvasRenderer),
                typeof(LearningPillGraphic));
            pill.transform.SetParent(parent, false);
            var graphic = pill.GetComponent<LearningPillGraphic>();
            graphic.color = color;
            graphic.raycastTarget = false;
            if (height > 0f)
                SetPreferredHeight(pill, height);
            return pill;
        }

        static TMP_Text CreateText(
            Transform parent,
            string name,
            string value,
            float size,
            TMP_FontAsset font,
            Color? color = null,
            TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            textObject.transform.SetParent(parent, false);
            var text = textObject.GetComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = size;
            text.color = color ?? LearningUiTheme.Ink;
            text.alignment = alignment;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Truncate;
            text.raycastTarget = false;
            if (font != null)
                text.font = font;

            SetPreferredHeight(textObject, Mathf.Max(46f, size + 16f));
            NativeIndicText.Track(text);
            return text;
        }

        static Button CreateButton(
            Transform parent,
            string label,
            TMP_FontAsset font,
            UnityEngine.Events.UnityAction action,
            float height = 58f,
            LearningButtonStyle style = LearningButtonStyle.Surface,
            float radius = LearningUiTheme.RadiusMedium)
        {
            var buttonObject = new GameObject(
                $"{label} Button",
                typeof(RectTransform),
                typeof(Image),
                typeof(Button),
                typeof(LayoutElement));
            buttonObject.transform.SetParent(parent, false);
            var image = buttonObject.GetComponent<Image>();
            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(action);
            SetPreferredHeight(buttonObject, height);
            LearningUiTheme.StyleButton(button, style, radius);

            var textColor = style == LearningButtonStyle.Primary ||
                            style == LearningButtonStyle.Secondary ||
                            style == LearningButtonStyle.Success ||
                            style == LearningButtonStyle.Error
                ? Color.white
                : LearningUiTheme.Ink;
            var text = CreateText(buttonObject.transform, "Label", label, 23f, font, textColor, TextAlignmentOptions.Center);
            text.fontStyle = FontStyles.Bold;
            Stretch(text.rectTransform, 8f);
            return button;
        }

        void RefreshHomeSummary()
        {
            if (m_CompositionRoot?.ProgressRepository == null)
                return;

            var progress = m_CompositionRoot.ProgressRepository.LoadAll();
            var totalXp = CalculateTotalXp(progress);
            var level = Mathf.Max(1, (totalXp / 250) + 1);
            var activity = LearningActivityTracker.GetSnapshot();
            if (m_HomeTodayXpText != null)
                m_HomeTodayXpText.text = $"{activity.TodayXp} XP";
            if (m_HomeStreakText != null)
                m_HomeStreakText.text = $"{activity.StreakDays} day{(activity.StreakDays == 1 ? string.Empty : "s")}";
            if (m_HomeLevelText != null)
                m_HomeLevelText.text = $"Lv {level}";
        }

        static int CalculateTotalXp(IReadOnlyList<WordProgress> progress)
        {
            if (progress == null)
                return 0;

            return progress.Sum(item =>
                (item.TimesSeen * 5) +
                (item.PronunciationAttempts * 3) +
                (item.QuizCorrect * 15) +
                (item.QuizIncorrect * 3));
        }

        void SetAppBackgroundVisible(bool visible)
        {
            if (m_AppBackground == null)
                return;

            m_AppBackground.color = visible ? LearningUiTheme.Background : Color.clear;
            m_AppBackground.enabled = visible;
        }

        static void SetQuizButtonState(Button button, Color background, Color textColor)
        {
            if (button == null)
                return;

            var pressed = Color.Lerp(background, Color.black, 0.1f);
            LearningUiTheme.SetButtonColor(button, background, pressed);
            var colors = button.colors;
            colors.disabledColor = background;
            button.colors = colors;
            var label = button.GetComponentInChildren<TMP_Text>();
            if (label != null)
                label.color = textColor;
        }

        void AnimateScreenIn(CanvasGroup group, float verticalOffset)
        {
            if (group == null || !group.gameObject.activeInHierarchy)
                return;

            if (!UnityEngine.Application.isPlaying)
            {
                group.alpha = 1f;
                return;
            }

            StartCoroutine(AnimateCanvasGroupIn(group, verticalOffset));
        }

        static IEnumerator AnimateCanvasGroupIn(CanvasGroup group, float verticalOffset)
        {
            var rect = group.transform as RectTransform;
            var restingPosition = rect != null ? rect.anchoredPosition : Vector2.zero;
            var elapsed = 0f;
            const float duration = 0.28f;
            group.alpha = 0f;
            if (rect != null)
                rect.anchoredPosition = restingPosition + Vector2.down * verticalOffset;

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                var amount = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                group.alpha = amount;
                if (rect != null)
                    rect.anchoredPosition = Vector2.Lerp(restingPosition + Vector2.down * verticalOffset, restingPosition, amount);
                yield return null;
            }

            group.alpha = 1f;
            if (rect != null)
                rect.anchoredPosition = restingPosition;
        }

        static IEnumerator AnimateQuizResult(RectTransform target, bool wasCorrect)
        {
            if (target == null)
                yield break;

            if (wasCorrect)
            {
                var elapsed = 0f;
                const float duration = 0.34f;
                while (elapsed < duration)
                {
                    elapsed += Time.unscaledDeltaTime;
                    var amount = Mathf.Clamp01(elapsed / duration);
                    var bounce = 1f + Mathf.Sin(amount * Mathf.PI) * 0.08f;
                    target.localScale = Vector3.one * bounce;
                    yield return null;
                }
                target.localScale = Vector3.one;
                yield break;
            }

            var restingPosition = target.anchoredPosition;
            var shakeElapsed = 0f;
            const float shakeDuration = 0.34f;
            while (shakeElapsed < shakeDuration)
            {
                shakeElapsed += Time.unscaledDeltaTime;
                var amount = Mathf.Clamp01(shakeElapsed / shakeDuration);
                target.anchoredPosition = restingPosition + Vector2.right * Mathf.Sin(amount * Mathf.PI * 8f) * (1f - amount) * 18f;
                yield return null;
            }
            target.anchoredPosition = restingPosition;
        }

        static IEnumerator AnimatePopIn(RectTransform target, float startScale, float duration)
        {
            if (target == null)
                yield break;

            var elapsed = 0f;
            target.localScale = Vector3.one * startScale;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                var amount = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
                target.localScale = Vector3.one * Mathf.Lerp(startScale, 1f, amount);
                yield return null;
            }
            target.localScale = Vector3.one;
        }

        void AnimateFill(LearningPillGraphic fill, float targetAmount)
        {
            if (fill == null)
                return;

            targetAmount = Mathf.Clamp01(targetAmount);
            if (!UnityEngine.Application.isPlaying)
            {
                SetRoundedFillAmount(fill, targetAmount);
                return;
            }

            StartCoroutine(AnimateFillRoutine(fill, targetAmount));
        }

        static IEnumerator AnimateFillRoutine(LearningPillGraphic fill, float targetAmount)
        {
            var startAmount = fill.rectTransform.anchorMax.x;
            var elapsed = 0f;
            const float duration = 0.34f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                SetRoundedFillAmount(
                    fill,
                    Mathf.Lerp(startAmount, targetAmount, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration))));
                yield return null;
            }
            SetRoundedFillAmount(fill, targetAmount);
        }

        static void SetRoundedFillAmount(LearningPillGraphic fill, float amount)
        {
            if (fill == null)
                return;

            amount = Mathf.Clamp01(amount);
            var rect = fill.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(amount, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            fill.enabled = amount > 0.001f;
        }

        void ShowXpBurst(int amount)
        {
            if (m_XpBurstRect == null || m_XpBurstText == null)
                return;

            m_XpBurstText.text = $"+{amount} XP";
            if (UnityEngine.Application.isPlaying)
                StartCoroutine(AnimateXpBurst());
        }

        IEnumerator AnimateXpBurst()
        {
            var canvasGroup = m_XpBurstRect.GetComponent<CanvasGroup>();
            var restingPosition = new Vector2(0f, 120f);
            var elapsed = 0f;
            const float duration = 0.8f;
            canvasGroup.alpha = 1f;
            m_XpBurstRect.anchoredPosition = restingPosition;
            m_XpBurstRect.localScale = Vector3.one * 0.8f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                var amount = Mathf.Clamp01(elapsed / duration);
                canvasGroup.alpha = 1f - Mathf.Clamp01((amount - 0.55f) / 0.45f);
                m_XpBurstRect.anchoredPosition = restingPosition + Vector2.up * amount * 70f;
                m_XpBurstRect.localScale = Vector3.one * Mathf.Lerp(0.8f, 1.08f, Mathf.Sin(amount * Mathf.PI));
                yield return null;
            }
            canvasGroup.alpha = 0f;
            m_XpBurstRect.localScale = Vector3.one;
        }

        static void SetPreferredWidth(GameObject target, float width)
        {
            var layout = target.GetComponent<LayoutElement>() ?? target.AddComponent<LayoutElement>();
            layout.minWidth = width;
            layout.preferredWidth = width;
            layout.flexibleWidth = 0f;
        }

        static void SetPreferredHeight(GameObject target, float height)
        {
            var layout = target.GetComponent<LayoutElement>() ?? target.AddComponent<LayoutElement>();
            layout.minHeight = height;
            layout.preferredHeight = height;
            layout.flexibleHeight = 0f;
        }

        static void SetPreferredSize(GameObject target, float width, float height)
        {
            SetPreferredWidth(target, width);
            SetPreferredHeight(target, height);
        }

        static void SetFlexibleWidth(GameObject target, float flexibleWidth)
        {
            var layout = target.GetComponent<LayoutElement>() ?? target.AddComponent<LayoutElement>();
            layout.flexibleWidth = flexibleWidth;
        }

        static void SetMetricText(
            TMP_Text text,
            string value,
            Color color,
            FontStyles fontStyle = FontStyles.Bold)
        {
            if (text == null)
                return;

            text.text = value;
            text.color = color;
            text.alpha = 1f;
            text.enabled = true;
            text.fontStyle = fontStyle;
        }

        static void ResetScrollToTop(ScrollRect scrollRect)
        {
            if (scrollRect?.content == null)
                return;

            scrollRect.StopMovement();
            scrollRect.verticalNormalizedPosition = 1f;
            scrollRect.content.anchoredPosition = new Vector2(scrollRect.content.anchoredPosition.x, 0f);
        }

        static void Stretch(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
        }

        static void SetVisible(CanvasGroup group, bool visible)
        {
            if (group == null)
                return;

            group.alpha = visible ? 1f : 0f;
            group.interactable = visible;
            group.blocksRaycasts = visible;
        }

        void SetQuizOptionsVisible(bool visible)
        {
            foreach (var button in m_QuizOptionButtons)
            {
                if (button != null)
                    button.gameObject.SetActive(visible);
            }
        }

        void SetStatus(string message)
        {
            if (m_StatusText != null)
                m_StatusText.text = message;
        }

        void TransitionToScreen(ExperienceScreen nextScreen)
        {
            if (!UnityEngine.Application.isPlaying)
            {
                m_CurrentScreen = nextScreen;
                m_SessionSegmentStartedAt = -1f;
                return;
            }

            SaveSessionDuration(false);
            m_CurrentScreen = nextScreen;
            if (!IsLearningScreen(nextScreen))
                return;

            m_SessionSegmentStartedAt = Time.realtimeSinceStartup;
            m_NextSessionSaveAt = m_SessionSegmentStartedAt + 30f;
        }

        static bool IsLearningScreen(ExperienceScreen screen)
        {
            return screen == ExperienceScreen.Scan || screen == ExperienceScreen.Quiz;
        }

        float GetTotalLearningSeconds()
        {
            var savedSeconds = PlayerPrefs.GetFloat(TotalLearningSecondsKey, 0f);
            var activeSeconds = m_SessionSegmentStartedAt < 0f
                ? 0f
                : Mathf.Max(0f, Time.realtimeSinceStartup - m_SessionSegmentStartedAt);
            return savedSeconds + activeSeconds;
        }

        void SaveSessionDuration(bool continueTracking)
        {
            if (!UnityEngine.Application.isPlaying)
            {
                m_SessionSegmentStartedAt = -1f;
                return;
            }

            if (m_SessionSegmentStartedAt < 0f)
                return;

            var now = Time.realtimeSinceStartup;
            var segmentSeconds = Mathf.Max(0f, now - m_SessionSegmentStartedAt);
            if (segmentSeconds > 0f)
            {
                PlayerPrefs.SetFloat(
                    TotalLearningSecondsKey,
                    PlayerPrefs.GetFloat(TotalLearningSecondsKey, 0f) + segmentSeconds);
                PlayerPrefs.Save();
                LearningActivityTracker.RecordLearningSeconds(segmentSeconds);
            }

            m_SessionSegmentStartedAt = continueTracking && IsLearningScreen(m_CurrentScreen)
                ? now
                : -1f;
        }
    }
}
