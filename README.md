# Sorting Factory

A Unity-based smart sorting factory simulation with three independent SO-101-style robot workstations. Each station streams its own camera feed to a Python vision server, tracks supported objects, decides whether they can still be picked, and executes a local pick-and-place cycle.

[![Watch the Sorting Factory demo](https://img.youtube.com/vi/sRn4y9llr-s/maxresdefault.jpg)](https://www.youtube.com/watch?v=sRn4y9llr-s)

**[Watch the project demo on YouTube](https://www.youtube.com/watch?v=sRn4y9llr-s)**

## Run The Project

### Requirements

- Unity `6000.3.10f1`
- Python 3 with `venv` and `pip`
- Current setup tested on macOS; the vision server can select Apple MPS, CUDA, or CPU inference

### 1. Start the vision server

From the repository root, create the Python environment and install its dependencies:

```bash
cd PythonVisionServer
python3 -m venv .venv
source .venv/bin/activate
python3 -m pip install -r requirements.txt
python server.py
```

After the first setup, later runs only need:

```bash
cd PythonVisionServer
source .venv/bin/activate
python server.py
```

The first launch downloads `yolo26n.pt` if the weight file is missing. The server automatically selects CUDA, Apple MPS, or CPU.

Keep this Terminal process running while Unity is in Play Mode. The server provides:

- Control Room: [http://127.0.0.1:8000/control-room](http://127.0.0.1:8000/control-room)
- Health API: [http://127.0.0.1:8000/health](http://127.0.0.1:8000/health)
- Camera WebSocket: `ws://127.0.0.1:8000/ws/camera`

### 2. Run the Unity scene

1. Open the project with Unity `6000.3.10f1`.
2. Open `Assets/aSimingSpace/MainSceneCopy2.unity`.
3. Enter Play Mode.
4. Open the Control Room in a browser.
5. Start a session, run the conveyor, enable the required arms, and start their camera streams.

These controls are independent. `Start Session` starts timing, counters, and CSV recording; it does not automatically start the conveyor, arms, or cameras. `Stop Session` blocks new grasp tasks, waits for active arms to return to Idle, and writes the session summary.

### 3. Run a standalone build

`Assets/aSimingSpace/MainSceneCopy2.unity` is already enabled in Unity Build Settings. Build and launch the application normally, then start `PythonVisionServer/server.py` separately on the same computer. The standalone application uses the same localhost server and Control Room URLs.

## What It Does

- Runs a closed-loop conveyor and feeder area with independently released sorting objects.
- Uses three fixed `1280 x 720` workstation cameras streaming JPEG frames at up to `10 FPS`.
- Detects `apple`, `banana`, `orange`, `bottle`, and `cup` with COCO-pretrained YOLO26n.
- Uses ByteTrack to keep stable target IDs through brief detection gaps.
- Projects each workstation workspace into the camera as a region of interest.
- Calculates a Latest Pick Line and skips targets that no longer leave enough time to grasp.
- Drives three segmented SO-101-style robot arms through IK-based pick-and-place cycles.
- Allows downstream arms to detect and recover objects missed by an upstream station.
- Provides a browser-based Control Room for sessions, conveyor speed, arms, camera streams, and live KPIs.
- Records per-arm SO-101 observations, actions, detections, decisions, outcomes, and failure reasons to CSV.
- Includes a looping showcase camera track for presenting the complete production line.

## How It Works

```text
Unity workstation camera
        |
        |  JPEG + arm/camera/frame metadata over WebSocket
        v
FastAPI vision server
        |
        |  YOLO26n detection + per-camera ByteTrack IDs
        v
Unity target evaluation
        |
        |  ROI filtering + Latest Pick Line + time-to-pick decision
        v
SO-101 IK controller
        |
        |  Pick, drop, recovery, and return to Idle
        v
Session CSV + localhost Control Room
```

Each arm makes its own detection and grasp decision. There is no central target queue. A temporary conveyor-object claim prevents two arms from handling the same physical object, while failed or skipped objects remain available for downstream stations.

The current demo objects use a single labeled image on a box as a controllable proxy for real products. This keeps the pretrained detector testable while the physical object and robot assets are being developed.

## Tech Stack

- Unity `6000.3.10f1`
- Universal Render Pipeline
- C# robot, conveyor, camera, decision, and telemetry systems
- Python, FastAPI, and Uvicorn
- Ultralytics YOLO26n with COCO weights
- ByteTrack multi-object tracking
- WebSocket camera protocol and REST control/telemetry APIs

## Recorded Data

Each session creates:

```text
arm_1.csv
arm_2.csv
arm_3.csv
metadata.csv
session_summary.csv
```

The arm files are sampled at `10 Hz` and include:

- SO-101 joint observations and commanded actions
- Workflow and motion state
- Camera frame, target class, confidence, and track IDs
- Target world position and remaining pickable time
- Execute/skip decisions and Latest Pick Line state
- Success/failure outcome, reason, grasp duration, and cycle duration

By default, CSV sessions are written to:

```text
/Users/simon/Documents/WsmFiles/SortingFactoryScreenshots/csvdata
```

This path is currently configured in `So101CsvRecorder.cs` and should be changed when running on another machine.

## Main Project Areas

| Path | Purpose |
| --- | --- |
| `Assets/aSimingSpace/MainSceneCopy2.unity` | Current full demonstration scene |
| `Assets/aSimingSpace/Scripts/Phase1Prototype` | Conveyor, feeder, workstation, and robot setup |
| `Assets/aSimingSpace/Scripts/Step2Vision` | Camera streaming, tracking UI, grasp decisions, IK, sessions, and CSV |
| `Assets/aSimingSpace/Scripts/Showcase` | Automated project showcase camera track |
| `PythonVisionServer` | YOLO, ByteTrack, WebSocket receiver, APIs, and Control Room |

## Tests

Run the Python protocol, ROI, tracking, and Control Room tests from the project root:

```bash
PYTHONPATH=PythonVisionServer PythonVisionServer/.venv/bin/python \
  -m unittest discover -s PythonVisionServer/tests -v
```

For detailed local checks and troubleshooting commands, see `Assets/aSimingSpace/selfTest.md`.
