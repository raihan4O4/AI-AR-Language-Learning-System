using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AIAR.LanguageLearning.Unity.Presentation
{
    public enum LearningUiIconType
    {
        Camera,
        Quiz,
        Chart,
        Back,
        ChevronRight,
        Capture,
        Translate,
        Speak,
        Save,
        Settings,
        Trophy,
        Star,
        Flame,
        Refresh,
        Close
    }

    [DisallowMultipleComponent]
    public sealed class LearningUiIcon : MaskableGraphic
    {
        [SerializeField]
        LearningUiIconType m_Icon;

        public LearningUiIconType Icon
        {
            get => m_Icon;
            set
            {
                m_Icon = value;
                SetVerticesDirty();
            }
        }

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            var iconRect = GetPixelAdjustedRect();
            var size = Mathf.Min(iconRect.width, iconRect.height);
            var center = iconRect.center;
            var stroke = Mathf.Max(2.5f, size * 0.075f);

            switch (m_Icon)
            {
                case LearningUiIconType.Camera:
                    DrawCamera(vertexHelper, center, size, stroke);
                    break;
                case LearningUiIconType.Quiz:
                    DrawQuiz(vertexHelper, center, size, stroke);
                    break;
                case LearningUiIconType.Chart:
                    DrawChart(vertexHelper, center, size, stroke);
                    break;
                case LearningUiIconType.Back:
                    DrawChevron(vertexHelper, center, size, stroke, false);
                    break;
                case LearningUiIconType.ChevronRight:
                    DrawChevron(vertexHelper, center, size, stroke, true);
                    break;
                case LearningUiIconType.Capture:
                    AddRing(vertexHelper, center, size * 0.34f, size * 0.22f, 32);
                    AddCircle(vertexHelper, center, size * 0.1f, 20);
                    break;
                case LearningUiIconType.Translate:
                    DrawLanguage(vertexHelper, center, size, stroke);
                    break;
                case LearningUiIconType.Speak:
                    DrawSpeaker(vertexHelper, center, size, stroke);
                    break;
                case LearningUiIconType.Save:
                    AddPolygon(vertexHelper, new[]
                    {
                        center + new Vector2(-0.24f, -0.34f) * size,
                        center + new Vector2(-0.24f, 0.34f) * size,
                        center + new Vector2(0.24f, 0.34f) * size,
                        center + new Vector2(0.24f, -0.34f) * size,
                        center
                    });
                    break;
                case LearningUiIconType.Settings:
                    DrawSettings(vertexHelper, center, size, stroke);
                    break;
                case LearningUiIconType.Trophy:
                    DrawTrophy(vertexHelper, center, size, stroke);
                    break;
                case LearningUiIconType.Star:
                    DrawStar(vertexHelper, center, size);
                    break;
                case LearningUiIconType.Flame:
                    DrawFlame(vertexHelper, center, size);
                    break;
                case LearningUiIconType.Refresh:
                    DrawRefresh(vertexHelper, center, size, stroke);
                    break;
                case LearningUiIconType.Close:
                    AddLine(vertexHelper, center + new Vector2(-0.25f, -0.25f) * size, center + new Vector2(0.25f, 0.25f) * size, stroke);
                    AddLine(vertexHelper, center + new Vector2(-0.25f, 0.25f) * size, center + new Vector2(0.25f, -0.25f) * size, stroke);
                    break;
            }
        }

        void DrawCamera(VertexHelper vertexHelper, Vector2 center, float size, float stroke)
        {
            var left = center.x - size * 0.34f;
            var right = center.x + size * 0.34f;
            var bottom = center.y - size * 0.24f;
            var top = center.y + size * 0.24f;
            AddLine(vertexHelper, new Vector2(left, bottom), new Vector2(right, bottom), stroke);
            AddLine(vertexHelper, new Vector2(right, bottom), new Vector2(right, top), stroke);
            AddLine(vertexHelper, new Vector2(right, top), new Vector2(left, top), stroke);
            AddLine(vertexHelper, new Vector2(left, top), new Vector2(left, bottom), stroke);
            AddLine(vertexHelper, center + new Vector2(-0.18f, 0.3f) * size, center + new Vector2(0.06f, 0.3f) * size, stroke);
            AddRing(vertexHelper, center, size * 0.17f, size * 0.11f, 24);
        }

        void DrawQuiz(VertexHelper vertexHelper, Vector2 center, float size, float stroke)
        {
            var half = size * 0.31f;
            AddLine(vertexHelper, center + new Vector2(-half, -half), center + new Vector2(half, -half), stroke);
            AddLine(vertexHelper, center + new Vector2(half, -half), center + new Vector2(half, half), stroke);
            AddLine(vertexHelper, center + new Vector2(half, half), center + new Vector2(-half, half), stroke);
            AddLine(vertexHelper, center + new Vector2(-half, half), center + new Vector2(-half, -half), stroke);
            AddLine(vertexHelper, center + new Vector2(-0.18f, 0f) * size, center + new Vector2(-0.03f, -0.14f) * size, stroke);
            AddLine(vertexHelper, center + new Vector2(-0.03f, -0.14f) * size, center + new Vector2(0.22f, 0.16f) * size, stroke);
        }

        void DrawChart(VertexHelper vertexHelper, Vector2 center, float size, float stroke)
        {
            var baseline = center.y - size * 0.28f;
            AddRect(vertexHelper, new Rect(center.x - size * 0.31f, baseline, size * 0.14f, size * 0.28f));
            AddRect(vertexHelper, new Rect(center.x - size * 0.07f, baseline, size * 0.14f, size * 0.45f));
            AddRect(vertexHelper, new Rect(center.x + size * 0.17f, baseline, size * 0.14f, size * 0.62f));
            AddLine(vertexHelper, center + new Vector2(-0.34f, -0.31f) * size, center + new Vector2(0.35f, -0.31f) * size, stroke * 0.7f);
        }

        void DrawChevron(VertexHelper vertexHelper, Vector2 center, float size, float stroke, bool right)
        {
            var direction = right ? 1f : -1f;
            AddLine(vertexHelper, center + new Vector2(-0.12f * direction, 0.28f) * size, center + new Vector2(0.16f * direction, 0f) * size, stroke);
            AddLine(vertexHelper, center + new Vector2(0.16f * direction, 0f) * size, center + new Vector2(-0.12f * direction, -0.28f) * size, stroke);
        }

        void DrawSpeaker(VertexHelper vertexHelper, Vector2 center, float size, float stroke)
        {
            AddPolygon(vertexHelper, new[]
            {
                center + new Vector2(-0.3f, -0.13f) * size,
                center + new Vector2(-0.12f, -0.13f) * size,
                center + new Vector2(0.08f, -0.3f) * size,
                center + new Vector2(0.08f, 0.3f) * size,
                center + new Vector2(-0.12f, 0.13f) * size,
                center + new Vector2(-0.3f, 0.13f) * size
            });
            AddArc(vertexHelper, center + new Vector2(0.03f, 0f) * size, size * 0.2f, -55f, 55f, stroke);
            AddArc(vertexHelper, center + new Vector2(0.03f, 0f) * size, size * 0.34f, -55f, 55f, stroke);
        }

        void DrawLanguage(VertexHelper vertexHelper, Vector2 center, float size, float stroke)
        {
            var languageStroke = stroke * 0.82f;
            AddLine(
                vertexHelper,
                center + new Vector2(-0.04f, 0.36f) * size,
                center + new Vector2(0.08f, 0.24f) * size,
                languageStroke);
            AddLine(
                vertexHelper,
                center + new Vector2(-0.32f, 0.15f) * size,
                center + new Vector2(0.32f, 0.15f) * size,
                languageStroke);
            AddLine(
                vertexHelper,
                center + new Vector2(0f, 0.14f) * size,
                center + new Vector2(-0.28f, -0.34f) * size,
                languageStroke);
            AddLine(
                vertexHelper,
                center + new Vector2(0f, 0.14f) * size,
                center + new Vector2(0.3f, -0.34f) * size,
                languageStroke);
        }

        void DrawSettings(VertexHelper vertexHelper, Vector2 center, float size, float stroke)
        {
            var verticalOffsets = new[] { 0.24f, 0f, -0.24f };
            var knobOffsets = new[] { -0.12f, 0.17f, -0.02f };
            for (var index = 0; index < verticalOffsets.Length; index++)
            {
                var vertical = center.y + verticalOffsets[index] * size;
                AddLine(vertexHelper, new Vector2(center.x - size * 0.32f, vertical), new Vector2(center.x + size * 0.32f, vertical), stroke * 0.7f);
                AddCircle(vertexHelper, new Vector2(center.x + knobOffsets[index] * size, vertical), size * 0.075f, 16);
            }
        }

        void DrawTrophy(VertexHelper vertexHelper, Vector2 center, float size, float stroke)
        {
            AddLine(vertexHelper, center + new Vector2(-0.22f, 0.28f) * size, center + new Vector2(-0.14f, -0.04f) * size, stroke);
            AddLine(vertexHelper, center + new Vector2(0.22f, 0.28f) * size, center + new Vector2(0.14f, -0.04f) * size, stroke);
            AddLine(vertexHelper, center + new Vector2(-0.22f, 0.28f) * size, center + new Vector2(0.22f, 0.28f) * size, stroke);
            AddLine(vertexHelper, center + new Vector2(-0.14f, -0.04f) * size, center + new Vector2(0.14f, -0.04f) * size, stroke);
            AddLine(vertexHelper, center + new Vector2(0f, -0.04f) * size, center + new Vector2(0f, -0.25f) * size, stroke);
            AddLine(vertexHelper, center + new Vector2(-0.18f, -0.27f) * size, center + new Vector2(0.18f, -0.27f) * size, stroke);
        }

        void DrawStar(VertexHelper vertexHelper, Vector2 center, float size)
        {
            var points = new List<Vector2>(10);
            for (var index = 0; index < 10; index++)
            {
                var radius = index % 2 == 0 ? size * 0.34f : size * 0.15f;
                var angle = Mathf.Deg2Rad * (90f + index * 36f);
                points.Add(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
            }
            AddPolygon(vertexHelper, points);
        }

        void DrawFlame(VertexHelper vertexHelper, Vector2 center, float size)
        {
            AddPolygon(vertexHelper, new[]
            {
                center + new Vector2(0f, 0.38f) * size,
                center + new Vector2(0.2f, 0.08f) * size,
                center + new Vector2(0.28f, -0.18f) * size,
                center + new Vector2(0.12f, -0.36f) * size,
                center + new Vector2(-0.14f, -0.34f) * size,
                center + new Vector2(-0.28f, -0.1f) * size,
                center + new Vector2(-0.2f, 0.16f) * size,
                center + new Vector2(-0.04f, 0.02f) * size
            });
        }

        void DrawRefresh(VertexHelper vertexHelper, Vector2 center, float size, float stroke)
        {
            AddArc(vertexHelper, center, size * 0.3f, -40f, 230f, stroke);
            AddPolygon(vertexHelper, new[]
            {
                center + new Vector2(-0.31f, 0.08f) * size,
                center + new Vector2(-0.36f, 0.31f) * size,
                center + new Vector2(-0.12f, 0.25f) * size
            });
        }

        void AddArc(VertexHelper vertexHelper, Vector2 center, float radius, float startDegrees, float endDegrees, float width)
        {
            const int segmentCount = 14;
            var previous = center + Direction(startDegrees) * radius;
            for (var segmentIndex = 1; segmentIndex <= segmentCount; segmentIndex++)
            {
                var current = center + Direction(Mathf.Lerp(startDegrees, endDegrees, segmentIndex / (float)segmentCount)) * radius;
                AddLine(vertexHelper, previous, current, width);
                previous = current;
            }
        }

        static Vector2 Direction(float degrees)
        {
            var radians = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        }

        void AddLine(VertexHelper vertexHelper, Vector2 start, Vector2 end, float width)
        {
            var direction = (end - start).normalized;
            var perpendicular = new Vector2(-direction.y, direction.x) * width * 0.5f;
            AddQuad(vertexHelper, start - perpendicular, start + perpendicular, end + perpendicular, end - perpendicular);
        }

        void AddRect(VertexHelper vertexHelper, Rect rect)
        {
            AddQuad(vertexHelper, new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMin, rect.yMax), new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMax, rect.yMin));
        }

        void AddCircle(VertexHelper vertexHelper, Vector2 center, float radius, int segments)
        {
            var centerIndex = vertexHelper.currentVertCount;
            AddVertex(vertexHelper, center);
            for (var segmentIndex = 0; segmentIndex <= segments; segmentIndex++)
                AddVertex(vertexHelper, center + Direction(segmentIndex * 360f / segments) * radius);
            for (var segmentIndex = 0; segmentIndex < segments; segmentIndex++)
                vertexHelper.AddTriangle(centerIndex, centerIndex + segmentIndex + 1, centerIndex + segmentIndex + 2);
        }

        void AddRing(VertexHelper vertexHelper, Vector2 center, float outerRadius, float innerRadius, int segments)
        {
            var startIndex = vertexHelper.currentVertCount;
            for (var segmentIndex = 0; segmentIndex <= segments; segmentIndex++)
            {
                var direction = Direction(segmentIndex * 360f / segments);
                AddVertex(vertexHelper, center + direction * outerRadius);
                AddVertex(vertexHelper, center + direction * innerRadius);
            }
            for (var segmentIndex = 0; segmentIndex < segments; segmentIndex++)
            {
                var outerStart = startIndex + segmentIndex * 2;
                vertexHelper.AddTriangle(outerStart, outerStart + 2, outerStart + 3);
                vertexHelper.AddTriangle(outerStart, outerStart + 3, outerStart + 1);
            }
        }

        void AddPolygon(VertexHelper vertexHelper, IReadOnlyList<Vector2> points)
        {
            var center = Vector2.zero;
            for (var index = 0; index < points.Count; index++)
                center += points[index];
            center /= points.Count;

            var centerIndex = vertexHelper.currentVertCount;
            AddVertex(vertexHelper, center);
            for (var index = 0; index < points.Count; index++)
                AddVertex(vertexHelper, points[index]);
            for (var index = 0; index < points.Count; index++)
                vertexHelper.AddTriangle(centerIndex, centerIndex + index + 1, centerIndex + ((index + 1) % points.Count) + 1);
        }

        void AddQuad(VertexHelper vertexHelper, Vector2 bottomLeft, Vector2 topLeft, Vector2 topRight, Vector2 bottomRight)
        {
            var startIndex = vertexHelper.currentVertCount;
            AddVertex(vertexHelper, bottomLeft);
            AddVertex(vertexHelper, topLeft);
            AddVertex(vertexHelper, topRight);
            AddVertex(vertexHelper, bottomRight);
            vertexHelper.AddTriangle(startIndex, startIndex + 1, startIndex + 2);
            vertexHelper.AddTriangle(startIndex, startIndex + 2, startIndex + 3);
        }

        void AddVertex(VertexHelper vertexHelper, Vector2 position)
        {
            vertexHelper.AddVert(position, color, Vector2.zero);
        }
    }
}
