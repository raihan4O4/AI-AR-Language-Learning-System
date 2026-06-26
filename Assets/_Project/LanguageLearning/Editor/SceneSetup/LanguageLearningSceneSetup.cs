using System;
using System.IO;
using AIAR.LanguageLearning.Domain;
using AIAR.LanguageLearning.Unity.Bootstrap;
using AIAR.LanguageLearning.Unity.Data;
using AIAR.LanguageLearning.Unity.Presentation;
using AIAR.LanguageLearning.Unity.Recognition;
using AIAR.LanguageLearning.Unity.Speech;
using TMPro;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace AIAR.LanguageLearning.Editor.SceneSetup
{
    [InitializeOnLoad]
    public static class LanguageLearningSceneSetup
    {
        const string MainScenePath = "Assets/Scenes/Main.unity";
        const string CatalogPath = "Assets/_Project/LanguageLearning/Content/Resources/Vocabulary/vocabulary_catalog.json";
        const string PendingSetupPath = "Assets/_Project/LanguageLearning/Editor/SceneSetup/PENDING_MAIN_SCENE_SETUP.txt";
        const string SetupReportPath = "Logs/language-learning-scene-setup.log";
        const string FontAssetDirectory = "Assets/_Project/LanguageLearning/Content/Resources/Fonts/TMP";
        const string NotoSansFontPath = "Assets/_Project/LanguageLearning/Content/Resources/Fonts/NotoSans-Regular.ttf";
        const string NotoSansDevanagariFontPath = "Assets/_Project/LanguageLearning/Content/Resources/Fonts/NotoSansDevanagari-Regular.ttf";
        const string NotoSansMalayalamFontPath = "Assets/_Project/LanguageLearning/Content/Resources/Fonts/NotoSansMalayalam-Regular.ttf";
        const string NotoSansTmpPath = FontAssetDirectory + "/NotoSans-Regular SDF.asset";
        const string NotoSansDevanagariTmpPath = FontAssetDirectory + "/NotoSansDevanagari-Regular SDF.asset";
        const string NotoSansMalayalamTmpPath = FontAssetDirectory + "/NotoSansMalayalam-Regular SDF.asset";

        static readonly string[] MockLabels =
        {
            "book",
            "bottle",
            "chair",
            "table",
            "laptop",
            "phone",
            "cup",
            "pen",
            "bag",
            "apple",
            "banana",
            "flower"
        };

        static LanguageLearningSceneSetup()
        {
            EditorApplication.delayCall += RunPendingSetupIfRequested;
        }

        [MenuItem("AI-AR Language Learning/Setup Main Scene")]
        public static void SetupMainSceneMenu()
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

        static void SetupMainScene()
        {
            var scene = OpenMainScene();

            ConfigureBuildSettings();
            ConfigureAndroidPlayerSettings();
            var uiFont = EnsureMultilingualFontAsset();

            var runtimeRoot = RecreateRoot("LanguageLearning Runtime");
            var repository = runtimeRoot.AddComponent<ResourcesVocabularyRepository>();
            var mockRecognition = runtimeRoot.AddComponent<MockObjectRecognitionService>();
            var tts = runtimeRoot.AddComponent<AndroidTextToSpeechService>();
            var compositionRoot = runtimeRoot.AddComponent<LanguageLearningCompositionRoot>();
            var sessionController = runtimeRoot.AddComponent<LearningSessionController>();

            ConfigureRepository(repository);
            ConfigureMockRecognition(mockRecognition);
            ConfigureCompositionRoot(compositionRoot, repository, mockRecognition, tts);
            ConfigureSessionController(sessionController, compositionRoot);

            EnsureEventSystem();
            var presenter = CreateLanguageLearningUi(sessionController, uiFont);
            UnityEventTools.AddPersistentListener(sessionController.onVocabularyDetected, presenter.ShowVocabulary);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            WriteReport(
                "Language learning Main scene setup complete.\n" +
                "- Main scene added to Build Settings.\n" +
                "- Android package/settings prepared for APK builds.\n" +
                "- Runtime composition root, mock recognizer, TTS adapter, and vocabulary repository added.\n" +
                "- Screen-space vocabulary overlay and language/speech buttons added.\n" +
                "- Existing AR Session and XR Origin were preserved.");
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
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.raihan.aiarlanguagelearning");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel25;
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            EditorUserBuildSettings.buildAppBundle = false;
        }

        static TMP_FontAsset EnsureMultilingualFontAsset()
        {
            Directory.CreateDirectory(FontAssetDirectory);

            var latin = EnsureTmpFontAsset(NotoSansFontPath, NotoSansTmpPath);
            var devanagari = EnsureTmpFontAsset(NotoSansDevanagariFontPath, NotoSansDevanagariTmpPath);
            var malayalam = EnsureTmpFontAsset(NotoSansMalayalamFontPath, NotoSansMalayalamTmpPath);

            if (latin != null)
            {
                AddFallback(latin, devanagari);
                AddFallback(latin, malayalam);
                EditorUtility.SetDirty(latin);
            }

            AddTmpSettingsFallback(latin);
            AddTmpSettingsFallback(devanagari);
            AddTmpSettingsFallback(malayalam);

            AssetDatabase.SaveAssets();
            return latin;
        }

        static TMP_FontAsset EnsureTmpFontAsset(string fontPath, string assetPath)
        {
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
            if (existing != null)
                return existing;

            var font = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
            if (font == null)
            {
                Debug.LogWarning($"Font file not found: {fontPath}");
                return null;
            }

            var fontAsset = TMP_FontAsset.CreateFontAsset(
                font,
                90,
                9,
                GlyphRenderMode.SDFAA,
                1024,
                1024,
                AtlasPopulationMode.Dynamic,
                true);
            fontAsset.name = Path.GetFileNameWithoutExtension(assetPath);
            AssetDatabase.CreateAsset(fontAsset, assetPath);
            return fontAsset;
        }

        static void AddFallback(TMP_FontAsset fontAsset, TMP_FontAsset fallback)
        {
            if (fontAsset == null || fallback == null || fontAsset == fallback)
                return;

            if (!fontAsset.fallbackFontAssetTable.Contains(fallback))
                fontAsset.fallbackFontAssetTable.Add(fallback);
        }

        static void AddTmpSettingsFallback(TMP_FontAsset fontAsset)
        {
            if (fontAsset == null || TMP_Settings.instance == null)
                return;

            var settings = TMP_Settings.instance;
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

        static void ConfigureMockRecognition(MockObjectRecognitionService mockRecognition)
        {
            var serializedObject = new SerializedObject(mockRecognition);
            var labels = serializedObject.FindProperty("m_Labels");
            labels.arraySize = MockLabels.Length;

            for (var index = 0; index < MockLabels.Length; index++)
                labels.GetArrayElementAtIndex(index).stringValue = MockLabels[index];

            serializedObject.FindProperty("m_IntervalSeconds").floatValue = 2.5f;
            serializedObject.FindProperty("m_StartOnEnable").boolValue = false;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        static void ConfigureCompositionRoot(
            LanguageLearningCompositionRoot compositionRoot,
            ResourcesVocabularyRepository repository,
            MockObjectRecognitionService mockRecognition,
            AndroidTextToSpeechService tts)
        {
            var serializedObject = new SerializedObject(compositionRoot);
            serializedObject.FindProperty("m_VocabularyRepository").objectReferenceValue = repository;
            serializedObject.FindProperty("m_ObjectRecognitionService").objectReferenceValue = mockRecognition;
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
            var canvasRoot = RecreateRoot("LanguageLearning UI");
            var canvas = canvasRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            var scaler = canvasRoot.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasRoot.AddComponent<GraphicRaycaster>();

            CreateRecognitionBoundingBoxUi(canvasRoot.transform, sessionController, fontAsset);

            var panel = CreatePanel(canvasRoot.transform);
            var presenter = panel.AddComponent<ARVocabularyOverlayPresenter>();

            var languageText = CreateText(panel.transform, "Language", "Hindi", 28f, FontStyles.Bold, TextAlignmentOptions.Left, fontAsset);
            var sourceText = CreateText(panel.transform, "Source Word", "book", 36f, FontStyles.Normal, TextAlignmentOptions.Left, fontAsset);
            var translatedText = CreateText(panel.transform, "Translated Word", "किताब", 68f, FontStyles.Bold, TextAlignmentOptions.Left, fontAsset);
            var phoneticText = CreateText(panel.transform, "Phonetic Hint", "kitaab", 28f, FontStyles.Italic, TextAlignmentOptions.Left, fontAsset);
            var buttonRow = CreateButtonRow(panel.transform);

            var speakButton = CreateButton(buttonRow.transform, "Speak Button", "Speak", new Color(0.12f, 0.47f, 0.84f, 1f), 150f);
            UnityEventTools.AddPersistentListener(speakButton.onClick, sessionController.SpeakCurrentWord);

            CreateLanguageButton(buttonRow.transform, sessionController, LanguageCode.English, "EN");
            CreateLanguageButton(buttonRow.transform, sessionController, LanguageCode.Hindi, "HI");
            CreateLanguageButton(buttonRow.transform, sessionController, LanguageCode.Malayalam, "ML");
            CreateLanguageButton(buttonRow.transform, sessionController, LanguageCode.French, "FR");

            var presenterObject = new SerializedObject(presenter);
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
                typeof(Outline),
                typeof(RecognitionBoundingBoxPresenter));
            boxObject.transform.SetParent(parent, false);

            var rectTransform = boxObject.GetComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.sizeDelta = new Vector2(360f, 260f);

            var image = boxObject.GetComponent<Image>();
            image.color = new Color(0.07f, 0.88f, 0.48f, 0.12f);
            image.raycastTarget = false;

            var outline = boxObject.GetComponent<Outline>();
            outline.effectColor = new Color(0.07f, 0.88f, 0.48f, 1f);
            outline.effectDistance = new Vector2(3f, -3f);

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
            labelText.overflowMode = TextOverflowModes.Ellipsis;
            if (fontAsset != null)
                labelText.font = fontAsset;

            var presenter = boxObject.GetComponent<RecognitionBoundingBoxPresenter>();
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
            rectTransform.anchorMin = new Vector2(0f, 0f);
            rectTransform.anchorMax = new Vector2(1f, 0f);
            rectTransform.pivot = new Vector2(0.5f, 0f);
            rectTransform.anchoredPosition = new Vector2(0f, 24f);
            rectTransform.sizeDelta = new Vector2(-48f, 360f);

            var image = panel.GetComponent<Image>();
            image.color = new Color(0.04f, 0.05f, 0.06f, 0.88f);

            var layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(32, 32, 24, 24);
            layout.spacing = 8f;
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
            textComponent.overflowMode = TextOverflowModes.Ellipsis;
            if (fontAsset != null)
                textComponent.font = fontAsset;

            var layout = textObject.GetComponent<LayoutElement>();
            layout.minHeight = Mathf.Max(42f, fontSize + 16f);
            layout.preferredHeight = Mathf.Max(42f, fontSize + 18f);

            return textComponent;
        }

        static GameObject CreateButtonRow(Transform parent)
        {
            var row = new GameObject("Controls", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(parent, false);

            var horizontalLayout = row.GetComponent<HorizontalLayoutGroup>();
            horizontalLayout.spacing = 10f;
            horizontalLayout.childAlignment = TextAnchor.MiddleLeft;
            horizontalLayout.childControlWidth = true;
            horizontalLayout.childControlHeight = true;
            horizontalLayout.childForceExpandWidth = false;
            horizontalLayout.childForceExpandHeight = false;

            var layout = row.GetComponent<LayoutElement>();
            layout.minHeight = 64f;
            layout.preferredHeight = 64f;

            return row;
        }

        static void CreateLanguageButton(
            Transform parent,
            LearningSessionController sessionController,
            LanguageCode language,
            string label)
        {
            var button = CreateButton(parent, $"{label} Button", label, new Color(0.22f, 0.24f, 0.27f, 1f), 76f);
            UnityEventTools.AddIntPersistentListener(button.onClick, sessionController.SetLearningLanguage, (int)language);
        }

        static Button CreateButton(Transform parent, string name, string label, Color color, float width)
        {
            var buttonObject = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            buttonObject.transform.SetParent(parent, false);

            var image = buttonObject.GetComponent<Image>();
            image.color = color;

            var button = buttonObject.GetComponent<Button>();
            button.targetGraphic = image;

            var colors = button.colors;
            colors.highlightedColor = Color.Lerp(color, Color.white, 0.15f);
            colors.pressedColor = Color.Lerp(color, Color.black, 0.15f);
            button.colors = colors;

            var layout = buttonObject.GetComponent<LayoutElement>();
            layout.minWidth = width;
            layout.preferredWidth = width;
            layout.minHeight = 56f;
            layout.preferredHeight = 56f;

            var text = CreateText(buttonObject.transform, "Label", label, 24f, FontStyles.Bold, TextAlignmentOptions.Center, null);
            var textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            text.color = Color.white;

            return button;
        }

        static void WriteReport(string report)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SetupReportPath) ?? ".");
            File.WriteAllText(SetupReportPath, report);
            Debug.Log(report);
        }
    }
}
