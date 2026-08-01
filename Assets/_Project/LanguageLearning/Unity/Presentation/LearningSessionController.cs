using System;
using System.Collections.Generic;
using AIAR.LanguageLearning.Application.Models;
using AIAR.LanguageLearning.Domain;
using AIAR.LanguageLearning.Unity.Bootstrap;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

namespace AIAR.LanguageLearning.Unity.Presentation
{
    public sealed class LearningSessionController : MonoBehaviour
    {
        [SerializeField]
        LanguageLearningCompositionRoot m_CompositionRoot;

        [SerializeField]
        LanguageCode m_LearningLanguage = LanguageCode.Hindi;

        [SerializeField]
        float m_MinimumRecognitionConfidence = 0.35f;

        [SerializeField]
        VocabularyDetectedEvent m_OnVocabularyDetected = new VocabularyDetectedEvent();

        [SerializeField]
        VocabularyMissedEvent m_OnVocabularyMissed = new VocabularyMissedEvent();

        [SerializeField]
        bool m_AutoCreateBoundingBoxOverlay = true;

        [SerializeField]
        bool m_DebugRecognitionLogging;

        [SerializeField, Min(1)]
        int m_MinimumStableFrames = 1;

        [SerializeField, Min(0f)]
        float m_DetectionHoldSeconds = 2f;

        [SerializeField, Min(0f)]
        float m_ViewRecordingCooldownSeconds = 12f;

        readonly List<RecognitionPresentation> m_RecognitionPresentations = new List<RecognitionPresentation>(8);
        readonly List<ObjectRecognitionResult> m_StableRecognitionResults = new List<ObjectRecognitionResult>(8);
        readonly Dictionary<string, TrackedRecognition> m_TrackedRecognitions =
            new Dictionary<string, TrackedRecognition>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, float> m_LastViewRecordedAt =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        VocabularyLookupResult m_CurrentLookup;
        NormalizedBoundingBox m_CurrentBoundingBox;
        bool m_IsSubscribed;

        public event Action<RecognitionPresentation> RecognitionPresented;

        public event Action<IReadOnlyList<RecognitionPresentation>> RecognitionsPresented;

        public event Action<string, string, string, string> VocabularyPresented;

        public VocabularyDetectedEvent onVocabularyDetected => m_OnVocabularyDetected;

        public VocabularyMissedEvent onVocabularyMissed => m_OnVocabularyMissed;

        public LanguageCode LearningLanguage => m_LearningLanguage;

        public VocabularyEntry CurrentVocabularyEntry => m_CurrentLookup?.Entry;

        void Awake()
        {
            ResolveCompositionRoot();
        }

        void OnEnable()
        {
            SubscribeToRecognizer();
        }

        void Start()
        {
            SubscribeToRecognizer();
            EnsureDetectionOverlay();
            EnsureWorldVocabularyPresenter();
            EnsureMvpController();
        }

        void OnDisable()
        {
            UnsubscribeFromRecognizer();
            m_CompositionRoot?.ObjectRecognitionService?.StopRecognition();
        }

        public void SetLearningLanguage(int languageIndex)
        {
            if (!Enum.IsDefined(typeof(LanguageCode), languageIndex))
                return;

            m_LearningLanguage = (LanguageCode)languageIndex;

            if (m_CurrentLookup?.Entry != null)
                PublishVocabulary(
                    m_CurrentLookup.Entry,
                    m_CurrentLookup.RecognizedLabel,
                    m_CurrentLookup.RecognitionConfidence,
                    m_CurrentBoundingBox);
        }

        public void SpeakCurrentWord()
        {
            if (m_CurrentLookup == null || !m_CurrentLookup.Found || m_CompositionRoot?.TextToSpeechService == null)
                return;

            m_CompositionRoot.TextToSpeechService.Speak(m_CurrentLookup.Translation);
            m_CompositionRoot.LearningService.RecordPronunciationAttempt(m_CurrentLookup.Entry, m_LearningLanguage);
            if (UnityEngine.Application.isPlaying)
                LearningActivityTracker.RecordPronunciationPlayed();
        }

        public void ResumeRecognition()
        {
            SubscribeToRecognizer();
            m_CompositionRoot?.ObjectRecognitionService?.StartRecognition();
        }

        public void PauseRecognition()
        {
            m_CompositionRoot?.ObjectRecognitionService?.StopRecognition();
            PublishRecognitionPresentations(Array.Empty<RecognitionPresentation>());
        }

