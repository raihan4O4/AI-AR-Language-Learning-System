using System;
using System.IO;
using AIAR.LanguageLearning.Domain;
using AIAR.LanguageLearning.Unity.Bootstrap;
using AIAR.LanguageLearning.Unity.Data;
using AIAR.LanguageLearning.Unity.ObjectDetection;
using AIAR.LanguageLearning.Unity.Presentation;
using AIAR.LanguageLearning.Unity.Speech;
using TMPro;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

namespace AIAR.LanguageLearning.Editor.SceneSetup
{
    [InitializeOnLoad]
    public static class LanguageLearningSceneSetup
    {
        // Keep scene generation deterministic so stale detector components cannot survive a rebuild.
        const string MainScenePath = "Assets/Scenes/Main.unity";
        const string CatalogPath = "Assets/_Project/LanguageLearning/Content/Resources/Vocabulary/vocabulary_catalog.json";
        const string ModelDirectoryPath = "Assets/_Project/LanguageLearning/ObjectDetection/Models";
        const string Yolo11ModelPath = ModelDirectoryPath + "/yolo11n_320.onnx";
        const string CocoLabelsPath = ModelDirectoryPath + "/coco80.txt";
        const string PendingSetupPath = "Assets/_Project/LanguageLearning/Editor/SceneSetup/PENDING_MAIN_SCENE_SETUP.txt";
        const string SetupReportPath = "Logs/language-learning-scene-setup.log";
        const string FontAssetDirectory = "Assets/_Project/LanguageLearning/Content/Resources/Fonts/TMP";
        const string GoogleSansFontPath = "Assets/_Project/LanguageLearning/Content/Resources/Fonts/GoogleSans-Regular.ttf";
        const string NotoSansFontPath = "Assets/_Project/LanguageLearning/Content/Resources/Fonts/NotoSans-Regular.ttf";
        const string NotoSansDevanagariFontPath = "Assets/_Project/LanguageLearning/Content/Resources/Fonts/NotoSansDevanagari-Regular.ttf";
        const string NotoSansMalayalamFontPath = "Assets/_Project/LanguageLearning/Content/Resources/Fonts/NotoSansMalayalam-Regular.ttf";
        const string GoogleSansTmpPath = FontAssetDirectory + "/GoogleSans-Regular SDF.asset";
        const string NotoSansTmpPath = FontAssetDirectory + "/NotoSans-Regular SDF.asset";
        const string NotoSansDevanagariTmpPath = FontAssetDirectory + "/NotoSansDevanagari-Regular SDF.asset";
        const string NotoSansMalayalamTmpPath = FontAssetDirectory + "/NotoSansMalayalam-Regular SDF.asset";

        static LanguageLearningSceneSetup()
        {
            EnforcePortraitOrientation();
            EditorApplication.delayCall += RunPendingSetupIfRequested;
            EditorApplication.delayCall += EnsureFontAssetsReady;
        }

        [MenuItem("AI-AR Language Learning/Setup Main Scene")]
        public static void SetupMainSceneMenu()
        {
            SetupMainScene();
        }

        [MenuItem("AI-AR Language Learning/Configure Rebuilt YOLO11 Detector")]
        public static void ConfigureRebuiltYolo11Detector()
        {
            SetupMainScene();
        }

        public static void RunFromCommandLine()
        {
            SetupMainScene();
        }

        [MenuItem("AI-AR Language Learning/Build Android Debug APK")]
        public static void BuildAndroidDebugApk()
        {
            SetupMainScene();

            var buildDirectory = Path.Combine("Builds", "Android");
            Directory.CreateDirectory(buildDirectory);

            var buildPath = Path.Combine(buildDirectory, "AIARLanguageLearning-debug.apk");
            var buildOptions = new BuildPlayerOptions
            {
                scenes = new[] { MainScenePath },
                locationPathName = buildPath,
                target = BuildTarget.Android,
                options = BuildOptions.Development | BuildOptions.AllowDebugging
            };

            var report = BuildPipeline.BuildPlayer(buildOptions);
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"Android APK build failed: {report.summary.result}");

