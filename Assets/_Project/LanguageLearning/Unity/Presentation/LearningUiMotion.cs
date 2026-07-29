using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AIAR.LanguageLearning.Unity.Presentation
{
    public static class LearningPortraitBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
        static void BeforeSplashScreen()
        {
            Apply();
        }

        public static void Apply()
        {
            Screen.autorotateToPortrait = true;
            Screen.autorotateToPortraitUpsideDown = false;
            Screen.autorotateToLandscapeLeft = false;
            Screen.autorotateToLandscapeRight = false;
            Screen.orientation = ScreenOrientation.Portrait;
        }
    }

    [DisallowMultipleComponent]
    public sealed class LearningUiPressFeedback : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        [SerializeField, Range(0.85f, 1f)]
        float m_PressedScale = 0.96f;

        [SerializeField, Min(1f)]
        float m_ResponseSpeed = 18f;

        Vector3 m_RestScale = Vector3.one;
        Vector3 m_TargetScale = Vector3.one;

        void Awake()
        {
            m_RestScale = transform.localScale;
            m_TargetScale = m_RestScale;
        }

        void OnEnable()
        {
            if (m_RestScale == Vector3.zero)
                m_RestScale = Vector3.one;

            transform.localScale = m_RestScale;
            m_TargetScale = m_RestScale;
        }

        void Update()
        {
            transform.localScale = Vector3.Lerp(
                transform.localScale,
                m_TargetScale,
                1f - Mathf.Exp(-m_ResponseSpeed * Time.unscaledDeltaTime));
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            m_TargetScale = m_RestScale * m_PressedScale;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            m_TargetScale = m_RestScale;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            m_TargetScale = m_RestScale;
        }
    }

    [DisallowMultipleComponent]
    public sealed class LearningSafeAreaFitter : MonoBehaviour
    {
        Rect m_LastSafeArea;
        Vector2Int m_LastScreenSize;

        void OnEnable()
        {
            ApplySafeArea();
        }

        void Update()
        {
            if (m_LastSafeArea != Screen.safeArea ||
                m_LastScreenSize.x != Screen.width ||
                m_LastScreenSize.y != Screen.height)
            {
                ApplySafeArea();
            }
        }

        void ApplySafeArea()
        {
            if (!(transform is RectTransform rectTransform) || Screen.width <= 0 || Screen.height <= 0)
                return;

            var safeArea = Screen.safeArea;
            rectTransform.anchorMin = new Vector2(
                safeArea.xMin / Screen.width,
                safeArea.yMin / Screen.height);
            rectTransform.anchorMax = new Vector2(
                safeArea.xMax / Screen.width,
                safeArea.yMax / Screen.height);
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;

            m_LastSafeArea = safeArea;
            m_LastScreenSize = new Vector2Int(Screen.width, Screen.height);
        }
    }

    [DisallowMultipleComponent]
    public sealed class LearningPillGraphic : MaskableGraphic
    {
        const int CapSegments = 16;

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            var rect = GetPixelAdjustedRect();
            if (rect.width <= 0f || rect.height <= 0f)
                return;

            var radius = Mathf.Min(rect.height * 0.5f, rect.width * 0.5f);
            var center = rect.center;
            var rightCenter = new Vector2(rect.xMax - radius, center.y);
            var leftCenter = new Vector2(rect.xMin + radius, center.y);
            var vertexColor = color;

            vertexHelper.AddVert(center, vertexColor, new Vector2(0.5f, 0.5f));
            for (var segment = 0; segment <= CapSegments; segment++)
            {
                var angle = Mathf.Lerp(-90f, 90f, segment / (float)CapSegments) * Mathf.Deg2Rad;
                AddPerimeterVertex(vertexHelper, rightCenter, radius, angle, rect, vertexColor);
            }

            for (var segment = 0; segment <= CapSegments; segment++)
            {
                var angle = Mathf.Lerp(90f, 270f, segment / (float)CapSegments) * Mathf.Deg2Rad;
                AddPerimeterVertex(vertexHelper, leftCenter, radius, angle, rect, vertexColor);
            }

            var perimeterCount = (CapSegments + 1) * 2;
            for (var index = 0; index < perimeterCount; index++)
            {
                var current = index + 1;
                var next = ((index + 1) % perimeterCount) + 1;
                vertexHelper.AddTriangle(0, current, next);
            }
        }

        static void AddPerimeterVertex(
            VertexHelper vertexHelper,
            Vector2 capCenter,
            float radius,
            float angle,
            Rect rect,
            Color vertexColor)
        {
            var position = capCenter + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            var uv = new Vector2(
                Mathf.InverseLerp(rect.xMin, rect.xMax, position.x),
                Mathf.InverseLerp(rect.yMin, rect.yMax, position.y));
            vertexHelper.AddVert(position, vertexColor, uv);
        }
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform), typeof(GridLayoutGroup))]
    public sealed class LearningResponsiveGrid : MonoBehaviour
    {
        int m_Columns = 2;
        float m_Spacing = 18f;
        float m_CellHeight = 170f;
        float m_LastWidth = -1f;

        RectTransform m_RectTransform;
        GridLayoutGroup m_Grid;

        public void Configure(int columns, float spacing, float cellHeight)
        {
            m_Columns = Mathf.Max(1, columns);
            m_Spacing = Mathf.Max(0f, spacing);
            m_CellHeight = Mathf.Max(1f, cellHeight);
            ApplyLayout(true);
        }

        void OnEnable()
        {
            ApplyLayout(true);
        }

        void OnRectTransformDimensionsChange()
        {
            ApplyLayout(false);
        }

        void LateUpdate()
        {
            ApplyLayout(false);
        }

        void ApplyLayout(bool force)
        {
            m_RectTransform ??= transform as RectTransform;
            m_Grid ??= GetComponent<GridLayoutGroup>();
            if (m_RectTransform == null || m_Grid == null)
                return;

            var availableWidth = Mathf.Max(1f, m_RectTransform.rect.width - m_Grid.padding.horizontal);
            if (!force && Mathf.Abs(availableWidth - m_LastWidth) < 0.5f)
                return;

            var cellWidth = Mathf.Max(1f, (availableWidth - (m_Spacing * (m_Columns - 1))) / m_Columns);
            m_Grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            m_Grid.constraintCount = m_Columns;
            m_Grid.spacing = new Vector2(m_Spacing, m_Spacing);
            m_Grid.cellSize = new Vector2(cellWidth, m_CellHeight);
            m_LastWidth = availableWidth;
        }
    }
}