        /// <summary>
        /// Clears only the transient object presentation state. Learning progress and the selected
        /// language remain untouched, while a subsequent detector update can populate a fresh card.
        /// </summary>
        public void ResetObjectDisplayState()
        {
            m_CurrentLookup = null;
            m_CurrentBoundingBox = default;
            m_RecognitionPresentations.Clear();
            m_StableRecognitionResults.Clear();
            m_TrackedRecognitions.Clear();
            PublishRecognitionPresentations(Array.Empty<RecognitionPresentation>());
        }

        void OnObjectRecognized(ObjectRecognitionResult recognitionResult)
        {
            if (recognitionResult == null)
            {
                OnObjectRecognitionsUpdated(Array.Empty<ObjectRecognitionResult>());
                return;
            }

            OnObjectRecognitionsUpdated(new[] { recognitionResult });
        }

        void OnObjectRecognitionsUpdated(IReadOnlyList<ObjectRecognitionResult> recognitionResults)
        {
            var stableResults = UpdateStableRecognitions(recognitionResults);
            if (stableResults.Count == 0)
            {
                LogRecognition("No temporally stable detection is currently available; clearing recognition overlay.");
                PublishRecognitionPresentations(Array.Empty<RecognitionPresentation>());
                return;
            }

            if (!ResolveCompositionRoot())
                return;

            m_RecognitionPresentations.Clear();
            VocabularyLookupResult primaryLookup = null;
            ObjectRecognitionResult primaryRecognitionResult = null;
            ObjectRecognitionResult firstPresentedRecognitionResult = null;

            for (var i = 0; i < stableResults.Count; i++)
            {
                var recognitionResult = stableResults[i];
                if (recognitionResult == null || recognitionResult.Confidence < m_MinimumRecognitionConfidence)
                    continue;

                LogRecognition(
                    $"Received recognition label='{recognitionResult.Label}', confidence={recognitionResult.Confidence:0.000}, " +
                    $"source='{recognitionResult.TrackingId}', box={FormatBoundingBox(recognitionResult.BoundingBox)}.");

                var lookup = m_CompositionRoot.LearningService.FindVocabularyForObject(recognitionResult, m_LearningLanguage);
                if (lookup.Found)
                {
                    m_RecognitionPresentations.Add(new RecognitionPresentation(
                        lookup.RecognizedLabel,
                        lookup.Translation.Text,
                        lookup.RecognitionConfidence,
                        recognitionResult.BoundingBox,
                        true));

                    if (primaryLookup == null)
                    {
                        primaryLookup = lookup;
                        primaryRecognitionResult = recognitionResult;
                    }
                }
                else
                {
                    m_RecognitionPresentations.Add(new RecognitionPresentation(
                        recognitionResult.Label,
                        recognitionResult.Label,
                        recognitionResult.Confidence,
                        recognitionResult.BoundingBox,
                        false));

                    LogRecognition($"No vocabulary entry matched '{recognitionResult.Label}'. Rendering raw detector label.");
                }

                if (firstPresentedRecognitionResult == null)
                    firstPresentedRecognitionResult = recognitionResult;
            }

            if (m_RecognitionPresentations.Count == 0)
            {
                LogRecognition(
                    $"Received {stableResults.Count} stable detector result(s), but none passed the UI confidence threshold of " +
                    $"{m_MinimumRecognitionConfidence:0.00}.");
                PublishRecognitionPresentations(Array.Empty<RecognitionPresentation>());
                return;
            }

            PublishRecognitionPresentations(m_RecognitionPresentations);

            if (primaryLookup != null && primaryRecognitionResult != null)
            {
                m_CurrentLookup = primaryLookup;
                RecordObjectViewIfDue(primaryLookup.Entry);
                PublishVocabulary(
                    primaryLookup.Entry,
                    primaryLookup.RecognizedLabel,
                    primaryLookup.RecognitionConfidence,
                    primaryRecognitionResult.BoundingBox,
                    false);
                return;
            }

            if (firstPresentedRecognitionResult != null)
            {
                m_CurrentBoundingBox = firstPresentedRecognitionResult.BoundingBox;
                m_OnVocabularyMissed.Invoke(firstPresentedRecognitionResult.Label);
            }
        }