            WriteReport($"Android debug APK build complete: {buildPath}");
        }

        static void RunPendingSetupIfRequested()
        {
            if (!File.Exists(PendingSetupPath))
                return;

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += RunPendingSetupIfRequested;
                return;
            }

            try
            {
                SetupMainScene();
                File.Delete(PendingSetupPath);
                AssetDatabase.Refresh();
            }
            catch (Exception exception)
            {
                WriteReport("Scene setup failed:\n" + exception);
                Debug.LogException(exception);
            }
        }

        static void EnsureFontAssetsReady()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += EnsureFontAssetsReady;
                return;
            }

            if (!File.Exists(GoogleSansFontPath) || HasUsableAtlas(AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(GoogleSansTmpPath)))
                return;

            EnsureMultilingualFontAsset();
        }

        static void SetupMainScene()
        {
            var scene = OpenMainScene();

            ConfigureBuildSettings();
            ConfigureAndroidPlayerSettings();
            var uiFont = EnsureMultilingualFontAsset();
            var sentisModel = AssetDatabase.LoadAssetAtPath<ModelAsset>(Yolo11ModelPath);
            var cocoLabels = AssetDatabase.LoadAssetAtPath<TextAsset>(CocoLabelsPath);

            if (sentisModel == null)
            {
                throw new InvalidOperationException(
                    $"The verified YOLO11n model did not import as a ModelAsset at {Yolo11ModelPath}.");
            }

            if (cocoLabels == null)
                throw new InvalidOperationException($"The COCO label asset is missing at {CocoLabelsPath}.");

            var runtimeRoot = RecreateRoot("LanguageLearning Runtime");
            var repository = runtimeRoot.AddComponent<ResourcesVocabularyRepository>();
            var objectDetector = runtimeRoot.AddComponent<RealtimeCocoObjectDetector>();
            var tts = runtimeRoot.AddComponent<AndroidTextToSpeechService>();
            var compositionRoot = runtimeRoot.AddComponent<LanguageLearningCompositionRoot>();
            var sessionController = runtimeRoot.AddComponent<LearningSessionController>();
            var mvpController = runtimeRoot.AddComponent<LearningMvpController>();

            ConfigureRepository(repository);
            ConfigureObjectDetector(objectDetector, sentisModel, cocoLabels);
            ConfigureCompositionRoot(compositionRoot, repository, objectDetector, tts);
            ConfigureSessionController(sessionController, compositionRoot);
            ConfigureMvpController(mvpController, sessionController, compositionRoot);
            ConfigureArScanLifecycle();

            EnsureEventSystem();
            var presenter = CreateLanguageLearningUi(sessionController, uiFont);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            WriteReport(
                "Language learning Main scene setup complete.\n" +
                "- Main scene added to Build Settings.\n" +
                "- Android package/settings prepared for APK builds.\n" +
                "- Runtime composition root, verified YOLO11n detector, TTS adapter, and vocabulary repository added.\n" +
                "- Detector contract: RGB float32 NCHW [1,3,320,320] to [1,84,2100], class-aware NMS.\n" +
                "- AR camera components remain off until the learner opens Scan.\n" +
                "- Screen-space vocabulary overlay and language/speech buttons added.\n" +
                "- Existing AR Session and XR Origin were preserved.");
        }

        static void ConfigureArScanLifecycle()
        {
            var arSession = UnityEngine.Object.FindAnyObjectByType<ARSession>(FindObjectsInactive.Include);
            if (arSession != null)
                arSession.enabled = false;

            var cameraManager = UnityEngine.Object.FindAnyObjectByType<ARCameraManager>(FindObjectsInactive.Include);
            if (cameraManager != null)
                cameraManager.enabled = false;

            var cameraBackground = UnityEngine.Object.FindAnyObjectByType<ARCameraBackground>(FindObjectsInactive.Include);
            if (cameraBackground != null)
                cameraBackground.enabled = false;
        }

        static Scene OpenMainScene()
        {
            if (!File.Exists(MainScenePath))
                throw new FileNotFoundException("Main scene was not found.", MainScenePath);

            var activeScene = EditorSceneManager.GetActiveScene();
            if (activeScene.path == MainScenePath)
                return activeScene;

            if (activeScene.isDirty && !string.IsNullOrWhiteSpace(activeScene.path))
                throw new InvalidOperationException($"Cannot auto-open Main because the active scene has unsaved changes: {activeScene.path}");

            return EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
        }

        static void ConfigureBuildSettings()
        {
            var mainScene = new EditorBuildSettingsScene(MainScenePath, true);
            EditorBuildSettings.scenes = new[] { mainScene };
        }

        static void ConfigureAndroidPlayerSettings()
        {
            PlayerSettings.productName = "AI AR Language Learning";
            PlayerSettings.bundleVersion = "0.1.0";
            EnforcePortraitOrientation();
            PlayerSettings.Android.resizeableActivity = false;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.raihan.aiarlanguagelearning");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel25;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            EditorUserBuildSettings.buildAppBundle = false;
        }

        public static void EnforcePortraitOrientation()
        {
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
        }

        static TMP_FontAsset EnsureMultilingualFontAsset()
        {
            Directory.CreateDirectory(FontAssetDirectory);

            var latin = EnsureTmpFontAsset(GoogleSansFontPath, GoogleSansTmpPath);
            var latinFallback = EnsureTmpFontAsset(NotoSansFontPath, NotoSansTmpPath);
            var devanagari = EnsureTmpFontAsset(NotoSansDevanagariFontPath, NotoSansDevanagariTmpPath);
            var malayalam = EnsureTmpFontAsset(NotoSansMalayalamFontPath, NotoSansMalayalamTmpPath);

            if (latin != null)
            {
                AddFallback(latin, latinFallback);
                AddFallback(latin, devanagari);
                AddFallback(latin, malayalam);
                EditorUtility.SetDirty(latin);
            }

            AddTmpSettingsFallback(latin);
            AddTmpSettingsFallback(latinFallback);
            AddTmpSettingsFallback(devanagari);
            AddTmpSettingsFallback(malayalam);

            AssetDatabase.SaveAssets();
            return latin;
        }

        static TMP_FontAsset EnsureTmpFontAsset(string fontPath, string assetPath)
        {
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (HasUsableAtlas(existing))
                return existing;

            var font = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
            if (font == null)
            {
                Debug.LogWarning($"Font file not found: {fontPath}");
                return null;
            }

            if (existing != null)
                AssetDatabase.DeleteAsset(assetPath);

            FontEngine.InitializeFontEngine();
            var fontAsset = TMP_FontAsset.CreateFontAsset(
                font,
                90,
                9,
                GlyphRenderMode.SDFAA,
                1024,
                1024,
                AtlasPopulationMode.Dynamic,
                true);
            if (fontAsset == null)
            {
                Debug.LogWarning($"Could not create a TextMeshPro font asset from {fontPath}.");
                return null;
            }

            fontAsset.name = Path.GetFileNameWithoutExtension(assetPath);
            AssetDatabase.CreateAsset(fontAsset, assetPath);

            // CreateFontAsset builds these objects in memory. They must be saved as sub-assets or
            // scene reloads leave m_AtlasTextures/material unassigned and every TMP label fails.
            var atlasTextures = fontAsset.atlasTextures;
            if (atlasTextures != null)
            {
                for (var index = 0; index < atlasTextures.Length; index++)
                {
                    if (atlasTextures[index] != null &&
                        !AssetDatabase.Contains(atlasTextures[index]))
                    {
                        AssetDatabase.AddObjectToAsset(atlasTextures[index], fontAsset);
                    }
                }
            }

            if (fontAsset.material != null && !AssetDatabase.Contains(fontAsset.material))
                AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);

            EditorUtility.SetDirty(fontAsset);
            AssetDatabase.SaveAssets();
            return fontAsset;
        }

        static bool HasUsableAtlas(TMP_FontAsset fontAsset)
        {
            return fontAsset != null &&
                   fontAsset.atlasTextures != null &&
                   fontAsset.atlasTextures.Length > 0 &&
                   fontAsset.atlasTextures[0] != null &&
                   fontAsset.material != null;
        }

        static void AddFallback(TMP_FontAsset fontAsset, TMP_FontAsset fallback)
        {
            if (fontAsset == null || fallback == null || fontAsset == fallback)
                return;

            if (fontAsset.fallbackFontAssetTable == null)
                fontAsset.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset>();

            if (!fontAsset.fallbackFontAssetTable.Contains(fallback))
                fontAsset.fallbackFontAssetTable.Add(fallback);
        }

        static void AddTmpSettingsFallback(TMP_FontAsset fontAsset)
        {
            if (fontAsset == null || TMP_Settings.instance == null)
                return;

            var settings = TMP_Settings.instance;
            if (TMP_Settings.fallbackFontAssets == null)
                TMP_Settings.fallbackFontAssets = new System.Collections.Generic.List<TMP_FontAsset>();

            if (!TMP_Settings.fallbackFontAssets.Contains(fontAsset))
            {
                TMP_Settings.fallbackFontAssets.Add(fontAsset);
                EditorUtility.SetDirty(settings);
            }
        }

        static GameObject RecreateRoot(string name)
        {
            var existing = GameObject.Find(name);
            if (existing != null)
                UnityEngine.Object.DestroyImmediate(existing);

            var root = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(root, $"Create {name}");
            return root;
        }

        static void ConfigureRepository(ResourcesVocabularyRepository repository)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<TextAsset>(CatalogPath);
            var serializedObject = new SerializedObject(repository);
            serializedObject.FindProperty("m_CatalogAsset").objectReferenceValue = catalog;
            serializedObject.FindProperty("m_ResourcesPath").stringValue = "Vocabulary/vocabulary_catalog";
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        static void ConfigureObjectDetector(
            RealtimeCocoObjectDetector objectDetector,
            ModelAsset modelAsset,
            TextAsset labelAsset)
        {
            var serializedObject = new SerializedObject(objectDetector);
            serializedObject.FindProperty("m_CameraManager").objectReferenceValue = UnityEngine.Object.FindAnyObjectByType<ARCameraManager>();
            serializedObject.FindProperty("m_CameraBackground").objectReferenceValue = UnityEngine.Object.FindAnyObjectByType<ARCameraBackground>();
            serializedObject.FindProperty("m_ModelAsset").objectReferenceValue = modelAsset;
            serializedObject.FindProperty("m_CocoLabels").objectReferenceValue = labelAsset;
            serializedObject.FindProperty("m_PreferredBackend").intValue = (int)BackendType.GPUCompute;
            serializedObject.FindProperty("m_ConfidenceThreshold").floatValue = 0.30f;
            serializedObject.FindProperty("m_IouThreshold").floatValue = 0.45f;
            serializedObject.FindProperty("m_MaxDetections").intValue = 10;
            serializedObject.FindProperty("m_LayersPerFrame").intValue = 16;
            serializedObject.FindProperty("m_MinimumInferenceInterval").floatValue = 0.05f;
            serializedObject.FindProperty("m_VerboseLogging").boolValue = false;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        static void ConfigureCompositionRoot(
            LanguageLearningCompositionRoot compositionRoot,
            ResourcesVocabularyRepository repository,
            MonoBehaviour recognitionService,
            AndroidTextToSpeechService tts)
        {
            var serializedObject = new SerializedObject(compositionRoot);
            serializedObject.FindProperty("m_VocabularyRepository").objectReferenceValue = repository;
            serializedObject.FindProperty("m_ObjectRecognitionService").objectReferenceValue = recognitionService;
            serializedObject.FindProperty("m_TextToSpeechService").objectReferenceValue = tts;
            serializedObject.FindProperty("m_PlayerPrefsProgressKey").stringValue = "aiar.language-learning.progress.v1";
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        static void ConfigureSessionController(
            LearningSessionController sessionController,
            LanguageLearningCompositionRoot compositionRoot)
        {
            var serializedObject = new SerializedObject(sessionController);
            serializedObject.FindProperty("m_CompositionRoot").objectReferenceValue = compositionRoot;
            serializedObject.FindProperty("m_LearningLanguage").enumValueIndex = (int)LanguageCode.Hindi;
            serializedObject.FindProperty("m_MinimumRecognitionConfidence").floatValue = 0.35f;
            serializedObject.FindProperty("m_AutoCreateBoundingBoxOverlay").boolValue = true;
            serializedObject.FindProperty("m_DebugRecognitionLogging").boolValue = false;
            serializedObject.FindProperty("m_MinimumStableFrames").intValue = 1;
            serializedObject.FindProperty("m_DetectionHoldSeconds").floatValue = 2f;
            serializedObject.FindProperty("m_ViewRecordingCooldownSeconds").floatValue = 12f;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        static void ConfigureMvpController(
            LearningMvpController mvpController,
            LearningSessionController sessionController,
            LanguageLearningCompositionRoot compositionRoot)
        {
            var serializedObject = new SerializedObject(mvpController);
            serializedObject.FindProperty("m_SessionController").objectReferenceValue = sessionController;
            serializedObject.FindProperty("m_CompositionRoot").objectReferenceValue = compositionRoot;
            serializedObject.FindProperty("m_QuizLength").intValue = 5;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        static void EnsureEventSystem()
        {
            var eventSystem = UnityEngine.Object.FindAnyObjectByType<EventSystem>();
            if (eventSystem != null)
            {
                if (eventSystem.GetComponent<InputSystemUIInputModule>() == null)
                    eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();

                return;
            }

            var eventSystemObject = new GameObject("EventSystem");
            eventSystemObject.AddComponent<EventSystem>();
            eventSystemObject.AddComponent<InputSystemUIInputModule>();
        }

        static ARVocabularyOverlayPresenter CreateLanguageLearningUi(
            LearningSessionController sessionController,
            TMP_FontAsset fontAsset)
        {
            var existingCanvasRoot = GameObject.Find("LanguageLearning UI");
            if (existingCanvasRoot != null)
                UnityEngine.Object.DestroyImmediate(existingCanvasRoot);

            var canvasRoot = new GameObject("LanguageLearning UI", typeof(RectTransform));
            Undo.RegisterCreatedObjectUndo(canvasRoot, "Create LanguageLearning UI");
            var canvasRect = canvasRoot.GetComponent<RectTransform>();
            canvasRect.localScale = Vector3.one;
            canvasRect.anchorMin = Vector2.zero;
            canvasRect.anchorMax = Vector2.one;
            canvasRect.offsetMin = Vector2.zero;
            canvasRect.offsetMax = Vector2.zero;
            var canvas = canvasRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = canvasRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1440f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            canvasRoot.AddComponent<GraphicRaycaster>();

            CreateRecognitionBoundingBoxUi(canvasRoot.transform, sessionController, fontAsset);

            var panel = CreatePanel(canvasRoot.transform);
            var presenter = panel.AddComponent<ARVocabularyOverlayPresenter>();

            var languageText = CreateText(panel.transform, "Language", "Hindi", 28f, FontStyles.Bold, TextAlignmentOptions.Left, fontAsset);
            var sourceText = CreateText(panel.transform, "Source Word", "book", 36f, FontStyles.Normal, TextAlignmentOptions.Left, fontAsset);
            var translatedText = CreateText(panel.transform, "Translated Word", "किताब", 68f, FontStyles.Bold, TextAlignmentOptions.Left, fontAsset);
            var phoneticText = CreateText(panel.transform, "Phonetic Hint", "kitaab", 28f, FontStyles.Italic, TextAlignmentOptions.Left, fontAsset);
            translatedText.enableAutoSizing = true;
            translatedText.fontSizeMin = 30f;
            translatedText.fontSizeMax = 54f;
            translatedText.overflowMode = TextOverflowModes.Overflow;
            var translatedLayout = translatedText.GetComponent<LayoutElement>();
            translatedLayout.minHeight = 68f;
            translatedLayout.preferredHeight = 74f;

            var presenterObject = new SerializedObject(presenter);
            presenterObject.FindProperty("m_SessionController").objectReferenceValue = sessionController;
            presenterObject.FindProperty("m_TranslatedWordText").objectReferenceValue = translatedText;
            presenterObject.FindProperty("m_SourceWordText").objectReferenceValue = sourceText;
            presenterObject.FindProperty("m_PhoneticHintText").objectReferenceValue = phoneticText;
            presenterObject.FindProperty("m_LanguageText").objectReferenceValue = languageText;
            presenterObject.FindProperty("m_CanvasGroup").objectReferenceValue = panel.GetComponent<CanvasGroup>();
            presenterObject.ApplyModifiedPropertiesWithoutUndo();

            return presenter;
        }

        static void CreateRecognitionBoundingBoxUi(
            Transform parent,
            LearningSessionController sessionController,
            TMP_FontAsset fontAsset)
        {
            var boxObject = new GameObject(
                "Recognition Bounding Box",
                typeof(RectTransform),
                typeof(CanvasGroup),
                typeof(Image),
                typeof(PooledDetectionOverlay));
            boxObject.transform.SetParent(parent, false);

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
            if (fontAsset != null)
                labelText.font = fontAsset;

            var presenter = boxObject.GetComponent<PooledDetectionOverlay>();
            var serializedObject = new SerializedObject(presenter);
            serializedObject.FindProperty("m_SessionController").objectReferenceValue = sessionController;
            serializedObject.FindProperty("m_BoxRectTransform").objectReferenceValue = rectTransform;
            serializedObject.FindProperty("m_LabelText").objectReferenceValue = labelText;
            serializedObject.FindProperty("m_CanvasGroup").objectReferenceValue = boxObject.GetComponent<CanvasGroup>();
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        static GameObject CreatePanel(Transform parent)
        {
            var panel = new GameObject("Vocabulary Panel", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
            panel.transform.SetParent(parent, false);

            var rectTransform = panel.GetComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0f, 1f);
            rectTransform.anchorMax = new Vector2(1f, 1f);
            rectTransform.pivot = new Vector2(0.5f, 1f);
            rectTransform.anchoredPosition = new Vector2(0f, -28f);
            rectTransform.sizeDelta = new Vector2(-64f, 284f);

            var image = panel.GetComponent<Image>();
            image.color = LearningUiTheme.Glass;

            var layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(30, 112, 24, 22);
            layout.spacing = 4f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            return panel;
        }

        static TextMeshProUGUI CreateText(
            Transform parent,
            string name,
            string text,
            float fontSize,
            FontStyles style,
            TextAlignmentOptions alignment,
            TMP_FontAsset fontAsset)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            textObject.transform.SetParent(parent, false);

            var textComponent = textObject.GetComponent<TextMeshProUGUI>();
            textComponent.text = text;
            textComponent.fontSize = fontSize;
            textComponent.fontStyle = style;
            textComponent.alignment = alignment;
            textComponent.color = Color.white;
            textComponent.textWrappingMode = TextWrappingModes.NoWrap;
            textComponent.overflowMode = TextOverflowModes.Truncate;
            if (fontAsset != null)
                textComponent.font = fontAsset;

            var layout = textObject.GetComponent<LayoutElement>();
            layout.minHeight = Mathf.Max(42f, fontSize + 16f);
            layout.preferredHeight = Mathf.Max(42f, fontSize + 18f);

            return textComponent;
        }

        static void WriteReport(string report)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SetupReportPath) ?? ".");
            File.WriteAllText(SetupReportPath, report);
            Debug.Log(report);
        }
    }

    public sealed class LanguageLearningPortraitBuildProcessor : IPreprocessBuildWithReport
    {
        public int callbackOrder => -1000;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.Android)
                return;

            LanguageLearningSceneSetup.EnforcePortraitOrientation();
            PlayerSettings.Android.resizeableActivity = false;
        }
    }
}
