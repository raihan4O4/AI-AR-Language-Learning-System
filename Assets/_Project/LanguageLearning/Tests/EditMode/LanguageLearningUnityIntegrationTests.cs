using AIAR.LanguageLearning.Unity.Bootstrap;
using AIAR.LanguageLearning.Unity.Data;
using AIAR.LanguageLearning.Unity.Presentation;
using AIAR.LanguageLearning.Unity.Recognition;
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
        const string ModelPath = "Assets/_Project/LanguageLearning/ML/Models/yolov8n.onnx";

        [Test]
        public void MainScene_HasValidMvpRuntimeWiring()
        {
            var scene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);

            Assert.IsTrue(scene.IsValid());
            Assert.IsNotNull(Object.FindAnyObjectByType<LanguageLearningCompositionRoot>());
            Assert.IsNotNull(Object.FindAnyObjectByType<LearningSessionController>());
            Assert.IsNotNull(Object.FindAnyObjectByType<LearningMvpController>());
            Assert.IsNotNull(Object.FindAnyObjectByType<SentisObjectRecognitionService>());
            Assert.IsNotNull(Object.FindAnyObjectByType<ARVocabularyOverlayPresenter>());

            var uiRoot = GameObject.Find("LanguageLearning UI");
            Assert.IsNotNull(uiRoot, "The main MVP canvas is missing.");
            Assert.Greater(
                uiRoot.transform.localScale.sqrMagnitude,
                0.0001f,
                "The root canvas must not have the zero scale that previously made the UI invisible.");

            var recognizer = Object.FindAnyObjectByType<SentisObjectRecognitionService>();
            var serializedRecognizer = new SerializedObject(recognizer);
            Assert.IsNotNull(
                serializedRecognizer.FindProperty("m_ModelAsset").objectReferenceValue,
                "The Sentis model is not assigned.");
            Assert.AreEqual(
                (int)SentisDetectionOutputFormat.YoloV8CenterXYWH,
                serializedRecognizer.FindProperty("m_OutputFormat").enumValueIndex);
            Assert.IsTrue(serializedRecognizer.FindProperty("m_ApplyCameraDisplayTransform").boolValue);
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
            var recognizer = Object.FindAnyObjectByType<SentisObjectRecognitionService>();
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

            var serializedRecognizer = new SerializedObject(recognizer);
            Assert.LessOrEqual(
                serializedRecognizer.FindProperty("m_LayersPerFrame").intValue,
                8,
                "YOLO scheduling must be divided across rendered frames.");
            Assert.GreaterOrEqual(
                serializedRecognizer.FindProperty("m_InferenceIntervalSeconds").floatValue,
                0.1f,
                "Each completed inference needs a short camera-preview recovery window.");
            Assert.IsFalse(
                serializedRecognizer.FindProperty("m_DebugLogging").boolValue,
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
            Assert.AreEqual(4, model.inputs[0].shape.rank, "The detector input must be a rank-4 image tensor.");
            Assert.GreaterOrEqual(model.outputs.Count, 1, "The detector must expose at least one prediction output.");
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