        bool ResolveCompositionRoot()
        {
            if (m_CompositionRoot == null)
                m_CompositionRoot = LanguageLearningCompositionRoot.Instance ?? FindAnyObjectByType<LanguageLearningCompositionRoot>();

            if (m_CompositionRoot == null)
            {
                Debug.LogWarning("LearningSessionController could not find LanguageLearningCompositionRoot.");
                return false;
            }

            m_CompositionRoot.EnsureComposed();
            return true;
        }

        void SubscribeToRecognizer()
        {
            if (m_IsSubscribed)
                return;

            if (!ResolveCompositionRoot())
                return;

            if (m_CompositionRoot.ObjectRecognitionService == null)
            {
                Debug.LogWarning("No object recognition service configured for language learning.");
                return;
            }

            m_CompositionRoot.ObjectRecognitionService.ObjectRecognitionsUpdated += OnObjectRecognitionsUpdated;
            m_IsSubscribed = true;
        }

        void UnsubscribeFromRecognizer()
        {
            if (!m_IsSubscribed || m_CompositionRoot?.ObjectRecognitionService == null)
                return;

            m_CompositionRoot.ObjectRecognitionService.ObjectRecognitionsUpdated -= OnObjectRecognitionsUpdated;
            m_IsSubscribed = false;
        }

        void PublishVocabulary(
            VocabularyEntry entry,
            string recognizedLabel,
            float recognitionConfidence,
            NormalizedBoundingBox boundingBox = default,
            bool publishRecognitionPresentation = true)
        {
            if (entry == null)
                return;

            if (!entry.TryGetTranslation(m_LearningLanguage, out var translation))
                return;

            m_CurrentBoundingBox = boundingBox.IsValid
                ? boundingBox
                : NormalizedBoundingBox.Centered(0.5f, 0.35f);

            var english = entry.TryGetTranslation(LanguageCode.English, out var englishTranslation)
                ? englishTranslation.Text
                : entry.CanonicalObjectLabel;

            m_CurrentLookup = VocabularyLookupResult.FoundEntry(
                entry,
                translation,
                recognizedLabel,
                recognitionConfidence);

            m_OnVocabularyDetected.Invoke(
                translation.Text,
                english,
                translation.PhoneticHint,
                translation.Language.ToDisplayName());
            VocabularyPresented?.Invoke(
                translation.Text,
                english,
                translation.PhoneticHint,
                translation.Language.ToDisplayName());

            if (publishRecognitionPresentation)
            {
                PublishRecognitionPresentations(new[]
                {
                    new RecognitionPresentation(
                        recognizedLabel,
                        translation.Text,
                        recognitionConfidence,
                        m_CurrentBoundingBox,
                        true)
                });
            }

            LogRecognition(
                $"Published vocabulary for '{recognizedLabel}' as '{translation.Text}' with box={FormatBoundingBox(m_CurrentBoundingBox)}.");
        }

        void PublishRecognitionPresentations(IReadOnlyList<RecognitionPresentation> presentations)
        {
            var snapshot = presentations == null || presentations.Count == 0
                ? Array.Empty<RecognitionPresentation>()
                : CopyPresentations(presentations);

            RecognitionsPresented?.Invoke(snapshot);

            if (snapshot.Length > 0)
                RecognitionPresented?.Invoke(snapshot[0]);
        }

        static RecognitionPresentation[] CopyPresentations(IReadOnlyList<RecognitionPresentation> presentations)
        {
            var snapshot = new RecognitionPresentation[presentations.Count];
            for (var i = 0; i < presentations.Count; i++)
                snapshot[i] = presentations[i];

            return snapshot;
        }

