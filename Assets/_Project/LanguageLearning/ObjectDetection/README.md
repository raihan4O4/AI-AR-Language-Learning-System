# Rebuilt YOLO11 Object Detection

This folder contains the complete, reproducible detector asset set. Runtime code is under
`Unity/ObjectDetection`; export tooling is under `Tools/ObjectDetection`.

## Verified model contract

- Model: Ultralytics YOLO11n trained on COCO 80
- Source weights: official `yolo11n.pt`
- Unity asset: `Models/yolo11n_320.onnx`
- SHA-256: `3FE015B910BC39E650E91607E22F3D30BAA7038ACBD8279F7EA729E74A1BDCFC`
- ONNX opset: 12
- Input: `images`, float32 `[1,3,320,320]`
- Input values: RGB, NCHW, sRGB pixel values divided by 255
- Resize: bilinear, aspect-preserving letterbox with fill value 114
- Output: `output0`, float32 `[1,84,2100]`
- Output fields: `cx`, `cy`, `width`, `height`, then 80 class scores
- Objectness channel: none
- NMS: external, greedy and class-aware

`Models/yolo11n_320.metadata.json` is the machine-readable source of truth.

## Reproduce and validate

Run `Tools/ObjectDetection/export_yolo11_for_unity.py` in an environment with Ultralytics,
ONNX, and ONNX Runtime. The tool downloads the official weights, exports a static opset-12 graph,
checks the ONNX graph, performs golden-image inference, validates its exact shapes, and requires
correct `person` and `bus` detections before writing the Unity assets.

## Runtime design

`RealtimeCocoObjectDetector` renders AR Foundation's background material into a reusable,
screen-oriented GPU texture. It letterboxes that visible image into a reusable 320×320 sRGB render
texture, converts it into a preallocated float tensor, schedules inference across rendered frames,
requests asynchronous output readback, applies the contract-specific decoder and class-aware NMS,
then emits screen-normalized results.

The camera preview does not wait for inference. A new frame is accepted only when the previous
inference and readback have completed, preventing unbounded queues and stale-frame freezes.

## Licensing

Ultralytics documents YOLO11 under AGPL-3.0 or its Enterprise license. Confirm that the selected
license is suitable before distributing this application.
