using System;
using SortingFactory.Phase1;
using SortingFactory.Step2;
using UnityEngine;

namespace SortingFactory.Step4
{
    [DisallowMultipleComponent]
    public sealed class PrototypeRobotArmIKController : MonoBehaviour
    {
        private enum MotionPhase
        {
            Idle,
            ResolvingTarget,
            Approach,
            Descend,
            CloseGripper,
            PlacementGesture,
            Lift,
            MoveToDrop,
            LowerToDrop,
            OpenGripper,
            ReturnHome
        }

        [Header("IK Motion")]
        [SerializeField, Min(10f)] private float jointRotationSpeed = 90f;
        [SerializeField, Min(0.02f)] private float targetPositionTolerance = 0.2f;
        [SerializeField, Min(0.1f)] private float approachHeight = 0.55f;
        [SerializeField, Min(0f)] private float graspHeightOffset = 0.02f;
        [SerializeField, Min(0.1f)] private float liftHeight = 0.85f;
        [SerializeField, Min(0.1f)] private float dropApproachHeight = 0.55f;
        [SerializeField, Min(0.1f)] private float dropTargetTolerance = 0.45f;
        [SerializeField, Min(0.1f)] private float maximumNaturalDropHeight = 0.22f;
        [SerializeField, Min(0.2f)] private float motionPhaseTimeout = 4f;
        [SerializeField] private bool allowAssistedPrototypeGrasp = true;
        [SerializeField, Min(0.2f)] private float assistedAttachDistance = 1.2f;

        [Header("Prototype Reliability")]
        [SerializeField] private bool useReliableInstantPlacement = true;
        [SerializeField, Min(0.1f)] private float placementGestureDuration = 0.65f;
        [SerializeField, Min(0f)] private float placementGestureDistance = 0.45f;
        [SerializeField, Min(0f)] private float placementGestureLift = 0.25f;

        [Header("Gripper")]
        [SerializeField, Min(0.05f)] private float gripperCloseDuration = 0.25f;
        [SerializeField, Range(0.1f, 1f)] private float closedFingerSpacingScale = 0.42f;

        private WorkstationPickDecisionController decisionController;
        private WorkstationCameraController cameraController;
        private RobotWorkstation workstation;
        private Transform baseYawPivot;
        private Transform shoulderPivot;
        private Transform elbowPivot;
        private Transform wristPivot;
        private Transform gripPoint;
        private Transform objectHoldPoint;
        private Transform leftFinger;
        private Transform rightFinger;
        private Vector3 leftFingerOpenPosition;
        private Vector3 rightFingerOpenPosition;
        private Vector3 leftFingerClosedPosition;
        private Vector3 rightFingerClosedPosition;
        private float baseYawAngle;
        private float shoulderAngle;
        private float elbowAngle;
        private float gripperBlend;
        private float phaseElapsed;
        private float resolveElapsed;
        private int activeLogicalTargetId = -1;
        private PersistentVisionTarget activeVisionTarget;
        private Transform physicalTarget;
        private Transform originalTargetParent;
        private Rigidbody heldBody;
        private bool heldBodyDetectedCollisions;
        private Vector3 placementGestureTarget;
        private Vector3 liftTarget;
        private float dropTransportHeight;
        private MotionPhase phase = MotionPhase.Idle;
        private bool configured;
        private bool rigBuilt;

        public string MotionState => phase.ToString();
        public string HeldObjectName => physicalTarget == null ? string.Empty : physicalTarget.name;

        public void Configure(
            WorkstationPickDecisionController newDecisionController,
            WorkstationCameraController newCameraController,
            RobotWorkstation newWorkstation)
        {
            decisionController = newDecisionController;
            cameraController = newCameraController;
            workstation = newWorkstation;
            configured = decisionController != null && cameraController != null && workstation != null;
            jointRotationSpeed = 90f;
            motionPhaseTimeout = 4f;
            dropApproachHeight = 0.55f;
            dropTargetTolerance = 0.45f;
            maximumNaturalDropHeight = 0.22f;
            useReliableInstantPlacement = true;
            placementGestureDuration = 0.65f;
            placementGestureDistance = 0.45f;
            placementGestureLift = 0.25f;

            if (!configured || !BuildRigHierarchy())
            {
                Debug.LogError("Prototype robot IK could not build the placeholder arm joint chain.", this);
                return;
            }

            decisionController.SetExternalMotionControllerActive(true);
            decisionController.SetMotionStatus("Robot IK ready");
        }

