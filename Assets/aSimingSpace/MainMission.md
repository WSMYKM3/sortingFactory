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

During the initial Unity prototype, these failure conditions may be simulated randomly to test system recovery behavior.

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

# Step 8: Record Data for Each Robotic Arm

Each robotic arm should independently record:

* Local detection results.
* Object class.
* Object position inside the local workspace.
* The time at which the object entered the workspace.
* Whether the object crossed the Latest Pick Line.
* Estimated remaining pickable time.
* Estimated required grasp time.
* `Execute` or `Skip` decision.
* Grasp action.
* Grasp success or failure.
* Failure reason.
* Total task duration.
* Time at which the robotic arm returned to the idle state.

Failure reasons should be retained for:

* Performance analysis.
* Identifying repeated failure patterns.
* Future grasp-policy improvement.
* Future advisory information for downstream robotic arms.

During Phase 1, these failure records should not replace downstream visual detection or directly control another robotic arm.

The data from all robotic arms should eventually be aggregated to evaluate:

* Individual robotic-arm performance.
* Comparative performance between robotic arms.
* Overall system throughput.
* Common failure conditions.
* Missed-object rates.

The recorded variables should later be designed so they can be mapped to a LeRobot-compatible dataset structure.

---

# Step 9: Add Lightweight Global Monitoring

The first phase does not require a shared task queue.

However, a lightweight global monitoring system should collect system-wide status and performance information.

The global monitoring system should record:

* Whether each robotic arm is `Idle` or `Busy`.
* The number of grasp attempts for each robotic arm.
* The success rate of each robotic arm.
* The number of `Execute` decisions.
* The number of `Skip` decisions.
* The number and categories of failures.
* The total number of successfully processed objects.
* The number of objects not processed by any robotic arm.
* Overall conveyor throughput.
* Average task duration.
* Individual robotic-arm utilization.

The global monitoring system must not:

* Assign grasping tasks.
* Lock local objects.
* Transfer object positions between robotic arms.
* Control local target selection.
* Control local grasping sequences.

Its purpose is monitoring, logging, and evaluation only.

---

# Final System Workflow

```text
Objects pass through the entry dispersal mechanism
        ↓
Objects enter Robotic Arm 1’s detection area
        ↓
Robotic Arm 1 detects and tracks an object
        ↓
Evaluate whether enough pickable time remains
        ↓
Enough time:
Lock and execute the grasp

Not enough time:
Skip the object
        ↓
Successful grasp:
Remove the object from the conveyor

Failed or skipped:
Allow the object to continue downstream
        ↓
The object enters Robotic Arm 2’s detection area
        ↓
Robotic Arm 2 independently re-detects and re-localizes the object
        ↓
Robotic Arm 2 performs a new time and graspability evaluation
        ↓
Repeat the same process for each downstream robotic arm
```

---

# Phase 1 Completion Criteria

Phase 1 is complete when the system can:

1. Separate objects before they enter the main conveyor workflow.
2. Move objects along a constant-speed conveyor belt.
3. Allow each robotic arm to independently observe its own workspace.
4. Capture and display each robotic arm’s camera feed.
5. Display a Latest Pick Line in each camera feed.
6. Detect and track objects inside each local detection area.
7. Determine whether enough time remains to complete a grasp.
8. Lock and grasp objects that satisfy the local conditions.
9. Skip objects that cannot be processed in time.
10. Prevent overlapping robotic-arm workspaces and grasp conflicts.
11. Allow multiple robotic arms to operate simultaneously.
12. Recover safely from simulated grasp failures.
13. Allow downstream robotic arms to independently re-detect failed or skipped objects.
14. Record local detection, decision, action, timing, and result data.
15. Display system-wide performance through a monitoring interface.

---

# Out of Scope for Phase 1

The following features should not be included in the first implementation phase:

* XR or VR teleoperation.
* Shared grasp-task assignment.
* Real-time transfer of object positions between robotic arms.
* Learned grasp policies.
* VLA models.
* Dynamic conveyor speed.
* Dynamic Latest Pick Line adjustment.
* Real robotic-arm hardware integration.
* Cross-robot trajectory coordination.
* Automatic downstream behavior based solely on upstream failure metadata.

These features may be considered after the decentralized Unity system is stable.

---

# Future Extensions

Possible later extensions include:

* Dynamic Latest Pick Line placement.
* Variable conveyor speed.
* Object-specific grasp-time estimation.
* Data-driven grasp-success prediction.
* Learned grasp-position correction.
* LeRobot-compatible data recording.
* SO-100 or SO-101 hardware integration.
* XR-based human correction.
* Failure-aware advisory information for downstream robotic arms.
* More advanced system-wide performance optimization.

---

# Project Positioning

> A decentralized multi-robot conveyor picking system in which each robotic arm independently detects, evaluates, and handles objects within a non-overlapping local workspace.

The system does not rely on a shared grasp-task queue.

Each robotic arm makes local decisions based on current visual observations, remaining pickable time, and its own operational state.

This architecture reduces dependency between robotic arms, prevents outdated object-state information from propagating through the system, and improves scalability, fault tolerance, and maintainability.
