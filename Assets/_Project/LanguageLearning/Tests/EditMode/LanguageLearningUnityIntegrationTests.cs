using AIAR.LanguageLearning.Application.Models;
using AIAR.LanguageLearning.Unity.Bootstrap;
using AIAR.LanguageLearning.Unity.Data;
using AIAR.LanguageLearning.Unity.ObjectDetection;
using AIAR.LanguageLearning.Unity.Presentation;
using NUnit.Framework;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace AIAR.LanguageLearning.Tests
{
    public sealed class LanguageLearningUnityIntegrationTests
    {
        const string MainScenePath = "Assets/Scenes/Main.unity";
        const string ModelPath = "Assets/_Project/LanguageLearning/ObjectDetection/Models/yolo11n_320.onnx";
        const string LabelsPath = "Assets/_Project/LanguageLearning/ObjectDetection/Models/coco80.txt";

        [Test]
        public void MainScene_HasValidMvpRuntimeWiring()
        {
            var scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);

            Assert.IsTrue(scene.IsValid());
            Assert.IsNotNull(Object.FindAnyObjectByType<LanguageLearningCompositionRoot>());
            Assert.IsNotNull(Object.FindAnyObjectByType<LearningSessionController>());
            Assert.IsNotNull(Object.FindAnyObjectByType<LearningMvpController>());
            Assert.IsNotNull(Object.FindAnyObjectByType<RealtimeCocoObjectDetector>());
            Assert.IsNotNull(Object.FindAnyObjectByType<PooledDetectionOverlay>(FindObjectsInactive.Include));
            Assert.IsNotNull(Object.FindAnyObjectByType<ARVocabularyOverlayPresenter>());

            var uiRoot = GameObject.Find("LanguageLearning UI");
            Assert.IsNotNull(uiRoot, "The main MVP canvas is missing.");
            Assert.Greater(
                uiRoot.transform.localScale.sqrMagnitude,
                0.0001f,
                "The root canvas must not have the zero scale that previously made the UI invisible.");

            var recognizer = Object.FindAnyObjectByType<RealtimeCocoObjectDetector>();
            var serializedRecognizer = new SerializedObject(recognizer);
            Assert.IsNotNull(
                serializedRecognizer.FindProperty("m_ModelAsset").objectReferenceValue,
                "The verified YOLO11n model is not assigned.");
            Assert.IsNotNull(
                serializedRecognizer.FindProperty("m_CocoLabels").objectReferenceValue,
                "The COCO labels are not assigned.");
            Assert.AreEqual(
                (int)BackendType.GPUCompute,
                serializedRecognizer.FindProperty("m_PreferredBackend").intValue);
        }

        [Test]
        public void MainScene_MenuQuizAndBackButtonsDriveTheMvpFlow()
        {
            EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);

            var mvp = Object.FindAnyObjectByType<LearningMvpController>();
            Assert.IsNotNull(mvp);
            mvp.ShowHome();

            var mainMenu = GameObject.Find("Main Menu").GetComponent<CanvasGroup>();
            var quizButton = GameObject.Find("Quiz Button").GetComponent<Button>();
            Assert.IsTrue(mainMenu.interactable);
            Assert.IsTrue(mainMenu.blocksRaycasts);

            quizButton.onClick.Invoke();

            var quizPanel = GameObject.Find("Quiz Panel").GetComponent<CanvasGroup>();
            var languageSelector = GameObject.Find("Quiz Language Selector");
            var nextButton = GameObject.Find("Next Question Button").GetComponent<Button>();
            Assert.IsFalse(mainMenu.interactable);
            Assert.IsTrue(quizPanel.interactable);
            Assert.AreEqual(
                System.Enum.GetValues(typeof(Domain.LanguageCode)).Length,
                languageSelector.transform.childCount,
                "Every supported quiz language must have a selector button.");
            Assert.IsFalse(nextButton.interactable, "Next must remain disabled until an answer is chosen.");

            var firstPrompt = GameObject.Find("Quiz Prompt").GetComponent<TMPro.TMP_Text>().text;
            GameObject.Find("Quiz Option 1 Button").GetComponent<Button>().onClick.Invoke();
            Assert.IsTrue(nextButton.interactable);
            nextButton.onClick.Invoke();
            var secondPrompt = GameObject.Find("Quiz Prompt").GetComponent<TMPro.TMP_Text>().text;
            Assert.AreNotEqual(firstPrompt, secondPrompt, "Next must advance to a different question.");

            GameObject.Find("Back Button").GetComponent<Button>().onClick.Invoke();
            Assert.IsTrue(mainMenu.interactable);
            Assert.IsFalse(quizPanel.interactable);
        }

        [Test]
        public void MainScene_ScanHasRealtimeBudgetAndLanguageSpeechControls()
        {
            EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);

            var mvp = Object.FindAnyObjectByType<LearningMvpController>();
            var session = Object.FindAnyObjectByType<LearningSessionController>();
            var recognizer = Object.FindAnyObjectByType<RealtimeCocoObjectDetector>();
            Assert.IsNotNull(mvp);
            Assert.IsNotNull(session);
            Assert.IsNotNull(recognizer);
            mvp.ShowHome();

            var scanControls = GameObject.Find("Scan Controls");
            var languageSelector = GameObject.Find("Scan Language Selector");
            var scanActions = GameObject.Find("Scan Actions");
            Assert.IsNotNull(scanControls, "Scan controls must be available before the first detection.");
            Assert.IsNotNull(languageSelector);
            Assert.IsNotNull(scanActions);
            Assert.AreEqual(
                System.Enum.GetValues(typeof(Domain.LanguageCode)).Length,
                languageSelector.GetComponentsInChildren<Button>(true).Length);

            var speakButton = System.Array.Find(
                scanActions.GetComponentsInChildren<Button>(true),
                button => button.name == "Speak Button");
            var backButton = System.Array.Find(
                scanActions.GetComponentsInChildren<Button>(true),
                button => button.name == "Back Button");
            Assert.IsNotNull(speakButton);
            Assert.IsNotNull(backButton);
            Assert.IsFalse(speakButton.interactable, "Speak must wait until a vocabulary object is detected.");

            var malayalamButton = System.Array.Find(
                languageSelector.GetComponentsInChildren<Button>(true),
                button => button.name == "ML Button");
            Assert.IsNotNull(malayalamButton);
            malayalamButton.onClick.Invoke();
            Assert.AreEqual(Domain.LanguageCode.Malayalam, session.LearningLanguage);

            foreach (Domain.LanguageCode language in System.Enum.GetValues(typeof(Domain.LanguageCode)))
            {
                var languageButtonName =
                    $"{Domain.LanguageCodeExtensions.ToIsoCode(language).ToUpperInvariant()} Button";
                var languageButton = System.Array.Find(
                    languageSelector.GetComponentsInChildren<Button>(true),
                    button => button.name == languageButtonName);
                Assert.IsNotNull(languageButton, $"The scan selector is missing {languageButtonName}.");
                languageButton.onClick.Invoke();
                Assert.AreEqual(
                    language,
                    session.LearningLanguage,
                    $"The scan selector did not select {language}.");
            }

            var serializedRecognizer = new SerializedObject(recognizer);
            Assert.LessOrEqual(
                serializedRecognizer.FindProperty("m_LayersPerFrame").intValue,
                32,
                "YOLO scheduling must be divided across rendered frames.");
            Assert.Greater(
                serializedRecognizer.FindProperty("m_LayersPerFrame").intValue,
                0);
            Assert.LessOrEqual(
                serializedRecognizer.FindProperty("m_MinimumInferenceInterval").floatValue,
                0.1f,
                "Completed inference should promptly accept a fresh camera frame.");
            Assert.IsFalse(
                serializedRecognizer.FindProperty("m_VerboseLogging").boolValue,
                "Per-frame detector logging must be disabled in the device build.");

            var serializedSession = new SerializedObject(session);
            Assert.AreEqual(
                1,
                serializedSession.FindProperty("m_MinimumStableFrames").intValue,
                "A valid first detection should be visible immediately.");
            Assert.GreaterOrEqual(
                serializedSession.FindProperty("m_DetectionHoldSeconds").floatValue,
                1f,
                "The overlay must survive the interval between mobile inference results.");
        }

        [Test]
        public void MainScene_BottomTitleUpdatesForEverySupportedLanguage()
        {
            EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);

            var session = Object.FindAnyObjectByType<LearningSessionController>();
            var repository = Object.FindAnyObjectByType<ResourcesVocabularyRepository>();
            var vocabularyPanel = GameObject.Find("Vocabulary Panel");
            var presenter = vocabularyPanel != null
                ? vocabularyPanel.GetComponent<ARVocabularyOverlayPresenter>()
                : null;
            var mvp = Object.FindAnyObjectByType<LearningMvpController>();
            Assert.IsNotNull(session);
            Assert.IsNotNull(repository);
            Assert.IsNotNull(presenter);
            Assert.IsNotNull(mvp);

            var presenterObject = new SerializedObject(presenter);
            Assert.AreEqual(
                session,
                presenterObject.FindProperty("m_SessionController").objectReferenceValue,
                "The bottom title must subscribe directly to the active learning session.");
            var translatedWordText = (TMPro.TMP_Text)presenterObject
                .FindProperty("m_TranslatedWordText")
                .objectReferenceValue;
            Assert.IsNotNull(translatedWordText);

            typeof(ARVocabularyOverlayPresenter)
                .GetMethod("SubscribeToSession", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.Invoke(presenter, null);

            Assert.IsTrue(repository.TryGetById("book", out var entry));
            var publishVocabulary = typeof(LearningSessionController).GetMethod(
                "PublishVocabulary",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(publishVocabulary);
            publishVocabulary.Invoke(
                session,
                new object[]
                {
                    entry,
                    "book",
                    0.9f,
                    NormalizedBoundingBox.Centered(0.5f, 0.5f),
                    false
                });

            foreach (Domain.LanguageCode language in System.Enum.GetValues(typeof(Domain.LanguageCode)))
            {
                mvp.SelectScanLanguage((int)language);
                var expected = entry.GetRequiredTranslation(language);

                Assert.AreEqual(
                    language,
                    session.LearningLanguage,
                    $"The scan selector did not select {language}.");
                Assert.AreEqual(
                    expected.Text,
                    translatedWordText.text,
                    $"The bottom title did not refresh for {language}.");
                Assert.IsFalse(
                    string.IsNullOrWhiteSpace(translatedWordText.text),
                    $"The bottom title is blank for {language}.");
                Assert.IsNotNull(translatedWordText.font, $"The bottom title has no font for {language}.");

                var expectedFontName = language switch
                {
                    Domain.LanguageCode.Hindi => "NotoSansDevanagari-Regular SDF",
                    Domain.LanguageCode.Malayalam => "NotoSansMalayalam-Regular SDF",
                    _ => "NotoSans-Regular SDF"
                };
                Assert.AreEqual(expectedFontName, translatedWordText.font.name);

                translatedWordText.ForceMeshUpdate(true, true);
                Assert.Greater(
                    translatedWordText.textInfo.characterCount,
                    0,
                    $"TextMesh Pro generated no characters for {language}.");
                Assert.IsTrue(
                    System.Array.Exists(
                        translatedWordText.textInfo.characterInfo,
                        character => character.isVisible),
                    $"TextMesh Pro generated no visible glyphs for {language}.");
                Assert.AreEqual(
                    translatedWordText.font.atlasTexture,
                    translatedWordText.fontSharedMaterial.mainTexture,
                    $"The font atlas and material texture do not match for {language}.");
            }
        }

        [Test]
        public void MainScene_DetectionBoxesUseOutlineOnlyGraphics()
        {
            EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);

            var session = Object.FindAnyObjectByType<LearningSessionController>();
            var overlay = Object.FindAnyObjectByType<PooledDetectionOverlay>(FindObjectsInactive.Include);
            Assert.IsNotNull(session);
            Assert.IsNotNull(overlay);

            var serializedOverlay = new SerializedObject(overlay);
            var boxRect = (RectTransform)serializedOverlay.FindProperty("m_BoxRectTransform").objectReferenceValue;
            var labelText = (TMPro.TMP_Text)serializedOverlay.FindProperty("m_LabelText").objectReferenceValue;
            var canvasGroup = (CanvasGroup)serializedOverlay.FindProperty("m_CanvasGroup").objectReferenceValue;
            overlay.Configure(session, boxRect, labelText, canvasGroup);

            var background = boxRect.GetComponent<Image>();
            Assert.IsNotNull(background);
            Assert.IsFalse(background.enabled, "The detection box background graphic must stay disabled.");
            Assert.AreEqual(0f, background.color.a, 0.0001f, "The detection box interior must be transparent.");

            var legacyOutline = boxRect.GetComponent<Outline>();
            Assert.IsTrue(
                legacyOutline == null || !legacyOutline.enabled,
                "The filled UI Outline effect must not render the detection box.");

            var border = boxRect.Find("Detection Border");
            Assert.IsNotNull(border, "The four-edge detection border was not created.");
            Assert.AreEqual(4, border.childCount);
            foreach (var edgeName in new[] { "Top", "Bottom", "Left", "Right" })
            {
                var edge = border.Find(edgeName);
                Assert.IsNotNull(edge, $"The {edgeName} border edge is missing.");
                var edgeImage = edge.GetComponent<Image>();
                Assert.IsNotNull(edgeImage);
                Assert.Greater(edgeImage.color.a, 0.99f);
                Assert.IsFalse(edgeImage.raycastTarget);
            }
        }

        [Test]
        public void MainScene_HasNoMissingMonoBehaviourScripts()
        {
            var scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    Assert.Zero(
                        GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject),
                        $"Missing script found on {GetHierarchyPath(transform)}.");
                }
            }
        }

        [Test]
        public void MainScene_DefersArCameraUntilScanAndAutoLoadsAndroidProvider()
        {
            EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);

            var arSession = FindSceneBehaviour("UnityEngine.XR.ARFoundation.ARSession");
            var cameraManager = FindSceneBehaviour("UnityEngine.XR.ARFoundation.ARCameraManager");
            var cameraBackground = FindSceneBehaviour("UnityEngine.XR.ARFoundation.ARCameraBackground");
            Assert.IsNotNull(arSession);
            Assert.IsNotNull(cameraManager);
            Assert.IsNotNull(cameraBackground);
            Assert.IsFalse(arSession.enabled, "ARSession must remain off on the main menu.");
            Assert.IsFalse(cameraManager.enabled, "The camera must remain off until Scan is selected.");
            Assert.IsFalse(cameraBackground.enabled, "The camera background must remain off until Scan is selected.");

            var xrSettingsAssets = AssetDatabase.LoadAllAssetsAtPath("Assets/XR/XRGeneralSettings.asset");
            const string providerName = "Android Providers";
            var provider = System.Array.Find(xrSettingsAssets, asset => asset != null && asset.name == providerName);
            Assert.IsNotNull(provider, $"{providerName} settings are missing.");
            var serializedProvider = new SerializedObject(provider);
            Assert.IsTrue(
                serializedProvider.FindProperty("m_AutomaticLoading").boolValue,
                $"{providerName} must initialize its XR loader automatically.");
            Assert.IsTrue(
                serializedProvider.FindProperty("m_AutomaticRunning").boolValue,
                $"{providerName} must start its XR loader automatically.");
        }

        static Behaviour FindSceneBehaviour(string fullTypeName)
        {
            foreach (var behaviour in Resources.FindObjectsOfTypeAll<MonoBehaviour>())
            {
                if (behaviour != null &&
                    behaviour.gameObject.scene.IsValid() &&
                    behaviour.GetType().FullName == fullTypeName)
                {
                    return behaviour;
                }
            }

            return null;
        }

        static string GetHierarchyPath(Transform transform)
        {
            var path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = $"{transform.name}/{path}";
            }

            return path;
        }

        [Test]
        public void VocabularyCatalog_LoadsCompleteMultilingualMvpContent()
        {
            var repository = Object.FindAnyObjectByType<ResourcesVocabularyRepository>();
            Assert.IsNotNull(repository);

            var entries = repository.GetAll();

            Assert.GreaterOrEqual(entries.Count, 30);
            foreach (var entry in entries)
            {
                Assert.IsTrue(entry.TryGetTranslation(Domain.LanguageCode.English, out _), $"{entry.Id} is missing English.");
                Assert.IsTrue(entry.TryGetTranslation(Domain.LanguageCode.Hindi, out _), $"{entry.Id} is missing Hindi.");
                Assert.IsTrue(entry.TryGetTranslation(Domain.LanguageCode.Malayalam, out _), $"{entry.Id} is missing Malayalam.");
                Assert.IsTrue(entry.TryGetTranslation(Domain.LanguageCode.French, out _), $"{entry.Id} is missing French.");
            }
        }

        [Test]
        public void YoloModel_ImportsWithExpectedInputAndOutputContract()
        {
            var modelAsset = AssetDatabase.LoadAssetAtPath<ModelAsset>(ModelPath);
            Assert.IsNotNull(modelAsset, "The MVP YOLO model did not import as a Unity ModelAsset.");

            var model = ModelLoader.Load(modelAsset);

            Assert.AreEqual(1, model.inputs.Count, "The detector must have one image input.");
            Assert.IsTrue(model.inputs[0].shape.IsStatic());
            Assert.AreEqual(
                new TensorShape(1, 3, Yolo11Contract.InputHeight, Yolo11Contract.InputWidth),
                model.inputs[0].shape.ToTensorShape());
            Assert.AreEqual(DataType.Float, model.inputs[0].dataType);
            Assert.AreEqual(1, model.outputs.Count, "The detector must expose one raw prediction output.");

            var labels = AssetDatabase.LoadAssetAtPath<TextAsset>(LabelsPath);
            Assert.AreEqual(Yolo11Contract.ClassCount, Yolo11Contract.ParseAndValidateLabels(labels).Length);
        }

        [Test]
        public void YoloModel_CpuSmokeInferenceProducesVerifiedRuntimeShape()
        {
            var modelAsset = AssetDatabase.LoadAssetAtPath<ModelAsset>(ModelPath);
            Assert.IsNotNull(modelAsset);

            var model = ModelLoader.Load(modelAsset);
            using var worker = new Worker(model, BackendType.CPU);
            using var input = new Tensor<float>(
                new TensorShape(1, 3, Yolo11Contract.InputHeight, Yolo11Contract.InputWidth));

            worker.Schedule(input);
            var output = worker.PeekOutput() as Tensor<float>;
            Assert.IsNotNull(output);
            using var readable = output.ReadbackAndClone();
            Assert.AreEqual(
                new TensorShape(1, Yolo11Contract.OutputChannels, Yolo11Contract.PredictionCount),
                readable.shape);
        }

        [Test]
        public void RuntimeFont_ProvidesLatinDevanagariAndMalayalamAtlases()
        {
            var font = ARVocabularyOverlayPresenter.GetMultilingualFontAsset();

            Assert.IsNotNull(font);
            Assert.IsNotNull(font.atlasTextures);
            Assert.Greater(font.atlasTextures.Length, 0);
            Assert.IsNotNull(font.atlasTextures[0]);
            Assert.GreaterOrEqual(font.fallbackFontAssetTable.Count, 2);
            foreach (var fallback in font.fallbackFontAssetTable)
            {
                Assert.IsNotNull(fallback);
                Assert.IsNotNull(fallback.atlasTextures);
                Assert.Greater(fallback.atlasTextures.Length, 0);
                Assert.IsNotNull(fallback.atlasTextures[0]);
            }
        }

        [Test]
        public void AllTmpFontAssets_HaveAValidAtlasReference()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:TMP_FontAsset"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(path);
                if (font == null)
                    continue;

                Assert.IsNotNull(font.atlasTextures, $"{path} has no atlas collection.");
                Assert.Greater(font.atlasTextures.Length, 0, $"{path} has an empty atlas collection.");
                Assert.IsNotNull(
                    font.atlasTextures[0],
                    $"{path} contains a null atlas reference and will break TextMesh Pro's build preprocessor.");
            }
        }
    }
}
