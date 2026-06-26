# Real Object Recognition Integration

The app is now ready to receive real detections:

- `ObjectRecognitionResult` supports `label`, `confidence`, and normalized bounding box coordinates.
- `LearningSessionController` maps detector labels to vocabulary entries.
- `RecognitionBoundingBoxPresenter` displays the detection rectangle and translated label.
- `MockObjectRecognitionService` uses the same output contract, so it can be replaced without changing UI or learning logic.

## Recommended MVP Detector

Use a mobile object detection model trained on COCO-style classes, because many MVP vocabulary words overlap with COCO:

- book
- bottle
- chair
- table / dining table
- laptop
- cell phone
- cup
- backpack / handbag
- clock
- car
- bus
- bicycle
- bed
- spoon
- apple
- banana

Recommended runtime path:

1. Install Unity Sentis from Package Manager.
2. Add a mobile-friendly ONNX model such as SSD MobileNet, EfficientDet Lite, YOLO Nano, or another lightweight detector.
3. Add a labels text file next to the model.
4. Implement a Sentis adapter that:
   - Subscribes to `ARCameraManager.frameReceived`.
   - Acquires the latest CPU camera image.
   - Converts and resizes it to the model input size.
   - Runs inference at a throttled interval, for example 2-5 FPS.
   - Applies confidence filtering and non-maximum suppression.
   - Converts model boxes to `NormalizedBoundingBox`.
   - Emits `ObjectRecognitionResult`.
5. Replace `MockObjectRecognitionService` in `LanguageLearningCompositionRoot` with the real Sentis service.

## Coordinate Contract

`NormalizedBoundingBox` uses normalized screen/image coordinates:

- `X`: left, from `0` to `1`
- `Y`: top, from `0` to `1`
- `Width`: box width, from `0` to `1`
- `Height`: box height, from `0` to `1`

The presenter converts this into screen-space UI coordinates.

## Scene Wiring

After implementing the Sentis service:

1. Open `Assets/Scenes/Main.unity`.
2. Select `LanguageLearning Runtime`.
3. Disable or remove `MockObjectRecognitionService`.
4. Add the real Sentis recognition component.
5. Assign it to `LanguageLearningCompositionRoot > Object Recognition Service`.
6. Keep `ResourcesVocabularyRepository`, `AndroidTextToSpeechService`, and `LearningSessionController` unchanged.

## Performance Notes

- Do not run inference every camera frame on a phone.
- Start with 320x320 or 416x416 input.
- Use GPU/compute backend if stable on the test device, otherwise use CPU Burst.
- Keep the vocabulary mapper separate from the detector; model labels should stay in English and map through `vocabulary_catalog.json`.
- Log the top raw labels during testing so missing vocabulary aliases can be added to `recognitionLabels`.
