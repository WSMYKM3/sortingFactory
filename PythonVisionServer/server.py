"""Step 2 WebSocket receiver for Unity workstation camera frames."""

from __future__ import annotations

import re
import time
from pathlib import Path
from typing import Any

from fastapi import FastAPI, HTTPException, WebSocket, WebSocketDisconnect
from fastapi.responses import Response

from protocol import FrameProtocolError, decode_frame_packet


APP_DIR = Path(__file__).resolve().parent
FRAME_DIR = APP_DIR / "received_frames"
SAFE_ID = re.compile(r"[^a-zA-Z0-9_-]+")

app = FastAPI(title="Sorting Factory Step 2 Vision Receiver", version="1.0")
camera_stats: dict[str, dict[str, Any]] = {}


def safe_camera_id(camera_id: str) -> str:
    safe_id = SAFE_ID.sub("_", camera_id).strip("_")
    if not safe_id:
        raise FrameProtocolError("camera_id cannot be used as a file name")
    return safe_id


@app.get("/health")
async def health() -> dict[str, Any]:
    return {"status": "ok", "camera_count": len(camera_stats)}


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

                previous = camera_stats.get(camera_id, {})
                frame_count = int(previous.get("frame_count", 0)) + 1
                camera_stats[camera_id] = {
                    "robot_arm_id": robot_arm_id,
                    "frame_count": frame_count,
                    "last_frame_id": metadata["frame_id"],
                    "last_received_at_unix_ms": received_at_ms,
                    "width": metadata["width"],
                    "height": metadata["height"],
                    "jpeg_bytes": len(frame.jpeg),
                }

                await websocket.send_json(
                    {
                        "received": True,
                        "robot_arm_id": robot_arm_id,
                        "camera_id": camera_id,
                        "frame_id": metadata["frame_id"],
                        "server_received_at_unix_ms": received_at_ms,
                    }
                )
            except FrameProtocolError as exc:
                await websocket.send_json({"received": False, "error": str(exc)})
    except WebSocketDisconnect:
        return


if __name__ == "__main__":
    import uvicorn

    uvicorn.run("server:app", host="127.0.0.1", port=8000, reload=False)