        void EnsureDetectionOverlay()
        {
            if (!m_AutoCreateBoundingBoxOverlay || FindAnyObjectByType<PooledDetectionOverlay>() != null)
                return;

            var canvas = FindAnyObjectByType<Canvas>();
            if (canvas == null)
            {
                var canvasObject = new GameObject("LanguageLearning UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 100;

                var scaler = canvasObject.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1080f, 1920f);
                scaler.matchWidthOrHeight = 0.5f;
            }

            var boxObject = new GameObject(
                "Recognition Bounding Box",
                typeof(RectTransform),
                typeof(CanvasGroup),
                typeof(Image),
                typeof(PooledDetectionOverlay));
            boxObject.transform.SetParent(canvas.transform, false);
            boxObject.transform.SetAsFirstSibling();

            var rectTransform = boxObject.GetComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.sizeDelta = new Vector2(360f, 260f);

            var image = boxObject.GetComponent<Image>();
            image.color = Color.clear;
            image.raycastTarget = false;

            var labelObject = new GameObject("Detection Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(boxObject.transform, false);

            var labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 1f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.pivot = new Vector2(0f, 1f);
            labelRect.anchoredPosition = new Vector2(0f, 42f);
            labelRect.sizeDelta = new Vector2(0f, 40f);

            var labelText = labelObject.GetComponent<TextMeshProUGUI>();
            labelText.text = "detecting...";
            labelText.fontSize = 28f;
            labelText.fontStyle = FontStyles.Bold;
            labelText.color = Color.white;
            labelText.alignment = TextAlignmentOptions.Left;
            labelText.textWrappingMode = TextWrappingModes.NoWrap;
            labelText.overflowMode = TextOverflowModes.Truncate;

            var presenter = boxObject.GetComponent<PooledDetectionOverlay>();
            presenter.Configure(this, rectTransform, labelText, boxObject.GetComponent<CanvasGroup>());
            LogRecognition("Created the pooled detection overlay at runtime.");
        }

        void EnsureMvpController()
        {
            var mvpController = FindAnyObjectByType<LearningMvpController>();
            if (mvpController == null)
                mvpController = gameObject.AddComponent<LearningMvpController>();

            mvpController.Configure(this, m_CompositionRoot);
        }

        void EnsureWorldVocabularyPresenter()
        {
            var worldPresenter = FindAnyObjectByType<ARWorldVocabularyPresenter>();
            if (worldPresenter == null)
                worldPresenter = gameObject.AddComponent<ARWorldVocabularyPresenter>();

            var cameraManager = FindAnyObjectByType<ARCameraManager>();
            worldPresenter.Configure(
                this,
                FindAnyObjectByType<ARRaycastManager>(),
                cameraManager != null ? cameraManager.GetComponent<Camera>() : Camera.main);
        }

        IReadOnlyList<ObjectRecognitionResult> UpdateStableRecognitions(
            IReadOnlyList<ObjectRecognitionResult> recognitionResults)
        {
            var now = Time.unscaledTime;

            if (recognitionResults != null)
            {
                for (var index = 0; index < recognitionResults.Count; index++)
                {
                    var result = recognitionResults[index];
                    if (result == null || result.Confidence < m_MinimumRecognitionConfidence)
                        continue;

                    var key = result.Label;
                    if (!m_TrackedRecognitions.TryGetValue(key, out var tracked))
                    {
                        tracked = new TrackedRecognition(result, now);
                        m_TrackedRecognitions[key] = tracked;
                    }
                    else
                    {
                        tracked.Update(result, now);
                    }
                }
            }

            m_StableRecognitionResults.Clear();
            var expiredKeys = new List<string>();
            foreach (var pair in m_TrackedRecognitions)
            {
                var tracked = pair.Value;
                if (now - tracked.LastSeenAt > m_DetectionHoldSeconds)
                {
                    expiredKeys.Add(pair.Key);
                    continue;
                }

                var requiredFrames = string.Equals(
                    tracked.Latest.TrackingId,
                    "mock",
                    StringComparison.OrdinalIgnoreCase)
                    ? 1
                    : Mathf.Max(1, m_MinimumStableFrames);

                if (tracked.ConsecutiveHits >= requiredFrames)
                    m_StableRecognitionResults.Add(tracked.ToSmoothedResult());
            }

            foreach (var expiredKey in expiredKeys)
                m_TrackedRecognitions.Remove(expiredKey);

            m_StableRecognitionResults.Sort((left, right) => right.Confidence.CompareTo(left.Confidence));
            return m_StableRecognitionResults;
        }

        void RecordObjectViewIfDue(VocabularyEntry entry)
        {
            if (entry == null)
                return;

            var now = Time.unscaledTime;
            if (m_LastViewRecordedAt.TryGetValue(entry.Id, out var lastRecordedAt) &&
                now - lastRecordedAt < m_ViewRecordingCooldownSeconds)
            {
                return;
            }

            m_LastViewRecordedAt[entry.Id] = now;
            m_CompositionRoot.LearningService.RecordObjectViewed(entry, m_LearningLanguage);
            if (UnityEngine.Application.isPlaying)
                LearningActivityTracker.RecordObjectScan();
        }

        void LogRecognition(string message)
        {
            if (m_DebugRecognitionLogging)
                Debug.Log($"[LearningSessionController] {message}");
        }

        static string FormatBoundingBox(NormalizedBoundingBox boundingBox)
        {
            return $"x={boundingBox.X:0.000}, y={boundingBox.Y:0.000}, w={boundingBox.Width:0.000}, h={boundingBox.Height:0.000}";
        }

        sealed class TrackedRecognition
        {
            const float SmoothingFactor = 0.35f;

            public TrackedRecognition(ObjectRecognitionResult result, float now)
            {
                Latest = result;
                SmoothedBox = result.BoundingBox;
                SmoothedConfidence = result.Confidence;
                LastSeenAt = now;
                ConsecutiveHits = 1;
            }

            public ObjectRecognitionResult Latest { get; private set; }

            public NormalizedBoundingBox SmoothedBox { get; private set; }

            public float SmoothedConfidence { get; private set; }

            public float LastSeenAt { get; private set; }

            public int ConsecutiveHits { get; private set; }

            public void Update(ObjectRecognitionResult result, float now)
            {
                Latest = result;
                SmoothedBox = Lerp(SmoothedBox, result.BoundingBox, SmoothingFactor);
                SmoothedConfidence = Mathf.Lerp(SmoothedConfidence, result.Confidence, SmoothingFactor);
                LastSeenAt = now;
                ConsecutiveHits++;
            }

            public ObjectRecognitionResult ToSmoothedResult()
            {
                return new ObjectRecognitionResult(
                    Latest.Label,
                    SmoothedConfidence,
                    $"stable:{Latest.Label}",
                    SmoothedBox);
            }

            static NormalizedBoundingBox Lerp(
                NormalizedBoundingBox current,
                NormalizedBoundingBox target,
                float amount)
            {
                return new NormalizedBoundingBox(
                    Mathf.Lerp(current.X, target.X, amount),
                    Mathf.Lerp(current.Y, target.Y, amount),
                    Mathf.Lerp(current.Width, target.Width, amount),
                    Mathf.Lerp(current.Height, target.Height, amount));
            }
        }

        [Serializable]
        public sealed class VocabularyDetectedEvent : UnityEvent<string, string, string, string>
        {
        }

        [Serializable]
        public sealed class VocabularyMissedEvent : UnityEvent<string>
        {
        }
    }

