using System;
using System.Collections.Generic;
using AIAR.LanguageLearning.Application.Models;
using UnityEngine;

namespace AIAR.LanguageLearning.Unity.ObjectDetection
{
    public static class Yolo11Contract
    {
        public const int InputWidth = 320;
        public const int InputHeight = 320;
        public const int InputChannels = 3;
        public const int ClassCount = 80;
        public const int OutputChannels = 4 + ClassCount;
        public const int PredictionCount = 2100;
        public const byte LetterboxFill = 114;

        static readonly string[] s_RequiredLabels =
        {
            "person",
            "backpack",
            "bottle",
            "cup",
            "chair",
            "laptop",
            "mouse",
            "keyboard",
            "cell phone",
            "book"
        };

        public static string[] ParseAndValidateLabels(TextAsset labelAsset)
        {
            if (labelAsset == null)
                throw new InvalidOperationException("The COCO label asset is not assigned.");

            var labels = labelAsset.text.Split(
                new[] { '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries);

            if (labels.Length != ClassCount)
            {
                throw new InvalidOperationException(
                    $"The detector requires exactly {ClassCount} COCO labels, but '{labelAsset.name}' contains {labels.Length}.");
            }

            for (var index = 0; index < labels.Length; index++)
                labels[index] = labels[index].Trim();

            for (var index = 0; index < s_RequiredLabels.Length; index++)
            {
                if (Array.IndexOf(labels, s_RequiredLabels[index]) < 0)
                {
                    throw new InvalidOperationException(
                        $"The COCO label asset is invalid: required label '{s_RequiredLabels[index]}' is missing.");
                }
            }

            return labels;
        }
    }

    public readonly struct LetterboxTransform
    {
        LetterboxTransform(
            int sourceWidth,
            int sourceHeight,
            int targetWidth,
            int targetHeight,
            int contentWidth,
            int contentHeight,
            int padX,
            int padY)
        {
            SourceWidth = sourceWidth;
            SourceHeight = sourceHeight;
            TargetWidth = targetWidth;
            TargetHeight = targetHeight;
            ContentWidth = contentWidth;
            ContentHeight = contentHeight;
            PadX = padX;
            PadY = padY;
        }

        public int SourceWidth { get; }

        public int SourceHeight { get; }

        public int TargetWidth { get; }

        public int TargetHeight { get; }

        public int ContentWidth { get; }

        public int ContentHeight { get; }

        public int PadX { get; }

        public int PadY { get; }

        public static LetterboxTransform Create(
            int sourceWidth,
            int sourceHeight,
            int targetWidth,
            int targetHeight)
        {
            if (sourceWidth <= 0 || sourceHeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(sourceWidth), "Source dimensions must be positive.");

            if (targetWidth <= 0 || targetHeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(targetWidth), "Target dimensions must be positive.");

            var scale = Math.Min(
                targetWidth / (double)sourceWidth,
                targetHeight / (double)sourceHeight);
            var contentWidth = Math.Max(1, Math.Min(targetWidth, (int)Math.Round(sourceWidth * scale)));
            var contentHeight = Math.Max(1, Math.Min(targetHeight, (int)Math.Round(sourceHeight * scale)));
            return new LetterboxTransform(
                sourceWidth,
                sourceHeight,
                targetWidth,
                targetHeight,
                contentWidth,
                contentHeight,
                (targetWidth - contentWidth) / 2,
                (targetHeight - contentHeight) / 2);
        }

        public NormalizedBoundingBox ModelToDisplay(YoloDetection detection)
        {
            var xMin = (detection.XMin - PadX) / Math.Max(1f, ContentWidth);
            var yMin = (detection.YMin - PadY) / Math.Max(1f, ContentHeight);
            var xMax = (detection.XMax - PadX) / Math.Max(1f, ContentWidth);
            var yMax = (detection.YMax - PadY) / Math.Max(1f, ContentHeight);

            xMin = Mathf.Clamp01(xMin);
            yMin = Mathf.Clamp01(yMin);
            xMax = Mathf.Clamp01(xMax);
            yMax = Mathf.Clamp01(yMax);

            return new NormalizedBoundingBox(
                xMin,
                yMin,
                Mathf.Max(0f, xMax - xMin),
                Mathf.Max(0f, yMax - yMin));
        }

        public override string ToString()
        {
            return
                $"{SourceWidth}x{SourceHeight} -> {TargetWidth}x{TargetHeight}, " +
                $"content={ContentWidth}x{ContentHeight}, pad=({PadX},{PadY})";
        }
    }

    public readonly struct YoloDetection
    {
        public YoloDetection(
            int classId,
            float confidence,
            float xMin,
            float yMin,
            float xMax,
            float yMax)
        {
            ClassId = classId;
            Confidence = confidence;
            XMin = xMin;
            YMin = yMin;
            XMax = xMax;
            YMax = yMax;
        }

        public int ClassId { get; }

        public float Confidence { get; }

        public float XMin { get; }

        public float YMin { get; }

        public float XMax { get; }

        public float YMax { get; }

        public float Area => Math.Max(0f, XMax - XMin) * Math.Max(0f, YMax - YMin);
    }

    public sealed class Yolo11Postprocessor
    {
        sealed class ConfidenceComparer : IComparer<YoloDetection>
        {
            public static readonly ConfidenceComparer Instance = new ConfidenceComparer();

            public int Compare(YoloDetection left, YoloDetection right)
            {
                return right.Confidence.CompareTo(left.Confidence);
            }
        }

        readonly List<YoloDetection> m_Candidates;

        public Yolo11Postprocessor(int initialCandidateCapacity = 256)
        {
            m_Candidates = new List<YoloDetection>(Math.Max(1, initialCandidateCapacity));
        }

        public int LastCandidateCount { get; private set; }

        public void Decode(
            ReadOnlySpan<float> output,
            int outputChannels,
            int predictionCount,
            float confidenceThreshold,
            float iouThreshold,
            int maxDetections,
            List<YoloDetection> results)
        {
            if (results == null)
                throw new ArgumentNullException(nameof(results));

            results.Clear();
            m_Candidates.Clear();
            LastCandidateCount = 0;

            if (outputChannels != Yolo11Contract.OutputChannels ||
                predictionCount != Yolo11Contract.PredictionCount)
            {
                throw new InvalidOperationException(
                    $"Unexpected YOLO11 output [{outputChannels},{predictionCount}]. " +
                    $"Expected [1,{Yolo11Contract.OutputChannels},{Yolo11Contract.PredictionCount}].");
            }

            var requiredLength = outputChannels * predictionCount;
            if (output.Length != requiredLength)
            {
                throw new InvalidOperationException(
                    $"YOLO11 output contains {output.Length} values; expected {requiredLength}.");
            }

            confidenceThreshold = Mathf.Clamp01(confidenceThreshold);
            iouThreshold = Mathf.Clamp01(iouThreshold);
            maxDetections = Math.Max(1, maxDetections);

            for (var prediction = 0; prediction < predictionCount; prediction++)
            {
                var bestClass = -1;
                var bestScore = float.NegativeInfinity;
                for (var classId = 0; classId < Yolo11Contract.ClassCount; classId++)
                {
                    var score = output[((4 + classId) * predictionCount) + prediction];
                    if (score <= bestScore)
                        continue;

                    bestScore = score;
                    bestClass = classId;
                }

                if (bestClass < 0 ||
                    bestScore < confidenceThreshold ||
                    float.IsNaN(bestScore) ||
                    float.IsInfinity(bestScore))
                {
                    continue;
                }

                var centerX = output[prediction];
                var centerY = output[predictionCount + prediction];
                var width = output[(2 * predictionCount) + prediction];
                var height = output[(3 * predictionCount) + prediction];
                if (!IsFinite(centerX) || !IsFinite(centerY) || !IsFinite(width) || !IsFinite(height) ||
                    width <= 0f || height <= 0f)
                {
                    continue;
                }

                var xMin = Mathf.Clamp(centerX - (width * 0.5f), 0f, Yolo11Contract.InputWidth);
                var yMin = Mathf.Clamp(centerY - (height * 0.5f), 0f, Yolo11Contract.InputHeight);
                var xMax = Mathf.Clamp(centerX + (width * 0.5f), 0f, Yolo11Contract.InputWidth);
                var yMax = Mathf.Clamp(centerY + (height * 0.5f), 0f, Yolo11Contract.InputHeight);
                if (xMax <= xMin || yMax <= yMin)
                    continue;

                m_Candidates.Add(new YoloDetection(
                    bestClass,
                    bestScore,
                    xMin,
                    yMin,
                    xMax,
                    yMax));
            }

            LastCandidateCount = m_Candidates.Count;
            m_Candidates.Sort(ConfidenceComparer.Instance);

            for (var candidateIndex = 0;
                 candidateIndex < m_Candidates.Count && results.Count < maxDetections;
                 candidateIndex++)
            {
                var candidate = m_Candidates[candidateIndex];
                var suppressed = false;
                for (var resultIndex = 0; resultIndex < results.Count; resultIndex++)
                {
                    var accepted = results[resultIndex];
                    if (accepted.ClassId == candidate.ClassId &&
                        IntersectionOverUnion(accepted, candidate) > iouThreshold)
                    {
                        suppressed = true;
                        break;
                    }
                }

                if (!suppressed)
                    results.Add(candidate);
            }
        }

        public static float IntersectionOverUnion(YoloDetection left, YoloDetection right)
        {
            var intersectionWidth = Math.Max(0f, Math.Min(left.XMax, right.XMax) - Math.Max(left.XMin, right.XMin));
            var intersectionHeight = Math.Max(0f, Math.Min(left.YMax, right.YMax) - Math.Max(left.YMin, right.YMin));
            var intersection = intersectionWidth * intersectionHeight;
            var union = left.Area + right.Area - intersection;
            return union <= 0f ? 0f : intersection / union;
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
