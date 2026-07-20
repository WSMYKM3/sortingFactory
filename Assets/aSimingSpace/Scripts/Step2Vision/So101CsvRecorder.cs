using System;
using System.Globalization;
using System.IO;
using System.Text;
using SortingFactory.Step2;
using SortingFactory.Step4;
using UnityEngine;

namespace SortingFactory.Step8
{
    [DisallowMultipleComponent]
    public sealed class So101CsvRecorder : MonoBehaviour
    {
        public const string OutputRoot =
            "/Users/simon/Documents/WsmFiles/SortingFactoryScreenshots/csvdata";

        private const string Header =
            "record_type,run_id,episode_id,arm_id,camera_id,utc_timestamp," +
            "unity_time_s,episode_time_s,camera_frame_id," +
            "observation_shoulder_pan,observation_shoulder_lift," +
            "observation_elbow_flex,observation_wrist_flex," +
            "observation_wrist_roll,observation_gripper," +
            "action_shoulder_pan,action_shoulder_lift,action_elbow_flex," +
            "action_wrist_flex,action_wrist_roll,action_gripper," +
            "workflow_state,motion_state,streaming,vision_connected," +
            "logical_target_id,source_track_id,object_class,confidence,target_state," +
            "world_x,world_y,world_z,remaining_pickable_time_s," +
            "required_grasp_time_s,decision,crossed_latest_pick_line," +
            "outcome,failure_reason,grasp_duration_s,cycle_duration_s," +
            "drop_zone_reached";

        [SerializeField, Min(1f)] private float sampleRateHz = 10f;

        private static string runId;
        private WorkstationPickDecisionController decisionController;
        private WorkstationCameraController cameraController;
        private PrototypeRobotArmIKController robotController;
        private StreamWriter writer;
        private float nextSampleTime;
        private float nextFlushTime;
        private string timedEpisodeId = string.Empty;
        private double timedEpisodeStartedAt;
        private bool configured;