    public readonly struct RecognitionPresentation
    {
        public RecognitionPresentation(
            string label,
            string displayText,
            float confidence,
            NormalizedBoundingBox boundingBox,
            bool matchedVocabulary)
        {
            Label = label;
            DisplayText = displayText;
            Confidence = confidence;
            BoundingBox = boundingBox;
            MatchedVocabulary = matchedVocabulary;
        }

        public string Label { get; }

        public string DisplayText { get; }

        public float Confidence { get; }

        public NormalizedBoundingBox BoundingBox { get; }

        public bool MatchedVocabulary { get; }
    }

    public abstract class PooledDetectionOverlayBase : MonoBehaviour
    {
        [SerializeField]
        LearningSessionController m_SessionController;

        [SerializeField]
        RectTransform m_BoxRectTransform;

        [SerializeField]
        TMP_Text m_LabelText;

        [SerializeField]
        CanvasGroup m_CanvasGroup;

        [SerializeField, Min(1)]
        int m_MaxRenderedDetections = 8;

        [SerializeField]
        float m_MinimumBoxWidth = 80f;

        [SerializeField]
        float m_MinimumBoxHeight = 64f;

        [SerializeField]
        Color m_BorderColor = new Color(0.07f, 0.88f, 0.48f, 1f);

        [SerializeField, Min(1f)]
        float m_BorderThickness = 3f;

        [SerializeField]
        bool m_DebugLogging;

        readonly List<BoxView> m_BoxViews = new List<BoxView>(8);
        RectTransform m_ParentRectTransform;

        protected virtual void Awake()
        {
            CacheReferences();
            NativeIndicText.Track(m_LabelText);
            EnsureBoxView(0);
            HideAllBoxViews();
        }

        protected virtual void OnEnable()
        {
            CacheReferences();
            if (m_SessionController != null)
                m_SessionController.RecognitionsPresented += OnRecognitionsPresented;
        }

        protected virtual void OnDisable()
        {
            if (m_SessionController != null)
                m_SessionController.RecognitionsPresented -= OnRecognitionsPresented;
        }

