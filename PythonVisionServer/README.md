# Step 3 Vision Service

This FastAPI service receives the three Unity workstation camera streams, runs
COCO-pretrained YOLO26n detection, assigns persistent IDs with ByteTrack, and
returns structured results to Unity.

## Run

```bash
cd /Users/simon/Documents/UnityProjects/sortingFactory/PythonVisionServer
source .venv/bin/activate
python3 -m pip install -r requirements.txt
python server.py
```

The model file is `PythonVisionServer/yolo26n.pt`. It is ignored by Git. If it
is missing, Ultralytics downloads it on first startup.

Unity connects to `ws://127.0.0.1:8000/ws/camera`. The v2 frame metadata adds a
normalized workspace ROI. The server crops that ROI before inference, remaps
boxes to the full frame, and filters implausible boxes larger than 70% of the
image area.

## Endpoints

- `GET http://127.0.0.1:8000/health`
- `GET http://127.0.0.1:8000/classes`
- `GET http://127.0.0.1:8000/stats`
- `GET http://127.0.0.1:8000/latest/arm_1_camera`

`/classes` returns all 80 supported COCO labels. `/stats` reports per-camera
frame count, inference time, and detection count.

## Detection Settings

| Setting | Value |
| --- | --- |
| Model | `yolo26n.pt` |
| Dataset | COCO, 80 classes |
| Input size | `640 x 640` |
| Detector confidence floor | `0.10` |
| Track/new-track threshold | `0.45` |
| NMS IoU | `0.70` |
| Tracker | ByteTrack (`sorting_bytetrack.yaml`) |
| Missing-track grace | `300 ms` |
| Device | Auto: CUDA, then Apple MPS, then CPU |

The active sorting targets are restricted to `bottle`, `cup`, `banana`,
`apple`, and `orange`. The model still contains all 80 COCO classes, but other
classes are ignored to prevent conveyor textures and robot parts from producing
irrelevant labels.

Low-confidence detections between `0.10` and `0.45` can maintain an existing
ByteTrack ID but cannot start a noisy new track. When one to three frames are
missed, the service returns a decaying `predicted` box for at most 300 ms.

Override the device when troubleshooting:

```bash
VISION_DEVICE=cpu python server.py
```

## Test

```bash
cd /Users/simon/Documents/UnityProjects/sortingFactory
PYTHONPATH=PythonVisionServer PythonVisionServer/.venv/bin/python \
  -m unittest discover -s PythonVisionServer/tests -v
```
