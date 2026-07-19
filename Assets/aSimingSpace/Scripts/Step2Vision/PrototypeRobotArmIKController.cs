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
            Lift,
            MoveToDrop,
            LowerToDrop,
            OpenGripper,
            ReturnHome
        }

        [Header("IK Motion")]
        [SerializeField, Min(10f)] private float jointRotationSpeed = 115f;
        [SerializeField, Min(0.02f)] private float targetPositionTolerance = 0.14f;
        [SerializeField, Min(0.1f)] private float approachHeight = 0.7f;
        [SerializeField, Min(0f)] private float graspHeightOffset = 0.02f;
        [SerializeField, Min(0.1f)] private float liftHeight = 0.85f;
        [SerializeField, Min(0.1f)] private float dropApproachHeight = 0.75f;
        [SerializeField, Min(0.2f)] private float motionPhaseTimeout = 3.5f;

        [Header("Gripper")]
        [SerializeField, Min(0.05f)] private float gripperCloseDuration = 0.4f;
        [SerializeField, Range(0.1f, 1f)] private float closedFingerSpacingScale = 0.42f;

        private WorkstationPickDecisionController decisionController;
        private WorkstationCameraController cameraController;
        private RobotWorkstation workstation;
        private Transform baseYawPivot;
        private Transform shoulderPivot;
        private Transform elbowPivot;
        private Transform wristPivot;
        private Transform gripPoint;
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
        private Vector3 liftTarget;
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
                FailCurrentTask("No physical object matched the detected target");
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
                FailCurrentTask("IK could not reach the approach position");
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
            if (Vector3.Distance(gripPoint.position, target) <= targetPositionTolerance)
            {
                SetPhase(MotionPhase.CloseGripper, "Closing gripper");
            }
            else if (phaseElapsed > motionPhaseTimeout)
            {
                FailCurrentTask("IK could not reach the grasp position");
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

            decisionController.ReportGraspSucceeded();
            liftTarget = gripPoint.position + Vector3.up * liftHeight;
            SetPhase(MotionPhase.Lift, $"Lifting {physicalTarget.name}");
        }

        private void UpdateLift(float deltaTime)
        {
            SolveIkTowards(liftTarget, deltaTime);
            if (Vector3.Distance(gripPoint.position, liftTarget) <= targetPositionTolerance)
            {
                SetPhase(MotionPhase.MoveToDrop, "Moving to drop zone");
            }
            else if (phaseElapsed > motionPhaseTimeout)
            {
                SetPhase(MotionPhase.MoveToDrop, "Lift limit reached; moving to drop zone");
            }
        }

        private void UpdateMoveToDrop(float deltaTime)
        {
            Vector3 target = GetDropPoint() + Vector3.up * dropApproachHeight;
            SolveIkTowards(target, deltaTime);
            if (Vector3.Distance(gripPoint.position, target) <= targetPositionTolerance)
            {
                SetPhase(MotionPhase.LowerToDrop, "Lowering object into drop zone");
            }
            else if (phaseElapsed > motionPhaseTimeout)
            {
                SetPhase(MotionPhase.LowerToDrop, "Drop approach limit reached");
            }
        }

        private void UpdateLowerToDrop(float deltaTime)
        {
            Vector3 target = GetDropPoint();
            SolveIkTowards(target, deltaTime);
            if (Vector3.Distance(gripPoint.position, target) <= targetPositionTolerance ||
                phaseElapsed > motionPhaseTimeout)
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
                leftFinger = FindDescendant(transform, "GripperLeft");
                rightFinger = FindDescendant(transform, "GripperRight");
                rigBuilt = shoulderPivot != null && elbowPivot != null && wristPivot != null &&
                    gripPoint != null && leftFinger != null && rightFinger != null;
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
            if (stationCamera != null && displayDetection != null)
            {
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
                    if (IsEligiblePhysicalTarget(candidate, visionTarget))
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
            float closestDistance = 1.25f;
            foreach (Rigidbody body in bodies)
            {
                if (!IsEligiblePhysicalTarget(body.transform, visionTarget))
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

            Renderer[] renderers = candidate.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                return false;
            }

            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }
            if (bounds.size.x > 2.5f || bounds.size.y > 2.5f || bounds.size.z > 2.5f)
            {
                return false;
            }

            if (visionTarget == null || !visionTarget.HasConveyorPosition)
            {
                return true;
            }

            Vector2 candidatePosition = new Vector2(bounds.center.x, bounds.center.z);
            Vector2 predictedPosition = new Vector2(
                visionTarget.PredictedBeltPosition.x,
                visionTarget.PredictedBeltPosition.z);
            return Vector2.Distance(candidatePosition, predictedPosition) <= 1.5f;
        }

        private bool TryGetPhysicalTargetCenter(out Vector3 center)
        {
            if (physicalTarget == null)
            {
                center = Vector3.zero;
                return false;
            }

            Renderer[] renderers = physicalTarget.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                center = physicalTarget.position;
                return true;
            }

            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }
            center = bounds.center;
            return true;
        }

        private bool AttachPhysicalTarget()
        {
            if (!TryGetPhysicalTargetCenter(out Vector3 center) ||
                Vector3.Distance(center, gripPoint.position) > targetPositionTolerance * 2.5f)
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

            heldBody.linearVelocity = Vector3.zero;
            heldBody.angularVelocity = Vector3.zero;
            heldBody.useGravity = false;
            heldBody.isKinematic = true;
            heldBody.detectCollisions = false;
            physicalTarget.position += gripPoint.position - center;
            physicalTarget.SetParent(gripPoint, true);
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

            if (!TryGetPhysicalTargetCenter(out Vector3 center))
            {
                return;
            }
            physicalTarget.position += GetDropPoint() - center;
        }

        private Vector3 GetDropPoint()
        {
            if (workstation.DropZone == null)
            {
                return transform.position + Vector3.up * 0.5f;
            }

            Renderer[] renderers = workstation.DropZone.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                return workstation.DropZone.position + Vector3.up * 0.25f;
            }

            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }
            return new Vector3(bounds.center.x, bounds.max.y + 0.2f, bounds.center.z);
        }

        private void FailCurrentTask(string reason)
        {
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