        void OnRecognitionPresented(RecognitionPresentation presentation)
        {
            OnRecognitionsPresented(new[] { presentation });
        }

        void OnRecognitionsPresented(IReadOnlyList<RecognitionPresentation> presentations)
        {
            CacheReferences();

            if (presentations == null || presentations.Count == 0)
            {
                HideAllBoxViews();
                LogDebug("Cleared detection boxes because no detections were published.");
                return;
            }

            if (m_ParentRectTransform == null)
            {
                Debug.LogWarning("[PooledDetectionOverlay] Cannot render detection boxes because the overlay is missing a parent RectTransform.");
                HideAllBoxViews();
                return;
            }

            var parentSize = m_ParentRectTransform.rect.size;
            if (parentSize.x <= 0f || parentSize.y <= 0f)
            {
                Debug.LogWarning($"[PooledDetectionOverlay] Cannot render detection boxes because the parent size is {parentSize.x:0.0}x{parentSize.y:0.0}.");
                HideAllBoxViews();
                return;
            }

            var renderedCount = 0;
            var maxRenderedDetections = Mathf.Max(1, m_MaxRenderedDetections);
            for (var i = 0; i < presentations.Count && renderedCount < maxRenderedDetections; i++)
            {
                var presentation = presentations[i];
                if (!presentation.BoundingBox.IsValid)
                    continue;

                var boxView = EnsureBoxView(renderedCount);
                if (boxView == null || boxView.RectTransform == null)
                    continue;

                RenderBox(boxView, presentation, parentSize);
                renderedCount++;
            }

            HideBoxViewsFrom(renderedCount);

            if (renderedCount == 0)
            {
                Debug.LogWarning("[PooledDetectionOverlay] Detection results arrived, but none had a valid bounding box.");
                return;
            }

            LogDebug($"Rendered {renderedCount} detection box(es) from {presentations.Count} published result(s).");
        }

        public void Configure(
            LearningSessionController sessionController,
            RectTransform boxRectTransform,
            TMP_Text labelText,
            CanvasGroup canvasGroup)
        {
            m_SessionController = sessionController;
            m_BoxRectTransform = boxRectTransform;
            m_LabelText = labelText;
            m_CanvasGroup = canvasGroup;
            CacheReferences();
            EnsureBoxView(0);
            HideAllBoxViews();
        }

        void CacheReferences()
        {
            if (m_SessionController == null)
                m_SessionController = FindAnyObjectByType<LearningSessionController>();

            if (m_BoxRectTransform == null)
                m_BoxRectTransform = transform as RectTransform;

            if (m_CanvasGroup == null)
                m_CanvasGroup = GetComponent<CanvasGroup>();

            if (m_LabelText == null && m_BoxRectTransform != null)
                m_LabelText = m_BoxRectTransform.GetComponentInChildren<TMP_Text>(true);

            m_ParentRectTransform = m_BoxRectTransform != null
                ? m_BoxRectTransform.parent as RectTransform
                : null;
        }

        void RenderBox(BoxView boxView, RecognitionPresentation presentation, Vector2 parentSize)
        {
            var width = Mathf.Max(m_MinimumBoxWidth, parentSize.x * presentation.BoundingBox.Width);
            var height = Mathf.Max(m_MinimumBoxHeight, parentSize.y * presentation.BoundingBox.Height);
            var centerX = (presentation.BoundingBox.X + (presentation.BoundingBox.Width * 0.5f) - 0.5f) * parentSize.x;
            var centerY = (0.5f - presentation.BoundingBox.Y - (presentation.BoundingBox.Height * 0.5f)) * parentSize.y;

            boxView.RectTransform.anchoredPosition = new Vector2(centerX, centerY);
            boxView.RectTransform.sizeDelta = new Vector2(width, height);

            if (boxView.LabelText != null)
            {
                var confidencePercent = Mathf.RoundToInt(Mathf.Clamp01(presentation.Confidence) * 100f);
                boxView.LabelText.text = $"{presentation.DisplayText}  {confidencePercent}%";
            }

            SetVisible(boxView, true);
            LogDebug(
                $"Rendered '{presentation.Label}' at " +
                $"x={presentation.BoundingBox.X:0.000}, y={presentation.BoundingBox.Y:0.000}, " +
                $"w={presentation.BoundingBox.Width:0.000}, h={presentation.BoundingBox.Height:0.000}.");
        }

