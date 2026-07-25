# Build And Test Checklist

## Current MVP State

- `Assets/Scenes/Main.unity` is the build scene.
- The scene contains the existing AR Session / XR Origin plus the language-learning runtime.
- `yolov8n.onnx` is bundled under `Assets/_Project/LanguageLearning/ML/Models/`, imports as a Unity model asset, and is assigned to `SentisObjectRecognitionService`.
- Recognition results include normalized bounding boxes. Sentis publishes only model predictions or an empty detection set; the mock recognizer is limited to intentional Editor/demo use.
- Detector input uses asynchronous RGB24 conversion; YOLO work is layer-budgeted across rendered frames, followed by asynchronous readback, non-maximum suppression, camera display-coordinate conversion, and temporal stabilization.
- The overlay supports English, Hindi, Malayalam, and French.
- Pre-generated Noto TextMeshPro assets are bundled under `Resources/Fonts/TMP`, with persisted
  Devanagari and Malayalam fallbacks. No transient font assets are created during startup or builds.
- The app opens on a Home screen with Scan, Quiz, and Progress actions.
- AR camera/session components stay disabled on Home and start only after Scan is selected, camera
  permission is granted, and AR Foundation reports availability.
- Scan always shows EN, HI, ML, and FR selectors plus Back and Speak. Speak stays disabled until a
  vocabulary-backed detection is available.
- Quiz has English, Hindi, Malayalam, and French selectors plus explicit Back and Next/Finish controls.
- Quiz questions are generated as a varied sequence and never repeat consecutively.
- Quiz outcomes, learning progress, revision priority, and total learning time persist locally.
- Android TTS initializes asynchronously, queues the first request, and reports unavailable language packs instead of failing silently.
- AR raycasting places a world-space vocabulary label when a plane or feature point is available; the screen-space overlay remains the reliable fallback.
- Android settings are configured for APK testing: package id `com.raihan.aiarlanguagelearning`, IL2CPP, ARM64, min SDK 25.

## Build APK

In Unity, use:

`AI-AR Language Learning > Build Android Debug APK`

Output:

`Builds/Android/AIARLanguageLearning-debug.apk`

The menu command reruns the scene setup first, then starts a development Android APK build.

## Phone Test

1. Use an Android phone with ARCore support.
2. Install the generated APK.
3. Allow camera permission when prompted.
4. Confirm that the Home screen appears without a session-subsystem startup error.
5. Tap `Scan`; confirm the permission prompt appears and the live camera opens.
6. Move the phone until AR tracking starts.
7. Point at COCO objects such as book, bottle, chair, cup, laptop, cell phone, apple, or banana;
   labels and boxes should come only from Sentis model predictions.
8. Tap `Speak` to test Android Text-to-Speech.
9. Tap `EN`, `HI`, `ML`, or `FR` to switch the learning language, then use Back to return Home.
10. Open `Quiz`, select each language, answer a question, tap Next, and confirm the prompt changes.
11. Complete five questions, then confirm Finish returns Home and the score is persisted.
12. Open `Progress`, confirm the statistics reflect the session, then use Back to return Home.

## Automated Validation

Run the EditMode suite from Unity Test Runner. It covers:

- label normalization and false-positive prevention;
- bounded viewport coordinates;
- progress, quiz generation, retention, and adaptive revision;
- complete four-language catalog loading;
- Main scene/runtime/model wiring;
- Home, Quiz, Next, and Back button wiring;
- Scan language, Speak, real-time scheduling budget, and production logging configuration;
- quiz-language selection and non-repeating question sequences;
- deferred AR startup and Android XR provider configuration;
- missing scene-script detection;
- imported YOLO input/output contract;
- Latin, Devanagari, and Malayalam font atlases/fallbacks;
- a project-wide guard against null TextMeshPro atlas references.

Normal C# compilation can also be checked by building the generated Domain, Application, Unity,
EditMode test, and Editor projects. Imported XR sample warnings are external sample code, not MVP failures.

Latest clean validation (Unity 6000.4.11f1):

- EditMode: 17 passed, 0 failed, 0 skipped.
- Android development build: succeeded with IL2CPP and ARM64.
- APK: `Builds/Android/AIARLanguageLearning-debug.apk`.
- Package: `com.raihan.aiarlanguagelearning`, min SDK 25, target SDK 36.
- Required manifest capabilities: camera, AR camera/depth, portrait orientation.

## Device Acceptance Gate

Camera permission, ARCore tracking, real camera detections, GPU throughput, world-label stability, and
installed Android voice packs must be accepted on at least one supported physical Android device.
These hardware behaviours cannot be proven by desktop EditMode tests. Capture a short test run and
device logs before release; Malayalam may prompt installation of a compatible system voice.
