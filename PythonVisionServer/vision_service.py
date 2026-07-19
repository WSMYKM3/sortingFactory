"""YOLO26n detection and per-camera ByteTrack state for Step 3."""

from __future__ import annotations

import os
import threading
import time
from math import ceil, floor
from dataclasses import dataclass
from pathlib import Path
from typing import Any

import cv2
import numpy as np


MODEL_NAME = "yolo26n.pt"
TRACKER_NAME = "ByteTrack"
TRACKER_CONFIG = "sorting_bytetrack.yaml"
DETECTOR_CONFIDENCE_FLOOR = 0.10
TRACK_HIGH_THRESHOLD = 0.45
IOU_THRESHOLD = 0.7
INFERENCE_SIZE = 640
MAX_NORMALIZED_BOX_AREA = 0.7
PREDICTION_GRACE_SECONDS = 0.30
TARGET_CLASS_IDS = (39, 41, 46, 47, 49)

COCO_CLASS_NAMES = (
    "person", "bicycle", "car", "motorcycle", "airplane", "bus", "train", "truck",
    "boat", "traffic light", "fire hydrant", "stop sign", "parking meter", "bench",
    "bird", "cat", "dog", "horse", "sheep", "cow", "elephant", "bear", "zebra",
    "giraffe", "backpack", "umbrella", "handbag", "tie", "suitcase", "frisbee",
    "skis", "snowboard", "sports ball", "kite", "baseball bat", "baseball glove",
    "skateboard", "surfboard", "tennis racket", "bottle", "wine glass", "cup", "fork",
    "knife", "spoon", "bowl", "banana", "apple", "sandwich", "orange", "broccoli",
    "carrot", "hot dog", "pizza", "donut", "cake", "chair", "couch", "potted plant",
    "bed", "dining table", "toilet", "tv", "laptop", "mouse", "remote", "keyboard",
    "cell phone", "microwave", "oven", "toaster", "sink", "refrigerator", "book",
    "clock", "vase", "scissors", "teddy bear", "hair drier", "toothbrush",
)


class VisionProcessingError(RuntimeError):
    """Raised when a camera frame cannot be decoded or inferred."""


@dataclass(frozen=True)
class NormalizedRoi:
    x_min: float
    y_min: float
    x_max: float
    y_max: float

    def as_dict(self) -> dict[str, float]:
        return {
            "x_min": self.x_min,
            "y_min": self.y_min,
            "x_max": self.x_max,
            "y_max": self.y_max,
        }


@dataclass(frozen=True)
class PixelCrop:
    x_min: int
    y_min: int
    x_max: int
    y_max: int
    image_width: int
    image_height: int

    @property
    def normalized_x_min(self) -> float:
        return self.x_min / self.image_width

    @property
    def normalized_y_min(self) -> float:
        return self.y_min / self.image_height

    @property
    def normalized_width(self) -> float:
        return (self.x_max - self.x_min) / self.image_width

    @property
    def normalized_height(self) -> float:
        return (self.y_max - self.y_min) / self.image_height


@dataclass
class TrackMemory:
    detection: dict[str, Any]
    last_seen_at: float
    velocity_x: float = 0.0
    velocity_y: float = 0.0
    velocity_width: float = 0.0
    velocity_height: float = 0.0
    observation_count: int = 1


def roi_from_metadata(metadata: dict[str, Any]) -> NormalizedRoi:
    return NormalizedRoi(
        x_min=float(metadata["roi_x_min"]),
        y_min=float(metadata["roi_y_min"]),
        x_max=float(metadata["roi_x_max"]),
        y_max=float(metadata["roi_y_max"]),
    )


def center_is_inside_roi(center_x: float, center_y: float, roi: NormalizedRoi) -> bool:
    return roi.x_min <= center_x <= roi.x_max and roi.y_min <= center_y <= roi.y_max


def detection_is_valid_for_workspace(
    center_x: float,
    center_y: float,
    width: float,
    height: float,
    roi: NormalizedRoi,
) -> bool:
    return (
        center_is_inside_roi(center_x, center_y, roi)
        and width * height <= MAX_NORMALIZED_BOX_AREA
    )


def pixel_crop_for_roi(image_width: int, image_height: int, roi: NormalizedRoi) -> PixelCrop:
    x_min = max(0, min(image_width - 1, floor(roi.x_min * image_width)))
    y_min = max(0, min(image_height - 1, floor(roi.y_min * image_height)))
    x_max = max(x_min + 1, min(image_width, ceil(roi.x_max * image_width)))
    y_max = max(y_min + 1, min(image_height, ceil(roi.y_max * image_height)))
    return PixelCrop(x_min, y_min, x_max, y_max, image_width, image_height)


