# Mission: Decentralized Multi-Robot Conveyor Picking System

## Project Overview

Build a decentralized multi-robot conveyor picking system in Unity.

Multiple robotic arms are positioned along a continuously moving conveyor belt. Each robotic arm independently observes, evaluates, and handles objects within its own non-overlapping workspace.

Each robotic arm operates as an independent processing unit:

```text
Local camera detection
→ Evaluate whether the object is pickable
→ Select and lock a target
→ Execute the grasp
→ Evaluate the result
→ Wait for the next object
```

Each robotic arm is responsible only for its own workspace.

Robotic arms do not directly share object positions or grasping tasks. If an upstream robotic arm skips or fails to pick an object, the downstream robotic arm must independently detect and localize that object again.

The system does not depend on a shared task queue during the first phase.

---

# Core Design Principles

1. Each robotic arm operates independently.
2. Each robotic arm has its own camera and local workspace.
3. Robotic-arm workspaces must not overlap.
4. Each object is evaluated using current local visual information.
5. Outdated object positions must not be transferred between robotic arms.
6. A downstream robotic arm must independently re-detect any object that reaches its workspace.
7. A lightweight global system may monitor performance but must not assign local grasping tasks.
8. The first phase should prioritize reliable system behavior over machine learning or XR integration.

---

# Step 1: Build the Multi-Robot Conveyor Scene

Create the following elements in Unity:

* A conveyor belt moving at a constant speed.
* Multiple robotic arms positioned along the conveyor belt.
* A separate, non-overlapping workspace(a visual box to debug) for each robotic arm.
* A corresponding drop zone or classification destination for each robotic arm.
* An object-dispersal mechanism at the beginning of the conveyor belt.(randomly spawn 15 objects first)

The object-dispersal mechanism should separate objects that are stacked or positioned too closely together.

Its purpose is to reduce:

* Object overlap.
* Visual occlusion.
* Multiple objects entering the same grasp area at once.
* Unreliable detection caused by stacked objects.

---

# Step 2: Validate the Camera Feed and Vision Input Pipeline

Begin with Robotic Arm 1.

The first objective is to verify that Unity can produce a stable camera stream that can later be processed by the external vision service.

Validate the camera pipeline in the following order:

```text
Capture the live camera feed
→ Trigger a single-frame capture with a debug button
→ Save or display the captured frame
→ Confirm that the detection area and local workspace are visible
→ Confirm that frames can be supplied continuously to the vision service
```

The debug capture button is only an initial validation tool. The final system must process a continuous camera stream.

This step should confirm that:

* The camera feed is stable.
* The conveyor belt and moving objects are clearly visible.
* The camera covers the full local detection area.
* The Latest Pick Line can be placed within the visible region.
* Individual frames can be captured for debugging.
* Continuous frames can be sent outside Unity for model inference.
* Returned vision results can be associated with the correct robotic arm and camera.

During Phase 1, each camera should remain fixed relative to its local workspace so that the visible workspace and Latest Pick Line remain consistent.

After the camera pipeline has been validated on Robotic Arm 1, apply the same camera setup to the remaining robotic arms.

Each camera stream should have a unique identity, such as:

```text
robot_arm_id: arm_1
camera_id: arm_1_camera
```

This allows the vision service to return results to the correct robotic arm.

---

# Step 3: Add Real-Time Object Detection and Tracking

During Phase 1, object detection and tracking will run in a separate Python vision service rather than directly inside Unity C# scripts.

The initial vision pipeline should use:

```text
Unity Camera Stream
→ Python Vision Service
→ YOLO Object Detection
→ Multi-Object Tracking
→ Structured Results Returned to Unity
```

The recommended initial configuration is:

```text
Lightweight YOLO detection model
+
ByteTrack
```

YOLO is responsible for identifying objects in each frame.

The detector should return:

* Object class.
* Detection confidence.
* Bounding-box position.
* Bounding-box dimensions.

ByteTrack is responsible for maintaining the identity of the same object across consecutive video frames.

The tracker should return:

* A persistent tracking ID.
* Whether the object is still being tracked.
* The object’s movement through the local detection area.
* Whether the object is approaching or has crossed the Latest Pick Line.

Example detection result:

```text
robot_arm_id: arm_1
camera_id: arm_1_camera
track_id: 12
class_name: bottle
confidence: 0.91
bbox_center_x: 0.43
bbox_center_y: 0.58
bbox_width: 0.16
bbox_height: 0.30
tracking_status: tracked
```

Each robotic arm should process only results returned for its own camera.

For example:

```text
Arm 1 Camera → Workspace A
Arm 2 Camera → Workspace B
Arm 3 Camera → Workspace C
```

Objects outside a robotic arm’s local detection area should be ignored by that arm.

The first implementation should follow this sequence:

```text
Run detection on a captured test frame
→ Confirm that target objects are recognized
→ Run detection on the continuous camera stream
→ Add persistent tracking IDs
→ Display bounding boxes and labels in Unity
→ Use tracking results for Latest Pick Line evaluation
```

The Unity side is responsible for:

* Producing each camera stream.
* Sending frames to the Python vision service.
* Receiving detection and tracking results.
* Displaying bounding boxes and object information.
* Connecting results to the correct robotic arm.
* Passing tracked-object information to the local decision system.

The Python vision service is responsible for:

* Loading and running the detection model.
* Processing incoming video frames.
* Maintaining tracking IDs across frames.
* Returning structured results for each camera.
* Keeping results separated by robotic-arm and camera ID.

This step is complete when:

1. Objects are detected in the continuous camera stream.
2. Bounding boxes, classes, and confidence values are displayed.
3. Each tracked object receives a persistent ID.
4. The same object keeps its ID while moving through the local workspace.
5. Results are returned to the correct robotic arm.
6. Objects outside the local workspace are ignored.
7. Tracking data can be used by the Latest Pick Line decision system.
8. Multiple camera streams can later be added without changing the local decision logic.

A later deployment phase may export the trained detection model to ONNX and run inference inside Unity. This is not required for Phase 1.


# Step 4: Determine Whether There Is Enough Time to Pick

Display a vertical **Latest Pick Line** in each robotic arm’s camera feed.

The Latest Pick Line represents the latest position at which the robotic arm may begin a new grasping task.

The line must correspond to a fixed physical position on the conveyor belt. It should not exist only as an arbitrary visual overlay.

When an object enters the detection area, the robotic arm should continuously track it and evaluate:

* Whether the robotic arm is currently idle.
* Whether the object has crossed the Latest Pick Line.
* How much time remains before the object leaves the workspace.
* Whether the robotic arm can safely approach, align with, and grip the object before it leaves the reachable area.

The core decision rule is:

```text
Remaining pickable time
≥
Estimated time required to secure the object
+
Safety margin
```

Example:

```text
Complete robotic-arm cycle: 10 seconds
Time required to approach and grip: 4 seconds
Safety margin: 1 second

Minimum required pickable time: 5 seconds
```

Decision:

```text
Remaining time ≥ 5 seconds
→ Execute the grasp

Remaining time < 5 seconds
→ Skip the object
```

The complete robotic-arm cycle and the required grasp time must be treated separately.

The **required grasp time** covers the period from target selection until the object is securely held.

The **complete cycle time** covers:

```text
Target selection
→ Grasping
→ Lifting
→ Placement
→ Returning to the initial position
```

The Latest Pick Line is primarily determined by the time required to secure the object, not necessarily by the complete robotic-arm cycle.

## Latest Pick Line Behavior

If an object crosses the Latest Pick Line before a grasping task has started:

```text
Do not create a new grasping task
→ Skip the object
→ Allow it to continue downstream
```

If the robotic arm has already locked the target and started the grasp:

```text
Continue the active grasp
→ Do not cancel the task only because the object crosses the line
```

The Latest Pick Line determines whether a new grasp may begin. It does not interrupt an active grasp.

## Camera Feed Information

The camera feed may display:

* Object detection bounding box.
* Object class.
* Latest Pick Line.
* Local workspace boundary.
* Current robotic-arm state.
* Currently locked target.
* Estimated remaining pickable time.
* Estimated required grasp time.
* `Execute` or `Skip` decision.

Example:

```text
Object A
Time Remaining: 6.2 seconds
Required Time: 5.0 seconds
Decision: Execute
```

Example:

```text
Object B
Time Remaining: 3.4 seconds
Required Time: 5.0 seconds
Decision: Skip
```