        private void Update()
        {
            if (!configured || !rigBuilt)
            {
                return;
            }

            float deltaTime = Time.deltaTime;
            phaseElapsed += deltaTime;
            switch (phase)
            {
                case MotionPhase.Idle:
                    UpdateIdle();
                    break;
                case MotionPhase.ResolvingTarget:
                    UpdateResolvingTarget(deltaTime);
                    break;
                case MotionPhase.Approach:
                    UpdateApproach(deltaTime);
                    break;
                case MotionPhase.Descend:
                    UpdateDescend(deltaTime);
                    break;
                case MotionPhase.CloseGripper:
                    UpdateCloseGripper(deltaTime);
                    break;
                case MotionPhase.PlacementGesture:
                    UpdatePlacementGesture(deltaTime);
                    break;
                case MotionPhase.Lift:
                    UpdateLift(deltaTime);
                    break;
                case MotionPhase.MoveToDrop:
                    UpdateMoveToDrop(deltaTime);
                    break;
                case MotionPhase.LowerToDrop:
                    UpdateLowerToDrop(deltaTime);
                    break;
                case MotionPhase.OpenGripper:
                    UpdateOpenGripper(deltaTime);
                    break;
                case MotionPhase.ReturnHome:
                    UpdateReturnHome(deltaTime);
                    break;
            }

            StabilizeHeldObject();
        }

        private void UpdateIdle()
        {
            SetGripperBlend(0f);
            if (decisionController.ArmState != WorkstationArmState.SecuringObject ||
                decisionController.ActiveLogicalTargetId < 0)
            {
                return;
            }

            activeLogicalTargetId = decisionController.ActiveLogicalTargetId;
            activeVisionTarget = cameraController.LockedTarget;
            physicalTarget = null;
            resolveElapsed = 0f;
            SetPhase(MotionPhase.ResolvingTarget, $"Resolving physical L#{activeLogicalTargetId}");
        }

        private void UpdateResolvingTarget(float deltaTime)
        {
            resolveElapsed += deltaTime;
            if (activeVisionTarget == null)
            {
                activeVisionTarget = cameraController.LockedTarget;
            }

            physicalTarget = ResolvePhysicalTarget(activeLogicalTargetId, activeVisionTarget);
            if (physicalTarget != null)
            {
                SetPhase(MotionPhase.Approach, $"Approaching {physicalTarget.name}");
                return;
            }

            if (resolveElapsed >= 1.25f)
            {
                FailCurrentTask("No pickable object matched the detection");
            }
        }

        private void UpdateApproach(float deltaTime)
        {
            if (!TryGetPhysicalTargetCenter(out Vector3 center))
            {
                FailCurrentTask("Physical target disappeared before approach");
                return;
            }

            Vector3 target = center + Vector3.up * approachHeight;
            SolveIkTowards(target, deltaTime);
            if (Vector3.Distance(gripPoint.position, target) <= targetPositionTolerance)
            {
                SetPhase(MotionPhase.Descend, "Descending to target");
            }
            else if (phaseElapsed > motionPhaseTimeout)
            {
                float distance = Vector3.Distance(gripPoint.position, target);
                if (allowAssistedPrototypeGrasp && distance <= assistedAttachDistance + approachHeight)
                {
                    SetPhase(MotionPhase.Descend, "Approach limit reached; assisted descent");
                }
                else
                {
                    FailCurrentTask("IK could not reach the approach position");
                }
            }
        }

        private void UpdateDescend(float deltaTime)
        {
            if (!TryGetPhysicalTargetCenter(out Vector3 center))
            {
                FailCurrentTask("Physical target disappeared during descent");
                return;
            }

            Vector3 target = center + Vector3.up * graspHeightOffset;
            SolveIkTowards(target, deltaTime);
            float distance = Vector3.Distance(gripPoint.position, target);
            if (distance <= targetPositionTolerance)
            {
                SetPhase(MotionPhase.CloseGripper, "Closing gripper");
            }
            else if (phaseElapsed > motionPhaseTimeout)
            {
                if (allowAssistedPrototypeGrasp && distance <= assistedAttachDistance)
                {
                    SetPhase(MotionPhase.CloseGripper, "Grasp limit reached; assisted alignment");
                }
                else
                {
                    FailCurrentTask("IK could not reach the grasp position");
                }
            }
        }

