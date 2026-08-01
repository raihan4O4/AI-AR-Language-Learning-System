using System;
using System.Collections.Generic;
using AIAR.LanguageLearning.Unity.ObjectDetection;
using NUnit.Framework;

namespace AIAR.LanguageLearning.Tests
{
    public sealed class Yolo11PipelineTests
    {
        [Test]
        public void Letterbox_PortraitDisplayPreservesAspectRatio()
        {
            var transform = LetterboxTransform.Create(1080, 1920, 320, 320);

            Assert.AreEqual(180, transform.ContentWidth);
            Assert.AreEqual(320, transform.ContentHeight);
            Assert.AreEqual(70, transform.PadX);
            Assert.AreEqual(0, transform.PadY);
        }

        [Test]
        public void Letterbox_InverseMapsModelBoxToVisibleDisplay()
        {
            var transform = LetterboxTransform.Create(1080, 1920, 320, 320);
            var modelBox = new YoloDetection(0, 0.9f, 70f, 80f, 250f, 240f);

            var displayBox = transform.ModelToDisplay(modelBox);

            Assert.AreEqual(0f, displayBox.X, 0.0001f);
            Assert.AreEqual(0.25f, displayBox.Y, 0.0001f);
            Assert.AreEqual(1f, displayBox.Width, 0.0001f);
            Assert.AreEqual(0.5f, displayBox.Height, 0.0001f);
        }

        [Test]
        public void Decoder_UsesActualYolo11ChannelFirstContract()
        {
            var output = CreateEmptyOutput();
            const int prediction = 25;
            SetBox(output, prediction, centerX: 160f, centerY: 120f, width: 80f, height: 40f);
            SetClassScore(output, prediction, classId: 67, score: 0.91f);

            var results = new List<YoloDetection>();
            var decoder = new Yolo11Postprocessor();
            decoder.Decode(
                output,
                Yolo11Contract.OutputChannels,
                Yolo11Contract.PredictionCount,
                0.30f,
                0.45f,
                10,
                results);

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(67, results[0].ClassId, "COCO class 67 is cell phone.");
            Assert.AreEqual(0.91f, results[0].Confidence, 0.0001f);
            Assert.AreEqual(120f, results[0].XMin, 0.0001f);
            Assert.AreEqual(100f, results[0].YMin, 0.0001f);
            Assert.AreEqual(200f, results[0].XMax, 0.0001f);
            Assert.AreEqual(140f, results[0].YMax, 0.0001f);
        }

        [Test]
        public void Nms_SuppressesOverlappingBoxesOfSameClass()
        {
            var output = CreateEmptyOutput();
            SetBox(output, 0, 160f, 160f, 100f, 100f);
            SetClassScore(output, 0, 39, 0.90f);
            SetBox(output, 1, 164f, 164f, 100f, 100f);
            SetClassScore(output, 1, 39, 0.80f);

            var results = new List<YoloDetection>();
            new Yolo11Postprocessor().Decode(
                output,
                Yolo11Contract.OutputChannels,
                Yolo11Contract.PredictionCount,
                0.30f,
                0.45f,
                10,
                results);

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(0.90f, results[0].Confidence, 0.0001f);
        }

        [Test]
        public void Nms_KeepsOverlappingBoxesOfDifferentClasses()
        {
            var output = CreateEmptyOutput();
            SetBox(output, 0, 160f, 160f, 100f, 100f);
            SetClassScore(output, 0, 39, 0.90f);
            SetBox(output, 1, 164f, 164f, 100f, 100f);
            SetClassScore(output, 1, 41, 0.80f);

            var results = new List<YoloDetection>();
            new Yolo11Postprocessor().Decode(
                output,
                Yolo11Contract.OutputChannels,
                Yolo11Contract.PredictionCount,
                0.30f,
                0.45f,
                10,
                results);

            Assert.AreEqual(2, results.Count);
        }

        [Test]
        public void Decoder_RejectsAnyUnverifiedOutputLayout()
        {
            var decoder = new Yolo11Postprocessor();
            var results = new List<YoloDetection>();

            Assert.Throws<InvalidOperationException>(() =>
                decoder.Decode(
                    new float[85 * Yolo11Contract.PredictionCount],
                    85,
                    Yolo11Contract.PredictionCount,
                    0.30f,
                    0.45f,
                    10,
                    results));
        }

        static float[] CreateEmptyOutput()
        {
            return new float[Yolo11Contract.OutputChannels * Yolo11Contract.PredictionCount];
        }

        static void SetBox(
            float[] output,
            int prediction,
            float centerX,
            float centerY,
            float width,
            float height)
        {
            output[prediction] = centerX;
            output[Yolo11Contract.PredictionCount + prediction] = centerY;
            output[(2 * Yolo11Contract.PredictionCount) + prediction] = width;
            output[(3 * Yolo11Contract.PredictionCount) + prediction] = height;
        }

        static void SetClassScore(float[] output, int prediction, int classId, float score)
        {
            output[((4 + classId) * Yolo11Contract.PredictionCount) + prediction] = score;
        }
    }
}