During the first phase:

* Camera positions remain fixed.
* Conveyor speed remains constant.
* Grasp duration uses a predefined value.
* The Latest Pick Line remains fixed.

Dynamic adjustment of the Latest Pick Line may be added later.

---

# Step 5: Lock the Target and Execute the Local Grasping Cycle

When an object satisfies all grasping conditions, the robotic arm should lock it as the current target.

While the target is locked:

* The robotic arm temporarily ignores other objects in its workspace.
* The robotic arm enters a busy state.
* No new target may be selected until the current cycle is complete.

The local state sequence is:

```text
Idle
→ Detecting
→ Evaluating
→ Target Locked
→ Grasping
→ Placing
→ Returning
→ Idle
```

Once the robotic arm has completed the placement operation and returned to its initial position, it becomes idle again and resumes object detection.

If an object does not satisfy the grasping conditions:

```text
Detecting
→ Evaluating
→ Skip
→ Continue waiting for another object
```

Each robotic arm performs this cycle independently.

Multiple robotic arms may operate simultaneously as long as they are handling different objects inside separate workspaces.

---

# Step 6: Handle Failures and Downstream Re-Detection

Each robotic arm should independently determine the result of its grasping attempt.

Possible outcomes include:

* Successful grasp.
* Object missed.
* Object dropped or released.
* Object slipped from the gripper.
* Object moved during the grasp.
* Object left the workspace.
* Target tracking was lost.
* The robotic arm could not complete the grasp.

During the initial Unity prototype, these failure conditions should be simulated randomly to test system recovery behavior.

Failure simulation should:

* Be enabled by default during recovery testing.
* Use an initial failure probability of **15%**.
* Allow the probability to be changed later.
* Allow failure simulation to be turned on or off without changing the normal grasping workflow.

After a failed attempt:

```text
End the current attempt
→ Return the robotic arm to a safe state
→ Discard the previous object position
→ Wait for the next local target
```

The failed object may continue moving along the conveyor belt.

When it reaches the next robotic arm’s workspace, the downstream arm must independently perform:

```text
Detection
→ Localization
→ Remaining-time evaluation
→ New grasp-task generation
```

The downstream robotic arm must not use the previous arm’s stored object position or orientation.

This is necessary because a failed grasp may have:

* Moved the object.
* Rotated the object.
* Dropped the object in a different position.
* Changed the object’s velocity.
* Altered its relationship to the conveyor belt.

Failure reasons may be logged centrally for future analysis or advisory systems. However, during Phase 1, downstream robotic arms must still make their decisions using fresh local observations.

---

# Step 7: Validate Parallel Multi-Robot Operation

Test the following scenarios:

* Different robotic arms grasp different objects at the same time.
* An upstream robotic arm fails and a downstream robotic arm independently detects the object again.
* Multiple objects pass through one workspace in close succession.
* A busy robotic arm skips an object it cannot process in time.
* An object passes through all workspaces without being picked.
* Two robotic arms do not attempt to pick the same object near a workspace boundary.
* A robotic arm completes its current task before selecting another target.
* A skipped object remains available for downstream processing.

The main goal is to confirm that the system behaves correctly without a shared grasp-task queue.

---

# Step 8: Record SO-101-Compatible Data for Each Robotic Arm

Each robotic arm should independently record data in a structure that can later be mapped to an SO-101 and LeRobot-compatible training dataset.

The system must record every grasp attempt, including both successful and failed attempts.

Successful grasp episodes may later be used to train imitation-learning or grasping policies. Failed episodes must also be retained for failure analysis, grasp-success prediction, recovery research, and future policy improvement.

For each robotic arm, the dataset should distinguish between:

* **Robot observation:** the arm’s current simulated joint and gripper state.
* **Robot action:** the joint and gripper target being commanded.
* **Visual observation:** the local workspace camera feed and detection results.
* **Task context:** the object, timing decision, robot state, and grasp result.

The SO-101-compatible robot vector should follow a consistent six-value order:

```text
shoulder_pan
shoulder_lift
elbow_flex
wrist_flex
wrist_roll
gripper
```

For each recorded frame, store:

* Session ID.
* Episode ID.
* Robotic-arm ID.
* Camera ID.
* Timestamp.
* Frame index.
* Current robot observation state.
* Current robot action command.
* Current robotic-arm workflow state.
* Local object detection results.
* Object class.
* Tracking ID.
* Detection confidence.
* Object position inside the local workspace.
* Whether the object crossed the Latest Pick Line.
* Estimated remaining pickable time.
* Estimated required grasp time.
* `Execute` or `Skip` decision.

For each completed grasp episode, also record:

* Whether the attempt succeeded or failed.
* Failure reason, when applicable.
* Time required to secure the object.
* Total task duration.
* Time at which the robotic arm returned to `Idle`.
* Whether the object reached the correct drop zone.

A grasp counts as successful only when:

```text
The object is secured
→ The object is lifted from the conveyor
→ The object reaches the correct drop zone
```

An object that is initially grasped but later dropped or misplaced must be recorded as a failed attempt.

The first training dataset may use successful episodes as the primary demonstrations for learning grasp behavior.

Failed episodes must remain in the raw dataset and may later support:

* Failure-pattern analysis.
* Grasp-success classification.
* Recovery-policy development.
* Dynamic grasp-time estimation.
* Advisory information for downstream robotic arms.

During Phase 1, failure metadata must not replace fresh local visual observations or directly control downstream robotic arms.

---

# Step 9: Build a Localhost Control Room

Create a lightweight HTML control room that runs locally through `localhost`.

The control room should provide a live overview of all robotic arms, camera streams, vision services, grasp decisions, and system-level performance.

Each robotic-arm panel should display:

* Robotic-arm ID.
* Camera-stream status.
* Vision-service connection status.
* Current robot state.
* Current target or `No Target`.
* Current `Execute` or `Skip` decision.
* Successful grasp count.
* Failed grasp count.
* Total grasp-attempt count.
* Skip count.
* Success rate.
* Average grasp time.
* Average complete-cycle time.
* Objects processed per minute.
* Robotic-arm utilization.
* Most recent failure reason.

Use clear status icons:

```text
✓ Streaming / Connected / Active
✕ Offline / Disconnected / Inactive
```

The success rate should be calculated as:

```text
Successful grasp count
÷
Total grasp-attempt count
×
100
```

Skipped objects must not be counted as grasp attempts because the robotic arm did not begin a grasping action.

The global section of the control room should display:

* Number of active camera streams.
* Number of connected vision services.
* Number of active robotic arms.
* Total successful grasps.
* Total grasp attempts.
* Overall success rate.
* Total skipped objects.
* Total failed attempts.
* Failure counts by category.
* Objects not processed by any robotic arm.
* Overall conveyor throughput.
* Average task duration.
* Current session duration.
* Failure-simulation status.
* Current failure probability.

The control room should also make it possible to observe comparisons such as:

* Success rate by robotic arm.
* Workload distribution between robotic arms.
* Execute-versus-Skip counts.
* Upstream failures recovered by downstream robotic arms.
* Objects that passed through the complete system without being picked.
* Changes in throughput and success rate across sessions.

The control room must remain a monitoring and evaluation interface.

It must not:

* Assign grasping tasks.
* Lock local targets.
* Transfer object positions between robotic arms.
* Replace local decision making.
* Directly control local grasping sequences.

---

# Step 10: Add Session and Failure-Simulation Controls

Add a session-control layer for repeatable testing and dataset collection.

A session begins when the user selects:

```text
Start Session
```

A session ends when the user selects:

```text
Stop Session
```

When a session starts:

* Create a new unique Session ID.
* Reset session-level counters.
* Start recording all robotic-arm observations, actions, decisions, and results.
* Start the session timer.
* Record the current conveyor and failure-simulation settings.

When a session ends:

* Stop adding new records.
* Finalize all active episodes.
* Save the session data.
* Generate a session summary.
* Keep the control-room results available for review.

Failure simulation should include:

* An `On / Off` control.
* A configurable failure-probability value.
* A default failure probability of **15%**.
* A visible status in the control room.

Random failures should be applied only after a robotic arm has committed to a grasp attempt.

A simulated failure must still follow the normal recovery workflow:

```text
Random failure occurs
→ Record the failure category
→ End the local attempt
→ Return the arm to a safe state
→ Allow the object to continue downstream when applicable
→ Let the next robotic arm independently re-detect it
```