        void LogDebug(string message)
        {
            if (m_DebugLogging)
                Debug.Log($"[PooledDetectionOverlay] {message}");
        }

        BoxView EnsureBoxView(int index)
        {
            while (m_BoxViews.Count <= index)
            {
                BoxView boxView;
                if (m_BoxViews.Count == 0)
                {
                    boxView = CreatePrimaryBoxView();
                }
                else
                {
                    var template = m_BoxViews[0];
                    boxView = CreateAdditionalBoxView(template);
                }

                if (boxView == null)
                    return null;

                m_BoxViews.Add(boxView);
            }

            return m_BoxViews[index];
        }

        BoxView CreatePrimaryBoxView()
        {
            if (m_BoxRectTransform == null)
                return null;

            var image = m_BoxRectTransform.GetComponent<Image>();
            var outline = m_BoxRectTransform.GetComponent<Outline>();
            var borderRoot = EnsureOutlineOnlyAppearance(m_BoxRectTransform, image, outline);
            return new BoxView(
                m_BoxRectTransform,
                m_LabelText,
                m_CanvasGroup,
                image,
                outline,
                borderRoot);
        }

        BoxView CreateAdditionalBoxView(BoxView template)
        {
            if (template == null || m_ParentRectTransform == null)
                return null;

            var boxObject = new GameObject(
                $"Recognition Bounding Box {m_BoxViews.Count + 1}",
                typeof(RectTransform),
                typeof(CanvasGroup),
                typeof(Image));
            boxObject.transform.SetParent(m_ParentRectTransform, false);
            boxObject.transform.SetAsFirstSibling();

            var rectTransform = boxObject.GetComponent<RectTransform>();
            rectTransform.anchorMin = template.RectTransform.anchorMin;
            rectTransform.anchorMax = template.RectTransform.anchorMax;
            rectTransform.pivot = template.RectTransform.pivot;
            rectTransform.anchoredPosition = template.RectTransform.anchoredPosition;
            rectTransform.sizeDelta = template.RectTransform.sizeDelta;

            var image = boxObject.GetComponent<Image>();
            image.raycastTarget = false;

            var labelText = CreateLabelText(boxObject.transform, template.LabelText);
            var borderRoot = EnsureOutlineOnlyAppearance(rectTransform, image, null);
            return new BoxView(
                rectTransform,
                labelText,
                boxObject.GetComponent<CanvasGroup>(),
                image,
                null,
                borderRoot);
        }

        RectTransform EnsureOutlineOnlyAppearance(
            RectTransform boxRectTransform,
            Image backgroundImage,
            Outline legacyOutline)
        {
            if (backgroundImage != null)
            {
                backgroundImage.color = Color.clear;
                backgroundImage.raycastTarget = false;
                backgroundImage.enabled = false;
            }

            if (legacyOutline != null)
                legacyOutline.enabled = false;

            var borderTransform = boxRectTransform.Find("Detection Border") as RectTransform;
            if (borderTransform == null)
            {
                var borderObject = new GameObject("Detection Border", typeof(RectTransform));
                borderObject.transform.SetParent(boxRectTransform, false);
                borderObject.transform.SetAsFirstSibling();
                borderTransform = borderObject.GetComponent<RectTransform>();
            }

            borderTransform.anchorMin = Vector2.zero;
            borderTransform.anchorMax = Vector2.one;
            borderTransform.offsetMin = Vector2.zero;
            borderTransform.offsetMax = Vector2.zero;

            ConfigureBorderEdge(borderTransform, "Top", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, m_BorderThickness));
            ConfigureBorderEdge(borderTransform, "Bottom", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, m_BorderThickness));
            ConfigureBorderEdge(borderTransform, "Left", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(0f, 0.5f), new Vector2(m_BorderThickness, 0f));
            ConfigureBorderEdge(borderTransform, "Right", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(1f, 0.5f), new Vector2(m_BorderThickness, 0f));
            return borderTransform;
        }

