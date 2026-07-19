using System;
using System.Collections.Generic;
using SortingFactory.Phase1;
using SortingFactory.Step2;
using SortingFactory.Step8;
using SplineMeshTools.Core;
using UnityEngine;
using UnityEngine.Splines;

namespace SortingFactory.Step4
{
    public enum WorkstationArmState
    {
        Idle,
        SecuringObject,
        CompletingCycle
    }

    public enum PickDecision
    {
        Waiting,
        Execute,
        Skip,
        Completed,
        Failed
    }

    public sealed class PickTargetEvaluation
    {
        public int LogicalTargetId { get; internal set; }
        public string ClassName { get; internal set; }
        public PickDecision Decision { get; internal set; }
        public string Reason { get; internal set; }
        public float RemainingDistance { get; internal set; }
        public float RemainingTime { get; internal set; }
        public float RequiredTime { get; internal set; }
        public bool IsInsideWorkspace { get; internal set; }
        public bool HasCrossedLatestPickLine { get; internal set; }
        public bool IsTerminal { get; internal set; }
        public bool EnteredWorkspace { get; internal set; }
        internal double LastUpdatedAt { get; set; }
    }

    public sealed class GraspEpisodeSummary
    {
        public long Sequence { get; internal set; }
        public string EpisodeId { get; internal set; }
        public string ArmId { get; internal set; }
        public string CameraId { get; internal set; }
        public int LogicalTargetId { get; internal set; }
        public string ClassName { get; internal set; }
        public bool Succeeded { get; internal set; }
        public string FailureReason { get; internal set; }
        public float GraspDurationSeconds { get; internal set; }
        public float CycleDurationSeconds { get; internal set; }
        public double CompletedAt { get; internal set; }
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(WorkstationCameraController))]
    public sealed class WorkstationPickDecisionController : MonoBehaviour
    {
        [Header("Step 4 Timing")]
        [SerializeField, Min(0.1f)] private float requiredGraspTimeSeconds = 1.25f;
        [SerializeField, Min(0f)] private float safetyMarginSeconds = 0.25f;
        [SerializeField, Min(0.1f)] private float completeCycleTimeSeconds = 10f;
        [SerializeField] private bool useFastPrototypePickWindow = true;

        [Header("Decision")]
        [SerializeField] private bool automaticallyLockExecutableTargets = true;
        [SerializeField, Min(0.5f)] private float completedEvaluationRetentionSeconds = 3f;
        [SerializeField, Min(0.25f)] private float latestPickLineHalfWidth = 1.25f;

        private readonly Dictionary<int, PickTargetEvaluation> evaluations =
            new Dictionary<int, PickTargetEvaluation>();
        private WorkstationCameraController cameraController;
        private Phase1SceneSetup sceneSetup;
        private RobotWorkstation workstation;
        private BoxCollider workspace;
        private SplineContainer conveyorPath;
        private float conveyorLength;
        private float workspaceEntryDistance;
        private float workspaceExitDistance;
        private float workspaceSpan;
        private float latestPickLineDistance;
        private float latestPickProgress;
        private float lastCalculatedSpeed = float.NaN;
        private bool hasPhysicalPickWindow;
        private int activeLogicalTargetId = -1;
        private double graspSecuredAt;
        private double cycleCompletedAt;
        private bool externalMotionControllerActive;
        private PrototypeRobotArmIKController prototypeRobotController;
        private So101CsvRecorder telemetryRecorder;
        private long episodeSequence;
        private long activeEpisodeSequence;
        private string activeEpisodeId = string.Empty;
        private int cycleTargetLogicalId = -1;
        private string cycleTargetClass = string.Empty;
        private string activeOutcome = string.Empty;
        private string activeFailureReason = string.Empty;
        private double attemptStartedAt;
        private float activeGraspDuration;

        public WorkstationArmState ArmState { get; private set; } = WorkstationArmState.Idle;
        public float RequiredDecisionTime => requiredGraspTimeSeconds + safetyMarginSeconds;
        public float CompleteCycleTime => completeCycleTimeSeconds;
        public float WorkspaceEntryDistance => workspaceEntryDistance;
        public float WorkspaceExitDistance => workspaceExitDistance;
        public float WorkspaceSpan => workspaceSpan;
        public float LatestPickLineDistance => latestPickLineDistance;
        public bool HasPhysicalPickWindow => hasPhysicalPickWindow;
        public Vector3 LatestPickLineStartWorld { get; private set; }
        public Vector3 LatestPickLineEndWorld { get; private set; }
        public int ActiveLogicalTargetId => activeLogicalTargetId;
        public bool ExternalMotionControllerActive => externalMotionControllerActive;
        public string MotionStatus { get; private set; } = "Decision controller ready";
        public PrototypeRobotArmIKController PrototypeRobotController => prototypeRobotController;
        public long ActiveEpisodeSequence => activeEpisodeSequence;
        public string ActiveEpisodeId => activeEpisodeId;
        public int CycleTargetLogicalId => cycleTargetLogicalId;
        public string CycleTargetClass => cycleTargetClass;
        public string ActiveOutcome => activeOutcome;
        public string ActiveFailureReason => activeFailureReason;
        public int TotalGraspAttemptCount { get; private set; }
        public int SuccessfulGraspCount { get; private set; }
        public int FailedGraspCount { get; private set; }
        public int SkipCount { get; private set; }
        public int PhysicalClaimConflictCount { get; private set; }

        public event Action<GraspEpisodeSummary> EpisodeCompleted;

        public PickTargetEvaluation[] Evaluations
        {
            get
            {
                PickTargetEvaluation[] snapshot = new PickTargetEvaluation[evaluations.Count];
                evaluations.Values.CopyTo(snapshot, 0);
                Array.Sort(snapshot, (left, right) =>
                    left.LogicalTargetId.CompareTo(right.LogicalTargetId));
                return snapshot;
            }
        }

        public PickTargetEvaluation ActiveEvaluation =>
            activeLogicalTargetId < 0 || !evaluations.TryGetValue(
                activeLogicalTargetId,
                out PickTargetEvaluation evaluation)
                ? null
                : evaluation;

        private void Awake()
        {
            ApplyPrototypeTimingProfile();
            ResolveReferences();
            RebuildPhysicalPickWindow();
        }

        private void OnValidate()
        {
            ApplyPrototypeTimingProfile();
            completeCycleTimeSeconds = Mathf.Max(
                requiredGraspTimeSeconds,
                completeCycleTimeSeconds);
            if (Application.isPlaying)
            {
                RebuildPhysicalPickWindow();
            }
        }

        private void ApplyPrototypeTimingProfile()
        {
            if (!useFastPrototypePickWindow)
            {
                return;
            }

            requiredGraspTimeSeconds = 1.25f;
            safetyMarginSeconds = 0.25f;
        }

        private void Update()
        {
            if (!ResolveReferences())
            {
                return;
            }

            if (!hasPhysicalPickWindow)
            {
                RebuildPhysicalPickWindow();
            }

            float speed = sceneSetup.ConveyorObjectSpeed;
            if (!Mathf.Approximately(speed, lastCalculatedSpeed))
            {
                UpdateLatestPickLine(speed);
            }

            double now = Time.unscaledTimeAsDouble;
            UpdateArmStateFallback(now);
            EvaluateTargets(now, speed);

            if (hasPhysicalPickWindow)
            {
                Debug.DrawLine(
                    LatestPickLineStartWorld,
                    LatestPickLineEndWorld,
                    new Color(1f, 0.18f, 0.12f),
                    0f,
                    false);
            }
        }

        public PickTargetEvaluation GetEvaluation(int logicalTargetId)
        {
            evaluations.TryGetValue(logicalTargetId, out PickTargetEvaluation evaluation);
            return evaluation;
        }

        private bool ResolveReferences()
        {
            if (cameraController == null)
            {
                cameraController = GetComponent<WorkstationCameraController>();
            }
            if (workstation == null)
            {
                workstation = GetComponentInParent<RobotWorkstation>();
            }
            if (sceneSetup == null)
            {
                sceneSetup = FindFirstObjectByType<Phase1SceneSetup>();
            }

            workspace = cameraController == null ? null : cameraController.Workspace;
            conveyorPath = sceneSetup == null ? null : sceneSetup.ConveyorPath;
            conveyorLength = conveyorPath == null || conveyorPath.Spline == null
                ? 0f
                : conveyorPath.Spline.GetLength();
            bool resolved = cameraController != null && sceneSetup != null && workstation != null &&
                workspace != null && conveyorPath != null && conveyorLength > 0.001f;
            if (resolved)
            {
                EnsurePrototypeRobotController();
            }
            return resolved;
        }

        public void SetExternalMotionControllerActive(bool active)
        {
            externalMotionControllerActive = active;
        }

        public void SetMotionStatus(string status)
        {
            MotionStatus = string.IsNullOrEmpty(status) ? "Robot motion active" : status;
        }

        public void ReportGraspSucceeded()
        {
            if (ArmState != WorkstationArmState.SecuringObject)
            {
                return;
            }

            double now = Time.unscaledTimeAsDouble;
            activeOutcome = "success";
            activeFailureReason = string.Empty;
            activeGraspDuration = Mathf.Max(0f, (float)(now - attemptStartedAt));
            SuccessfulGraspCount++;
            if (evaluations.TryGetValue(
                activeLogicalTargetId,
                out PickTargetEvaluation evaluation))
            {
                evaluation.Decision = PickDecision.Completed;
                evaluation.Reason = "Object placed in the correct drop zone";
                evaluation.IsTerminal = true;
                evaluation.LastUpdatedAt = now;
            }

            cameraController.ReleaseLockedTarget();
            activeLogicalTargetId = -1;
            ArmState = WorkstationArmState.CompletingCycle;
        }

        public void ReportGraspFailed(string reason)
        {
            if (ArmState != WorkstationArmState.SecuringObject)
            {
                return;
            }

            double now = Time.unscaledTimeAsDouble;
            activeOutcome = "failed";
            activeFailureReason = string.IsNullOrEmpty(reason)
                ? "Physical grasp failed"
                : reason;
            activeGraspDuration = Mathf.Max(0f, (float)(now - attemptStartedAt));
            FailedGraspCount++;
            if (evaluations.TryGetValue(
                activeLogicalTargetId,
                out PickTargetEvaluation evaluation))
            {
                evaluation.Decision = PickDecision.Failed;
                evaluation.Reason = activeFailureReason;
                evaluation.IsTerminal = true;
                evaluation.LastUpdatedAt = now;
            }

            cameraController.ReleaseLockedTarget();
            activeLogicalTargetId = -1;
            ArmState = WorkstationArmState.CompletingCycle;
        }

        public void ReportCycleCompleted()
        {
            double now = Time.unscaledTimeAsDouble;
            if (activeEpisodeSequence > 0)
            {
                EpisodeCompleted?.Invoke(new GraspEpisodeSummary
                {
                    Sequence = activeEpisodeSequence,
                    EpisodeId = activeEpisodeId,
                    ArmId = workstation == null ? string.Empty : workstation.ArmId,
                    CameraId = workstation == null ? string.Empty : workstation.CameraId,
                    LogicalTargetId = cycleTargetLogicalId,
                    ClassName = cycleTargetClass,
                    Succeeded = activeOutcome == "success",
                    FailureReason = activeFailureReason,
                    GraspDurationSeconds = activeGraspDuration,
                    CycleDurationSeconds = Mathf.Max(0f, (float)(now - attemptStartedAt)),
                    CompletedAt = now
                });
            }

            ArmState = WorkstationArmState.Idle;
            MotionStatus = "Robot ready";
            activeEpisodeSequence = 0;
            activeEpisodeId = string.Empty;
            cycleTargetLogicalId = -1;
            cycleTargetClass = string.Empty;
            activeOutcome = string.Empty;
            activeFailureReason = string.Empty;
            activeGraspDuration = 0f;
        }

        public void ReportPhysicalClaimConflict()
        {
            PhysicalClaimConflictCount++;
        }

        private void EnsurePrototypeRobotController()
        {
            if (workstation.RobotMount == null)
            {
                return;
            }

            if (prototypeRobotController == null)
            {
                Transform placeholder = workstation.RobotMount.Find(
                    "RobotArmPlaceholder_REPLACE_ME");
                if (placeholder == null)
                {
                    return;
                }

                prototypeRobotController = placeholder.GetComponent<
                    PrototypeRobotArmIKController>();
                if (prototypeRobotController == null)
                {
                    prototypeRobotController = placeholder.gameObject.AddComponent<
                        PrototypeRobotArmIKController>();
                }
                prototypeRobotController.Configure(this, cameraController, workstation);
            }

            if (telemetryRecorder == null)
            {
                telemetryRecorder = GetComponent<So101CsvRecorder>();
                if (telemetryRecorder == null)
                {
                    telemetryRecorder = gameObject.AddComponent<So101CsvRecorder>();
                }
                telemetryRecorder.Configure(
                    this,
                    cameraController,
                    prototypeRobotController);
            }
        }

        private void RebuildPhysicalPickWindow()
        {
            hasPhysicalPickWindow = false;
            if (!ResolveReferences())
            {
                return;
            }

            (Spline closestSpline, float centerDistance) =
                SplineMeshUtils.FindClosestSplineAndPosition(
                    conveyorPath,
                    workspace.bounds.center);
            if (closestSpline == null)
            {
                return;
            }

            float sampleStep = Mathf.Max(0.01f, conveyorLength / 1024f);
            if (!IsSplinePointInsideWorkspace(centerDistance))
            {
                return;
            }

            if (!FindBoundaryDistance(centerDistance, -1f, sampleStep, out float backwardSpan) ||
                !FindBoundaryDistance(centerDistance, 1f, sampleStep, out float forwardSpan))
            {
                return;
            }

            workspaceEntryDistance = Mathf.Repeat(centerDistance - backwardSpan, conveyorLength);
            workspaceExitDistance = Mathf.Repeat(centerDistance + forwardSpan, conveyorLength);
            workspaceSpan = ForwardDistance(
                workspaceEntryDistance,
                workspaceExitDistance,
                conveyorLength);
            hasPhysicalPickWindow = workspaceSpan > 0.05f;
            UpdateLatestPickLine(sceneSetup.ConveyorObjectSpeed);
        }

        private bool FindBoundaryDistance(
            float centerDistance,
            float direction,
            float sampleStep,
            out float insideDistance)
        {
            insideDistance = 0f;
            float outsideDistance = -1f;
            for (float distance = sampleStep;
                distance <= conveyorLength * 0.5f;
                distance += sampleStep)
            {
                float sampleDistance = Mathf.Repeat(
                    centerDistance + direction * distance,
                    conveyorLength);
                if (IsSplinePointInsideWorkspace(sampleDistance))
                {
                    insideDistance = distance;
                    continue;
                }

                outsideDistance = distance;
                break;
            }

            if (outsideDistance < 0f)
            {
                return false;
            }

            for (int iteration = 0; iteration < 8; iteration++)
            {
                float midpoint = (insideDistance + outsideDistance) * 0.5f;
                float sampleDistance = Mathf.Repeat(
                    centerDistance + direction * midpoint,
                    conveyorLength);
                if (IsSplinePointInsideWorkspace(sampleDistance))
                {
                    insideDistance = midpoint;
                }
                else
                {
                    outsideDistance = midpoint;
                }
            }
            return true;
        }

        private bool IsSplinePointInsideWorkspace(float distance)
        {
            Vector3 localPoint = workspace.transform.InverseTransformPoint(
                EvaluateConveyorPosition(distance));
            Vector3 halfSize = workspace.size * 0.5f;
            Vector3 offset = localPoint - workspace.center;
            return Mathf.Abs(offset.x) <= halfSize.x &&
                Mathf.Abs(offset.z) <= halfSize.z;
        }

        private void UpdateLatestPickLine(float conveyorSpeed)
        {
            lastCalculatedSpeed = conveyorSpeed;
            if (!hasPhysicalPickWindow)
            {
                return;
            }

            float requiredDistance = Mathf.Max(0f, conveyorSpeed) * RequiredDecisionTime;
            latestPickProgress = Mathf.Clamp(workspaceSpan - requiredDistance, 0f, workspaceSpan);
            latestPickLineDistance = Mathf.Repeat(
                workspaceEntryDistance + latestPickProgress,
                conveyorLength);

            Vector3 center = EvaluateConveyorPosition(latestPickLineDistance);
            Vector3 tangent = EvaluateConveyorTangent(latestPickLineDistance);
            Vector3 side = Vector3.Cross(Vector3.up, tangent).normalized;
            center.y = workspace.bounds.min.y + 0.06f;
            LatestPickLineStartWorld = center - side * latestPickLineHalfWidth;
            LatestPickLineEndWorld = center + side * latestPickLineHalfWidth;
        }

        private void EvaluateTargets(double now, float conveyorSpeed)
        {
            PersistentVisionTarget[] targets = cameraController.PersistentTargets;
            HashSet<int> currentIds = new HashSet<int>();
            PickTargetEvaluation bestExecutable = null;

            foreach (PersistentVisionTarget target in targets)
            {
                currentIds.Add(target.LogicalId);
                PickTargetEvaluation evaluation = GetOrCreateEvaluation(target, now);
                evaluation.LastUpdatedAt = now;
                evaluation.ClassName = target.ClassName;
                evaluation.RequiredTime = RequiredDecisionTime;

                if (evaluation.IsTerminal)
                {
                    continue;
                }

                if (target.LogicalId == activeLogicalTargetId &&
                    ArmState == WorkstationArmState.SecuringObject)
                {
                    evaluation.Decision = PickDecision.Execute;
                    evaluation.Reason = "Target locked; securing object";
                    continue;
                }

                if (!target.HasConveyorPosition || !hasPhysicalPickWindow)
                {
                    SetWaiting(evaluation, "Waiting for conveyor position");
                    continue;
                }

                float progress = ForwardDistance(
                    workspaceEntryDistance,
                    target.PredictedSplineDistance,
                    conveyorLength);
                evaluation.IsInsideWorkspace = progress <= workspaceSpan + 0.02f;
                if (!evaluation.IsInsideWorkspace)
                {
                    if (evaluation.EnteredWorkspace)
                    {
                        SetTerminalSkip(evaluation, "Object left the local workspace");
                    }
                    else
                    {
                        SetWaiting(evaluation, "Approaching local workspace");
                    }
                    continue;
                }

                evaluation.EnteredWorkspace = true;
                evaluation.RemainingDistance = Mathf.Max(0f, workspaceSpan - progress);
                evaluation.RemainingTime = conveyorSpeed <= 0.001f
                    ? float.PositiveInfinity
                    : evaluation.RemainingDistance / conveyorSpeed;
                evaluation.HasCrossedLatestPickLine = progress > latestPickProgress + 0.01f;

                if (evaluation.RemainingTime + 0.001f < RequiredDecisionTime)
                {
                    SetTerminalSkip(evaluation, "Not enough time remaining to secure object");
                    continue;
                }

                if (evaluation.HasCrossedLatestPickLine)
                {
                    SetTerminalSkip(evaluation, "Crossed Latest Pick Line before lock");
                    continue;
                }

                if (target.State != PersistentVisionTargetState.Confirmed)
                {
                    SetWaiting(evaluation, "Waiting for confirmed detection");
                    continue;
                }

                if (ArmState != WorkstationArmState.Idle)
                {
                    SetWaiting(evaluation, "Robotic arm is busy");
                    continue;
                }

                evaluation.Decision = PickDecision.Execute;
                evaluation.Reason = "Enough time to pick";
                if (bestExecutable == null ||
                    evaluation.RemainingTime < bestExecutable.RemainingTime)
                {
                    bestExecutable = evaluation;
                }
            }

            if (bestExecutable != null && automaticallyLockExecutableTargets)
            {
                BeginPick(bestExecutable, now);
            }

            RemoveExpiredEvaluations(currentIds, now);
        }

        private PickTargetEvaluation GetOrCreateEvaluation(
            PersistentVisionTarget target,
            double now)
        {
            if (evaluations.TryGetValue(target.LogicalId, out PickTargetEvaluation evaluation))
            {
                return evaluation;
            }

            evaluation = new PickTargetEvaluation
            {
                LogicalTargetId = target.LogicalId,
                ClassName = target.ClassName,
                Decision = PickDecision.Waiting,
                Reason = "Waiting for target confirmation",
                RequiredTime = RequiredDecisionTime,
                LastUpdatedAt = now
            };
            evaluations.Add(target.LogicalId, evaluation);
            return evaluation;
        }

        private void BeginPick(PickTargetEvaluation evaluation, double now)
        {
            if (!cameraController.TryLockTarget(
                evaluation.LogicalTargetId,
                out PersistentVisionTarget lockedTarget) ||
                lockedTarget == null)
            {
                SetWaiting(evaluation, "Target confirmation changed before lock");
                return;
            }

            activeLogicalTargetId = evaluation.LogicalTargetId;
            ArmState = WorkstationArmState.SecuringObject;
            activeEpisodeSequence = ++episodeSequence;
            activeEpisodeId = $"{workstation.ArmId}_{activeEpisodeSequence:D6}";
            cycleTargetLogicalId = evaluation.LogicalTargetId;
            cycleTargetClass = evaluation.ClassName ?? string.Empty;
            activeOutcome = "pending";
            activeFailureReason = string.Empty;
            activeGraspDuration = 0f;
            attemptStartedAt = now;
            TotalGraspAttemptCount++;
            graspSecuredAt = now + requiredGraspTimeSeconds;
            cycleCompletedAt = now + completeCycleTimeSeconds;
            evaluation.Decision = PickDecision.Execute;
            evaluation.Reason = "Target locked; local grasp started";
        }

        private void UpdateArmStateFallback(double now)
        {
            if (externalMotionControllerActive)
            {
                return;
            }

            if (ArmState == WorkstationArmState.SecuringObject && now >= graspSecuredAt)
            {
                if (evaluations.TryGetValue(
                    activeLogicalTargetId,
                    out PickTargetEvaluation evaluation))
                {
                    evaluation.Decision = PickDecision.Completed;
                    evaluation.Reason = "Object secured; completing arm cycle";
                    evaluation.IsTerminal = true;
                    evaluation.LastUpdatedAt = now;
                }

                cameraController.ReleaseLockedTarget();
                activeLogicalTargetId = -1;
                ArmState = WorkstationArmState.CompletingCycle;
            }

            if (ArmState == WorkstationArmState.CompletingCycle && now >= cycleCompletedAt)
            {
                ArmState = WorkstationArmState.Idle;
            }
        }

        private void RemoveExpiredEvaluations(HashSet<int> currentIds, double now)
        {
            List<int> expired = new List<int>();
            foreach (KeyValuePair<int, PickTargetEvaluation> pair in evaluations)
            {
                if (pair.Key == activeLogicalTargetId ||
                    (activeEpisodeSequence > 0 && pair.Key == cycleTargetLogicalId) ||
                    currentIds.Contains(pair.Key))
                {
                    continue;
                }

                if (now - pair.Value.LastUpdatedAt > completedEvaluationRetentionSeconds)
                {
                    expired.Add(pair.Key);
                }
            }

            foreach (int logicalId in expired)
            {
                evaluations.Remove(logicalId);
            }
        }

        private static void SetWaiting(PickTargetEvaluation evaluation, string reason)
        {
            evaluation.Decision = PickDecision.Waiting;
            evaluation.Reason = reason;
        }

        private void SetTerminalSkip(PickTargetEvaluation evaluation, string reason)
        {
            if (!evaluation.IsTerminal)
            {
                SkipCount++;
            }
            evaluation.Decision = PickDecision.Skip;
            evaluation.Reason = reason;
            evaluation.IsTerminal = true;
        }

        private Vector3 EvaluateConveyorPosition(float distance)
        {
            float normalized = SplineUtility.GetNormalizedInterpolation(
                conveyorPath.Spline,
                Mathf.Repeat(distance, conveyorLength),
                PathIndexUnit.Distance);
            return conveyorPath.EvaluatePosition(normalized);
        }

        private Vector3 EvaluateConveyorTangent(float distance)
        {
            float normalized = SplineUtility.GetNormalizedInterpolation(
                conveyorPath.Spline,
                Mathf.Repeat(distance, conveyorLength),
                PathIndexUnit.Distance);
            Vector3 tangent = conveyorPath.EvaluateTangent(normalized);
            tangent.y = 0f;
            return tangent.sqrMagnitude > 0.0001f ? tangent.normalized : Vector3.forward;
        }

        private static float ForwardDistance(float from, float to, float length)
        {
            return Mathf.Repeat(to - from, length);
        }
    }
}
