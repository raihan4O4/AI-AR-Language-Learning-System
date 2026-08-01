using System;
using System.Collections;
using System.Collections.Generic;
using AIAR.LanguageLearning.Application.Models;
using AIAR.LanguageLearning.Application.Ports;
using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;

namespace AIAR.LanguageLearning.Unity.ObjectDetection
{
    [DisallowMultipleComponent]
    public sealed class RealtimeCocoObjectDetector : MonoBehaviour, IObjectRecognitionService
    {
        const string CameraTextureProperty = "_MainTex";
        const float MissingFrameLogInterval = 3f;

        enum PipelineState
        {
            Idle,
            Scheduling,
            ReadingOutput,
            Faulted
        }

        [Header("AR camera")]
        [SerializeField]
        ARCameraManager m_CameraManager;

        [SerializeField]
        ARCameraBackground m_CameraBackground;

        [Header("Verified YOLO11n model")]
        [SerializeField]
        ModelAsset m_ModelAsset;

        [SerializeField]
        TextAsset m_CocoLabels;

        [Header("Inference")]
        [SerializeField]
        BackendType m_PreferredBackend = BackendType.GPUCompute;

        [SerializeField, Range(0.05f, 0.95f)]
        float m_ConfidenceThreshold = 0.30f;

        [SerializeField, Range(0.05f, 0.95f)]
        float m_IouThreshold = 0.45f;

        [SerializeField, Min(1)]
        int m_MaxDetections = 10;

        [SerializeField, Min(1)]
        int m_LayersPerFrame = 16;

        [SerializeField, Min(0f)]
        float m_MinimumInferenceInterval = 0.05f;

        [Header("Diagnostics")]
        [SerializeField]
        bool m_VerboseLogging;

        readonly List<YoloDetection> m_Detections = new List<YoloDetection>(16);
        readonly List<ObjectRecognitionResult> m_PublicResults = new List<ObjectRecognitionResult>(16);
        readonly Yolo11Postprocessor m_Postprocessor = new Yolo11Postprocessor();

        Model m_Model;
        Worker m_Worker;
        Tensor<float> m_InputTensor;
        Tensor<float> m_PendingOutput;
        IEnumerator m_Schedule;
        RenderTexture m_VisibleCameraTexture;
        RenderTexture m_ModelTexture;
        string[] m_Labels = Array.Empty<string>();
        LetterboxTransform m_InferenceLetterbox;
        PipelineState m_State;
        BackendType m_ActiveBackend;
        float m_NextInferenceTime;
        float m_LastMissingFrameLogTime = float.NegativeInfinity;
        int m_InferenceCount;
        bool m_IsRunning;

        public event Action<ObjectRecognitionResult> ObjectRecognized;

        public event Action<IReadOnlyList<ObjectRecognitionResult>> ObjectRecognitionsUpdated;

        public event Action<RecognitionServiceStatus> StatusChanged;

        public bool IsRunning => m_IsRunning;

        public BackendType ActiveBackend => m_ActiveBackend;

        public int CompletedInferenceCount => m_InferenceCount;

        void OnValidate()
        {
            m_ConfidenceThreshold = Mathf.Clamp(m_ConfidenceThreshold, 0.05f, 0.95f);
            m_IouThreshold = Mathf.Clamp(m_IouThreshold, 0.05f, 0.95f);
            m_MaxDetections = Mathf.Max(1, m_MaxDetections);
            m_LayersPerFrame = Mathf.Max(1, m_LayersPerFrame);
            m_MinimumInferenceInterval = Mathf.Max(0f, m_MinimumInferenceInterval);
        }

        void Update()
        {
            if (m_State == PipelineState.Scheduling)
                AdvanceSchedule();
            else if (m_State == PipelineState.ReadingOutput)
                TryReadOutput();
        }

        void OnDisable()
        {
            StopRecognition();
        }

