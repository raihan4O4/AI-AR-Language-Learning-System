#!/usr/bin/env python3
"""Export and verify the exact YOLO11n ONNX artifact used by the Unity project."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import shutil
import tempfile
import urllib.request
from datetime import datetime, timezone
from pathlib import Path

import numpy as np
import onnx
import onnxruntime as ort
from PIL import Image
from ultralytics import YOLO, __version__ as ultralytics_version


MODEL_NAME = "yolo11n"
INPUT_SIZE = 320
ONNX_OPSET = 12
LETTERBOX_VALUE = 114
VALIDATION_IMAGE_URL = "https://ultralytics.com/images/bus.jpg"


def parse_args() -> argparse.Namespace:
    repository_root = Path(__file__).resolve().parents[2]
    detector_root = (
        repository_root
        / "Assets"
        / "_Project"
        / "LanguageLearning"
        / "ObjectDetection"
    )
    parser = argparse.ArgumentParser()
    parser.add_argument(
        "--output",
        type=Path,
        default=detector_root / "Models" / "yolo11n_320.onnx",
    )
    parser.add_argument(
        "--labels",
        type=Path,
        default=detector_root / "Models" / "coco80.txt",
    )
    parser.add_argument(
        "--metadata",
        type=Path,
        default=detector_root / "Models" / "yolo11n_320.metadata.json",
    )
    parser.add_argument(
        "--validation-image",
        type=Path,
        default=detector_root / "Validation" / "bus.jpg",
    )
    return parser.parse_args()


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest().upper()


def inspect_onnx(path: Path) -> tuple[list[dict], list[dict], int]:
    model = onnx.load(path)
    onnx.checker.check_model(model)

    def tensor_contract(value_info: onnx.ValueInfoProto) -> dict:
        tensor_type = value_info.type.tensor_type
        dimensions: list[int | str | None] = []
        for dimension in tensor_type.shape.dim:
            if dimension.HasField("dim_value"):
                dimensions.append(int(dimension.dim_value))
            elif dimension.HasField("dim_param"):
                dimensions.append(dimension.dim_param)
            else:
                dimensions.append(None)
        return {
            "name": value_info.name,
            "element_type": onnx.TensorProto.DataType.Name(tensor_type.elem_type),
            "shape": dimensions,
        }

    inputs = [tensor_contract(value) for value in model.graph.input]
    outputs = [tensor_contract(value) for value in model.graph.output]
    opset = max(import_entry.version for import_entry in model.opset_import)
    return inputs, outputs, opset


def letterbox(image: Image.Image) -> tuple[np.ndarray, dict]:
    rgb = image.convert("RGB")
    source_width, source_height = rgb.size
    scale = min(INPUT_SIZE / source_width, INPUT_SIZE / source_height)
    content_width = max(1, round(source_width * scale))
    content_height = max(1, round(source_height * scale))
    pad_x = (INPUT_SIZE - content_width) // 2
    pad_y = (INPUT_SIZE - content_height) // 2

    resized = rgb.resize((content_width, content_height), Image.Resampling.BILINEAR)
    canvas = Image.new(
        "RGB",
        (INPUT_SIZE, INPUT_SIZE),
        (LETTERBOX_VALUE, LETTERBOX_VALUE, LETTERBOX_VALUE),
    )
    canvas.paste(resized, (pad_x, pad_y))
    tensor = np.asarray(canvas, dtype=np.float32).transpose(2, 0, 1)[None] / 255.0
    transform = {
        "source_width": source_width,
        "source_height": source_height,
        "content_width": content_width,
        "content_height": content_height,
        "pad_x": pad_x,
        "pad_y": pad_y,
        "scale": scale,
    }
    return tensor, transform


def intersection_over_union(box: np.ndarray, other: np.ndarray) -> float:
    x1 = max(float(box[0]), float(other[0]))
    y1 = max(float(box[1]), float(other[1]))
    x2 = min(float(box[2]), float(other[2]))
    y2 = min(float(box[3]), float(other[3]))
    intersection = max(0.0, x2 - x1) * max(0.0, y2 - y1)
    box_area = max(0.0, float(box[2] - box[0])) * max(0.0, float(box[3] - box[1]))
    other_area = max(0.0, float(other[2] - other[0])) * max(
        0.0, float(other[3] - other[1])
    )
    union = box_area + other_area - intersection
    return intersection / union if union > 0.0 else 0.0


def decode_and_nms(
    output: np.ndarray,
    labels: list[str],
    confidence_threshold: float = 0.25,
    iou_threshold: float = 0.45,
) -> list[dict]:
    if output.shape[0] != 1 or output.shape[1] != 4 + len(labels):
        raise ValueError(f"Unexpected YOLO output shape: {output.shape}")

    matrix = output[0]
    class_scores = matrix[4:]
    class_ids = np.argmax(class_scores, axis=0)
    confidences = class_scores[class_ids, np.arange(class_scores.shape[1])]
    candidate_indices = np.flatnonzero(confidences >= confidence_threshold)

    candidates: list[dict] = []
    for index in candidate_indices:
        center_x, center_y, width, height = matrix[:4, index]
        candidates.append(
            {
                "class_id": int(class_ids[index]),
                "confidence": float(confidences[index]),
                "box": np.array(
                    [
                        center_x - width * 0.5,
                        center_y - height * 0.5,
                        center_x + width * 0.5,
                        center_y + height * 0.5,
                    ],
                    dtype=np.float32,
                ),
            }
        )

    candidates.sort(key=lambda item: item["confidence"], reverse=True)
    selected: list[dict] = []
    for candidate in candidates:
        if any(
            candidate["class_id"] == accepted["class_id"]
            and intersection_over_union(candidate["box"], accepted["box"])
            > iou_threshold
            for accepted in selected
        ):
            continue
        selected.append(candidate)
        if len(selected) >= 20:
            break

    return [
        {
            "class_id": item["class_id"],
            "label": labels[item["class_id"]],
            "confidence": round(item["confidence"], 6),
            "model_xyxy": [round(float(value), 3) for value in item["box"]],
        }
        for item in selected
    ]


def export_model(destination: Path) -> tuple[Path, YOLO]:
    destination.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="aiar-yolo11-export-") as temporary:
        temporary_path = Path(temporary)
        previous_directory = Path.cwd()
        try:
            os.chdir(temporary_path)
            model = YOLO(f"{MODEL_NAME}.pt")
            exported = Path(
                model.export(
                    format="onnx",
                    imgsz=INPUT_SIZE,
                    opset=ONNX_OPSET,
                    simplify=True,
                    dynamic=False,
                    nms=False,
                    batch=1,
                    device="cpu",
                )
            )
            shutil.copy2(exported, destination)
        finally:
            os.chdir(previous_directory)
    return destination, model


def main() -> None:
    args = parse_args()
    model_path, model = export_model(args.output.resolve())

    labels = [str(model.names[index]) for index in sorted(model.names)]
    if len(labels) != 80:
        raise ValueError(f"Expected 80 COCO labels, found {len(labels)}")

    args.labels.parent.mkdir(parents=True, exist_ok=True)
    args.labels.write_text("\n".join(labels) + "\n", encoding="utf-8")

    args.validation_image.parent.mkdir(parents=True, exist_ok=True)
    urllib.request.urlretrieve(VALIDATION_IMAGE_URL, args.validation_image)

    inputs, outputs, opset = inspect_onnx(model_path)
    expected_input = [1, 3, INPUT_SIZE, INPUT_SIZE]
    expected_output = [1, 84, 2100]
    if inputs != [
        {"name": "images", "element_type": "FLOAT", "shape": expected_input}
    ]:
        raise ValueError(f"Unexpected ONNX input contract: {inputs}")
    if len(outputs) != 1 or outputs[0]["shape"] != expected_output:
        raise ValueError(f"Unexpected ONNX output contract: {outputs}")

    session = ort.InferenceSession(
        str(model_path),
        providers=["CPUExecutionProvider"],
    )
    tensor, transform = letterbox(Image.open(args.validation_image))
    output = session.run(None, {session.get_inputs()[0].name: tensor})[0]
    detections = decode_and_nms(output, labels)
    detected_labels = {item["label"] for item in detections}
    if "person" not in detected_labels or "bus" not in detected_labels:
        raise ValueError(
            f"Golden-image verification expected person and bus, got {detections}"
        )

    metadata = {
        "schema_version": 1,
        "model": "Ultralytics YOLO11n COCO detection",
        "source_weights": "yolo11n.pt",
        "source_documentation": "https://docs.ultralytics.com/models/yolo11/",
        "license": "AGPL-3.0-or-later or Ultralytics Enterprise",
        "exported_utc": datetime.now(timezone.utc).isoformat(),
        "ultralytics_version": ultralytics_version,
        "onnx_opset": opset,
        "sha256": sha256(model_path),
        "inputs": inputs,
        "outputs": outputs,
        "preprocessing": {
            "color_order": "RGB",
            "tensor_layout": "NCHW",
            "normalization": "float32 pixel / 255.0",
            "resize": "bilinear letterbox",
            "letterbox_value": LETTERBOX_VALUE,
            "input_width": INPUT_SIZE,
            "input_height": INPUT_SIZE,
        },
        "postprocessing": {
            "output_layout": "NCHW-like [batch, 4 + class_count, prediction_count]",
            "box_encoding": "center_x, center_y, width, height in model pixels",
            "class_encoding": "80 independent class scores; no objectness channel",
            "nms_in_model": False,
        },
        "validation": {
            "image_url": VALIDATION_IMAGE_URL,
            "image_sha256": sha256(args.validation_image),
            "letterbox": transform,
            "output_shape": list(output.shape),
            "detections": detections,
        },
    }
    args.metadata.parent.mkdir(parents=True, exist_ok=True)
    args.metadata.write_text(
        json.dumps(metadata, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )

    print(json.dumps(metadata, indent=2))


if __name__ == "__main__":
    main()