Turning failure simulation off should restore normal grasp execution without changing the rest of the system.

---

# Step 11: Optional Reproducible Demo Scenario

This step is optional and is not required for the core implementation.

A predefined demo sequence may be added later to make the final presentation easier to understand and reproduce.

An example scenario may include:

```text
Object A is detected and successfully picked by Arm 1
        ↓
Object B is skipped by Arm 1 because insufficient time remains
        ↓
Arm 2 independently detects and picks Object B
        ↓
A random or predefined failure occurs during another grasp
        ↓
The object continues downstream
        ↓
A later arm independently re-detects and recovers the object
        ↓
The control room displays the final session results
```

The optional demo must use the same normal detection, decision, recovery, logging, and monitoring systems as the standard runtime.

It should not depend on a separate artificial workflow that bypasses the real system.

---

# Step 12: Export a Session-Based Dataset

Each completed session should be saved inside a folder named with a unique timestamp.

Example:

```text
sessions/
└── 2026-07-19_14-32-08/
    ├── session_summary.csv
    ├── arm_1.csv
    ├── arm_2.csv
    ├── arm_3.csv
    └── metadata.csv
```

Each robotic-arm CSV should contain the frame-level and episode-level data collected in Step 8.

The session folder should preserve:

* Session ID.
* Session start and end time.
* Conveyor settings.
* Failure-simulation status.
* Failure probability.
* Robotic-arm configuration.
* Camera and vision-service status.
* Per-frame robot observations.
* Per-frame robot actions.
* Detection and tracking results.
* Execute and Skip decisions.
* Episode boundaries.
* Success and failure labels.
* Failure categories.
* Task timing.
* Drop-zone results.

The session summary should include:

* Total objects introduced.
* Total grasp attempts.
* Successful grasps.
* Failed grasps.
* Skipped objects.
* Objects missed by the full system.
* Overall success rate.
* Per-arm success rate.
* Average grasp time.
* Average complete-cycle time.
* Overall throughput.
* Downstream recovery count.

The raw dataset should retain both successful and failed episodes.

For the first SO-101 training experiments:

* Successful episodes may be selected as demonstrations.
* Failed episodes should remain available for analysis and later training tasks.
* The observation and action columns must preserve the same variable order across all sessions and robotic arms.
* Session and Episode IDs must make it possible to separate continuous recordings into individual grasp attempts.

The initial Phase 1 export may use CSV files.

A later conversion stage may transform these session folders into a full LeRobot dataset containing structured metadata, data files, and synchronized videos.

---

# Step 13: Add Data-Driven Grasp-Time Estimation

After enough session data has been collected, replace the fixed grasp-time estimate with a data-driven estimate.

During Phase 1, the Latest Pick Line uses a predefined required-grasp time.

For example:

```text
Required grasp time: 4 seconds
Safety margin: 1 second
```

The future estimator should learn from recorded successful and failed episodes.

Possible inputs include:

* Object class.
* Object position inside the workspace.
* Object distance from the robotic arm.
* Robotic-arm starting state.
* Current end-effector position.
* Grasp strategy.
* Detection confidence.
* Conveyor speed.
* Historical grasp duration for similar attempts.
* Recent performance of the same robotic arm.

The output should be:

```text
Estimated time required to secure the object
```

The decision system can then calculate:

```text
Remaining pickable time
≥
Predicted required grasp time
+
Safety margin
```

This allows the Latest Pick Line to become dynamic.

For example:

```text
Bottle:
Predicted grasp time = 3.7 seconds

Box:
Predicted grasp time = 4.6 seconds

Difficult object orientation:
Predicted grasp time = 5.3 seconds
```

The dynamic Latest Pick Line may move earlier or later according to the predicted grasp time.

The first evaluation should compare:

* Fixed grasp-time estimation.
* Data-driven grasp-time estimation.

Measure:

* Grasp success rate.
* Late-grasp failure rate.
* Unnecessary Skip rate.
* Overall throughput.
* Objects recovered downstream.
* Prediction error between estimated and actual grasp time.

This step should be implemented only after the fixed-line system, dataset recording, and control-room monitoring are stable.

---

# Final System Workflow

