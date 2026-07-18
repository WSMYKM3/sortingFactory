# Step 2 Vision Receiver

This service validates Unity's workstation camera stream before YOLO and tracking are added in Step 3.

## Run

From `PythonVisionServer`:

```bash
python3 -m venv .venv
source .venv/bin/activate
python3 -m pip install -r requirements.txt
python3 server.py
```

The Unity cameras connect to `ws://127.0.0.1:8000/ws/camera`.

Useful endpoints:

- `GET http://127.0.0.1:8000/health`
- `GET http://127.0.0.1:8000/stats`
- `GET http://127.0.0.1:8000/latest/arm_1_camera`

Each binary WebSocket message contains a four-byte big-endian JSON-header length, the UTF-8 JSON metadata, and one JPEG image. The metadata includes the robot arm ID, camera ID, frame ID, timestamp, and frame dimensions.

## Test

```bash
python3 -m unittest discover -s tests -v
```
