# Build And Test Checklist

## Current MVP State

- `Assets/Scenes/Main.unity` is the build scene.
- The scene contains the existing AR Session / XR Origin plus the language-learning runtime.
- The verified `yolo11n_320.onnx` is bundled under `ObjectDetection/Models`, imports as a Unity model asset, and is assigned to `RealtimeCocoObjectDetector`.
- Recognition results include normalized bounding boxes. Production has no mock detector or silent fallback.
- Detector input is generated entirely on the GPU from AR Foundation's display-oriented camera
  background; YOLO work is layer-budgeted across rendered frames, followed by asynchronous readback,
  explicit `[1,84,2100]` decoding, class-aware NMS, and temporal stabilization.
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
- Quiz outcomes, learning progress, revision priority, and focused learning time persist locally.
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
7. Point at COCO objects such as person, backpack, bottle, cup, chair, laptop, mouse, keyboard,
   cell phone, or book; labels and boxes should come only from YOLO11 predictions.
8. Tap `Speak` to test Android Text-to-Speech.
9. Tap `EN`, `HI`, `ML`, or `FR` to switch the learning language, then use Back to return Home.
10. Open `Quiz`, select each language, answer a question, tap Next, and confirm the prompt changes.
11. Complete five questions, then confirm Finish returns Home and the score is persisted.
12. Open `Progress`; confirm scans, completed quizzes, accuracy, focused time, and today's bar reflect the session.
13. Confirm incorrectly answered or not-yet-tested learned words appear in the adaptive revision queue.
14. Start another quiz and confirm weak learned words recur more often without appearing twice in a row.
15. Use Back to return Home.

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
- exact YOLO input/output contract, CPU inference smoke execution, letterbox inversion, decoder, and NMS;
- Latin, Devanagari, and Malayalam font atlases/fallbacks;
- a project-wide guard against null TextMeshPro atlas references.

Normal C# compilation can also be checked by building the generated Domain, Application, Unity,
EditMode test, and Editor projects. Imported XR sample warnings are external sample code, not MVP failures.

Latest clean validation (Unity 6000.4.11f1):

- EditMode: 26 passed, 0 failed, 0 skipped.
- Android development build: succeeded with IL2CPP and ARM64.
- APK: `Builds/Android/AIARLanguageLearning-debug.apk` (213,797,976 bytes).
- APK SHA-256: `0BBE7A27148989DFB4E7E43EE6594036D0A5772144F36CB59BCA3029F98E42CC`.
- APK signature: valid Android APK Signature Scheme v2 debug signature.
- YOLO11n model SHA-256: `3FE015B910BC39E650E91607E22F3D30BAA7038ACBD8279F7EA729E74A1BDCFC`.
- Package: `com.raihan.aiarlanguagelearning`, min SDK 25, target SDK 36.
- Required manifest capabilities: camera, AR camera/depth, portrait orientation.

## Device Acceptance Gate

Camera permission, ARCore tracking, real camera detections, GPU throughput, world-label stability, and
installed Android voice packs must be accepted on at least one supported physical Android device.
These hardware behaviours cannot be proven by desktop EditMode tests. Capture a short test run and
device logs before release; Malayalam may prompt installation of a compatible system voice.