        void OnDestroy()
        {
            DisposeRuntime();
        }

        public void Configure(
            ARCameraManager cameraManager,
            ARCameraBackground cameraBackground,
            ModelAsset modelAsset,
            TextAsset cocoLabels)
        {
            if (m_IsRunning)
                throw new InvalidOperationException("The detector cannot be reconfigured while it is running.");

            m_CameraManager = cameraManager;
            m_CameraBackground = cameraBackground;
            m_ModelAsset = modelAsset;
            m_CocoLabels = cocoLabels;
        }

        public void StartRecognition()
        {
            if (m_IsRunning)
                return;

            PublishStatus(RecognitionServiceState.Initializing, "Loading the real-time object detector...");
            try
            {
                ResolveSceneReferences();
                InitializeRuntime();
                m_IsRunning = true;
                m_State = PipelineState.Idle;
                m_InferenceCount = 0;
                m_NextInferenceTime = 0f;
                m_CameraManager.frameReceived += OnCameraFrameReceived;
                PublishStatus(RecognitionServiceState.Running, "Scanning for COCO objects...");
                Log(
                    $"Started. backend={m_ActiveBackend}, input=[1,3,320,320], " +
                    $"output=[1,84,2100], confidence={m_ConfidenceThreshold:0.00}, " +
                    $"IoU={m_IouThreshold:0.00}, layersPerFrame={m_LayersPerFrame}.");
            }
            catch (Exception exception)
            {
                Fail("initialization", exception);
            }
        }

        public void StopRecognition()
        {
            if (m_CameraManager != null)
                m_CameraManager.frameReceived -= OnCameraFrameReceived;

            var wasRunning = m_IsRunning;
            m_IsRunning = false;
            m_State = PipelineState.Idle;
            m_Schedule = null;
            m_PendingOutput = null;
            ClearDetections();
            DisposeRuntime();

            if (wasRunning)
                PublishStatus(RecognitionServiceState.Stopped, "Recognition paused.");
        }

        void ResolveSceneReferences()
        {
            if (m_CameraManager == null)
                m_CameraManager = FindAnyObjectByType<ARCameraManager>(FindObjectsInactive.Include);

            if (m_CameraBackground == null && m_CameraManager != null)
                m_CameraBackground = m_CameraManager.GetComponent<ARCameraBackground>();

            if (m_CameraManager == null)
                throw new InvalidOperationException("No ARCameraManager exists in the scene.");

            if (m_CameraBackground == null)
                throw new InvalidOperationException("The AR Camera is missing ARCameraBackground.");

            if (m_ModelAsset == null)
                throw new InvalidOperationException("The verified YOLO11n ModelAsset is not assigned.");
        }

        void InitializeRuntime()
        {
            DisposeRuntime();
            m_Labels = Yolo11Contract.ParseAndValidateLabels(m_CocoLabels);
            m_Model = ModelLoader.Load(m_ModelAsset);
            ValidateModelInput(m_Model);

            try
            {
                m_Worker = new Worker(m_Model, m_PreferredBackend);
                m_ActiveBackend = m_PreferredBackend;
            }
            catch (Exception preferredBackendException)
            {
                if (m_PreferredBackend == BackendType.CPU)
                    throw;

                Debug.LogWarning(
                    $"[ObjectDetection/Worker] {m_PreferredBackend} could not be created " +
                    $"({preferredBackendException.Message}). Falling back to CPU.");
                m_Worker = new Worker(m_Model, BackendType.CPU);
                m_ActiveBackend = BackendType.CPU;
            }

            m_InputTensor = new Tensor<float>(
                new TensorShape(
                    1,
                    Yolo11Contract.InputChannels,
                    Yolo11Contract.InputHeight,
                    Yolo11Contract.InputWidth));
        }

