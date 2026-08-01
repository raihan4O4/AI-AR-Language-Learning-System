using System;

namespace AIAR.LanguageLearning.Application.Models
{
    public enum RecognitionServiceState
    {
        Stopped,
        Initializing,
        Running,
        AwaitingCamera,
        Degraded,
        Faulted
    }

    public readonly struct RecognitionServiceStatus
    {
        public RecognitionServiceStatus(RecognitionServiceState state, string message)
        {
            State = state;
            Message = string.IsNullOrWhiteSpace(message) ? state.ToString() : message.Trim();
        }

        public RecognitionServiceState State { get; }

        public string Message { get; }
    }

    public readonly struct NormalizedBoundingBox
    {
        public NormalizedBoundingBox(float x, float y, float width, float height)
        {
            X = Clamp01(x);
            Y = Clamp01(y);
            Width = Math.Min(Clamp01(width), 1f - X);
            Height = Math.Min(Clamp01(height), 1f - Y);
        }

        public float X { get; }

        public float Y { get; }

        public float Width { get; }

        public float Height { get; }

        public bool IsValid => Width > 0f && Height > 0f;

        public static NormalizedBoundingBox Centered(float width, float height)
        {
            var clampedWidth = Clamp01(width);
            var clampedHeight = Clamp01(height);
            return new NormalizedBoundingBox(
                (1f - clampedWidth) * 0.5f,
                (1f - clampedHeight) * 0.5f,
                clampedWidth,
                clampedHeight);
        }

        static float Clamp01(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                return 0f;

            return Math.Max(0f, Math.Min(1f, value));
        }
    }

    public sealed class ObjectRecognitionResult
    {
        public ObjectRecognitionResult(
            string label,
            float confidence,
            string trackingId = "",
            NormalizedBoundingBox boundingBox = default)
        {
            if (string.IsNullOrWhiteSpace(label))
                throw new ArgumentException("Recognition label is required.", nameof(label));

            Label = label.Trim();
            Confidence = confidence;
            TrackingId = string.IsNullOrWhiteSpace(trackingId) ? string.Empty : trackingId.Trim();
            BoundingBox = boundingBox.IsValid ? boundingBox : NormalizedBoundingBox.Centered(0.5f, 0.35f);
        }

        public string Label { get; }

        public float Confidence { get; }

        public string TrackingId { get; }

        public NormalizedBoundingBox BoundingBox { get; }
    }
}
