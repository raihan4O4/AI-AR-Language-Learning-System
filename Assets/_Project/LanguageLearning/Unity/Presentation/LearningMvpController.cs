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
        const string TotalLearningSecondsKey = "aiar.language-learning.total-seconds.v1";

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
        CanvasGroup m_QuizCanvasGroup;
        CanvasGroup m_ProgressCanvasGroup;
        TMP_Text m_StatusText;
        TMP_Text m_QuizCounterText;
        TMP_Text m_QuizPromptText;
        TMP_Text m_QuizFeedbackText;
        TMP_Text m_ProgressSummaryText;
        TMP_Text m_RevisionText;
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

        public string CurrentScreenName => m_CurrentScreen.ToString();

        void Awake()
        {
            ResolveDependencies();
            EnsureUi();
            m_SessionSegmentStartedAt = Time.realtimeSinceStartup;
            m_NextSessionSaveAt = m_SessionSegmentStartedAt + 30f;
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
            SaveSessionDuration();
        }

        void Update()
        {
            if (Time.realtimeSinceStartup < m_NextSessionSaveAt)
                return;

            SaveSessionDuration();
            m_NextSessionSaveAt = Time.realtimeSinceStartup + 30f;
        }

        void OnApplicationPause(bool paused)
        {
            if (paused)
                SaveSessionDuration();
            else
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

            m_CurrentScreen = ExperienceScreen.Scan;
            SetVisible(m_HomeCanvasGroup, false);
            SetVisible(m_QuizCanvasGroup, false);
            SetVisible(m_ProgressCanvasGroup, false);
            SetVisible(m_ScanCanvasGroup, true);
            SetVisible(m_VocabularyCanvasGroup, m_SessionController?.CurrentVocabularyEntry != null);
            SetScanSpeakAvailable(false);
            RefreshLanguageButtonStyles();
            SetStatus("Starting camera...");

            if (m_ScanStartupCoroutine != null)
                StopCoroutine(m_ScanStartupCoroutine);
            m_ScanStartupCoroutine = StartCoroutine(ActivateScanRuntime());
        }

        public void ShowHome()
        {
            EnsureUi();
            m_CurrentScreen = ExperienceScreen.Home;
            DeactivateScanRuntime();
            SetVisible(m_HomeCanvasGroup, true);
            SetVisible(m_ScanCanvasGroup, false);
            SetVisible(m_QuizCanvasGroup, false);
            SetVisible(m_ProgressCanvasGroup, false);
            SetVisible(m_VocabularyCanvasGroup, false);
            SetStatus("Choose an activity to begin.");
        }

        public void ShowQuiz()
        {
            EnsureUi();
            if (!ResolveDependencies())
                return;

            m_CurrentScreen = ExperienceScreen.Quiz;
            DeactivateScanRuntime();
            m_SessionController.PauseRecognition();
            SetVisible(m_HomeCanvasGroup, false);
            SetVisible(m_ScanCanvasGroup, false);
            SetVisible(m_VocabularyCanvasGroup, false);
            SetVisible(m_ProgressCanvasGroup, false);
            SetVisible(m_QuizCanvasGroup, true);
            StartQuiz();
        }

        public void ShowProgress()
        {
            EnsureUi();
            if (!ResolveDependencies())
                return;

            m_CurrentScreen = ExperienceScreen.Progress;
            DeactivateScanRuntime();
            m_SessionController.PauseRecognition();
            SetVisible(m_HomeCanvasGroup, false);
            SetVisible(m_ScanCanvasGroup, false);
            SetVisible(m_VocabularyCanvasGroup, false);
            SetVisible(m_QuizCanvasGroup, false);
            SetVisible(m_ProgressCanvasGroup, true);
            RefreshProgress();
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

            for (var index = 0; index < m_QuizOptionButtons.Length; index++)
            {
                var colors = m_QuizOptionButtons[index].colors;
                if (index == m_CurrentQuestion.CorrectOptionIndex)
                    colors.normalColor = new Color(0.12f, 0.58f, 0.31f, 1f);
                else if (index == selectedOptionIndex)
                    colors.normalColor = new Color(0.72f, 0.18f, 0.18f, 1f);

                colors.selectedColor = colors.normalColor;
                m_QuizOptionButtons[index].colors = colors;
                m_QuizOptionButtons[index].interactable = false;
            }

            m_QuizFeedbackText.text = wasCorrect
                ? "Correct. Great recall!"
                : $"Not quite. The correct answer is {m_CurrentQuestion.Options[m_CurrentQuestion.CorrectOptionIndex].Text}.";
            m_QuizCounterText.text = $"Question {m_QuestionsAnswered}/{m_QuizLength}  •  Score {m_CorrectAnswers}";

            if (m_QuestionsAnswered >= m_QuizLength)
            {
                m_QuizFeedbackText.text += $"\nQuiz complete: {m_CorrectAnswers}/{m_QuestionsAnswered}.";
                SetNextQuestionButton(true, "Finish");
                return;
            }

            SetNextQuestionButton(true, "Next Question");
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
        }

        public void SpeakCurrentScanWord()
        {
            m_SessionController?.SpeakCurrentWord();
        }

        void StartQuiz()
        {
            m_QuestionsAnswered = 0;
            m_CorrectAnswers = 0;
            m_QuestionAnswered = false;
            m_CurrentQuestion = null;
            m_CurrentQuizEntry = null;
            m_QuizFeedbackText.text = string.Empty;
            SetNextQuestionButton(false, "Next Question");

            var catalog = m_CompositionRoot?.VocabularyRepository?.GetAll();
            if (catalog == null || catalog.Count < 2)
            {
                ShowQuizConfigurationError("At least two vocabulary entries are required to start a quiz.");
                return;
            }

            try
            {
                m_QuizEntries = m_CompositionRoot.QuizService.CreateQuestionSequence(
                    catalog,
                    m_SessionController.LearningLanguage,
                    m_QuizLength,
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

            m_QuizCounterText.text = $"Question {m_QuestionsAnswered + 1}/{m_QuizLength}  •  Score {m_CorrectAnswers}";
            m_QuizPromptText.text = m_CurrentQuestion.Prompt;
            m_QuizFeedbackText.text = "Choose the best translation.";
            SetNextQuestionButton(false, "Next Question");

            for (var index = 0; index < m_QuizOptionButtons.Length; index++)
            {
                var hasOption = index < m_CurrentQuestion.Options.Count;
                m_QuizOptionButtons[index].gameObject.SetActive(hasOption);
                if (!hasOption)
                    continue;

                m_QuizOptionLabels[index].text = m_CurrentQuestion.Options[index].Text;
                m_QuizOptionButtons[index].interactable = true;
                var colors = m_QuizOptionButtons[index].colors;
                colors.normalColor = new Color(0.16f, 0.19f, 0.24f, 1f);
                colors.selectedColor = colors.normalColor;
                m_QuizOptionButtons[index].colors = colors;
            }
        }

        void ShowQuizConfigurationError(string message)
        {
            m_QuizPromptText.text = message;
            m_QuizFeedbackText.text = "Choose another language or return to the main menu.";
            m_QuizEntries = Array.Empty<VocabularyEntry>();
            SetQuizOptionsVisible(false);
            SetNextQuestionButton(false, "Next Question");
        }

        void RefreshProgress()
        {
            var catalog = m_CompositionRoot.VocabularyRepository.GetAll();
            var progress = m_CompositionRoot.ProgressRepository.LoadAll();
            var summary = m_CompositionRoot.RetentionAnalysisService.CreateSummary(catalog, progress);
            var quizAttempts = progress.Sum(item => item.QuizAttempts);
            var quizCorrect = progress.Sum(item => item.QuizCorrect);
            var quizAccuracy = quizAttempts == 0 ? 0d : quizCorrect / (double)quizAttempts;
            var totalLearningSeconds = GetTotalLearningSeconds();
            var totalLearningTime = TimeSpan.FromSeconds(totalLearningSeconds);

            m_ProgressSummaryText.text =
                $"Words explored   {summary.SeenWords}/{summary.TotalWords}\n" +
                $"Words mastered   {summary.MasteredWords}\n" +
                $"Needs practice   {summary.StrugglingWords}\n" +
                $"Average mastery  {summary.AverageMastery:P0}\n" +
                $"Quiz accuracy    {quizAccuracy:P0}\n" +
                $"Learning time    {(int)totalLearningTime.TotalHours:00}:{totalLearningTime.Minutes:00}:{totalLearningTime.Seconds:00}";

            var recommendations = m_CompositionRoot.AdaptiveRevisionService.GetRecommendations(
                catalog,
                progress,
                DateTime.UtcNow,
                5);

            m_RevisionText.text = recommendations.Count == 0
                ? "Scan an object to begin building your revision plan."
                : "Recommended next\n" + string.Join(
                    "\n",
                    recommendations.Select((item, index) =>
                        $"{index + 1}. {item.Entry.CanonicalObjectLabel} — {item.Reason}"));
        }

        void OnRecognitionsPresented(IReadOnlyList<RecognitionPresentation> presentations)
        {
            if (m_CurrentScreen != ExperienceScreen.Scan)
                return;

            if (presentations == null || presentations.Count == 0)
            {
                SetScanSpeakAvailable(false);
                SetStatus("Scanning for supported objects...");
                return;
            }

            var matchedCount = presentations.Count(item => item.MatchedVocabulary);
            SetScanSpeakAvailable(matchedCount > 0);
            SetStatus(matchedCount > 0
                ? $"{matchedCount} learning object{(matchedCount == 1 ? string.Empty : "s")} found"
                : "Object found, but it is not in the learning catalog");
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
            RefreshLanguageButtonStyles(m_QuizLanguageButtons, languages);
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
                var colors = button.colors;
                colors.normalColor = selected
                    ? new Color(0.12f, 0.48f, 0.78f, 1f)
                    : new Color(0.16f, 0.19f, 0.24f, 1f);
                colors.selectedColor = colors.normalColor;
                button.colors = colors;
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

            RepairCanvasTransform(m_Canvas);
            var vocabularyPresenter = FindAnyObjectByType<ARVocabularyOverlayPresenter>();
            m_VocabularyCanvasGroup = vocabularyPresenter != null
                ? vocabularyPresenter.GetComponent<CanvasGroup>()
                : null;

            var font = ARVocabularyOverlayPresenter.GetMultilingualFontAsset() ??
                       FindAnyObjectByType<TMP_Text>()?.font;
            CreateStatus(font);
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
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
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
                m_Canvas.transform,
                "Learning Status",
                new Color(0.03f, 0.04f, 0.06f, 0.82f));
            var rect = statusObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.08f, 1f);
            rect.anchorMax = new Vector2(0.92f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -52f);
            rect.sizeDelta = new Vector2(0f, 72f);
            m_StatusText = CreateText(statusObject.transform, "Status", "Starting camera...", 27f, font);
            Stretch(m_StatusText.rectTransform, 24f);
        }

        void CreateNavigation(TMP_FontAsset font)
        {
            var navigation = CreatePanel(
                m_Canvas.transform,
                "Main Menu",
                new Color(0.03f, 0.04f, 0.06f, 0.94f));
            m_HomeCanvasGroup = navigation.GetComponent<CanvasGroup>();
            var rect = navigation.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.1f, 0.3f);
            rect.anchorMax = new Vector2(0.9f, 0.7f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var layout = navigation.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(36, 36, 34, 34);
            layout.spacing = 18f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var title = CreateText(navigation.transform, "Title", "AR Language Learning", 40f, font);
            title.fontStyle = FontStyles.Bold;
            title.alignment = TextAlignmentOptions.Center;
            SetPreferredHeight(title.gameObject, 76f);
            CreateButton(navigation.transform, "Scan", font, ShowScan, 82f);
            CreateButton(navigation.transform, "Quiz", font, ShowQuiz, 82f);
            CreateButton(navigation.transform, "Progress", font, ShowProgress, 82f);
        }

        void CreateScanControls(TMP_FontAsset font)
        {
            var controls = CreatePanel(
                m_Canvas.transform,
                "Scan Controls",
                new Color(0.03f, 0.04f, 0.06f, 0.9f));
            m_ScanCanvasGroup = controls.GetComponent<CanvasGroup>();
            var rect = controls.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.08f, 1f);
            rect.anchorMax = new Vector2(0.92f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -142f);
            rect.sizeDelta = new Vector2(0f, 174f);

            var layout = controls.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var languageSelector = CreateHorizontalGroup(
                controls.transform,
                "Scan Language Selector",
                64f,
                8f);
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
                    60f);
            }

            var actions = CreateHorizontalGroup(controls.transform, "Scan Actions", 68f, 10f);
            CreateButton(actions.transform, "Back", font, ShowHome, 64f);
            m_ScanSpeakButton = CreateButton(
                actions.transform,
                "Speak",
                font,
                SpeakCurrentScanWord,
                64f);
            SetScanSpeakAvailable(false);
            RefreshLanguageButtonStyles();
            SetVisible(m_ScanCanvasGroup, false);
        }

        void CreateQuizPanel(TMP_FontAsset font)
        {
            var panel = CreateCenteredContentPanel("Quiz Panel");
            m_QuizCanvasGroup = panel.GetComponent<CanvasGroup>();
            var layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(34, 34, 30, 30);
            layout.spacing = 16f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var languageTitle = CreateText(
                panel.transform,
                "Quiz Language Title",
                "Quiz language",
                22f,
                font);
            SetPreferredHeight(languageTitle.gameObject, 46f);
            CreateQuizLanguageSelector(panel.transform, font);

            m_QuizCounterText = CreateText(panel.transform, "Quiz Counter", "Question 1/5", 24f, font);
            m_QuizPromptText = CreateText(panel.transform, "Quiz Prompt", "Preparing question...", 34f, font);
            m_QuizPromptText.textWrappingMode = TextWrappingModes.Normal;
            SetPreferredHeight(m_QuizPromptText.gameObject, 120f);

            m_QuizOptionButtons = new Button[QuizOptionCount];
            m_QuizOptionLabels = new TMP_Text[QuizOptionCount];
            for (var index = 0; index < QuizOptionCount; index++)
            {
                var capturedIndex = index;
                m_QuizOptionButtons[index] = CreateButton(
                    panel.transform,
                    $"Quiz Option {index + 1}",
                    font,
                    () => AnswerQuizOption(capturedIndex),
                    72f);
                m_QuizOptionLabels[index] = m_QuizOptionButtons[index].GetComponentInChildren<TMP_Text>();
            }

            m_QuizFeedbackText = CreateText(panel.transform, "Quiz Feedback", string.Empty, 24f, font);
            m_QuizFeedbackText.textWrappingMode = TextWrappingModes.Normal;
            SetPreferredHeight(m_QuizFeedbackText.gameObject, 96f);

            var navigation = CreateHorizontalGroup(panel.transform, "Quiz Navigation", 68f, 10f);
            CreateButton(navigation.transform, "Back", font, ShowHome, 64f);
            CreateButton(navigation.transform, "Restart", font, RestartQuiz, 64f);
            m_NextQuestionButton = CreateButton(
                navigation.transform,
                "Next Question",
                font,
                NextQuizQuestion,
                64f);
            m_NextQuestionButtonLabel = m_NextQuestionButton.GetComponentInChildren<TMP_Text>();
            SetNextQuestionButton(false, "Next Question");
            SetVisible(m_QuizCanvasGroup, false);
        }

        void CreateQuizLanguageSelector(Transform parent, TMP_FontAsset font)
        {
            var selector = CreateHorizontalGroup(parent, "Quiz Language Selector", 64f, 8f);
            var languages = (LanguageCode[])Enum.GetValues(typeof(LanguageCode));
            m_QuizLanguageButtons = new Button[languages.Length];
            for (var index = 0; index < languages.Length; index++)
            {
                var language = languages[index];
                var capturedLanguageIndex = (int)language;
                m_QuizLanguageButtons[index] = CreateButton(
                    selector.transform,
                    language.ToDisplayName(),
                    font,
                    () => SelectQuizLanguage(capturedLanguageIndex),
                    60f);
            }
        }

        void CreateProgressPanel(TMP_FontAsset font)
        {
            var panel = CreateCenteredContentPanel("Progress Panel");
            m_ProgressCanvasGroup = panel.GetComponent<CanvasGroup>();
            var layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(38, 38, 34, 34);
            layout.spacing = 24f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var title = CreateText(panel.transform, "Progress Title", "Learning progress", 38f, font);
            title.fontStyle = FontStyles.Bold;
            m_ProgressSummaryText = CreateText(panel.transform, "Progress Summary", string.Empty, 28f, font);
            m_ProgressSummaryText.textWrappingMode = TextWrappingModes.Normal;
            SetPreferredHeight(m_ProgressSummaryText.gameObject, 260f);
            m_RevisionText = CreateText(panel.transform, "Revision Recommendations", string.Empty, 25f, font);
            m_RevisionText.textWrappingMode = TextWrappingModes.Normal;
            SetPreferredHeight(m_RevisionText.gameObject, 280f);

            var navigation = CreateHorizontalGroup(panel.transform, "Progress Navigation", 68f, 10f);
            CreateButton(navigation.transform, "Back", font, ShowHome, 64f);
            CreateButton(navigation.transform, "Refresh", font, RefreshProgress, 64f);
            SetVisible(m_ProgressCanvasGroup, false);
        }

        static GameObject CreateHorizontalGroup(
            Transform parent,
            string name,
            float height,
            float spacing)
        {
            var group = new GameObject(name, typeof(RectTransform), typeof(LayoutElement), typeof(HorizontalLayoutGroup));
            group.transform.SetParent(parent, false);
            SetPreferredHeight(group, height);
            var layout = group.GetComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            return group;
        }

        GameObject CreateCenteredContentPanel(string name)
        {
            var panel = CreatePanel(
                m_Canvas.transform,
                name,
                new Color(0.035f, 0.045f, 0.065f, 0.97f));
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.06f, 0.16f);
            rect.anchorMax = new Vector2(0.94f, 0.88f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return panel;
        }

        static GameObject CreatePanel(Transform parent, string name, Color color)
        {
            var panel = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            panel.transform.SetParent(parent, false);
            var image = panel.GetComponent<Image>();
            image.color = color;
            return panel;
        }

        static TMP_Text CreateText(
            Transform parent,
            string name,
            string value,
            float size,
            TMP_FontAsset font)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            textObject.transform.SetParent(parent, false);
            var text = textObject.GetComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = size;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Truncate;
            text.raycastTarget = false;
            if (font != null)
                text.font = font;

            SetPreferredHeight(textObject, Mathf.Max(54f, size + 20f));
            return text;
        }

        static Button CreateButton(
            Transform parent,
            string label,
            TMP_FontAsset font,
            UnityEngine.Events.UnityAction action,
            float height = 58f)
        {
            var buttonObject = new GameObject(
                $"{label} Button",
                typeof(RectTransform),
                typeof(Image),
                typeof(Button),
                typeof(LayoutElement));
            buttonObject.transform.SetParent(parent, false);
            var image = buttonObject.GetComponent<Image>();
            image.color = new Color(0.16f, 0.19f, 0.24f, 1f);
            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(action);
            SetPreferredHeight(buttonObject, height);

            var text = CreateText(buttonObject.transform, "Label", label, 24f, font);
            text.alignment = TextAlignmentOptions.Center;
            Stretch(text.rectTransform, 8f);
            return button;
        }

        static void SetPreferredHeight(GameObject target, float height)
        {
            var layout = target.GetComponent<LayoutElement>() ?? target.AddComponent<LayoutElement>();
            layout.minHeight = height;
            layout.preferredHeight = height;
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

        float GetTotalLearningSeconds()
        {
            var savedSeconds = PlayerPrefs.GetFloat(TotalLearningSecondsKey, 0f);
            var activeSeconds = Mathf.Max(0f, Time.realtimeSinceStartup - m_SessionSegmentStartedAt);
            return savedSeconds + activeSeconds;
        }

        void SaveSessionDuration()
        {
            if (m_SessionSegmentStartedAt <= 0f)
                return;

            var now = Time.realtimeSinceStartup;
            var segmentSeconds = Mathf.Max(0f, now - m_SessionSegmentStartedAt);
            if (segmentSeconds > 0f)
            {
                PlayerPrefs.SetFloat(
                    TotalLearningSecondsKey,
                    PlayerPrefs.GetFloat(TotalLearningSecondsKey, 0f) + segmentSeconds);
                PlayerPrefs.Save();
            }

            m_SessionSegmentStartedAt = now;
        }
    }
}