        static void ValidateModelInput(Model model)
        {
            if (model == null)
                throw new InvalidOperationException("Sentis returned a null model.");

            if (model.inputs.Count != 1)
                throw new InvalidOperationException($"YOLO11n must have one input; the imported model has {model.inputs.Count}.");

            var input = model.inputs[0];
            if (input.dataType != DataType.Float)
                throw new InvalidOperationException($"YOLO11n input must be float32; imported type is {input.dataType}.");

            if (!input.shape.IsStatic())
                throw new InvalidOperationException($"YOLO11n input must be static; imported shape is {input.shape}.");

            var shape = input.shape.ToTensorShape();
            var valid =
                shape.rank == 4 &&
                shape[0] == 1 &&
                shape[1] == Yolo11Contract.InputChannels &&
                shape[2] == Yolo11Contract.InputHeight &&
                shape[3] == Yolo11Contract.InputWidth;
            if (!valid)
            {
                throw new InvalidOperationException(
                    $"YOLO11n input contract mismatch. Imported '{input.name}' is {shape}; expected (1,3,320,320).");
            }

            if (model.outputs.Count != 1)
                throw new InvalidOperationException($"YOLO11n must have one output; the imported model has {model.outputs.Count}.");
        }

        void OnCameraFrameReceived(ARCameraFrameEventArgs _)
        {
            if (!m_IsRunning || m_State != PipelineState.Idle || Time.unscaledTime < m_NextInferenceTime)
                return;

            try
            {
                if (!TryPrepareVisibleCameraFrame(out var unavailableReason))
                {
                    PublishStatusThrottled(
                        RecognitionServiceState.AwaitingCamera,
                        unavailableReason);
                    return;
                }

                var transform = new TextureTransform()
                    .SetTensorLayout(TensorLayout.NCHW)
                    .SetCoordOrigin(CoordOrigin.TopLeft)
                    .SetChannelSwizzle(ChannelSwizzle.RGBA);
                TextureConverter.ToTensor(m_ModelTexture, m_InputTensor, transform);
                m_Schedule = m_Worker.ScheduleIterable(m_InputTensor);
                m_State = PipelineState.Scheduling;
                Log($"Frame prepared ({m_InferenceLetterbox}); inference scheduling started.");
            }
            catch (Exception exception)
            {
                Fail("camera preprocessing", exception);
            }
        }

        bool TryPrepareVisibleCameraFrame(out string unavailableReason)
        {
            unavailableReason = string.Empty;
            if (!m_CameraManager.enabled || !m_CameraBackground.enabled)
            {
                unavailableReason = "Waiting for the AR camera to start...";
                return false;
            }

            var backgroundMaterial = m_CameraBackground.material;
            if (backgroundMaterial == null)
            {
                unavailableReason = "Waiting for the AR camera background...";
                return false;
            }

            if (!backgroundMaterial.HasProperty(CameraTextureProperty))
            {
                throw new InvalidOperationException(
                    $"AR camera material '{backgroundMaterial.name}' has no {CameraTextureProperty} texture.");
            }

            var cameraTexture = backgroundMaterial.GetTexture(CameraTextureProperty);
            if (cameraTexture == null)
            {
                unavailableReason = "Waiting for camera pixels...";
                return false;
            }

            var screenWidth = Mathf.Max(1, Screen.width);
            var screenHeight = Mathf.Max(1, Screen.height);
            var letterbox = LetterboxTransform.Create(
                screenWidth,
                screenHeight,
                Yolo11Contract.InputWidth,
                Yolo11Contract.InputHeight);
            EnsureRenderTextures(letterbox);

            // The AR background material applies AR Foundation's display transform. The result is
            // exactly the orientation and crop visible behind the UI, so detections are already in
            // display space and need only the deterministic letterbox inverse.
            Graphics.Blit(cameraTexture, m_VisibleCameraTexture, backgroundMaterial);

            var previous = RenderTexture.active;
            RenderTexture.active = m_ModelTexture;
            var fillSrgb = Yolo11Contract.LetterboxFill / 255f;
            var fillLinear = Mathf.GammaToLinearSpace(fillSrgb);
            GL.Clear(false, true, new Color(fillLinear, fillLinear, fillLinear, 1f));
            RenderTexture.active = previous;

            if (SystemInfo.copyTextureSupport == CopyTextureSupport.None)
                throw new NotSupportedException("This device does not support the GPU texture copy required for letterboxing.");

            Graphics.CopyTexture(
                m_VisibleCameraTexture,
                0,
                0,
                0,
                0,
                letterbox.ContentWidth,
                letterbox.ContentHeight,
                m_ModelTexture,
                0,
                0,
                letterbox.PadX,
                letterbox.PadY);

            m_InferenceLetterbox = letterbox;
            return true;
        }

