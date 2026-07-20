"""Step 3 YOLO26n and ByteTrack service for Unity workstation cameras."""

from __future__ import annotations

import asyncio
import re
import time
from contextlib import asynccontextmanager
from pathlib import Path
from typing import Any

from fastapi import Body, FastAPI, HTTPException, WebSocket, WebSocketDisconnect
from fastapi.responses import FileResponse, RedirectResponse, Response
from fastapi.staticfiles import StaticFiles

from control_room_state import ControlRoomState
from protocol import FrameProtocolError, decode_frame_packet
from vision_service import (
    COCO_CLASS_NAMES,
    DETECTOR_CONFIDENCE_FLOOR,
    INFERENCE_SIZE,
    IOU_THRESHOLD,
    MAX_NORMALIZED_BOX_AREA,
    MODEL_NAME,
    PREDICTION_GRACE_SECONDS,
    TARGET_CLASS_IDS,
    TRACK_HIGH_THRESHOLD,
    TRACKER_NAME,
    VisionProcessingError,
    VisionService,
)


APP_DIR = Path(__file__).resolve().parent
FRAME_DIR = APP_DIR / "received_frames"
SAFE_ID = re.compile(r"[^a-zA-Z0-9_-]+")
DEFAULT_CAMERA_IDS = ("arm_1_camera", "arm_2_camera", "arm_3_camera")
vision_service = VisionService(APP_DIR)
control_room_state = ControlRoomState()


@asynccontextmanager
async def lifespan(_: FastAPI):
    await asyncio.to_thread(vision_service.preload, DEFAULT_CAMERA_IDS)
    yield

app = FastAPI(
    title="Sorting Factory Step 3 Vision Service",
    version="2.0",
    lifespan=lifespan,
)
camera_stats: dict[str, dict[str, Any]] = {}
CONTROL_ROOM_DIR = APP_DIR / "control_room"
app.mount(
    "/control-room-assets",
    StaticFiles(directory=CONTROL_ROOM_DIR),
    name="control-room-assets",
)


def safe_camera_id(camera_id: str) -> str:
    safe_id = SAFE_ID.sub("_", camera_id).strip("_")
    if not safe_id:
        raise FrameProtocolError("camera_id cannot be used as a file name")
    return safe_id


@app.get("/", include_in_schema=False)
async def root() -> RedirectResponse:
    return RedirectResponse(url="/control-room")


@app.get("/control-room", include_in_schema=False)
async def control_room() -> FileResponse:
    return FileResponse(CONTROL_ROOM_DIR / "index.html")


@app.get("/api/control")
async def get_control_state() -> dict[str, Any]:
    return control_room_state.controls()


@app.post("/api/control")
async def update_control_state(
    command: dict[str, Any] = Body(...),
) -> dict[str, Any]:
    try:
        return control_room_state.apply(command)
    except (TypeError, ValueError) as exc:
        raise HTTPException(status_code=400, detail=str(exc)) from exc


@app.post("/api/telemetry")
async def update_unity_telemetry(
    telemetry: dict[str, Any] = Body(...),
) -> dict[str, bool]:
    control_room_state.update_telemetry(telemetry)
    return {"received": True}


@app.get("/api/control-room")
async def control_room_dashboard() -> dict[str, Any]:
    return control_room_state.dashboard(camera_stats)


@app.get("/health")
async def health() -> dict[str, Any]:
    return {
        "status": "ok",
        "model": MODEL_NAME,
        "tracker": TRACKER_NAME,
        "device": vision_service.device,
        "inference_size": INFERENCE_SIZE,
        "detector_confidence_floor": DETECTOR_CONFIDENCE_FLOOR,
        "track_high_threshold": TRACK_HIGH_THRESHOLD,
        "iou_threshold": IOU_THRESHOLD,
        "max_normalized_box_area": MAX_NORMALIZED_BOX_AREA,
        "prediction_grace_ms": int(PREDICTION_GRACE_SECONDS * 1000),
        "roi_crop_enabled": True,
        "active_classes": [COCO_CLASS_NAMES[class_id] for class_id in TARGET_CLASS_IDS],
        "loaded_camera_ids": vision_service.loaded_camera_ids(),
        "camera_count": len(camera_stats),
    }