def remap_box_from_crop(
    center_x: float,
    center_y: float,
    width: float,
    height: float,
    crop: PixelCrop,
) -> tuple[float, float, float, float]:
    return (
        crop.normalized_x_min + center_x * crop.normalized_width,
        crop.normalized_y_min + center_y * crop.normalized_height,
        width * crop.normalized_width,
        height * crop.normalized_height,
    )


class TrackContinuityCache:
    def __init__(self, grace_seconds: float = PREDICTION_GRACE_SECONDS) -> None:
        self.grace_seconds = grace_seconds
        self._tracks: dict[int, TrackMemory] = {}

    def update(
        self,
        detections: list[dict[str, Any]],
        roi: NormalizedRoi,
        now: float,
    ) -> list[dict[str, Any]]:
        output = list(detections)
        observed_track_ids: set[int] = set()

        for detection in detections:
            track_id = int(detection["track_id"])
            detection["prediction_age_ms"] = 0.0
            if track_id < 0:
                continue

            observed_track_ids.add(track_id)
            previous = self._tracks.get(track_id)
            if previous is None:
                self._tracks[track_id] = TrackMemory(dict(detection), now)
                continue

            elapsed = max(0.001, now - previous.last_seen_at)
            raw_velocity = (
                (detection["bbox_center_x"] - previous.detection["bbox_center_x"]) / elapsed,
                (detection["bbox_center_y"] - previous.detection["bbox_center_y"]) / elapsed,
                (detection["bbox_width"] - previous.detection["bbox_width"]) / elapsed,
                (detection["bbox_height"] - previous.detection["bbox_height"]) / elapsed,
            )
            velocity_blend = 1.0 if previous.observation_count == 1 else 0.45
            previous.velocity_x += (raw_velocity[0] - previous.velocity_x) * velocity_blend
            previous.velocity_y += (raw_velocity[1] - previous.velocity_y) * velocity_blend
            previous.velocity_width += (raw_velocity[2] - previous.velocity_width) * velocity_blend
            previous.velocity_height += (raw_velocity[3] - previous.velocity_height) * velocity_blend
            previous.detection = dict(detection)
            previous.last_seen_at = now
            previous.observation_count += 1

        expired_track_ids: list[int] = []
        for track_id, memory in self._tracks.items():
            if track_id in observed_track_ids:
                continue

            age = now - memory.last_seen_at
            if age > self.grace_seconds:
                expired_track_ids.append(track_id)
                continue

            predicted = dict(memory.detection)
            predicted["bbox_center_x"] = max(
                0.0,
                min(1.0, memory.detection["bbox_center_x"] + memory.velocity_x * age),
            )
            predicted["bbox_center_y"] = max(
                0.0,
                min(1.0, memory.detection["bbox_center_y"] + memory.velocity_y * age),
            )
            predicted["bbox_width"] = max(
                0.001,
                memory.detection["bbox_width"] + memory.velocity_width * age,
            )
            predicted["bbox_height"] = max(
                0.001,
                memory.detection["bbox_height"] + memory.velocity_height * age,
            )
            confidence_scale = max(0.2, 1.0 - age / self.grace_seconds)
            predicted["confidence"] = round(
                memory.detection["confidence"] * confidence_scale,
                6,
            )
            predicted["tracking_status"] = "predicted"
            predicted["prediction_age_ms"] = round(age * 1000.0, 3)
            if detection_is_valid_for_workspace(
                predicted["bbox_center_x"],
                predicted["bbox_center_y"],
                predicted["bbox_width"],
                predicted["bbox_height"],
                roi,
            ):
                output.append(predicted)

        for track_id in expired_track_ids:
            del self._tracks[track_id]

        output.sort(key=lambda detection: (detection["track_id"] < 0, detection["track_id"]))
        return output


def select_device() -> str:
    override = os.environ.get("VISION_DEVICE")
    if override:
        return override

    try:
        import torch

        if torch.cuda.is_available():
            return "cuda:0"
        if torch.backends.mps.is_available():
            return "mps"
    except (ImportError, AttributeError):
        pass

    return "cpu"


