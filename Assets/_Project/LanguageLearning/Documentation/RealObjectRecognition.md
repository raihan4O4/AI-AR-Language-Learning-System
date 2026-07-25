# Real-Time AR Object Recognition

The production detector is `RealtimeCocoObjectDetector`. It is a clean YOLO11n pipeline built for
Unity 6, AR Foundation 6.4, ARCore 6.4, and Unity Inference Engine 2.6.

## Data flow

1. `ARCameraBackground` exposes the current AR camera texture and its display transform.
2. The AR background material renders a reusable GPU texture with the same orientation and crop the
   learner sees on screen.
3. The visible texture is bilinearly resized and centered in a reusable 320×320 sRGB texture. Empty
   space uses the YOLO letterbox value 114.
4. `TextureConverter` writes RGB values in `[0,1]` to a preallocated NCHW float tensor.
5. A GPUCompute worker schedules a bounded number of graph layers per rendered frame. CPU is used
   only if the preferred worker cannot be created.
6. Output readback is requested asynchronously.
7. `Yolo11Postprocessor` accepts only the verified `[1,84,2100]` layout, decodes `cx,cy,w,h` plus
   80 class scores, and applies configurable class-aware NMS.
8. The inverse letterbox maps boxes directly into normalized display coordinates.
9. `LearningSessionController` performs brief temporal smoothing and vocabulary lookup.
10. `PooledDetectionOverlay` reuses UI views for boxes, translated labels, and confidence values.

The preview never waits for this pipeline. While inference is busy, incoming detection frames are
dropped instead of queued.

## Assets and reproducibility

- Model: `ObjectDetection/Models/yolo11n_320.onnx`
- Labels: `ObjectDetection/Models/coco80.txt`
- Contract and golden result: `ObjectDetection/Models/yolo11n_320.metadata.json`
- Validation image: `ObjectDetection/Validation/bus.jpg`
- Export tool: `Tools/ObjectDetection/export_yolo11_for_unity.py`

The export tool downloads official YOLO11n weights, creates a static opset-12 ONNX model, runs ONNX
validation and ONNX Runtime golden inference, and verifies correct `person` and `bus` predictions.
Runtime does not guess input or output layouts.

## Runtime configuration

- Preferred backend: GPUCompute
- Confidence threshold: 0.30
- Class-aware NMS IoU threshold: 0.45
- Maximum displayed detections: 10
- Scheduled layers per rendered frame: 16
- Minimum interval after completed inference: 0.05 seconds
- Verbose per-frame logging: disabled in production

These defaults preserve a responsive preview. Tune `Layers Per Frame` per target device: lower
values reduce render-frame spikes; higher values improve detection refresh rate.

## Diagnostics

Every fatal stage identifies itself in the log:

- `ObjectDetection/initialization`
- `ObjectDetection/camera preprocessing`
- `ObjectDetection/inference scheduling`
- `ObjectDetection/output decoding`

Input and output shapes are checked before results can be published. A fatal contract or resource
error unsubscribes from the camera, clears boxes, disposes tensors, workers, and render textures, and
publishes a faulted service status.

## Physical-device acceptance

Desktop tests verify graph import, CPU inference, decoder behavior, NMS, coordinate conversion, and
scene wiring. Release acceptance still requires an ARCore-capable Android phone:

1. Confirm the preview remains smooth while the detection count increases.
2. Test portrait and landscape orientation if landscape is enabled.
3. Verify boxes on near, central, and edge objects.
4. Verify the ten target COCO classes and confidence labels.
5. Switch EN, HI, ML, and FR while scanning.
6. Confirm Speak uses the selected language.
7. Leave and re-enter Scan repeatedly and confirm memory remains stable.

Ultralytics provides YOLO11 under AGPL-3.0 or an Enterprise license. Confirm the distribution license
before release.
