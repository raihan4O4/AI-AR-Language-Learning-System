using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AIAR.LanguageLearning.Application.Models;
using AIAR.LanguageLearning.Application.Ports;
using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace AIAR.LanguageLearning.Unity.Recognition
{
    public enum SentisDetectionOutputFormat
    {
        AutoYolo = 0,
        YoloV8CenterXYWH = 1,
        YoloWithObjectnessCenterXYWH = 2,
        YoloEndToEndXYXY = 3
    }

    [DisallowMultipleComponent]
    public sealed class SentisObjectRecognitionService : MonoBehaviour, IObjectRecognitionService
    {
        const int InputChannels = 3;
        const int InputPixelBytes = 3;
        const byte LetterboxFillValue = 114;
        const float DiagnosticLogIntervalSeconds = 2f;

        [SerializeField]
        ARCameraManager m_CameraManager;

        [SerializeField]
        ModelAsset m_ModelAsset;

        [SerializeField]
        TextAsset m_LabelAsset;

        [SerializeField]
        BackendType m_Backend = BackendType.GPUCompute;

        [SerializeField]
        TensorLayout m_InputLayout = TensorLayout.NCHW;

        [SerializeField]
        SentisDetectionOutputFormat m_OutputFormat = SentisDetectionOutputFormat.AutoYolo;

        [SerializeField]
        XRCpuImage.Transformation m_ImageTransformation = XRCpuImage.Transformation.MirrorY;

        [SerializeField]
        bool m_PreserveAspectRatio = true;

        [SerializeField]
        bool m_ApplyCameraDisplayTransform = true;

        [SerializeField, Min(1)]
        int m_InputWidth = 640;

        [SerializeField, Min(1)]
        int m_InputHeight = 640;

        [SerializeField, Range(0.01f, 0.99f)]
        float m_ConfidenceThreshold = 0.35f;

        [SerializeField, Range(0.01f, 0.99f)]
        float m_NmsThreshold = 0.45f;

        [SerializeField, Min(0.05f)]
        float m_InferenceIntervalSeconds = 0.2f;

        [SerializeField, Min(1)]
        int m_LayersPerFrame = 6;

        [SerializeField, Min(1)]
        int m_MaxDetections = 8;

        [SerializeField]
        bool m_DebugLogging;

        readonly List<DetectionCandidate> m_DetectionCandidates = new List<DetectionCandidate>(64);
        readonly List<DetectionCandidate> m_NmsResults = new List<DetectionCandidate>(16);
        readonly List<ObjectRecognitionResult> m_RecognitionResults = new List<ObjectRecognitionResult>(8);

        Model m_RuntimeModel;
        Worker m_Worker;
        Tensor<float> m_InputTensor;
        Tensor<float> m_PendingOutputTensor;
        IEnumerator m_InferenceSchedule;
        XRCpuImage.AsyncConversion m_ConversionRequest;
        Texture2D m_FrameTexture;
        byte[] m_FrameBuffer;
        byte[] m_ConversionBuffer;
        string[] m_Labels = Array.Empty<string>();
        FramePreprocessInfo m_LastPreprocessInfo;
        Matrix4x4 m_PendingDisplayMatrix = Matrix4x4.identity;
        bool m_HasPendingDisplayMatrix;
        float m_NextInferenceTime;
        float m_StartTime;
        float m_LastNoCpuImageLogTime = float.NegativeInfinity;
        float m_LastNoDetectionLogTime = float.NegativeInfinity;
        float m_LastFrameConvertedLogTime = float.NegativeInfinity;
        float m_LastSkippedFrameLogTime = float.NegativeInfinity;
        bool m_IsRunning;
        bool m_ConversionPending;
        bool m_SchedulingInference;
        bool m_InferencePending;
        bool m_SentisReady;
        bool m_LoggedConfigurationWarning;
        BackendType m_ActiveBackend;
        int m_FrameReceivedCount;
        int m_FrameConvertedCount;
        int m_InferenceCount;
        int m_LastDecodedCandidateCount;
        int m_LastNmsResultCount;
        int m_LastBestRawClassIndex = -1;
        float m_LastBestRawScore;
        string m_LastDecodeSummary = string.Empty;
        RecognitionServiceState? m_LastPublishedState;
        string m_LastPublishedStatusMessage = string.Empty;

        public event Action<ObjectRecognitionResult> ObjectRecognized;

        public event Action<IReadOnlyList<ObjectRecognitionResult>> ObjectRecognitionsUpdated;

        public event Action<RecognitionServiceStatus> StatusChanged;

        public bool IsRunning => m_IsRunning;

        void OnValidate()
        {
            m_InputWidth = Mathf.Max(1, m_InputWidth);
            m_InputHeight = Mathf.Max(1, m_InputHeight);
            m_ConfidenceThreshold = Mathf.Clamp01(m_ConfidenceThreshold);
            m_NmsThreshold = Mathf.Clamp01(m_NmsThreshold);
            m_InferenceIntervalSeconds = Mathf.Max(0.05f, m_InferenceIntervalSeconds);
            m_LayersPerFrame = Mathf.Max(1, m_LayersPerFrame);
            m_MaxDetections = Mathf.Max(1, m_MaxDetections);
        }

        void OnDisable()
        {
            StopRecognition();
            DisposeRuntimeResources();
        }

        void Update()
        {
            TryCompleteFrameConversion();
            AdvanceIncrementalInference();
            TryCompletePendingInference();
        }

        public void StartRecognition()
        {
            if (m_IsRunning)
                return;

            PublishStatus(RecognitionServiceState.Initializing, "Initializing camera and object detector...");
            m_SentisReady = TryInitialize();
            if (!m_SentisReady)
            {
                PublishNoDetections("Sentis detector did not start because initialization failed.");
                PublishStatus(
                    RecognitionServiceState.Faulted,
                    "Object detector could not start. Check the AR camera and model configuration.");
                return;
            }

            m_IsRunning = true;
            m_StartTime = Time.unscaledTime;
            m_NextInferenceTime = 0f;
            m_FrameReceivedCount = 0;
            m_FrameConvertedCount = 0;
            m_InferenceCount = 0;
            m_ConversionPending = false;
            m_SchedulingInference = false;
            m_InferencePending = false;
            m_InferenceSchedule = null;
            m_LastBestRawClassIndex = -1;
            m_LastBestRawScore = 0f;
            m_LastDecodeSummary = string.Empty;

            LogDebug(
                $"Starting Sentis object recognition. ready={m_SentisReady}, backend={m_ActiveBackend}, " +
                $"input={m_InputWidth}x{m_InputHeight} {m_InputLayout}, preserveAspect={m_PreserveAspectRatio}, " +
                $"confidence={m_ConfidenceThreshold:0.00}, nms={m_NmsThreshold:0.00}, maxDetections={m_MaxDetections}, " +
                $"layersPerFrame={m_LayersPerFrame}. " +
                "Only model predictions will be emitted.");

            m_CameraManager.frameReceived += OnCameraFrameReceived;
            PublishStatus(RecognitionServiceState.Running, "Scanning for supported objects...");
        }

        public void StopRecognition()
        {
            if (!m_IsRunning)
                return;

            if (m_CameraManager != null)
                m_CameraManager.frameReceived -= OnCameraFrameReceived;

            LogDebug(
                $"Stopped Sentis object recognition. frames={m_FrameReceivedCount}, converted={m_FrameConvertedCount}, " +
                $"inferences={m_InferenceCount}.");

            m_IsRunning = false;
            m_SentisReady = false;
            CancelPendingPipeline(true);
            PublishStatus(RecognitionServiceState.Stopped, "Recognition paused.");
        }

        bool TryInitialize()
        {
            if (m_CameraManager == null)
                m_CameraManager = FindAnyObjectByType<ARCameraManager>();

            if (m_CameraManager == null)
            {
                LogConfigurationWarning("Sentis object recognition needs an ARCameraManager in the scene.");
                return false;
            }

            if (m_ModelAsset == null)
            {
                LogConfigurationWarning("Sentis object recognition needs a ModelAsset assigned in ML/Models.");
                return false;
            }

            try
            {
                if (m_RuntimeModel == null)
                {
                    m_RuntimeModel = ModelLoader.Load(m_ModelAsset);
                    m_Labels = ParseLabels(m_LabelAsset);
                    ConfigureInputFromModel();
                    LogModelSummary();
                }

                if (m_Worker == null && !TryCreateWorkerWithFallback())
                    return false;

                EnsureInputTensor();
                LogDebug($"Input tensor ready: {m_InputTensor.shape}. Camera manager: {m_CameraManager.name}.");
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                DisposeRuntimeResources();
                return false;
            }
        }

        void OnCameraFrameReceived(ARCameraFrameEventArgs eventArgs)
        {
            m_FrameReceivedCount++;

            if (!m_IsRunning)
                return;

            if (m_ConversionPending || m_SchedulingInference || m_InferencePending)
            {
                LogDebugThrottled(
                    ref m_LastSkippedFrameLogTime,
                    "Skipping camera frame because conversion, incremental inference, or GPU readback is still running.");
                return;
            }

            if (Time.unscaledTime < m_NextInferenceTime)
                return;

            try
            {
                StartFrameConversion(eventArgs);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                PublishNoDetections($"{exception.GetType().Name}: {exception.Message}");
                m_NextInferenceTime = Time.unscaledTime + m_InferenceIntervalSeconds;
                TryRecoverFromInferenceFailure(exception);
            }
        }

        void StartFrameConversion(ARCameraFrameEventArgs eventArgs)
        {
            if (!m_CameraManager.TryAcquireLatestCpuImage(out var cpuImage))
            {
                LogDebugThrottled(ref m_LastNoCpuImageLogTime, "No AR CPU camera image was available for this inference tick.");
                PublishNoDetections("No AR CPU camera image was available.");
                PublishStatus(
                    RecognitionServiceState.AwaitingCamera,
                    "Waiting for an AR camera frame. Check camera permission and AR tracking.");
                return;
            }

            try
            {
                var outputDimensions = CalculatePreprocessedImageSize(cpuImage.width, cpuImage.height);
                var padX = (m_InputWidth - outputDimensions.x) / 2;
                var padY = (m_InputHeight - outputDimensions.y) / 2;
                var conversionParams = new XRCpuImage.ConversionParams
                {
                    inputRect = new RectInt(0, 0, cpuImage.width, cpuImage.height),
                    outputDimensions = outputDimensions,
                    outputFormat = TextureFormat.RGB24,
                    transformation = m_ImageTransformation
                };

                m_ConversionRequest = cpuImage.ConvertAsync(conversionParams);
                m_ConversionPending = true;
                m_LastPreprocessInfo = new FramePreprocessInfo(
                    cpuImage.width,
                    cpuImage.height,
                    outputDimensions.x,
                    outputDimensions.y,
                    padX,
                    padY,
                    m_InputWidth,
                    m_InputHeight,
                    m_ImageTransformation,
                    cpuImage.format.ToString());
                m_HasPendingDisplayMatrix = m_ApplyCameraDisplayTransform && eventArgs.displayMatrix.HasValue;
                m_PendingDisplayMatrix = eventArgs.displayMatrix ?? Matrix4x4.identity;
                LogDebug($"Started asynchronous camera conversion. {m_LastPreprocessInfo}; frame={FormatFrameEvent(eventArgs)}.");
            }
            finally
            {
                cpuImage.Dispose();
            }
        }

        void TryCompleteFrameConversion()
        {
            if (!m_ConversionPending || !m_ConversionRequest.status.IsDone())
                return;

            try
            {
                if (m_ConversionRequest.status != XRCpuImage.AsyncConversionStatus.Ready)
                {
                    PublishNoDetections($"Camera conversion failed with status {m_ConversionRequest.status}.");
                    PublishStatus(
                        RecognitionServiceState.Degraded,
                        "The camera frame could not be prepared. Retrying...");
                    return;
                }

                var convertedData = m_ConversionRequest.GetData<byte>();
                if (!convertedData.IsCreated || convertedData.Length == 0)
                {
                    PublishNoDetections("The asynchronous camera conversion returned no pixels.");
                    return;
                }

                EnsureFrameStorage(m_InputWidth, m_InputHeight, convertedData.Length);
                convertedData.CopyTo(m_ConversionBuffer);
                var outputDimensions = m_ConversionRequest.conversionParams.outputDimensions;
                var padX = (m_InputWidth - outputDimensions.x) / 2;
                var padY = (m_InputHeight - outputDimensions.y) / 2;
                CopyConvertedFrameIntoModelTexture(outputDimensions, padX, padY);
                m_FrameTexture.LoadRawTextureData(m_FrameBuffer);
                m_FrameTexture.Apply(false, false);
                m_FrameConvertedCount++;
                LogDebugThrottled(
                    ref m_LastFrameConvertedLogTime,
                    $"Camera frame converted asynchronously. {m_LastPreprocessInfo}, format=RGB24, bytes={convertedData.Length}.");

                if (!m_IsRunning)
                    return;

                BeginIncrementalInference();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (m_IsRunning)
                    PublishNoDetections($"{exception.GetType().Name}: {exception.Message}");
                PublishStatus(
                    RecognitionServiceState.Degraded,
                    "Object detection encountered a camera conversion error and is retrying.");
                TryRecoverFromInferenceFailure(exception);
            }
            finally
            {
                m_ConversionRequest.Dispose();
                m_ConversionPending = false;
                if (!m_SchedulingInference)
                {
                    m_HasPendingDisplayMatrix = false;
                    m_NextInferenceTime = Time.unscaledTime + m_InferenceIntervalSeconds;
                }
            }
        }

        void BeginIncrementalInference()
        {
            var textureTransform = new TextureTransform().SetTensorLayout(m_InputLayout);
            LogDebug(
                $"Converting camera texture {m_FrameTexture.width}x{m_FrameTexture.height} to tensor {m_InputTensor.shape}. " +
                $"preprocess={m_LastPreprocessInfo}.");
            TextureConverter.ToTensor(m_FrameTexture, m_InputTensor, textureTransform);

            m_InferenceCount++;
            LogDebug(
                $"Starting incremental inference #{m_InferenceCount} on {m_Worker.backendType}, " +
                $"{m_LayersPerFrame} layer(s) per rendered frame.");
            m_InferenceSchedule = m_Worker.ScheduleIterable(m_InputTensor);
            m_SchedulingInference = true;
            PublishStatus(RecognitionServiceState.Running, "Scanning for supported objects...");
        }

        void AdvanceIncrementalInference()
        {
            if (!m_SchedulingInference || m_InferenceSchedule == null)
                return;

            try
            {
                for (var layer = 0; layer < m_LayersPerFrame; layer++)
                {
                    if (m_InferenceSchedule.MoveNext())
                        continue;

                    CompleteInferenceScheduling();
                    return;
                }
            }
            catch (Exception exception)
            {
                m_SchedulingInference = false;
                m_InferenceSchedule = null;
                Debug.LogException(exception);
                if (m_IsRunning)
                    PublishNoDetections($"{exception.GetType().Name}: {exception.Message}");
                PublishStatus(
                    RecognitionServiceState.Degraded,
                    "Object detection encountered an inference error and is retrying.");
                m_NextInferenceTime = Time.unscaledTime + m_InferenceIntervalSeconds;
                TryRecoverFromInferenceFailure(exception);
            }
        }

        void CompleteInferenceScheduling()
        {
            m_SchedulingInference = false;
            m_InferenceSchedule = null;

            if (!(m_Worker.PeekOutput() is Tensor<float> outputTensor))
            {
                Debug.LogWarning("Sentis detector output is not a float tensor.");
                PublishNoDetections("Sentis output was not a float tensor.");
                m_HasPendingDisplayMatrix = false;
                m_NextInferenceTime = Time.unscaledTime + m_InferenceIntervalSeconds;
                return;
            }

            m_PendingOutputTensor = outputTensor;
            m_PendingOutputTensor.ReadbackRequest();
            m_InferencePending = true;
            LogDebug($"Inference #{m_InferenceCount} finished scheduling; asynchronous output readback started.");
        }

        void TryCompletePendingInference()
        {
            if (!m_InferencePending || m_PendingOutputTensor == null)
                return;

            if (!m_PendingOutputTensor.IsReadbackRequestDone())
                return;

            try
            {
                using var cpuOutput = m_PendingOutputTensor.ReadbackAndClone();
                LogDebug($"Inference #{m_InferenceCount} output tensor shape: {cpuOutput.shape}.");

                if (!TryDecodeDetections(cpuOutput))
                {
                    LogDebugThrottled(
                        ref m_LastNoDetectionLogTime,
                        $"Inference #{m_InferenceCount} produced no accepted detection. {m_LastDecodeSummary}");
                    if (m_IsRunning)
                        PublishNoDetections(m_LastDecodeSummary);
                    return;
                }

                if (m_IsRunning)
                    PublishDetections();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (m_IsRunning)
                    PublishNoDetections($"{exception.GetType().Name}: {exception.Message}");
                PublishStatus(
                    RecognitionServiceState.Degraded,
                    "Object detection encountered an error and is attempting to recover.");
                TryRecoverFromInferenceFailure(exception);
            }
            finally
            {
                m_InferencePending = false;
                m_PendingOutputTensor = null;
                m_HasPendingDisplayMatrix = false;
                m_NextInferenceTime = Time.unscaledTime + m_InferenceIntervalSeconds;
            }
        }

        void EnsureInputTensor()
        {
            var expectedShape = m_InputLayout == TensorLayout.NHWC
                ? new TensorShape(1, m_InputHeight, m_InputWidth, InputChannels)
                : new TensorShape(1, InputChannels, m_InputHeight, m_InputWidth);

            if (m_InputTensor != null && m_InputTensor.shape == expectedShape)
                return;

            m_InputTensor?.Dispose();
            m_InputTensor = new Tensor<float>(expectedShape);
        }

        void ConfigureInputFromModel()
        {
            if (m_RuntimeModel == null || m_RuntimeModel.inputs.Count == 0)
                return;

            var input = m_RuntimeModel.inputs[0];
            if (!input.shape.IsStatic())
            {
                Debug.LogWarning(
                    $"Sentis detector input '{input.name}' has a dynamic shape ({input.shape}). " +
                    $"Using inspector input settings {m_InputWidth}x{m_InputHeight} {m_InputLayout}.");
                return;
            }

            var shape = input.shape.ToTensorShape();
            if (shape.rank != 4)
            {
                Debug.LogWarning(
                    $"Sentis detector input '{input.name}' is rank {shape.rank}, expected rank 4. " +
                    $"Using inspector input settings {m_InputWidth}x{m_InputHeight} {m_InputLayout}.");
                return;
            }

            if (shape[1] == InputChannels)
            {
                m_InputLayout = TensorLayout.NCHW;
                m_InputHeight = shape[2];
                m_InputWidth = shape[3];
            }
            else if (shape[3] == InputChannels)
            {
                m_InputLayout = TensorLayout.NHWC;
                m_InputHeight = shape[1];
                m_InputWidth = shape[2];
            }
            else
            {
                Debug.LogWarning(
                    $"Sentis detector input '{input.name}' shape {shape} does not expose a 3-channel image axis. " +
                    $"Using inspector input settings {m_InputWidth}x{m_InputHeight} {m_InputLayout}.");
                return;
            }

            LogDebug($"Model input '{input.name}' resolved to {m_InputWidth}x{m_InputHeight} {m_InputLayout}.");
        }

        void EnsureFrameStorage(int width, int height, int dataSize)
        {
            if (m_FrameTexture == null || m_FrameTexture.width != width || m_FrameTexture.height != height)
            {
                if (m_FrameTexture != null)
                    Destroy(m_FrameTexture);

                m_FrameTexture = new Texture2D(width, height, TextureFormat.RGB24, false)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
            }

            var frameDataSize = width * height * InputPixelBytes;
            if (m_FrameBuffer == null || m_FrameBuffer.Length != frameDataSize)
                m_FrameBuffer = new byte[frameDataSize];

            if (m_ConversionBuffer == null || m_ConversionBuffer.Length != dataSize)
                m_ConversionBuffer = new byte[dataSize];
        }

        Vector2Int CalculatePreprocessedImageSize(int sourceWidth, int sourceHeight)
        {
            if (!m_PreserveAspectRatio)
                return new Vector2Int(m_InputWidth, m_InputHeight);

            var scale = Mathf.Min(
                m_InputWidth / Mathf.Max(1f, sourceWidth),
                m_InputHeight / Mathf.Max(1f, sourceHeight));
            var width = Mathf.Clamp(Mathf.RoundToInt(sourceWidth * scale), 1, m_InputWidth);
            var height = Mathf.Clamp(Mathf.RoundToInt(sourceHeight * scale), 1, m_InputHeight);
            return new Vector2Int(width, height);
        }

        void CopyConvertedFrameIntoModelTexture(Vector2Int convertedDimensions, int padX, int padY)
        {
            if (m_PreserveAspectRatio)
                FillLetterboxBuffer();

            var sourceStride = convertedDimensions.x * InputPixelBytes;
            var targetStride = m_InputWidth * InputPixelBytes;

            for (var row = 0; row < convertedDimensions.y; row++)
            {
                var sourceOffset = row * sourceStride;
                var targetOffset = ((row + padY) * targetStride) + (padX * InputPixelBytes);
                Buffer.BlockCopy(m_ConversionBuffer, sourceOffset, m_FrameBuffer, targetOffset, sourceStride);
            }
        }

        void FillLetterboxBuffer()
        {
            Array.Fill(m_FrameBuffer, LetterboxFillValue);
        }

        bool TryDecodeDetections(Tensor<float> output)
        {
            m_DetectionCandidates.Clear();
            m_NmsResults.Clear();
            m_LastDecodedCandidateCount = 0;
            m_LastNmsResultCount = 0;
            m_LastBestRawClassIndex = -1;
            m_LastBestRawScore = 0f;
            m_LastDecodeSummary = $"outputShape={output.shape}, threshold={m_ConfidenceThreshold:0.00}.";

            if (!TryGetMatrixLayout(output.shape, out var layout))
            {
                m_LastDecodeSummary = $"Unsupported detector output shape: {output.shape}.";
                Debug.LogWarning(m_LastDecodeSummary);
                return false;
            }

            var firstLooksLikeAttributes = LooksLikeAttributeDimension(layout.FirstDimension, layout.SecondDimension);
            var secondLooksLikeAttributes = LooksLikeAttributeDimension(layout.SecondDimension, layout.FirstDimension);
            var firstLooksLikeEndToEnd = LooksLikeEndToEndDimension(layout.FirstDimension, layout.SecondDimension);
            var secondLooksLikeEndToEnd = LooksLikeEndToEndDimension(layout.SecondDimension, layout.FirstDimension);

            if (firstLooksLikeEndToEnd)
                DecodeYoloEndToEnd(output, layout, layout.SecondDimension, true);
            else if (secondLooksLikeEndToEnd)
                DecodeYoloEndToEnd(output, layout, layout.FirstDimension, false);
            else if (firstLooksLikeAttributes)
                DecodeYoloMatrix(output, layout, layout.FirstDimension, layout.SecondDimension, true);
            else if (secondLooksLikeAttributes)
                DecodeYoloMatrix(output, layout, layout.SecondDimension, layout.FirstDimension, false);
            else
            {
                m_LastDecodeSummary = $"Could not infer detector output layout from shape: {output.shape}.";
                Debug.LogWarning(m_LastDecodeSummary);
                return false;
            }

            m_LastDecodedCandidateCount = m_DetectionCandidates.Count;
            if (m_DetectionCandidates.Count == 0)
            {
                m_LastDecodeSummary =
                    $"Decoded layout={layout.FirstDimension}x{layout.SecondDimension}; accepted=0; " +
                    $"bestRaw={FormatBestRawCandidate()}; threshold={m_ConfidenceThreshold:0.00}.";
                return false;
            }

            ApplyNms(m_DetectionCandidates, m_NmsResults);
            m_LastNmsResultCount = m_NmsResults.Count;
            if (m_NmsResults.Count == 0)
            {
                m_LastDecodeSummary =
                    $"Decoded layout={layout.FirstDimension}x{layout.SecondDimension}; accepted={m_LastDecodedCandidateCount}; nms=0; " +
                    $"bestRaw={FormatBestRawCandidate()}; nmsThreshold={m_NmsThreshold:0.00}.";
                return false;
            }

            m_LastDecodeSummary =
                $"Decoded layout={layout.FirstDimension}x{layout.SecondDimension}; accepted={m_LastDecodedCandidateCount}; " +
                $"nms={m_LastNmsResultCount}; bestRaw={FormatBestRawCandidate()}; detections={FormatDetectionCandidates(m_NmsResults)}.";
            return true;
        }

        void DecodeYoloMatrix(
            Tensor<float> output,
            MatrixLayout layout,
            int attributeCount,
            int candidateCount,
            bool attributesFirst)
        {
            var hasObjectness = HasObjectness(attributeCount);
            var classStartIndex = hasObjectness ? 5 : 4;
            var availableClassCount = attributeCount - classStartIndex;
            var classCount = m_Labels.Length > 0
                ? Mathf.Min(m_Labels.Length, availableClassCount)
                : availableClassCount;
            if (classCount <= 0)
                return;

            for (var candidateIndex = 0; candidateIndex < candidateCount; candidateIndex++)
            {
                var objectness = hasObjectness
                    ? NormalizeScore(ReadYoloValue(output, layout, attributesFirst, 4, candidateIndex))
                    : 1f;

                var bestClassIndex = -1;
                var bestClassScore = 0f;
                for (var classOffset = 0; classOffset < classCount; classOffset++)
                {
                    var classScore = NormalizeScore(ReadYoloValue(
                        output,
                        layout,
                        attributesFirst,
                        classStartIndex + classOffset,
                        candidateIndex));

                    if (!IsFinite(classScore))
                        continue;

                    if (classScore <= bestClassScore)
                        continue;

                    bestClassScore = classScore;
                    bestClassIndex = classOffset;
                }

                var score = objectness * bestClassScore;
                TrackBestRawCandidate(bestClassIndex, score);
                if (bestClassIndex < 0 || !IsFinite(score) || score < m_ConfidenceThreshold)
                    continue;

                var centerX = ReadYoloValue(output, layout, attributesFirst, 0, candidateIndex);
                var centerY = ReadYoloValue(output, layout, attributesFirst, 1, candidateIndex);
                var width = ReadYoloValue(output, layout, attributesFirst, 2, candidateIndex);
                var height = ReadYoloValue(output, layout, attributesFirst, 3, candidateIndex);

                if (!TryCreateBoundingBox(centerX, centerY, width, height, out var boundingBox))
                    continue;

                m_DetectionCandidates.Add(new DetectionCandidate(
                    ResolveLabel(bestClassIndex),
                    bestClassIndex,
                    score,
                    boundingBox));
            }
        }

        void DecodeYoloEndToEnd(
            Tensor<float> output,
            MatrixLayout layout,
            int candidateCount,
            bool attributesFirst)
        {
            for (var candidateIndex = 0; candidateIndex < candidateCount; candidateIndex++)
            {
                var score = NormalizeScore(ReadYoloValue(output, layout, attributesFirst, 4, candidateIndex));
                var classIndex = Mathf.RoundToInt(ReadYoloValue(output, layout, attributesFirst, 5, candidateIndex));
                TrackBestRawCandidate(classIndex, score);
                if (!IsFinite(score) || score < m_ConfidenceThreshold || classIndex < 0)
                    continue;

                var xMin = ReadYoloValue(output, layout, attributesFirst, 0, candidateIndex);
                var yMin = ReadYoloValue(output, layout, attributesFirst, 1, candidateIndex);
                var xMax = ReadYoloValue(output, layout, attributesFirst, 2, candidateIndex);
                var yMax = ReadYoloValue(output, layout, attributesFirst, 3, candidateIndex);

                if (!TryCreateXyxyBoundingBox(xMin, yMin, xMax, yMax, out var boundingBox))
                    continue;

                m_DetectionCandidates.Add(new DetectionCandidate(
                    ResolveLabel(classIndex),
                    classIndex,
                    score,
                    boundingBox));
            }
        }

        bool HasObjectness(int attributeCount)
        {
            switch (m_OutputFormat)
            {
                case SentisDetectionOutputFormat.YoloV8CenterXYWH:
                    return false;
                case SentisDetectionOutputFormat.YoloWithObjectnessCenterXYWH:
                    return true;
                default:
                    return m_Labels.Length > 0
                        ? attributeCount == m_Labels.Length + 5
                        : attributeCount >= 85;
            }
        }

        void ApplyNms(List<DetectionCandidate> source, List<DetectionCandidate> destination)
        {
            source.Sort((left, right) => right.Score.CompareTo(left.Score));

            for (var sourceIndex = 0; sourceIndex < source.Count; sourceIndex++)
            {
                var candidate = source[sourceIndex];
                var suppressed = false;

                for (var selectedIndex = 0; selectedIndex < destination.Count; selectedIndex++)
                {
                    var selected = destination[selectedIndex];
                    if (candidate.ClassIndex == selected.ClassIndex &&
                        CalculateIntersectionOverUnion(candidate.BoundingBox, selected.BoundingBox) > m_NmsThreshold)
                    {
                        suppressed = true;
                        break;
                    }
                }

                if (suppressed)
                    continue;

                destination.Add(candidate);
                if (destination.Count >= m_MaxDetections)
                    return;
            }
        }

        void PublishDetections()
        {
            m_RecognitionResults.Clear();

            for (var index = 0; index < m_NmsResults.Count; index++)
            {
                var detection = m_NmsResults[index];
                m_RecognitionResults.Add(new ObjectRecognitionResult(
                    detection.Label,
                    detection.Score,
                    $"sentis:{m_InferenceCount}:{detection.ClassIndex}",
                    detection.BoundingBox));
            }

            if (m_RecognitionResults.Count == 0)
            {
                PublishNoDetections("NMS produced no publishable detections.");
                return;
            }

            var best = m_RecognitionResults[0];
            LogDebug(
                $"Inference #{m_InferenceCount} accepted {m_RecognitionResults.Count} model detection(s): " +
                $"{FormatRecognitionResults(m_RecognitionResults)}.");

            var snapshot = m_RecognitionResults.ToArray();
            ObjectRecognitionsUpdated?.Invoke(snapshot);
            ObjectRecognized?.Invoke(best);
        }

        void PublishNoDetections(string reason)
        {
            if (m_DebugLogging && !string.IsNullOrWhiteSpace(reason))
                LogDebug($"Publishing empty detection set. reason={reason}");

            ObjectRecognitionsUpdated?.Invoke(Array.Empty<ObjectRecognitionResult>());
        }

        bool TryCreateBoundingBox(
            float rawCenterX,
            float rawCenterY,
            float rawWidth,
            float rawHeight,
            out NormalizedBoundingBox boundingBox)
        {
            return TryCreateXyxyBoundingBox(
                rawCenterX - (rawWidth * 0.5f),
                rawCenterY - (rawHeight * 0.5f),
                rawCenterX + (rawWidth * 0.5f),
                rawCenterY + (rawHeight * 0.5f),
                out boundingBox);
        }

        bool TryCreateXyxyBoundingBox(
            float rawXMin,
            float rawYMin,
            float rawXMax,
            float rawYMax,
            out NormalizedBoundingBox boundingBox)
        {
            boundingBox = default;

            if (!IsFinite(rawXMin) || !IsFinite(rawYMin) || !IsFinite(rawXMax) || !IsFinite(rawYMax))
                return false;

            var xMin = NormalizeModelHorizontalCoordinate(rawXMin);
            var yMin = NormalizeModelVerticalCoordinate(rawYMin);
            var xMax = NormalizeModelHorizontalCoordinate(rawXMax);
            var yMax = NormalizeModelVerticalCoordinate(rawYMax);

            if (xMin > xMax)
                (xMin, xMax) = (xMax, xMin);

            if (yMin > yMax)
                (yMin, yMax) = (yMax, yMin);

            if (xMax <= 0f || yMax <= 0f || xMin >= 1f || yMin >= 1f)
                return false;

            xMin = Mathf.Clamp01(xMin);
            yMin = Mathf.Clamp01(yMin);
            xMax = Mathf.Clamp01(xMax);
            yMax = Mathf.Clamp01(yMax);

            boundingBox = new NormalizedBoundingBox(xMin, yMin, xMax - xMin, yMax - yMin);
            if (m_HasPendingDisplayMatrix)
                boundingBox = TransformCameraBoxToDisplay(boundingBox, m_PendingDisplayMatrix);

            return boundingBox.IsValid;
        }

        static NormalizedBoundingBox TransformCameraBoxToDisplay(
            NormalizedBoundingBox cameraBox,
            Matrix4x4 displayMatrix)
        {
            // AR Foundation's display matrix maps display UVs to camera UVs. Detection boxes
            // travel in the opposite direction, so transform all corners by its inverse.
            var cameraToDisplay = displayMatrix.inverse;
            var xMin = cameraBox.X;
            var xMax = cameraBox.X + cameraBox.Width;
            var yMin = cameraBox.Y;
            var yMax = cameraBox.Y + cameraBox.Height;
            var corners = new[]
            {
                TransformUv(cameraToDisplay, xMin, yMin),
                TransformUv(cameraToDisplay, xMax, yMin),
                TransformUv(cameraToDisplay, xMin, yMax),
                TransformUv(cameraToDisplay, xMax, yMax)
            };

            var displayXMin = corners.Min(point => point.x);
            var displayXMax = corners.Max(point => point.x);
            var displayYMin = corners.Min(point => point.y);
            var displayYMax = corners.Max(point => point.y);
            displayXMin = Mathf.Clamp01(displayXMin);
            displayXMax = Mathf.Clamp01(displayXMax);
            displayYMin = Mathf.Clamp01(displayYMin);
            displayYMax = Mathf.Clamp01(displayYMax);
            return new NormalizedBoundingBox(
                displayXMin,
                displayYMin,
                displayXMax - displayXMin,
                displayYMax - displayYMin);
        }

        static Vector2 TransformUv(Matrix4x4 cameraToDisplay, float x, float yFromTop)
        {
            var cameraUv = new Vector3(x, 1f - yFromTop, 0f);
            var displayUv = cameraToDisplay.MultiplyPoint3x4(cameraUv);
            return new Vector2(displayUv.x, 1f - displayUv.y);
        }

        float NormalizeModelHorizontalCoordinate(float value)
        {
            var modelX = Mathf.Abs(value) > 1.5f ? value : value * m_InputWidth;
            return (modelX - m_LastPreprocessInfo.PadX) / Mathf.Max(1f, m_LastPreprocessInfo.ContentWidth);
        }

        float NormalizeModelVerticalCoordinate(float value)
        {
            var modelY = Mathf.Abs(value) > 1.5f ? value : value * m_InputHeight;
            return (modelY - m_LastPreprocessInfo.PadY) / Mathf.Max(1f, m_LastPreprocessInfo.ContentHeight);
        }

        float ReadYoloValue(
            Tensor<float> output,
            MatrixLayout layout,
            bool attributesFirst,
            int attributeIndex,
            int candidateIndex)
        {
            return attributesFirst
                ? layout.Read(output, attributeIndex, candidateIndex)
                : layout.Read(output, candidateIndex, attributeIndex);
        }

        string ResolveLabel(int classIndex)
        {
            if (classIndex >= 0 && classIndex < m_Labels.Length)
                return m_Labels[classIndex];

            return $"class_{classIndex}";
        }

        void LogConfigurationWarning(string message)
        {
            if (m_LoggedConfigurationWarning)
                return;

            m_LoggedConfigurationWarning = true;
            Debug.LogWarning(message);
        }

        void PublishStatus(RecognitionServiceState state, string message)
        {
            if (m_LastPublishedState == state &&
                string.Equals(m_LastPublishedStatusMessage, message, StringComparison.Ordinal))
            {
                return;
            }

            m_LastPublishedState = state;
            m_LastPublishedStatusMessage = message ?? string.Empty;
            StatusChanged?.Invoke(new RecognitionServiceStatus(state, message));
        }

        void DisposeRuntimeResources()
        {
            CancelPendingPipeline(false);
            m_Worker?.Dispose();
            m_Worker = null;
            m_RuntimeModel = null;

            m_InputTensor?.Dispose();
            m_InputTensor = null;

            if (m_FrameTexture != null)
                Destroy(m_FrameTexture);

            m_FrameTexture = null;
            m_FrameBuffer = null;
            m_ConversionBuffer = null;
        }

        void CancelPendingPipeline(bool disposeWorker)
        {
            if (m_ConversionPending)
                m_ConversionRequest.Dispose();

            m_ConversionPending = false;
            m_InferenceSchedule = null;
            m_SchedulingInference = false;
            m_PendingOutputTensor = null;
            m_InferencePending = false;
            m_HasPendingDisplayMatrix = false;
            m_NextInferenceTime = Time.unscaledTime + m_InferenceIntervalSeconds;

            if (!disposeWorker)
                return;

            m_Worker?.Dispose();
            m_Worker = null;
        }

        static bool LooksLikeAttributeDimension(int dimension, int otherDimension)
        {
            return dimension >= 5 && dimension <= 512 && otherDimension > dimension;
        }

        bool LooksLikeEndToEndDimension(int dimension, int otherDimension)
        {
            return dimension == 6 &&
                otherDimension > 1 &&
                (m_OutputFormat == SentisDetectionOutputFormat.AutoYolo ||
                 m_OutputFormat == SentisDetectionOutputFormat.YoloEndToEndXYXY);
        }

        static bool TryGetMatrixLayout(TensorShape shape, out MatrixLayout layout)
        {
            layout = default;

            if (shape.rank == 2)
            {
                layout = MatrixLayout.Rank2(shape[0], shape[1]);
                return true;
            }

            if (shape.rank == 3 && shape[0] == 1)
            {
                layout = MatrixLayout.Rank3BatchFirst(shape[1], shape[2]);
                return true;
            }

            if (shape.rank == 3 && shape[2] == 1)
            {
                layout = MatrixLayout.Rank3TrailingOne(shape[0], shape[1]);
                return true;
            }

            if (shape.rank == 4 && shape[0] == 1 && shape[3] == 1)
            {
                layout = MatrixLayout.Rank4BatchFirstTrailingOne(shape[1], shape[2]);
                return true;
            }

            return false;
        }

        static string[] ParseLabels(TextAsset labelAsset)
        {
            if (labelAsset == null || string.IsNullOrWhiteSpace(labelAsset.text))
                return Array.Empty<string>();

            var rawLines = labelAsset.text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var labels = new List<string>(rawLines.Length);
            for (var index = 0; index < rawLines.Length; index++)
            {
                var label = CleanLabel(rawLines[index]);
                if (!string.IsNullOrWhiteSpace(label))
                    labels.Add(label);
            }

            return labels.ToArray();
        }

        static string CleanLabel(string line)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
                return string.Empty;

            var separatorIndex = trimmed.IndexOf(':');
            if (separatorIndex > 0 && int.TryParse(trimmed.Substring(0, separatorIndex), out _))
                return trimmed.Substring(separatorIndex + 1).Trim();

            for (var index = 0; index < trimmed.Length; index++)
            {
                if (char.IsDigit(trimmed[index]))
                    continue;

                if (index > 0 && char.IsWhiteSpace(trimmed[index]))
                    return trimmed.Substring(index + 1).Trim();

                break;
            }

            return trimmed;
        }

        bool TryCreateWorkerWithFallback()
        {
            if (TryCreateWorker(m_Backend, out var worker))
            {
                m_Worker = worker;
                return true;
            }

            if (m_Backend == BackendType.CPU)
                return false;

            Debug.LogWarning($"Sentis detector could not start with {m_Backend}; retrying with CPU backend.");
            if (!TryCreateWorker(BackendType.CPU, out worker))
                return false;

            m_Worker = worker;
            return true;
        }

        bool TryCreateWorker(BackendType backend, out Worker worker)
        {
            worker = null;

            try
            {
                worker = new Worker(m_RuntimeModel, backend);
                m_ActiveBackend = worker.backendType;
                LogDebug($"Sentis worker created with backend {m_ActiveBackend}.");
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Failed to create Sentis worker with backend {backend}: {exception.GetType().Name}: {exception.Message}");
                worker?.Dispose();
                return false;
            }
        }

        void TryRecoverFromInferenceFailure(Exception exception)
        {
            if (m_Worker == null || m_Worker.backendType == BackendType.CPU)
                return;

            Debug.LogWarning(
                $"Sentis inference failed on {m_Worker.backendType}: {exception.GetType().Name}: {exception.Message}. " +
                "Switching to CPU backend for subsequent frames.");

            try
            {
                m_Worker.Dispose();
                m_Worker = new Worker(m_RuntimeModel, BackendType.CPU);
                m_ActiveBackend = m_Worker.backendType;
                m_NextInferenceTime = Time.unscaledTime + m_InferenceIntervalSeconds;
                LogDebug("Sentis worker recovered on CPU backend.");
            }
            catch (Exception recoveryException)
            {
                Debug.LogException(recoveryException);
                DisposeRuntimeResources();
                m_SentisReady = false;
            }
        }

        void LogModelSummary()
        {
            LogDebug(
                $"Sentis object detector loaded from '{m_ModelAsset.name}'. " +
                $"inputs={FormatModelInputs()}, outputs={FormatModelOutputs()}, labels={m_Labels.Length}.");
        }

        string FormatModelInputs()
        {
            if (m_RuntimeModel == null || m_RuntimeModel.inputs.Count == 0)
                return "none";

            var parts = new string[m_RuntimeModel.inputs.Count];
            for (var index = 0; index < m_RuntimeModel.inputs.Count; index++)
            {
                var input = m_RuntimeModel.inputs[index];
                parts[index] = $"{input.name}:{input.shape}[{input.dataType}]";
            }

            return string.Join(", ", parts);
        }

        string FormatModelOutputs()
        {
            if (m_RuntimeModel == null || m_RuntimeModel.outputs.Count == 0)
                return "none";

            var parts = new string[m_RuntimeModel.outputs.Count];
            for (var index = 0; index < m_RuntimeModel.outputs.Count; index++)
                parts[index] = m_RuntimeModel.outputs[index].name;

            return string.Join(", ", parts);
        }

        void TrackBestRawCandidate(int classIndex, float score)
        {
            if (classIndex < 0 || !IsFinite(score) || score <= m_LastBestRawScore)
                return;

            m_LastBestRawClassIndex = classIndex;
            m_LastBestRawScore = score;
        }

        string FormatBestRawCandidate()
        {
            return m_LastBestRawClassIndex < 0
                ? "none"
                : $"{ResolveLabel(m_LastBestRawClassIndex)}:{m_LastBestRawScore:0.000}";
        }

        static string FormatDetectionCandidates(IReadOnlyList<DetectionCandidate> detections)
        {
            if (detections == null || detections.Count == 0)
                return "none";

            var count = Mathf.Min(5, detections.Count);
            var parts = new string[count];
            for (var index = 0; index < count; index++)
            {
                var detection = detections[index];
                parts[index] = $"{detection.Label}:{detection.Score:0.000}@{FormatBoundingBox(detection.BoundingBox)}";
            }

            return string.Join("; ", parts);
        }

        static string FormatRecognitionResults(IReadOnlyList<ObjectRecognitionResult> recognitions)
        {
            if (recognitions == null || recognitions.Count == 0)
                return "none";

            var count = Mathf.Min(5, recognitions.Count);
            var parts = new string[count];
            for (var index = 0; index < count; index++)
            {
                var recognition = recognitions[index];
                parts[index] = $"{recognition.Label}:{recognition.Confidence:0.000}@{FormatBoundingBox(recognition.BoundingBox)}";
            }

            return string.Join("; ", parts);
        }

        static string FormatFrameEvent(ARCameraFrameEventArgs eventArgs)
        {
            return
                $"timestampNs={(eventArgs.timestampNs.HasValue ? eventArgs.timestampNs.Value.ToString() : "none")}, " +
                $"displayMatrix={eventArgs.displayMatrix.HasValue}, projectionMatrix={eventArgs.projectionMatrix.HasValue}";
        }

        static string FormatBoundingBox(NormalizedBoundingBox boundingBox)
        {
            return $"x={boundingBox.X:0.000}, y={boundingBox.Y:0.000}, w={boundingBox.Width:0.000}, h={boundingBox.Height:0.000}";
        }

        void LogDebug(string message)
        {
            if (m_DebugLogging)
                Debug.Log($"[SentisObjectRecognition] {message}");
        }

        void LogDebugThrottled(ref float lastLogTime, string message)
        {
            if (!m_DebugLogging || Time.unscaledTime - lastLogTime < DiagnosticLogIntervalSeconds)
                return;

            lastLogTime = Time.unscaledTime;
            Debug.Log($"[SentisObjectRecognition] {message}");
        }

        static float NormalizeScore(float value)
        {
            if (!IsFinite(value))
                return float.NaN;

            if (value >= 0f && value <= 1f)
                return value;

            return 1f / (1f + Mathf.Exp(-value));
        }

        static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        static float CalculateIntersectionOverUnion(NormalizedBoundingBox left, NormalizedBoundingBox right)
        {
            var leftXMax = left.X + left.Width;
            var leftYMax = left.Y + left.Height;
            var rightXMax = right.X + right.Width;
            var rightYMax = right.Y + right.Height;

            var intersectionXMin = Mathf.Max(left.X, right.X);
            var intersectionYMin = Mathf.Max(left.Y, right.Y);
            var intersectionXMax = Mathf.Min(leftXMax, rightXMax);
            var intersectionYMax = Mathf.Min(leftYMax, rightYMax);

            var intersectionWidth = Mathf.Max(0f, intersectionXMax - intersectionXMin);
            var intersectionHeight = Mathf.Max(0f, intersectionYMax - intersectionYMin);
            var intersectionArea = intersectionWidth * intersectionHeight;
            var unionArea = (left.Width * left.Height) + (right.Width * right.Height) - intersectionArea;

            return unionArea <= 0f ? 0f : intersectionArea / unionArea;
        }

        readonly struct FramePreprocessInfo
        {
            public FramePreprocessInfo(
                int sourceWidth,
                int sourceHeight,
                int contentWidth,
                int contentHeight,
                int padX,
                int padY,
                int modelWidth,
                int modelHeight,
                XRCpuImage.Transformation transformation,
                string sourceFormat)
            {
                SourceWidth = sourceWidth;
                SourceHeight = sourceHeight;
                ContentWidth = contentWidth;
                ContentHeight = contentHeight;
                PadX = padX;
                PadY = padY;
                ModelWidth = modelWidth;
                ModelHeight = modelHeight;
                Transformation = transformation;
                SourceFormat = sourceFormat;
            }

            public int SourceWidth { get; }

            public int SourceHeight { get; }

            public int ContentWidth { get; }

            public int ContentHeight { get; }

            public int PadX { get; }

            public int PadY { get; }

            public int ModelWidth { get; }

            public int ModelHeight { get; }

            public XRCpuImage.Transformation Transformation { get; }

            public string SourceFormat { get; }

            public override string ToString()
            {
                return
                    $"source={SourceWidth}x{SourceHeight} ({SourceFormat}), " +
                    $"model={ModelWidth}x{ModelHeight}, content={ContentWidth}x{ContentHeight}, " +
                    $"pad=({PadX},{PadY}), transform={Transformation}";
            }
        }

        readonly struct DetectionCandidate
        {
            public DetectionCandidate(string label, int classIndex, float score, NormalizedBoundingBox boundingBox)
            {
                Label = label;
                ClassIndex = classIndex;
                Score = score;
                BoundingBox = boundingBox;
            }

            public string Label { get; }

            public int ClassIndex { get; }

            public float Score { get; }

            public NormalizedBoundingBox BoundingBox { get; }
        }

        readonly struct MatrixLayout
        {
            readonly MatrixLayoutKind m_Kind;

            MatrixLayout(MatrixLayoutKind kind, int firstDimension, int secondDimension)
            {
                m_Kind = kind;
                FirstDimension = firstDimension;
                SecondDimension = secondDimension;
            }

            public int FirstDimension { get; }

            public int SecondDimension { get; }

            public float Read(Tensor<float> tensor, int row, int column)
            {
                var shape = tensor.shape;
                switch (m_Kind)
                {
                    case MatrixLayoutKind.Rank2:
                        return tensor[(row * shape[1]) + column];
                    case MatrixLayoutKind.Rank3BatchFirst:
                        return tensor[(row * shape[2]) + column];
                    case MatrixLayoutKind.Rank3TrailingOne:
                        return tensor[((row * shape[1]) + column) * shape[2]];
                    case MatrixLayoutKind.Rank4BatchFirstTrailingOne:
                        return tensor[((row * shape[2]) + column) * shape[3]];
                    default:
                        return 0f;
                }
            }

            public static MatrixLayout Rank2(int firstDimension, int secondDimension)
            {
                return new MatrixLayout(MatrixLayoutKind.Rank2, firstDimension, secondDimension);
            }

            public static MatrixLayout Rank3BatchFirst(int firstDimension, int secondDimension)
            {
                return new MatrixLayout(MatrixLayoutKind.Rank3BatchFirst, firstDimension, secondDimension);
            }

            public static MatrixLayout Rank3TrailingOne(int firstDimension, int secondDimension)
            {
                return new MatrixLayout(MatrixLayoutKind.Rank3TrailingOne, firstDimension, secondDimension);
            }

            public static MatrixLayout Rank4BatchFirstTrailingOne(int firstDimension, int secondDimension)
            {
                return new MatrixLayout(MatrixLayoutKind.Rank4BatchFirstTrailingOne, firstDimension, secondDimension);
            }
        }

        enum MatrixLayoutKind
        {
            Rank2,
            Rank3BatchFirst,
            Rank3TrailingOne,
            Rank4BatchFirstTrailingOne
        }
    }
}
