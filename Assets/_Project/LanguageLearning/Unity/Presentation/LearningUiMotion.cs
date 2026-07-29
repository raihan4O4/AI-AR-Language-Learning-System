using UnityEngine;
using UnityEngine.EventSystems;

namespace AIAR.LanguageLearning.Unity.Presentation
{
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
}
