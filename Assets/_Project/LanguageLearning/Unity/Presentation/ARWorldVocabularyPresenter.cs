using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace AIAR.LanguageLearning.Unity.Presentation
{
    /// <summary>
    /// Projects the primary stable detection onto an AR plane or feature point. The screen-space
    /// overlay remains the fallback when the environment has not produced a raycastable surface.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ARWorldVocabularyPresenter : MonoBehaviour
    {
        static readonly List<ARRaycastHit> s_RaycastHits = new List<ARRaycastHit>();

        [SerializeField]
        LearningSessionController m_SessionController;

        [SerializeField]
        ARRaycastManager m_RaycastManager;

        [SerializeField]
        Camera m_ArCamera;

        [SerializeField, Min(0.01f)]
        float m_SurfaceOffsetMeters = 0.035f;

        GameObject m_LabelRoot;
        TMP_Text m_LabelText;
        bool m_HasWorldPlacement;

        void Awake()
        {
            ResolveReferences();
        }

        void OnEnable()
        {
            ResolveReferences();
            if (m_SessionController != null)
            {
                m_SessionController.RecognitionPresented += OnRecognitionPresented;
                m_SessionController.RecognitionsPresented += OnRecognitionsPresented;
            }
        }

        void OnDisable()
        {
            if (m_SessionController != null)
            {
                m_SessionController.RecognitionPresented -= OnRecognitionPresented;
                m_SessionController.RecognitionsPresented -= OnRecognitionsPresented;
            }
        }

        void LateUpdate()
        {
            if (!m_HasWorldPlacement || m_LabelRoot == null || m_ArCamera == null)
                return;

            var directionToCamera = m_ArCamera.transform.position - m_LabelRoot.transform.position;
            if (directionToCamera.sqrMagnitude > 0.0001f)
                m_LabelRoot.transform.rotation = Quaternion.LookRotation(-directionToCamera.normalized, Vector3.up);
        }

        public void Configure(
            LearningSessionController sessionController,
            ARRaycastManager raycastManager,
            Camera arCamera)
        {
            m_SessionController = sessionController;
            m_RaycastManager = raycastManager;
            m_ArCamera = arCamera;
            ResolveReferences();
        }

        void OnRecognitionPresented(RecognitionPresentation presentation)
        {
            if (!presentation.MatchedVocabulary || !presentation.BoundingBox.IsValid)
                return;

            ResolveReferences();
            if (m_RaycastManager == null)
                return;

            var center = new Vector2(
                (presentation.BoundingBox.X + presentation.BoundingBox.Width * 0.5f) * Screen.width,
                (1f - presentation.BoundingBox.Y - presentation.BoundingBox.Height * 0.5f) * Screen.height);

            if (!m_RaycastManager.Raycast(
                    center,
                    s_RaycastHits,
                    TrackableType.PlaneWithinPolygon | TrackableType.FeaturePoint))
            {
                return;
            }

            EnsureLabel();
            var hitPose = s_RaycastHits[0].pose;
            m_LabelRoot.transform.SetPositionAndRotation(
                hitPose.position + hitPose.up * m_SurfaceOffsetMeters,
                hitPose.rotation);
            m_LabelText.text = presentation.DisplayText;
            m_LabelRoot.SetActive(true);
            m_HasWorldPlacement = true;
        }

        void OnRecognitionsPresented(IReadOnlyList<RecognitionPresentation> presentations)
        {
            if (presentations != null && presentations.Count > 0)
                return;

            m_HasWorldPlacement = false;
            if (m_LabelRoot != null)
                m_LabelRoot.SetActive(false);
        }

        void ResolveReferences()
        {
            if (m_SessionController == null)
                m_SessionController = FindAnyObjectByType<LearningSessionController>();

            if (m_RaycastManager == null)
                m_RaycastManager = FindAnyObjectByType<ARRaycastManager>();

            if (m_ArCamera == null)
            {
                var cameraManager = FindAnyObjectByType<ARCameraManager>();
                m_ArCamera = cameraManager != null ? cameraManager.GetComponent<Camera>() : Camera.main;
            }
        }

        void EnsureLabel()
        {
            if (m_LabelRoot != null)
                return;

            m_LabelRoot = new GameObject(
                "AR Vocabulary Anchor Label",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(Image));
            var rect = m_LabelRoot.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(360f, 108f);
            rect.localScale = Vector3.one * 0.0008f;

            var canvas = m_LabelRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = m_ArCamera;
            canvas.sortingOrder = 120;

            var image = m_LabelRoot.GetComponent<Image>();
            image.color = new Color(0.025f, 0.04f, 0.065f, 0.92f);
            image.raycastTarget = false;

            var textObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(m_LabelRoot.transform, false);
            var textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(18f, 10f);
            textRect.offsetMax = new Vector2(-18f, -10f);

            m_LabelText = textObject.GetComponent<TextMeshProUGUI>();
            m_LabelText.text = string.Empty;
            var multilingualFont = ARVocabularyOverlayPresenter.GetMultilingualFontAsset();
            if (multilingualFont != null)
                m_LabelText.font = multilingualFont;
            m_LabelText.fontSize = 46f;
            m_LabelText.fontStyle = FontStyles.Bold;
            m_LabelText.color = Color.white;
            m_LabelText.alignment = TextAlignmentOptions.Center;
            m_LabelText.textWrappingMode = TextWrappingModes.NoWrap;
            m_LabelText.overflowMode = TextOverflowModes.Truncate;
            m_LabelText.raycastTarget = false;
            m_LabelRoot.SetActive(false);
        }
    }
}
