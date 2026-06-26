# Build And Test Checklist

## Current MVP State

- `Assets/Scenes/Main.unity` is the build scene.
- The scene contains the existing AR Session / XR Origin plus the language-learning runtime.
- The runtime currently uses `MockObjectRecognitionService` so the vocabulary, translation, TTS, progress, and AR camera flow can be tested before a Sentis or TensorFlow Lite detector is integrated.
- The mock detector is not camera-based recognition. It cycles through common object labels and updates the overlay every few seconds.
- Recognition results now include bounding boxes. The mock detector shows synthetic boxes; a real detector will provide actual object boxes.
- The overlay supports English, Hindi, Malayalam, and French.
- Noto fonts are bundled under `Resources/Fonts`; the overlay creates runtime TextMeshPro font fallbacks for Hindi and Malayalam glyphs.
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
4. Move the phone until AR tracking starts.
5. The vocabulary overlay should cycle through simulated object labels such as book, bottle, chair, table, laptop, and phone. A bounding box should also move around the screen. Pointing at real objects will not affect the label until the real detector is implemented.
6. Tap `Speak` to test Android Text-to-Speech.
7. Tap `EN`, `HI`, `ML`, or `FR` to switch the learning language.

## Still Pending For Full Project Scope

- Replace `MockObjectRecognitionService` with a real Sentis or TensorFlow Lite object detector that consumes AR camera frames.
- Add quiz UI screens that call the existing `QuizService`.
- Add a progress dashboard that displays `RetentionAnalysisService` results.
- Validate Android Text-to-Speech availability for Malayalam on the test device; some phones may need the language voice pack installed.
- Replace the simple screen-space overlay with anchored AR labels after real object detection returns object positions.

## Real Recognition Implementation Plan

1. Add the Unity Sentis or TensorFlow Lite Unity package.
2. Add a mobile-friendly model such as MobileNet SSD, EfficientDet Lite, or YOLO Nano exported to ONNX/TFLite.
3. Implement an `IObjectRecognitionService` adapter that reads frames from `ARCameraManager`, resizes/preprocesses them, runs inference, maps model class labels to `vocabulary_catalog.json`, and emits `ObjectRecognitionResult`.
4. Include a confidence threshold and throttle inference to avoid overheating on phones.
5. Replace `MockObjectRecognitionService` in `LanguageLearningCompositionRoot` with the real adapter.