        private void UpdateCloseGripper(float deltaTime)
        {
            gripperBlend = Mathf.MoveTowards(
                gripperBlend,
                1f,
                deltaTime / Mathf.Max(0.05f, gripperCloseDuration));
            SetGripperBlend(gripperBlend);
            if (gripperBlend < 0.999f)
            {
                return;
            }

            if (!AttachPhysicalTarget())
            {
                FailCurrentTask("Gripper closed without attaching a physical object");
                return;
            }

            if (useReliableInstantPlacement)
            {
                placementGestureTarget = BuildPlacementGestureTarget();
                SetPhase(
                    MotionPhase.PlacementGesture,
                    $"Carrying {physicalTarget.name} toward drop zone");
                return;
            }

            decisionController.ReportGraspSucceeded();
            liftTarget = gripPoint.position + Vector3.up * liftHeight;
            dropTransportHeight = liftTarget.y;
            SetPhase(MotionPhase.Lift, $"Lifting {physicalTarget.name}");
        }

        private void UpdatePlacementGesture(float deltaTime)
        {
            SolveIkTowards(placementGestureTarget, deltaTime);
            if (phaseElapsed < placementGestureDuration)
            {
                return;
            }

            string placedObjectName = physicalTarget == null
                ? "object"
                : physicalTarget.name;
            if (!PlacePhysicalTargetInDropZoneImmediately())
            {
                ReleasePhysicalTarget();
                FailCurrentTask("Could not place the attached object in the drop zone");
                return;
            }

            decisionController.ReportGraspSucceeded();
            SetGripperBlend(0f);
            SetPhase(
                MotionPhase.ReturnHome,
                $"Placed {placedObjectName}; returning robot home");
        }

        private Vector3 BuildPlacementGestureTarget()
        {
            Vector3 direction = workstation.DropZone == null
                ? transform.forward
                : workstation.DropZone.position - gripPoint.position;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                direction = transform.forward;
                direction.y = 0f;
            }

            return gripPoint.position +
                direction.normalized * placementGestureDistance +
                Vector3.up * placementGestureLift;
        }

        private void UpdateLift(float deltaTime)
        {
            SolveIkTowards(liftTarget, deltaTime);
            if (Vector3.Distance(gripPoint.position, liftTarget) <= targetPositionTolerance)
            {
                SetPhase(MotionPhase.MoveToDrop, "Swinging above drop zone");
            }
            else if (phaseElapsed > motionPhaseTimeout)
            {
                dropTransportHeight = Mathf.Max(dropTransportHeight, gripPoint.position.y);
                SetPhase(MotionPhase.MoveToDrop, "Lift limit reached; swinging above drop zone");
            }
        }

        private void UpdateMoveToDrop(float deltaTime)
        {
            Vector3 dropPoint = GetDropPoint();
            Vector3 target = new Vector3(
                dropPoint.x,
                Mathf.Max(dropPoint.y + dropApproachHeight, dropTransportHeight),
                dropPoint.z);
            SolveIkTowards(target, deltaTime);
            if (IsHeldObjectHorizontallyInsideDropZone() &&
                (Vector3.Distance(gripPoint.position, target) <= dropTargetTolerance ||
                    phaseElapsed >= motionPhaseTimeout))
            {
                SetPhase(MotionPhase.LowerToDrop, "Above drop zone; lowering object");
            }
        }

        private void UpdateLowerToDrop(float deltaTime)
        {
            Vector3 target = GetDropPoint();
            SolveIkTowards(target, deltaTime);
            if (CanReleaseNaturallyIntoDropZone() ||
                (phaseElapsed >= motionPhaseTimeout &&
                    IsHeldObjectHorizontallyInsideDropZone()))
            {
                SetPhase(MotionPhase.OpenGripper, "Releasing object");
            }
        }

        private void UpdateOpenGripper(float deltaTime)
        {
            gripperBlend = Mathf.MoveTowards(
                gripperBlend,
                0f,
                deltaTime / Mathf.Max(0.05f, gripperCloseDuration));
            SetGripperBlend(gripperBlend);
            if (gripperBlend > 0.001f)
            {
                return;
            }

            ReleasePhysicalTarget();
            SetPhase(MotionPhase.ReturnHome, "Returning robot to home pose");
        }

        private void UpdateReturnHome(float deltaTime)
        {
            float maxStep = jointRotationSpeed * deltaTime;
            baseYawAngle = Mathf.MoveTowards(baseYawAngle, 0f, maxStep);
            shoulderAngle = Mathf.MoveTowards(shoulderAngle, 0f, maxStep);
            elbowAngle = Mathf.MoveTowards(elbowAngle, 0f, maxStep);
            ApplyJointRotations();

            if (Mathf.Abs(baseYawAngle) <= 0.1f &&
                Mathf.Abs(shoulderAngle) <= 0.1f &&
                Mathf.Abs(elbowAngle) <= 0.1f)
            {
                ClearTaskReferences();
                decisionController.ReportCycleCompleted();
                SetPhase(MotionPhase.Idle, "Robot ready");
            }
        }