@app.get("/classes")
async def classes() -> dict[str, Any]:
    return {
        "model": MODEL_NAME,
        "dataset": "COCO",
        "class_count": len(COCO_CLASS_NAMES),
        "classes": list(COCO_CLASS_NAMES),
        "active_classes": [COCO_CLASS_NAMES[class_id] for class_id in TARGET_CLASS_IDS],
    }


@app.get("/stats")
async def stats() -> dict[str, Any]:
    return {"cameras": camera_stats}


@app.get("/latest/{camera_id}")
async def latest_frame(camera_id: str) -> Response:
    frame_path = FRAME_DIR / f"{safe_camera_id(camera_id)}_latest.jpg"
    if not frame_path.exists():
        raise HTTPException(status_code=404, detail="No frame received for this camera")
    return Response(content=frame_path.read_bytes(), media_type="image/jpeg")


@app.websocket("/ws/camera")
async def camera_stream(websocket: WebSocket) -> None:
    await websocket.accept()
    try:
        while True:
            raw_packet = await websocket.receive_bytes()
            received_at_ms = int(time.time() * 1000)
            try:
                frame = decode_frame_packet(raw_packet)
                metadata = frame.metadata
                camera_id = str(metadata["camera_id"])
                robot_arm_id = str(metadata["robot_arm_id"])

                FRAME_DIR.mkdir(parents=True, exist_ok=True)
                frame_path = FRAME_DIR / f"{safe_camera_id(camera_id)}_latest.jpg"
                frame_path.write_bytes(frame.jpeg)

                detections, inference_ms, roi = await asyncio.to_thread(
                    vision_service.process_frame,
                    camera_id,
                    frame.jpeg,
                    metadata,
                )

                previous = camera_stats.get(camera_id, {})
                frame_count = int(previous.get("frame_count", 0)) + 1
                previous_received_at_ms = int(
                    previous.get("last_received_at_unix_ms", received_at_ms)
                )
                frame_interval_ms = received_at_ms - previous_received_at_ms
                previous_fps = float(previous.get("effective_fps", 0.0))
                if frame_interval_ms <= 0 or frame_interval_ms > 1000:
                    effective_fps = 0.0
                else:
                    instant_fps = 1000.0 / frame_interval_ms
                    effective_fps = (
                        instant_fps
                        if previous_fps <= 0.0
                        else previous_fps * 0.8 + instant_fps * 0.2
                    )
                tracked_count = sum(
                    detection["tracking_status"] != "predicted"
                    for detection in detections
                )
                predicted_count = sum(
                    detection["tracking_status"] == "predicted"
                    for detection in detections
                )
                camera_stats[camera_id] = {
                    "robot_arm_id": robot_arm_id,
                    "frame_count": frame_count,
                    "last_frame_id": metadata["frame_id"],
                    "last_received_at_unix_ms": received_at_ms,
                    "width": metadata["width"],
                    "height": metadata["height"],
                    "jpeg_bytes": len(frame.jpeg),
                    "model": MODEL_NAME,
                    "tracker": TRACKER_NAME,
                    "detection_count": len(detections),
                    "tracked_count": tracked_count,
                    "predicted_count": predicted_count,
                    "effective_fps": round(effective_fps, 3),
                    "inference_ms": round(inference_ms, 3),
                }

                await websocket.send_json(
                    {
                        "protocol_version": 2,
                        "received": True,
                        "robot_arm_id": robot_arm_id,
                        "camera_id": camera_id,
                        "frame_id": metadata["frame_id"],
                        "server_received_at_unix_ms": received_at_ms,
                        "model_name": MODEL_NAME,
                        "tracker_name": TRACKER_NAME,
                        "inference_ms": round(inference_ms, 3),
                        "effective_fps": round(effective_fps, 3),
                        "tracked_count": tracked_count,
                        "predicted_count": predicted_count,
                        "roi": roi.as_dict(),
                        "detections": detections,
                    }
                )
            except FrameProtocolError as exc:
                await websocket.send_json({"received": False, "error": str(exc)})
            except VisionProcessingError as exc:
                await websocket.send_json({"received": False, "error": str(exc)})
            except Exception as exc:
                print(f"Vision processing failed: {exc}")
                await websocket.send_json({"received": False, "error": "vision processing failed"})
    except WebSocketDisconnect:
        return


if __name__ == "__main__":
    import uvicorn

    uvicorn.run("server:app", host="127.0.0.1", port=8000, reload=False)
