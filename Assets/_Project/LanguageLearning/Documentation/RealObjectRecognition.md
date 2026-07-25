# Real Object Recognition Integration

The app now has a Sentis-backed detector component:

- `ObjectRecognitionResult` supports `label`, `confidence`, and normalized bounding box coordinates.
- `LearningSessionController` stabilizes detections, then maps detector labels to vocabulary entries.
- `RecognitionBoundingBoxPresenter` displays one rectangle per published detection and clears stale boxes when a frame has no detections.
- `SentisObjectRecognitionService` converts AR camera images asynchronously to RGB24, letterboxes them to the model input, schedules a bounded number of Unity Inference Engine layers per rendered frame, requests non-blocking output readback, decodes YOLO-style detections, applies NMS, and emits model predictions only.
- `MockObjectRecognitionService` remains available for intentional demo/testing scenes, but it is not used as a fallback inside the Sentis detector.

## Recommended MVP Model

Use a small COCO-trained YOLO detection model exported to ONNX. COCO overlaps with many MVP vocabulary words:

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

Recommended starting point:

1. Install Python 3.10+ on your machine.
2. Run `pip install ultralytics onnx onnxslim`.
3. Export a lightweight model:

   `yolo export model=yolov8n.pt format=onnx imgsz=640 simplify=True opset=12 nms=False`

4. Copy the generated `yolov8n.onnx` into `Assets/_Project/LanguageLearning/ML/Models/`.
5. Return to Unity and wait until import finishes. The ONNX file should appear as a Sentis `ModelAsset`.
6. Keep `Assets/_Project/LanguageLearning/ML/Models/coco_labels.txt` next to the model.
7. Run `AI-AR Language Learning > Use Sentis Detector In Main Scene`.
8. Build the APK with `AI-AR Language Learning > Build Android Debug APK`.

The scene setup menu automatically assigns:

- AR camera manager
- ONNX/Sentis `ModelAsset`
- COCO labels
- 640x640 input size
- aspect-preserving preprocessing
- GPU compute backend
- 0.35 confidence threshold
- 0.45 NMS threshold

## Coordinate Contract

`NormalizedBoundingBox` uses normalized screen/image coordinates:

- `X`: left, from `0` to `1`
- `Y`: top, from `0` to `1`
- `Width`: box width, from `0` to `1`
- `Height`: box height, from `0` to `1`

The detector applies the inverse AR camera display matrix so camera-image boxes follow the phone's
current orientation/crop. The presenter then converts them into screen-space UI coordinates.

The Sentis detector keeps the camera image aspect ratio when resizing into the square model tensor. It stores the letterbox padding used for each frame and removes that padding before publishing normalized boxes.

The learning session shows the first accepted prediction immediately, smooths changing confidence/box
values, and holds a recognized label for 2 seconds through short detector gaps. Confidence filtering,
NMS, and vocabulary matching still reject unsupported or weak detections without making a slow phone
wait for a second full 640x640 inference before displaying anything.

## Runtime Diagnostics

Production scene setup keeps `m_DebugLogging` disabled so per-frame Android log I/O cannot throttle the
camera. Enable it only for a short diagnostic run. The detector logs model load status, input and output
shapes, camera frame conversion dimensions, tensor conversion, inference count, decode counts, NMS
results, final labels, and bounding boxes. If model inference fails or produces no accepted detection,
the service publishes an empty detection set instead of placeholder labels.

## Scene Wiring

After adding the ONNX model, the preferred path is the menu item:

`AI-AR Language Learning > Use Sentis Detector In Main Scene`

Manual inspector wiring, if needed:

1. Open `Assets/Scenes/Main.unity`.
2. Select `LanguageLearning Runtime`.
3. In `SentisObjectRecognitionService`, assign the AR camera manager, model asset, and `coco_labels.txt`.
4. In `LanguageLearningCompositionRoot`, set `Object Recognition Service` to `SentisObjectRecognitionService`.
5. Disable `MockObjectRecognitionService`.
6. Keep `ResourcesVocabularyRepository`, `AndroidTextToSpeechService`, and `LearningSessionController` unchanged.

## Performance Notes

- Do not run inference every camera frame on a phone.
- Keep at most one conversion/inference/readback pipeline in flight; extra camera frames are intentionally dropped.
- Keep camera conversion asynchronous and spread model scheduling across rendered frames. The default budget is six model layers per rendered frame.
- Start the next inference after the previous result has completed, not from the previous inference's start time.
- Prefer a camera configuration near 1280x720 and 30 FPS rather than the device's highest resolution.
- Start with `yolov8n` at 640x640. If the phone is slow, export at 320 or 416 and update the service input size to match.
- Use GPU/compute backend if stable on the test device, otherwise use CPU Burst.
- Keep the vocabulary mapper separate from the detector; model labels should stay in English and map through `vocabulary_catalog.json`.
- Log the top raw labels during testing so missing vocabulary aliases can be added to `recognitionLabels`.