        private void SolveIkTowards(Vector3 worldTarget, float deltaTime)
        {
            const int iterations = 4;
            float maxStep = jointRotationSpeed * deltaTime / iterations;
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                RotateJointTowards(
                    baseYawPivot,
                    Vector3.up,
                    worldTarget,
                    ref baseYawAngle,
                    -165f,
                    165f,
                    maxStep);
                RotateJointTowards(
                    elbowPivot,
                    Vector3.forward,
                    worldTarget,
                    ref elbowAngle,
                    -145f,
                    145f,
                    maxStep);
                RotateJointTowards(
                    shoulderPivot,
                    Vector3.forward,
                    worldTarget,
                    ref shoulderAngle,
                    -115f,
                    115f,
                    maxStep);
            }
        }

        private void RotateJointTowards(
            Transform joint,
            Vector3 localAxis,
            Vector3 worldTarget,
            ref float angle,
            float minimum,
            float maximum,
            float maxStep)
        {
            Vector3 axis = joint.parent == null
                ? localAxis
                : joint.parent.TransformDirection(localAxis).normalized;
            Vector3 toEnd = Vector3.ProjectOnPlane(gripPoint.position - joint.position, axis);
            Vector3 toTarget = Vector3.ProjectOnPlane(worldTarget - joint.position, axis);
            if (toEnd.sqrMagnitude <= 0.000001f || toTarget.sqrMagnitude <= 0.000001f)
            {
                return;
            }

            float correction = Vector3.SignedAngle(toEnd, toTarget, axis);
            angle = Mathf.Clamp(angle + Mathf.Clamp(correction, -maxStep, maxStep), minimum, maximum);
            joint.localRotation = Quaternion.AngleAxis(angle, localAxis);
        }

        private void ApplyJointRotations()
        {
            baseYawPivot.localRotation = Quaternion.AngleAxis(baseYawAngle, Vector3.up);
            shoulderPivot.localRotation = Quaternion.AngleAxis(shoulderAngle, Vector3.forward);
            elbowPivot.localRotation = Quaternion.AngleAxis(elbowAngle, Vector3.forward);
        }

        private bool BuildRigHierarchy()
        {
            if (rigBuilt)
            {
                return true;
            }

            baseYawPivot = transform.Find("IK_BaseYaw");
            if (baseYawPivot != null)
            {
                shoulderPivot = baseYawPivot.Find("IK_Shoulder");
                elbowPivot = shoulderPivot == null ? null : shoulderPivot.Find("IK_Elbow");
                wristPivot = elbowPivot == null ? null : elbowPivot.Find("IK_Wrist");
                gripPoint = wristPivot == null ? null : wristPivot.Find("GripPoint");
                objectHoldPoint = gripPoint == null ? null : gripPoint.Find("ObjectHoldPoint");
                if (gripPoint != null && objectHoldPoint == null)
                {
                    objectHoldPoint = CreatePivot(
                        "ObjectHoldPoint",
                        gripPoint,
                        gripPoint.position,
                        Quaternion.identity);
                }
                leftFinger = FindDescendant(transform, "GripperLeft");
                rightFinger = FindDescendant(transform, "GripperRight");
                rigBuilt = shoulderPivot != null && elbowPivot != null && wristPivot != null &&
                    gripPoint != null && objectHoldPoint != null &&
                    leftFinger != null && rightFinger != null;
                if (rigBuilt)
                {
                    CacheFingerPositions();
                }
                return rigBuilt;
            }

            Transform shoulderVisual = FindDescendant(transform, "Shoulder");
            Transform upperArmVisual = FindDescendant(transform, "UpperArm");
            Transform elbowVisual = FindDescendant(transform, "Elbow");
            Transform forearmVisual = FindDescendant(transform, "Forearm");
            Transform wristVisual = FindDescendant(transform, "Wrist");
            Transform palmVisual = FindDescendant(transform, "GripperPalm");
            leftFinger = FindDescendant(transform, "GripperLeft");
            rightFinger = FindDescendant(transform, "GripperRight");
            if (shoulderVisual == null || upperArmVisual == null || elbowVisual == null ||
                forearmVisual == null || wristVisual == null || palmVisual == null ||
                leftFinger == null || rightFinger == null)
            {
                return false;
            }

            Vector3 shoulderPosition = shoulderVisual.position;
            Vector3 elbowPosition = elbowVisual.position;
            Vector3 wristPosition = wristVisual.position;
            Vector3 gripPosition = (leftFinger.position + rightFinger.position) * 0.5f;

            baseYawPivot = CreatePivot("IK_BaseYaw", transform, shoulderPosition, transform.rotation);
            shoulderPivot = CreatePivot(
                "IK_Shoulder",
                baseYawPivot,
                shoulderPosition,
                transform.rotation);
            elbowPivot = CreatePivot(
                "IK_Elbow",
                shoulderPivot,
                elbowPosition,
                transform.rotation);
            wristPivot = CreatePivot(
                "IK_Wrist",
                elbowPivot,
                wristPosition,
                transform.rotation);

            shoulderVisual.SetParent(shoulderPivot, true);
            upperArmVisual.SetParent(shoulderPivot, true);
            elbowVisual.SetParent(elbowPivot, true);
            forearmVisual.SetParent(elbowPivot, true);
            wristVisual.SetParent(wristPivot, true);
            palmVisual.SetParent(wristPivot, true);
            leftFinger.SetParent(wristPivot, true);
            rightFinger.SetParent(wristPivot, true);

            gripPoint = CreatePivot("GripPoint", wristPivot, gripPosition, transform.rotation);
            objectHoldPoint = CreatePivot(
                "ObjectHoldPoint",
                gripPoint,
                gripPosition,
                Quaternion.identity);
            CacheFingerPositions();
            rigBuilt = true;
            return true;
        }

        private void CacheFingerPositions()
        {
            leftFingerOpenPosition = leftFinger.localPosition;
            rightFingerOpenPosition = rightFinger.localPosition;
            leftFingerClosedPosition = leftFingerOpenPosition;
            rightFingerClosedPosition = rightFingerOpenPosition;
            leftFingerClosedPosition.z *= closedFingerSpacingScale;
            rightFingerClosedPosition.z *= closedFingerSpacingScale;
        }

        private void SetGripperBlend(float blend)
        {
            gripperBlend = Mathf.Clamp01(blend);
            leftFinger.localPosition = Vector3.Lerp(
                leftFingerOpenPosition,
                leftFingerClosedPosition,
                gripperBlend);
            rightFinger.localPosition = Vector3.Lerp(
                rightFingerOpenPosition,
                rightFingerClosedPosition,
                gripperBlend);
        }

        private Transform ResolvePhysicalTarget(
            int logicalTargetId,
            PersistentVisionTarget visionTarget)
        {
            Camera stationCamera = cameraController.GetComponent<Camera>();
            VisionDetectionResult displayDetection = Array.Find(
                cameraController.DisplayDetections,
                detection => detection.track_id == logicalTargetId);
            if (displayDetection == null && visionTarget != null)
            {
                displayDetection = visionTarget.BuildDisplayDetection(1.5f);
            }
            if (stationCamera != null && displayDetection != null)
            {
                Transform screenMatchedTarget = ResolveConveyorTargetByScreenPosition(
                    stationCamera,
                    displayDetection);
                if (screenMatchedTarget != null)
                {
                    return screenMatchedTarget;
                }

                Ray ray = stationCamera.ViewportPointToRay(new Vector3(
                    displayDetection.bbox_center_x,
                    1f - displayDetection.bbox_center_y,
                    0f));
                RaycastHit[] hits = Physics.RaycastAll(
                    ray,
                    100f,
                    ~0,
                    QueryTriggerInteraction.Ignore);
                Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
                foreach (RaycastHit hit in hits)
                {
                    Transform candidate = GetPickableRoot(hit.collider);
                    if (IsKnownPickableTarget(candidate) &&
                        IsEligiblePhysicalTarget(candidate, null))
                    {
                        return candidate;
                    }
                }
            }

            if (visionTarget == null || !visionTarget.HasConveyorPosition)
            {
                return null;
            }

            Rigidbody[] bodies = FindObjectsByType<Rigidbody>(FindObjectsSortMode.None);
            Transform closest = null;
            float closestDistance = 2.5f;
            foreach (Rigidbody body in bodies)
            {
                if (!IsKnownPickableTarget(body.transform) ||
                    !IsEligiblePhysicalTarget(body.transform, visionTarget) ||
                    !MatchesDetectedClassWhenKnown(body.transform, visionTarget.ClassName))
                {
                    continue;
                }

                Vector2 bodyPosition = new Vector2(body.position.x, body.position.z);
                Vector2 predictedPosition = new Vector2(
                    visionTarget.PredictedBeltPosition.x,
                    visionTarget.PredictedBeltPosition.z);
                float distance = Vector2.Distance(bodyPosition, predictedPosition);
                if (distance < closestDistance)
                {
                    closest = body.transform;
                    closestDistance = distance;
                }
            }
            return closest;
        }

        private Transform ResolveConveyorTargetByScreenPosition(
            Camera stationCamera,
            VisionDetectionResult detection)
        {
            if (stationCamera == null || detection == null)
            {
                return null;
            }

            Vector2 detectionCenter = new Vector2(
                detection.bbox_center_x,
                1f - detection.bbox_center_y);
            float maximumScreenDistance = Mathf.Clamp(
                Mathf.Max(detection.bbox_width, detection.bbox_height) * 1.5f,
                0.14f,
                0.32f);
            float bestScore = maximumScreenDistance;
            Transform bestCandidate = null;

            foreach (DetectionLabeledBox labeledBox in
                FindObjectsByType<DetectionLabeledBox>(FindObjectsSortMode.None))
            {
                Transform candidate = labeledBox.transform;
                if (!IsEligiblePhysicalTarget(candidate, null) ||
                    !TryGetRendererBounds(candidate, out Bounds bounds))
                {
                    continue;
                }

                Vector3 viewportPosition = stationCamera.WorldToViewportPoint(bounds.center);
                if (viewportPosition.z <= 0f)
                {
                    continue;
                }

                float score = Vector2.Distance(
                    detectionCenter,
                    new Vector2(viewportPosition.x, viewportPosition.y));
                if (!labeledBox.MatchesVisionClass(detection.class_name))
                {
                    score += 0.08f;
                }

                if (score <= bestScore)
                {
                    bestScore = score;
                    bestCandidate = candidate;
                }
            }

            foreach (SplineConveyorObject conveyorObject in
                FindObjectsByType<SplineConveyorObject>(FindObjectsSortMode.None))
            {
                Transform candidate = conveyorObject.transform;
                if (!conveyorObject.IsFollowingConveyor ||
                    candidate.GetComponent<DetectionLabeledBox>() != null ||
                    !IsEligiblePhysicalTarget(candidate, null) ||
                    !TryGetRendererBounds(candidate, out Bounds bounds))
                {
                    continue;
                }

                Vector3 viewportPosition = stationCamera.WorldToViewportPoint(bounds.center);
                if (viewportPosition.z <= 0f)
                {
                    continue;
                }

                float screenDistance = Vector2.Distance(
                    detectionCenter,
                    new Vector2(viewportPosition.x, viewportPosition.y));
                if (screenDistance <= bestScore)
                {
                    bestScore = screenDistance;
                    bestCandidate = candidate;
                }
            }

            return bestCandidate;
        }

        private static bool IsKnownPickableTarget(Transform candidate)
        {
            return candidate != null &&
                (candidate.GetComponent<DetectionLabeledBox>() != null ||
                    candidate.GetComponent<SplineConveyorObject>() != null ||
                    candidate.GetComponent<SortingAreaFeedObject>() != null);
        }

        private static bool MatchesDetectedClassWhenKnown(
            Transform candidate,
            string detectedClass)
        {
            DetectionLabeledBox labeledBox = candidate == null
                ? null
                : candidate.GetComponent<DetectionLabeledBox>();
            return labeledBox == null || string.IsNullOrEmpty(detectedClass) ||
                labeledBox.MatchesVisionClass(detectedClass);
        }

        private Transform GetPickableRoot(Collider collider)
        {
            if (collider == null)
            {
                return null;
            }

            SplineConveyorObject conveyorObject = collider.GetComponentInParent<
                SplineConveyorObject>();
            if (conveyorObject != null)
            {
                return conveyorObject.transform;
            }

            SortingAreaFeedObject feedObject = collider.GetComponentInParent<SortingAreaFeedObject>();
            if (feedObject != null)
            {
                return feedObject.transform;
            }

            return collider.attachedRigidbody == null
                ? collider.transform
                : collider.attachedRigidbody.transform;
        }

        private bool IsEligiblePhysicalTarget(
            Transform candidate,
            PersistentVisionTarget visionTarget)
        {
            if (candidate == null || candidate == transform || candidate.IsChildOf(transform) ||
                (workstation.DropZone != null && candidate.IsChildOf(workstation.DropZone)))
            {
                return false;
            }

            SplineConveyorObject conveyorObject = candidate.GetComponent<SplineConveyorObject>();
            if (conveyorObject != null && !conveyorObject.IsFollowingConveyor)
            {
                return false;
            }

            if (!TryGetRendererBounds(candidate, out Bounds bounds))
            {
                return false;
            }
            if (bounds.size.x > 2.5f || bounds.size.y > 2.5f || bounds.size.z > 2.5f)
            {
                return false;
            }

            if (visionTarget == null || !visionTarget.HasConveyorPosition)
            {
                return true;
            }

            if (conveyorObject == null || !conveyorObject.IsFollowingConveyor)
            {
                return false;
            }

            Vector2 candidatePosition = new Vector2(bounds.center.x, bounds.center.z);
            Vector2 predictedPosition = new Vector2(
                visionTarget.PredictedBeltPosition.x,
                visionTarget.PredictedBeltPosition.z);
            return Vector2.Distance(candidatePosition, predictedPosition) <= 2f;
        }

        private static bool TryGetRendererBounds(Transform candidate, out Bounds bounds)
        {
            Renderer[] renderers = candidate == null
                ? Array.Empty<Renderer>()
                : candidate.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                bounds = default;
                return false;
            }

            bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }
            return true;
        }

        private bool TryGetPhysicalTargetCenter(out Vector3 center)
        {
            if (physicalTarget == null)
            {
                center = Vector3.zero;
                return false;
            }

            if (!TryGetPhysicalBounds(physicalTarget, out Bounds bounds))
            {
                center = physicalTarget.position;
                return true;
            }
            center = bounds.center;
            return true;
        }

        private static bool TryGetPhysicalBounds(Transform candidate, out Bounds bounds)
        {
            Collider[] colliders = candidate == null
                ? Array.Empty<Collider>()
                : candidate.GetComponentsInChildren<Collider>();
            bool hasBounds = false;
            bounds = default;
            foreach (Collider collider in colliders)
            {
                if (collider == null || !collider.enabled || collider.isTrigger)
                {
                    continue;
                }

                if (!hasBounds)
                {
                    bounds = collider.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }

            return hasBounds || TryGetRendererBounds(candidate, out bounds);
        }

        private bool AttachPhysicalTarget()
        {
            if (!TryGetPhysicalTargetCenter(out Vector3 center))
            {
                return false;
            }

            float maximumAttachDistance = allowAssistedPrototypeGrasp
                ? assistedAttachDistance
                : targetPositionTolerance * 2.5f;
            if (Vector3.Distance(center, gripPoint.position) > maximumAttachDistance)
            {
                return false;
            }

            originalTargetParent = physicalTarget.parent;
            heldBody = physicalTarget.GetComponent<Rigidbody>();
            if (heldBody == null)
            {
                MeshCollider[] meshColliders = physicalTarget.GetComponentsInChildren<
                    MeshCollider>();
                foreach (MeshCollider meshCollider in meshColliders)
                {
                    meshCollider.convex = true;
                }
                heldBody = physicalTarget.gameObject.AddComponent<Rigidbody>();
            }

            heldBodyDetectedCollisions = heldBody.detectCollisions;

            SplineConveyorObject conveyorObject = physicalTarget.GetComponent<
                SplineConveyorObject>();
            if (conveyorObject != null)
            {
                conveyorObject.SetConveyorMotionEnabled(false);
            }
            SortingAreaFeedObject feedObject = physicalTarget.GetComponent<SortingAreaFeedObject>();
            if (feedObject != null)
            {
                feedObject.enabled = false;
            }

            heldBody.linearVelocity = Vector3.zero;
            heldBody.angularVelocity = Vector3.zero;
            heldBody.useGravity = false;
            heldBody.isKinematic = true;
            heldBody.detectCollisions = false;
            objectHoldPoint.rotation = Quaternion.identity;
            physicalTarget.rotation = Quaternion.Euler(0f, physicalTarget.eulerAngles.y, 0f);
            if (TryGetPhysicalTargetCenter(out Vector3 alignedCenter))
            {
                center = alignedCenter;
            }
            physicalTarget.position += objectHoldPoint.position - center;
            physicalTarget.SetParent(objectHoldPoint, true);
            return true;
        }

        private void ReleasePhysicalTarget()
        {
            if (physicalTarget == null)
            {
                return;
            }

            physicalTarget.SetParent(originalTargetParent, true);
            if (heldBody != null)
            {
                heldBody.detectCollisions = heldBodyDetectedCollisions;
                heldBody.isKinematic = false;
                heldBody.useGravity = true;
                heldBody.linearVelocity = Vector3.zero;
                heldBody.angularVelocity = Vector3.zero;
            }
        }

        private bool PlacePhysicalTargetInDropZoneImmediately()
        {
            if (physicalTarget == null ||
                !TryGetDropSurfaceBounds(out Bounds surfaceBounds))
            {
                return false;
            }

            physicalTarget.rotation = Quaternion.Euler(
                0f,
                physicalTarget.eulerAngles.y,
                0f);
            if (!TryGetPhysicalBounds(physicalTarget, out Bounds objectBounds))
            {
                return false;
            }

            Vector3 placementCenter = new Vector3(
                surfaceBounds.center.x,
                surfaceBounds.max.y + objectBounds.extents.y + 0.08f,
                surfaceBounds.center.z);
            physicalTarget.position += placementCenter - objectBounds.center;
            ReleasePhysicalTarget();
            return true;
        }

        private void StabilizeHeldObject()
        {
            if (objectHoldPoint == null || physicalTarget == null || heldBody == null ||
                !heldBody.isKinematic)
            {
                return;
            }

            objectHoldPoint.rotation = Quaternion.identity;
        }

        private Vector3 GetDropPoint()
        {
            if (workstation.DropZone == null)
            {
                return transform.position + Vector3.up * 0.5f;
            }

            Transform dropSurface = workstation.DropZone.Find("DropSurface");
            Renderer surfaceRenderer = dropSurface == null
                ? null
                : dropSurface.GetComponent<Renderer>();
            if (surfaceRenderer == null)
            {
                return workstation.DropZone.position + Vector3.up * 0.25f;
            }

            float objectHalfHeight = 0.2f;
            if (TryGetPhysicalTargetCenter(out _) &&
                TryGetPhysicalBounds(physicalTarget, out Bounds objectBounds))
            {
                objectHalfHeight = Mathf.Max(0.1f, objectBounds.extents.y);
            }

            Bounds surfaceBounds = surfaceRenderer.bounds;
            return new Vector3(
                surfaceBounds.center.x,
                surfaceBounds.max.y + objectHalfHeight + 0.12f,
                surfaceBounds.center.z);
        }

        private bool IsHeldObjectHorizontallyInsideDropZone()
        {
            if (!TryGetDropSurfaceBounds(out Bounds surfaceBounds) ||
                !TryGetPhysicalBounds(physicalTarget, out Bounds objectBounds))
            {
                return false;
            }

            const float edgeClearance = 0.04f;
            return objectBounds.min.x >= surfaceBounds.min.x + edgeClearance &&
                objectBounds.max.x <= surfaceBounds.max.x - edgeClearance &&
                objectBounds.min.z >= surfaceBounds.min.z + edgeClearance &&
                objectBounds.max.z <= surfaceBounds.max.z - edgeClearance;
        }

        private bool CanReleaseNaturallyIntoDropZone()
        {
            if (!TryGetDropSurfaceBounds(out Bounds surfaceBounds) ||
                !TryGetPhysicalBounds(physicalTarget, out Bounds objectBounds))
            {
                return false;
            }

            float dropHeight = objectBounds.min.y - surfaceBounds.max.y;
            return IsHeldObjectHorizontallyInsideDropZone() &&
                dropHeight >= -0.15f &&
                dropHeight <= maximumNaturalDropHeight;
        }

        private bool TryGetDropSurfaceBounds(out Bounds bounds)
        {
            Transform dropSurface = workstation.DropZone == null
                ? null
                : workstation.DropZone.Find("DropSurface");
            Renderer renderer = dropSurface == null ? null : dropSurface.GetComponent<Renderer>();
            if (renderer == null)
            {
                bounds = default;
                return false;
            }

            bounds = renderer.bounds;
            return true;
        }

        private void FailCurrentTask(string reason)
        {
            string targetName = physicalTarget == null ? "unresolved target" : physicalTarget.name;
            Debug.LogWarning(
                $"{workstation.ArmId} prototype grasp failed during {phase}: {reason} ({targetName}).",
                this);
            decisionController.SetMotionStatus(reason);
            decisionController.ReportGraspFailed(reason);
            SetPhase(MotionPhase.ReturnHome, reason);
        }

        private void ClearTaskReferences()
        {
            activeLogicalTargetId = -1;
            activeVisionTarget = null;
            physicalTarget = null;
            originalTargetParent = null;
            heldBody = null;
        }

        private void SetPhase(MotionPhase nextPhase, string status)
        {
            phase = nextPhase;
            phaseElapsed = 0f;
            decisionController.SetMotionStatus(status);
        }

        private static Transform CreatePivot(
            string name,
            Transform parent,
            Vector3 worldPosition,
            Quaternion worldRotation)
        {
            Transform pivot = new GameObject(name).transform;
            pivot.SetParent(parent, false);
            pivot.SetPositionAndRotation(worldPosition, worldRotation);
            return pivot;
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            Transform[] descendants = root.GetComponentsInChildren<Transform>(true);
            foreach (Transform descendant in descendants)
            {
                if (descendant.name == name)
                {
                    return descendant;
                }
            }
            return null;
        }
    }
}