        void EnsureRenderTextures(LetterboxTransform letterbox)
        {
            if (m_ModelTexture == null)
            {
                m_ModelTexture = CreateRenderTexture(
                    Yolo11Contract.InputWidth,
                    Yolo11Contract.InputHeight,
                    "YOLO11 Model Input");
            }

            if (m_VisibleCameraTexture != null &&
                m_VisibleCameraTexture.width == letterbox.ContentWidth &&
                m_VisibleCameraTexture.height == letterbox.ContentHeight)
            {
                return;
            }

            ReleaseRenderTexture(ref m_VisibleCameraTexture);
            m_VisibleCameraTexture = CreateRenderTexture(
                letterbox.ContentWidth,
                letterbox.ContentHeight,
                "AR Camera Detection Frame");
        }

        static RenderTexture CreateRenderTexture(int width, int height, string textureName)
        {
            var descriptor = new RenderTextureDescriptor(width, height)
            {
                depthBufferBits = 0,
                msaaSamples = 1,
                mipCount = 1,
                useMipMap = false,
                autoGenerateMips = false,
                graphicsFormat = GraphicsFormat.R8G8B8A8_SRGB
            };

            var texture = new RenderTexture(descriptor)
            {
                name = textureName,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            if (!texture.Create())
                throw new InvalidOperationException($"Could not allocate {textureName} ({width}x{height}).");

            return texture;
        }

        void AdvanceSchedule()
        {
            try
            {
                for (var layer = 0; layer < m_LayersPerFrame; layer++)
                {
                    if (m_Schedule != null && m_Schedule.MoveNext())
                        continue;

                    m_Schedule = null;
                    var output = m_Worker.PeekOutput();
                    if (!(output is Tensor<float> floatOutput))
                        throw new InvalidOperationException("YOLO11n output is not a float tensor.");

                    ValidateOutputShape(floatOutput.shape);
                    m_PendingOutput = floatOutput;
                    m_PendingOutput.ReadbackRequest();
                    m_State = PipelineState.ReadingOutput;
                    return;
                }
            }
            catch (Exception exception)
            {
                Fail("inference scheduling", exception);
            }
        }

        void TryReadOutput()
        {
            if (m_PendingOutput == null || !m_PendingOutput.IsReadbackRequestDone())
                return;

            try
            {
                using var cpuOutput = m_PendingOutput.ReadbackAndClone();
                ValidateOutputShape(cpuOutput.shape);
                m_Postprocessor.Decode(
                    cpuOutput.AsReadOnlySpan(),
                    cpuOutput.shape[1],
                    cpuOutput.shape[2],
                    m_ConfidenceThreshold,
                    m_IouThreshold,
                    m_MaxDetections,
                    m_Detections);
                m_InferenceCount++;
                PublishDetections();
                Log(
                    $"Inference #{m_InferenceCount}: candidates={m_Postprocessor.LastCandidateCount}, " +
                    $"afterNms={m_Detections.Count}.");
            }
            catch (Exception exception)
            {
                Fail("output decoding", exception);
                return;
            }
            finally
            {
                m_PendingOutput = null;
            }

            if (m_IsRunning)
            {
                m_State = PipelineState.Idle;
                m_NextInferenceTime = Time.unscaledTime + m_MinimumInferenceInterval;
            }
        }

        static void ValidateOutputShape(TensorShape shape)
        {
            var valid =
                shape.rank == 3 &&
                shape[0] == 1 &&
                shape[1] == Yolo11Contract.OutputChannels &&
                shape[2] == Yolo11Contract.PredictionCount;
            if (!valid)
            {
                throw new InvalidOperationException(
                    $"YOLO11n output contract mismatch. Runtime output is {shape}; expected (1,84,2100).");
            }
        }

        void PublishDetections()
        {
            m_PublicResults.Clear();
            for (var index = 0; index < m_Detections.Count; index++)
            {
                var detection = m_Detections[index];
                if (detection.ClassId < 0 || detection.ClassId >= m_Labels.Length)
                    continue;

                var displayBox = m_InferenceLetterbox.ModelToDisplay(detection);
                if (!displayBox.IsValid)
                    continue;

                m_PublicResults.Add(new ObjectRecognitionResult(
                    m_Labels[detection.ClassId],
                    detection.Confidence,
                    $"coco:{detection.ClassId}",
                    displayBox));
            }

            ObjectRecognitionsUpdated?.Invoke(m_PublicResults);
            if (m_PublicResults.Count > 0)
                ObjectRecognized?.Invoke(m_PublicResults[0]);

            PublishStatus(
                RecognitionServiceState.Running,
                m_PublicResults.Count == 0
                    ? "Scanning for COCO objects..."
                    : $"Detected {m_PublicResults.Count} object{(m_PublicResults.Count == 1 ? string.Empty : "s")}.");
        }

        void ClearDetections()
        {
            m_Detections.Clear();
            m_PublicResults.Clear();
            ObjectRecognitionsUpdated?.Invoke(m_PublicResults);
        }

        void PublishStatus(RecognitionServiceState state, string message)
        {
            StatusChanged?.Invoke(new RecognitionServiceStatus(state, message));
        }

        void PublishStatusThrottled(RecognitionServiceState state, string message)
        {
            if (Time.unscaledTime - m_LastMissingFrameLogTime < MissingFrameLogInterval)
                return;

            m_LastMissingFrameLogTime = Time.unscaledTime;
            PublishStatus(state, message);
            Log(message);
        }

        void Fail(string stage, Exception exception)
        {
            Debug.LogError($"[ObjectDetection/{stage}] {exception.GetType().Name}: {exception.Message}");
            Debug.LogException(exception);

            if (m_CameraManager != null)
                m_CameraManager.frameReceived -= OnCameraFrameReceived;

            m_IsRunning = false;
            m_State = PipelineState.Faulted;
            m_Schedule = null;
            m_PendingOutput = null;
            ClearDetections();
            DisposeRuntime();
            PublishStatus(
                RecognitionServiceState.Faulted,
                $"Object detection stopped during {stage}. See the diagnostic log.");
        }

        void DisposeRuntime()
        {
            m_InputTensor?.Dispose();
            m_InputTensor = null;
            m_Worker?.Dispose();
            m_Worker = null;
            m_Model = null;
            m_Schedule = null;
            m_PendingOutput = null;
            ReleaseRenderTexture(ref m_VisibleCameraTexture);
            ReleaseRenderTexture(ref m_ModelTexture);
        }

        static void ReleaseRenderTexture(ref RenderTexture texture)
        {
            if (texture == null)
                return;

            texture.Release();
            if (UnityEngine.Application.isPlaying)
                UnityEngine.Object.Destroy(texture);
            else
                UnityEngine.Object.DestroyImmediate(texture);
            texture = null;
        }

        void Log(string message)
        {
            if (m_VerboseLogging)
                Debug.Log($"[ObjectDetection] {message}");
        }
    }
}