        public string RunId => GetRunId();
        public string OutputPath { get; private set; } = string.Empty;
        public long FrameRecordCount { get; private set; }
        public long EpisodeRecordCount { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRunId()
        {
            runId = string.Empty;
        }

        public void Configure(
            WorkstationPickDecisionController newDecisionController,
            WorkstationCameraController newCameraController,
            PrototypeRobotArmIKController newRobotController)
        {
            if (decisionController != null)
            {
                decisionController.EpisodeCompleted -= OnEpisodeCompleted;
            }

            decisionController = newDecisionController;
            cameraController = newCameraController;
            robotController = newRobotController;
            configured = decisionController != null && cameraController != null;
            if (!configured)
            {
                return;
            }

            decisionController.EpisodeCompleted += OnEpisodeCompleted;
            OpenWriter();
        }

        private void LateUpdate()
        {
            if (!configured || writer == null || Time.unscaledTime < nextSampleTime)
            {
                return;
            }

            nextSampleTime = Time.unscaledTime + 1f / Mathf.Max(1f, sampleRateHz);
            WriteFrameRecord();
            if (Time.unscaledTime >= nextFlushTime)
            {
                writer.Flush();
                nextFlushTime = Time.unscaledTime + 1f;
            }
        }

        private void OnDestroy()
        {
            if (decisionController != null)
            {
                decisionController.EpisodeCompleted -= OnEpisodeCompleted;
            }

            if (writer != null)
            {
                writer.Flush();
                writer.Dispose();
                writer = null;
            }
        }

        private void OpenWriter()
        {
            if (writer != null)
            {
                return;
            }

            try
            {
                string runFolder = Path.Combine(OutputRoot, GetRunId());
                Directory.CreateDirectory(runFolder);
                OutputPath = Path.Combine(
                    runFolder,
                    $"{cameraController.RobotArmId}.csv");
                writer = new StreamWriter(
                    OutputPath,
                    false,
                    new UTF8Encoding(false));
                writer.WriteLine(Header);
                writer.Flush();
                nextSampleTime = Time.unscaledTime;
                nextFlushTime = Time.unscaledTime + 1f;
                Debug.Log($"Step 8 SO-101 CSV recording started: {OutputPath}", this);
            }
            catch (Exception exception)
            {
                configured = false;
                Debug.LogError(
                    $"Could not start SO-101 CSV recording at {OutputRoot}: {exception.Message}",
                    this);
            }
        }

        private void WriteFrameRecord()
        {
            PersistentVisionTarget target = FindCurrentTarget();
            PickTargetEvaluation evaluation = FindCurrentEvaluation(target);
            string[] row = BuildCommonRow(
                "frame",
                decisionController.ActiveEpisodeId,
                target,
                evaluation);
            row[37] = decisionController.ActiveOutcome;
            row[38] = decisionController.ActiveFailureReason;
            writer.WriteLine(JoinCsv(row));
            FrameRecordCount++;
        }

        private void OnEpisodeCompleted(GraspEpisodeSummary summary)
        {
            if (writer == null || summary == null)
            {
                return;
            }

            PickTargetEvaluation evaluation = decisionController.GetEvaluation(
                summary.LogicalTargetId);
            string[] row = BuildCommonRow(
                "episode",
                summary.EpisodeId,
                null,
                evaluation);
            row[25] = summary.LogicalTargetId.ToString(CultureInfo.InvariantCulture);
            row[27] = summary.ClassName;
            row[37] = summary.Succeeded ? "success" : "failed";
            row[38] = summary.FailureReason;
            row[39] = Format(summary.GraspDurationSeconds);
            row[40] = Format(summary.CycleDurationSeconds);
            row[41] = summary.Succeeded ? "true" : "false";
            writer.WriteLine(JoinCsv(row));
            writer.Flush();
            EpisodeRecordCount++;
        }

        private string[] BuildCommonRow(
            string recordType,
            string episodeId,
            PersistentVisionTarget target,
            PickTargetEvaluation evaluation)
        {
            float pan = robotController == null ? 0f : robotController.ShoulderPanPosition;
            float lift = robotController == null ? 0f : robotController.ShoulderLiftPosition;
            float elbow = robotController == null ? 0f : robotController.ElbowFlexPosition;
            float wristFlex = robotController == null ? 0f : robotController.WristFlexPosition;
            float wristRoll = robotController == null ? 0f : robotController.WristRollPosition;
            float gripper = robotController == null ? 0f : robotController.GripperPosition;
            string episodeTime = GetEpisodeTime(episodeId);
            Vector3 worldPosition = target == null || !target.HasConveyorPosition
                ? Vector3.zero
                : target.PredictedBeltPosition;

            return new[]
            {
                recordType,
                GetRunId(),
                episodeId ?? string.Empty,
                cameraController.RobotArmId,
                cameraController.CameraId,
                DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                Format(Time.unscaledTime),
                episodeTime,
                cameraController.CapturedFrameCount.ToString(CultureInfo.InvariantCulture),
                Format(pan), Format(lift), Format(elbow), Format(wristFlex),
                Format(wristRoll), Format(gripper),
                Format(pan), Format(lift), Format(elbow), Format(wristFlex),
                Format(wristRoll), Format(gripper),
                decisionController.ArmState.ToString(),
                robotController == null ? string.Empty : robotController.MotionState,
                cameraController.IsStreaming ? "true" : "false",
                cameraController.IsConnected ? "true" : "false",
                target == null
                    ? FormatTargetId(decisionController.CycleTargetLogicalId)
                    : target.LogicalId.ToString(CultureInfo.InvariantCulture),
                target == null
                    ? string.Empty
                    : target.SourceTrackId.ToString(CultureInfo.InvariantCulture),
                target == null ? decisionController.CycleTargetClass : target.ClassName,
                target == null ? string.Empty : Format(target.Confidence),
                target == null ? string.Empty : target.State.ToString(),
                target == null ? string.Empty : Format(worldPosition.x),
                target == null ? string.Empty : Format(worldPosition.y),
                target == null ? string.Empty : Format(worldPosition.z),
                evaluation == null ? string.Empty : Format(evaluation.RemainingTime),
                evaluation == null ? string.Empty : Format(evaluation.RequiredTime),
                evaluation == null ? string.Empty : evaluation.Decision.ToString(),
                evaluation != null && evaluation.HasCrossedLatestPickLine ? "true" : "false",
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty
            };
        }

        private string GetEpisodeTime(string episodeId)
        {
            if (string.IsNullOrEmpty(episodeId))
            {
                timedEpisodeId = string.Empty;
                timedEpisodeStartedAt = 0d;
                return string.Empty;
            }

            double now = Time.unscaledTimeAsDouble;
            if (!string.Equals(timedEpisodeId, episodeId, StringComparison.Ordinal))
            {
                timedEpisodeId = episodeId;
                timedEpisodeStartedAt = now;
                return Format(0f);
            }

            return Format((float)Math.Max(0d, now - timedEpisodeStartedAt));
        }

        private PersistentVisionTarget FindCurrentTarget()
        {
            PersistentVisionTarget locked = cameraController.LockedTarget;
            if (locked != null)
            {
                return locked;
            }

            int targetId = decisionController.CycleTargetLogicalId;
            foreach (PersistentVisionTarget target in cameraController.PersistentTargets)
            {
                if (target.LogicalId == targetId)
                {
                    return target;
                }
            }
            return null;
        }

        private PickTargetEvaluation FindCurrentEvaluation(PersistentVisionTarget target)
        {
            PickTargetEvaluation active = decisionController.ActiveEvaluation;
            if (active != null)
            {
                return active;
            }

            int targetId = target == null
                ? decisionController.CycleTargetLogicalId
                : target.LogicalId;
            return targetId < 0 ? null : decisionController.GetEvaluation(targetId);
        }

        private static string GetRunId()
        {
            if (string.IsNullOrEmpty(runId))
            {
                runId = DateTime.Now.ToString(
                    "yyyy-MM-dd_HH-mm-ss-fff",
                    CultureInfo.InvariantCulture);
            }
            return runId;
        }

        private static string Format(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value)
                ? string.Empty
                : value.ToString("0.######", CultureInfo.InvariantCulture);
        }

        private static string FormatTargetId(int targetId)
        {
            return targetId < 0
                ? string.Empty
                : targetId.ToString(CultureInfo.InvariantCulture);
        }

        private static string JoinCsv(string[] values)
        {
            StringBuilder builder = new StringBuilder(512);
            for (int index = 0; index < values.Length; index++)
            {
                if (index > 0)
                {
                    builder.Append(',');
                }

                string value = values[index] ?? string.Empty;
                if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) < 0)
                {
                    builder.Append(value);
                    continue;
                }

                builder.Append('"');
                builder.Append(value.Replace("\"", "\"\""));
                builder.Append('"');
            }
            return builder.ToString();
        }
    }
}