        void ConfigureBorderEdge(
            RectTransform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Vector2 pivot,
            Vector2 sizeDelta)
        {
            var edgeTransform = parent.Find(name) as RectTransform;
            Image edgeImage;
            if (edgeTransform == null)
            {
                var edgeObject = new GameObject(name, typeof(RectTransform), typeof(Image));
                edgeObject.transform.SetParent(parent, false);
                edgeTransform = edgeObject.GetComponent<RectTransform>();
                edgeImage = edgeObject.GetComponent<Image>();
            }
            else
            {
                edgeImage = edgeTransform.GetComponent<Image>() ?? edgeTransform.gameObject.AddComponent<Image>();
            }

            edgeTransform.anchorMin = anchorMin;
            edgeTransform.anchorMax = anchorMax;
            edgeTransform.pivot = pivot;
            edgeTransform.anchoredPosition = Vector2.zero;
            edgeTransform.sizeDelta = sizeDelta;
            edgeImage.color = m_BorderColor;
            edgeImage.raycastTarget = false;
        }

        TMP_Text CreateLabelText(Transform parent, TMP_Text template)
        {
            var labelObject = new GameObject("Detection Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            labelObject.transform.SetParent(parent, false);

            var labelRect = labelObject.GetComponent<RectTransform>();
            if (template != null)
            {
                var templateRect = template.rectTransform;
                labelRect.anchorMin = templateRect.anchorMin;
                labelRect.anchorMax = templateRect.anchorMax;
                labelRect.pivot = templateRect.pivot;
                labelRect.anchoredPosition = templateRect.anchoredPosition;
                labelRect.sizeDelta = templateRect.sizeDelta;
            }
            else
            {
                labelRect.anchorMin = new Vector2(0f, 1f);
                labelRect.anchorMax = new Vector2(1f, 1f);
                labelRect.pivot = new Vector2(0f, 1f);
                labelRect.anchoredPosition = new Vector2(0f, 42f);
                labelRect.sizeDelta = new Vector2(0f, 40f);
            }

            var labelText = labelObject.GetComponent<TextMeshProUGUI>();
            if (template != null)
            {
                labelText.font = template.font;
                labelText.fontSize = template.fontSize;
                labelText.fontStyle = template.fontStyle;
                labelText.color = template.color;
                labelText.alignment = template.alignment;
                labelText.textWrappingMode = template.textWrappingMode;
                labelText.overflowMode = template.overflowMode;
            }
            else
            {
                labelText.fontSize = 28f;
                labelText.fontStyle = FontStyles.Bold;
                labelText.color = Color.white;
                labelText.alignment = TextAlignmentOptions.Left;
                labelText.textWrappingMode = TextWrappingModes.NoWrap;
                labelText.overflowMode = TextOverflowModes.Truncate;
            }

            labelText.raycastTarget = false;
            NativeIndicText.Track(labelText);
            return labelText;
        }

        void HideAllBoxViews()
        {
            HideBoxViewsFrom(0);
        }

        void HideBoxViewsFrom(int startIndex)
        {
            for (var i = Mathf.Max(0, startIndex); i < m_BoxViews.Count; i++)
                SetVisible(m_BoxViews[i], false);
        }

        void SetVisible(bool visible)
        {
            var boxView = EnsureBoxView(0);
            if (boxView != null)
            {
                SetVisible(boxView, visible);
                return;
            }

            if (m_CanvasGroup != null)
            {
                m_CanvasGroup.alpha = visible ? 1f : 0f;
                m_CanvasGroup.blocksRaycasts = false;
                m_CanvasGroup.interactable = false;
            }
        }

        static void SetVisible(BoxView boxView, bool visible)
        {
            if (boxView == null)
                return;

            if (boxView.CanvasGroup != null)
            {
                boxView.CanvasGroup.alpha = visible ? 1f : 0f;
                boxView.CanvasGroup.blocksRaycasts = false;
                boxView.CanvasGroup.interactable = false;
            }

            if (boxView.BorderRoot != null)
                boxView.BorderRoot.gameObject.SetActive(visible);

            if (boxView.LabelText != null)
                boxView.LabelText.enabled = visible;
        }

        sealed class BoxView
        {
            public BoxView(
                RectTransform rectTransform,
                TMP_Text labelText,
                CanvasGroup canvasGroup,
                Image image,
                Outline outline,
                RectTransform borderRoot)
            {
                RectTransform = rectTransform;
                LabelText = labelText;
                CanvasGroup = canvasGroup;
                Image = image;
                Outline = outline;
                BorderRoot = borderRoot;
            }

            public RectTransform RectTransform { get; }

            public TMP_Text LabelText { get; }

            public CanvasGroup CanvasGroup { get; }

            public Image Image { get; }

            public Outline Outline { get; }

            public RectTransform BorderRoot { get; }
        }
    }
}