class CameraVisionPipeline:
    def __init__(self, model_path: Path, tracker_config_path: Path, device: str) -> None:
        self.model_path = model_path
        self.tracker_config_path = tracker_config_path
        self.device = device
        self.model = None
        self.continuity_cache = TrackContinuityCache()

    @property
    def is_loaded(self) -> bool:
        return self.model is not None

    def load(self) -> None:
        if self.model is not None:
            return

        from ultralytics import YOLO

        self.model = YOLO(str(self.model_path))

    def process(self, jpeg: bytes, roi: NormalizedRoi) -> tuple[list[dict[str, Any]], float]:
        self.load()
        image_array = np.frombuffer(jpeg, dtype=np.uint8)
        image = cv2.imdecode(image_array, cv2.IMREAD_COLOR)
        if image is None:
            raise VisionProcessingError("OpenCV could not decode the JPEG frame")

        image_height, image_width = image.shape[:2]
        crop = pixel_crop_for_roi(image_width, image_height, roi)
        inference_image = image[crop.y_min:crop.y_max, crop.x_min:crop.x_max]

        started = time.perf_counter()
        result = self.model.track(
            source=inference_image,
            persist=True,
            tracker=str(self.tracker_config_path),
            conf=DETECTOR_CONFIDENCE_FLOOR,
            iou=IOU_THRESHOLD,
            imgsz=INFERENCE_SIZE,
            classes=list(TARGET_CLASS_IDS),
            device=self.device,
            verbose=False,
        )[0]
        elapsed_ms = (time.perf_counter() - started) * 1000.0

        boxes = result.boxes
        if boxes is None or len(boxes) == 0:
            predicted = self.continuity_cache.update([], roi, time.monotonic())
            return predicted, elapsed_ms

        normalized_boxes = boxes.xywhn.cpu().tolist()
        class_ids = boxes.cls.int().cpu().tolist()
        confidences = boxes.conf.cpu().tolist()
        track_ids = boxes.id.int().cpu().tolist() if boxes.id is not None else [-1] * len(boxes)
        detections: list[dict[str, Any]] = []

        for normalized_box, class_id, confidence, track_id in zip(
            normalized_boxes,
            class_ids,
            confidences,
            track_ids,
        ):
            crop_center_x, crop_center_y, crop_width, crop_height = (
                float(value) for value in normalized_box
            )
            center_x, center_y, width, height = remap_box_from_crop(
                crop_center_x,
                crop_center_y,
                crop_width,
                crop_height,
                crop,
            )
            if not detection_is_valid_for_workspace(
                center_x,
                center_y,
                width,
                height,
                roi,
            ):
                continue

            class_name = str(result.names.get(class_id, f"class_{class_id}"))
            detections.append(
                {
                    "track_id": int(track_id),
                    "class_id": int(class_id),
                    "class_name": class_name,
                    "confidence": round(float(confidence), 6),
                    "bbox_center_x": round(center_x, 6),
                    "bbox_center_y": round(center_y, 6),
                    "bbox_width": round(width, 6),
                    "bbox_height": round(height, 6),
                    "tracking_status": "tracked" if track_id >= 0 else "detected",
                }
            )

        detections = self.continuity_cache.update(detections, roi, time.monotonic())
        return detections, elapsed_ms


class VisionService:
    def __init__(self, app_dir: Path) -> None:
        cache_dir = app_dir / ".cache"
        cache_dir.mkdir(parents=True, exist_ok=True)
        os.environ.setdefault("MPLCONFIGDIR", str(cache_dir / "matplotlib"))
        os.environ.setdefault("XDG_CACHE_HOME", str(cache_dir))
        self.model_path = app_dir / MODEL_NAME
        self.tracker_config_path = app_dir / TRACKER_CONFIG
        self.device = select_device()
        self._pipelines: dict[str, CameraVisionPipeline] = {}
        self._pipelines_lock = threading.Lock()
        self._inference_lock = threading.Lock()

    def preload(self, camera_ids: tuple[str, ...]) -> None:
        for camera_id in camera_ids:
            self._pipeline_for(camera_id).load()

    def loaded_camera_ids(self) -> list[str]:
        with self._pipelines_lock:
            return sorted(
                camera_id
                for camera_id, pipeline in self._pipelines.items()
                if pipeline.is_loaded
            )

    def process_frame(
        self,
        camera_id: str,
        jpeg: bytes,
        metadata: dict[str, Any],
    ) -> tuple[list[dict[str, Any]], float, NormalizedRoi]:
        roi = roi_from_metadata(metadata)
        pipeline = self._pipeline_for(camera_id)
        with self._inference_lock:
            detections, inference_ms = pipeline.process(jpeg, roi)
        return detections, inference_ms, roi

    def _pipeline_for(self, camera_id: str) -> CameraVisionPipeline:
        with self._pipelines_lock:
            pipeline = self._pipelines.get(camera_id)
            if pipeline is None:
                pipeline = CameraVisionPipeline(
                    self.model_path,
                    self.tracker_config_path,
                    self.device,
                )
                self._pipelines[camera_id] = pipeline
            return pipeline