```text
Objects pass through the entry dispersal mechanism
        ↓
Objects enter a robotic arm’s local detection area
        ↓
The local camera stream is processed by the vision service
        ↓
The robotic arm detects and tracks the object
        ↓
Evaluate whether enough pickable time remains
        ↓
Enough time:
Lock and execute the grasp

Not enough time:
Skip the object
        ↓
The grasp succeeds:
Remove and place the object in the correct drop zone
Record a successful episode

The grasp fails:
Record a failed episode
Return the arm to a safe state
Allow the object to continue downstream when applicable

The object is skipped:
Record the decision
Allow the object to continue downstream
        ↓
A downstream robotic arm independently re-detects and re-localizes the object
        ↓
Repeat the same local workflow
        ↓
Save all session data to a timestamped dataset folder
        ↓
Display live and final results in the localhost control room
```

---

# Phase 1 Completion Criteria

Phase 1 is complete when the system can:

1. Separate and introduce objects onto the conveyor belt.
2. Move objects along a constant-speed conveyor.
3. Allow each robotic arm to independently observe its own non-overlapping workspace.
4. Capture and process each workspace camera stream.
5. Detect and track objects inside each local area.
6. Display a Latest Pick Line in each camera feed.
7. Determine whether enough time remains to complete a grasp.
8. Lock and grasp objects that satisfy local conditions.
9. Skip objects that cannot be processed in time.
10. Prevent overlapping workspace and grasp conflicts.
11. Allow multiple robotic arms to operate simultaneously.
12. Simulate random grasp failures with a default 15% probability.
13. Allow failure simulation to be enabled, disabled, and adjusted.
14. Recover safely from simulated failures.
15. Allow downstream robotic arms to independently re-detect failed or skipped objects.
16. Record SO-101-compatible robot observations and actions for every attempt.
17. Retain both successful and failed episodes.
18. Define success as secure grasp, conveyor removal, and correct placement.
19. Start and stop recording through explicit sessions.
20. Save each session to a timestamped folder containing per-arm CSV files and a summary.
21. Display per-arm and system-wide status through a localhost HTML control room.
22. Calculate success rate from successful grasps divided by total grasp attempts.
23. Display streaming, connection, failure-simulation, throughput, timing, and utilization status.

The optional reproducible demo scenario and data-driven grasp-time estimator are not required to complete Phase 1.

---

# Out of Scope for Phase 1

The following features should not be required for the first implementation phase:

* XR or VR teleoperation.
* Shared grasp-task assignment.
* Real-time transfer of object positions between robotic arms.
* Learned full grasp policies.
* VLA models.
* Dynamic conveyor speed.
* Data-driven dynamic Latest Pick Line adjustment.
* Real robotic-arm hardware integration.
* Cross-robot trajectory coordination.
* Automatic downstream behavior based only on upstream failure metadata.
* Full LeRobot dataset conversion.
* The optional predefined demonstration scenario.

These features may be considered after the decentralized Unity system, session recording, and control-room monitoring are stable.

---

# Future Extensions

Possible later extensions include:

* Data-driven grasp-time estimation.
* Dynamic Latest Pick Line placement.
* Variable conveyor speed.
* Object-specific grasp-time estimation.
* Grasp-success prediction.
* Learned grasp-position correction.
* Full LeRobot dataset conversion.
* SO-101 hardware integration.
* Training policies from successful simulated grasp episodes.
* Using failed episodes for failure prediction and recovery learning.
* Synchronized camera-video recording.
* XR-based human correction.
* Failure-aware advisory information for downstream robotic arms.
* More advanced system-wide performance optimization.

---

# Project Positioning

> A decentralized multi-robot conveyor picking and data-collection system in which each robotic arm independently detects, evaluates, and handles objects within a non-overlapping local workspace.

The system does not rely on a shared grasp-task queue.

Each robotic arm makes local decisions based on current visual observations, remaining pickable time, and its own operational state.

The system records SO-101-compatible observations, actions, and outcomes across timestamped sessions so that successful grasp demonstrations can later support robot-policy training, while failed attempts remain available for analysis and recovery research.

This architecture reduces dependency between robotic arms, prevents outdated object-state information from propagating through the system, supports scalable monitoring and dataset creation, and improves fault tolerance and maintainability.
