using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using SortingFactory.Phase1;
using SortingFactory.Step2;
using SortingFactory.Step4;
using SortingFactory.Step8;
using UnityEngine;
using UnityEngine.Networking;

namespace SortingFactory.Step9
{
    [DisallowMultipleComponent]
    public sealed class FactoryControlRoomController : MonoBehaviour
    {
        private enum SessionRuntimeState
        {
            Inactive,
            WaitingToStart,
            Recording,
            Stopping
        }

        [Serializable]
        private sealed class ControlState
        {
            public long revision;
            public bool session_requested;
            public bool conveyor_running = true;
            public float conveyor_speed = 0.5f;
            public ArmControl[] arms = Array.Empty<ArmControl>();
        }

        [Serializable]
        private sealed class ArmControl
        {
            public string arm_id;
            public bool enabled = true;
            public bool stream_enabled;
        }

        [Serializable]
        private sealed class FactoryTelemetry
        {
            public string unity_status;
            public string session_state;
            public string session_id;
            public float session_duration_s;
            public bool conveyor_running;
            public float conveyor_speed;
            public int active_arm_count;
            public int active_stream_count;
            public int connected_vision_count;
            public int total_attempts;
            public int successful_grasps;
            public int failed_grasps;
            public int skipped_objects;
            public float success_rate;
            public float throughput_per_minute;
            public float average_task_duration_s;
            public string last_error;
            public ArmTelemetry[] arms = Array.Empty<ArmTelemetry>();
        }

        [Serializable]
        private sealed class ArmTelemetry
        {
            public string arm_id;
            public string camera_id;
            public bool enabled;
            public bool disable_pending;
            public bool streaming;
            public bool vision_connected;
            public string workflow_state;
            public string motion_state;
            public string current_target;
            public string decision;
            public int successful_grasps;
            public int failed_grasps;
            public int total_attempts;
            public int skipped_objects;
            public float success_rate;
            public float average_grasp_time_s;
            public float average_cycle_time_s;
            public float throughput_per_minute;
            public float utilization;
            public string last_failure_reason;
            public long camera_frames;
        }

        [SerializeField] private string controlRoomServer = "http://127.0.0.1:8000";
        [SerializeField, Range(0.2f, 5f)] private float syncIntervalSeconds = 0.5f;

        private readonly Dictionary<string, double> armBusySeconds =
            new Dictionary<string, double>();
        private Phase1SceneSetup sceneSetup;
        private WorkstationCameraController[] cameras =
            Array.Empty<WorkstationCameraController>();
        private WorkstationPickDecisionController[] decisionControllers =
            Array.Empty<WorkstationPickDecisionController>();
        private So101CsvRecorder[] recorders = Array.Empty<So101CsvRecorder>();
        private SessionRuntimeState sessionState;
        private string sessionId = string.Empty;
        private string lastCompletedSessionId = string.Empty;
        private string lastError = string.Empty;
        private string sessionStartedUtc = string.Empty;
        private double sessionStartedAt;
        private double previousUpdateAt;
        private bool requestedSessionState;

        public string SessionState => sessionState.ToString();
        public string SessionId => sessionId;
        public bool IsSessionRecording => sessionState == SessionRuntimeState.Recording;

        private void Awake()
        {
            sceneSetup = GetComponent<Phase1SceneSetup>();
            previousUpdateAt = Time.unscaledTimeAsDouble;
        }

        private void Start()
        {
            RefreshReferences();
            StartCoroutine(SynchronizeWithControlRoom());
        }

        private void Update()
        {
            double now = Time.unscaledTimeAsDouble;
            double elapsed = Math.Max(0d, now - previousUpdateAt);
            previousUpdateAt = now;

            if (sessionState == SessionRuntimeState.Recording ||
                sessionState == SessionRuntimeState.Stopping)
            {
                foreach (WorkstationPickDecisionController controller in decisionControllers)
                {
                    if (controller != null && controller.ArmState != WorkstationArmState.Idle)
                    {
                        string armId = GetArmId(controller);
                        armBusySeconds.TryGetValue(armId, out double busySeconds);
                        armBusySeconds[armId] = busySeconds + elapsed;
                    }
                }
            }

            if (sessionState == SessionRuntimeState.WaitingToStart)
            {
                TryStartSession();
            }
            else if (sessionState == SessionRuntimeState.Stopping && AllArmsIdle())
            {
                FinalizeSession();
            }
        }

        private void OnDestroy()
        {
            foreach (So101CsvRecorder recorder in recorders)
            {
                recorder?.StopSession();
            }
        }

        private IEnumerator SynchronizeWithControlRoom()
        {
            WaitForSecondsRealtime delay = new WaitForSecondsRealtime(syncIntervalSeconds);
            while (true)
            {
                RefreshReferences();
                yield return PullControlState();
                yield return PushTelemetry();
                yield return delay;
            }
        }

        private IEnumerator PullControlState()
        {
            using UnityWebRequest request = UnityWebRequest.Get(
                $"{controlRoomServer.TrimEnd('/')}/api/control");
            request.timeout = 2;
            yield return request.SendWebRequest();
            if (request.result != UnityWebRequest.Result.Success)
            {
                lastError = "Control room offline";
                yield break;
            }

            ControlState state = JsonUtility.FromJson<ControlState>(
                request.downloadHandler.text);
            if (state == null)
            {
                lastError = "Invalid control response";
                yield break;
            }

            lastError = string.Empty;
            ApplyControlState(state);
        }

        private IEnumerator PushTelemetry()
        {
            FactoryTelemetry telemetry = BuildTelemetry();
            byte[] body = Encoding.UTF8.GetBytes(JsonUtility.ToJson(telemetry));
            using UnityWebRequest request = new UnityWebRequest(
                $"{controlRoomServer.TrimEnd('/')}/api/telemetry",
                UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = 2;
            yield return request.SendWebRequest();
        }

        private void ApplyControlState(ControlState state)
        {
            if (sceneSetup != null)
            {
                sceneSetup.SetConveyorObjectSpeed(Mathf.Clamp(state.conveyor_speed, 0f, 2f));
                sceneSetup.SetConveyorRunning(state.conveyor_running);
            }

            foreach (ArmControl armControl in state.arms ?? Array.Empty<ArmControl>())
            {
                WorkstationPickDecisionController controller = Array.Find(
                    decisionControllers,
                    candidate => GetArmId(candidate) == armControl.arm_id);
                controller?.SetOperationalEnabled(armControl.enabled);

                WorkstationCameraController camera = Array.Find(
                    cameras,
                    candidate => candidate != null && candidate.RobotArmId == armControl.arm_id);
                if (camera != null && camera.IsStreaming != armControl.stream_enabled)
                {
                    camera.SetStreaming(armControl.stream_enabled);
                }
            }

            requestedSessionState = state.session_requested;
            if (requestedSessionState && sessionState == SessionRuntimeState.Inactive)
            {
                sessionState = SessionRuntimeState.WaitingToStart;
                TryStartSession();
            }
            else if (!requestedSessionState &&
                (sessionState == SessionRuntimeState.Recording ||
                 sessionState == SessionRuntimeState.WaitingToStart))
            {
                BeginStoppingSession();
            }
        }

        private void TryStartSession()
        {
            RefreshReferences();
            if (!requestedSessionState || !AllArmsIdle() || decisionControllers.Length == 0 ||
                recorders.Length < decisionControllers.Length)
            {
                return;
            }

            foreach (WorkstationPickDecisionController controller in decisionControllers)
            {
                if (!controller.ResetSessionCounters())
                {
                    return;
                }
                controller.SetSessionAcceptingTasks(true);
            }

            sessionId = DateTime.Now.ToString(
                "yyyy-MM-dd_HH-mm-ss-fff",
                CultureInfo.InvariantCulture);
            sessionStartedAt = Time.unscaledTimeAsDouble;
            sessionStartedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            armBusySeconds.Clear();
            foreach (WorkstationPickDecisionController controller in decisionControllers)
            {
                armBusySeconds[GetArmId(controller)] = 0d;
            }

            bool started = true;
            foreach (So101CsvRecorder recorder in recorders)
            {
                started &= recorder.StartSession(sessionId);
            }

            if (!started)
            {
                foreach (So101CsvRecorder recorder in recorders)
                {
                    recorder.StopSession();
                }
                lastError = "Could not open one or more Session CSV files";
                sessionState = SessionRuntimeState.Inactive;
                return;
            }

            WriteSessionMetadata();
            lastError = string.Empty;
            sessionState = SessionRuntimeState.Recording;
            Debug.Log($"Sorting Factory Session started: {sessionId}", this);
        }

        private void BeginStoppingSession()
        {
            if (sessionState == SessionRuntimeState.WaitingToStart)
            {
                sessionState = SessionRuntimeState.Inactive;
                return;
            }

            sessionState = SessionRuntimeState.Stopping;
            foreach (WorkstationPickDecisionController controller in decisionControllers)
            {
                controller.SetSessionAcceptingTasks(false);
            }

            if (AllArmsIdle())
            {
                FinalizeSession();
            }
        }

        private void FinalizeSession()
        {
            foreach (So101CsvRecorder recorder in recorders)
            {
                recorder.StopSession();
            }
            WriteSessionSummary();

            foreach (WorkstationPickDecisionController controller in decisionControllers)
            {
                controller.SetSessionAcceptingTasks(true);
            }

            lastCompletedSessionId = sessionId;
            Debug.Log($"Sorting Factory Session stopped: {sessionId}", this);
            sessionId = string.Empty;
            sessionState = SessionRuntimeState.Inactive;
        }

        private FactoryTelemetry BuildTelemetry()
        {
            float sessionDuration = GetSessionDuration();
            ArmTelemetry[] arms = decisionControllers
                .Where(controller => controller != null)
                .OrderBy(GetArmId)
                .Select(controller => BuildArmTelemetry(controller, sessionDuration))
                .ToArray();
            int attempts = arms.Sum(arm => arm.total_attempts);
            int successes = arms.Sum(arm => arm.successful_grasps);
            int completedEpisodes = decisionControllers.Sum(
                controller => controller.CompletedEpisodeCount);
            float totalCycleDuration = decisionControllers.Sum(
                controller => controller.TotalCycleDurationSeconds);

            return new FactoryTelemetry
            {
                unity_status = "online",
                session_state = sessionState.ToString(),
                session_id = string.IsNullOrEmpty(sessionId)
                    ? lastCompletedSessionId
                    : sessionId,
                session_duration_s = sessionDuration,
                conveyor_running = sceneSetup != null && sceneSetup.IsConveyorRunning,
                conveyor_speed = sceneSetup == null
                    ? 0f
                    : sceneSetup.ConfiguredConveyorObjectSpeed,
                active_arm_count = arms.Count(arm => arm.workflow_state != "Idle"),
                active_stream_count = cameras.Count(camera => camera != null && camera.IsStreaming),
                connected_vision_count = cameras.Count(camera => camera != null && camera.IsConnected),
                total_attempts = attempts,
                successful_grasps = successes,
                failed_grasps = arms.Sum(arm => arm.failed_grasps),
                skipped_objects = arms.Sum(arm => arm.skipped_objects),
                success_rate = attempts <= 0 ? 0f : (float)successes / attempts,
                throughput_per_minute = sessionDuration <= 0.001f
                    ? 0f
                    : successes * 60f / sessionDuration,
                average_task_duration_s = completedEpisodes <= 0
                    ? 0f
                    : totalCycleDuration / completedEpisodes,
                last_error = lastError,
                arms = arms
            };
        }

        private ArmTelemetry BuildArmTelemetry(
            WorkstationPickDecisionController controller,
            float sessionDuration)
        {
            string armId = GetArmId(controller);
            WorkstationCameraController camera = Array.Find(
                cameras,
                candidate => candidate != null && candidate.RobotArmId == armId);
            PickTargetEvaluation evaluation = controller.ActiveEvaluation;
            armBusySeconds.TryGetValue(armId, out double busySeconds);

            return new ArmTelemetry
            {
                arm_id = armId,
                camera_id = camera == null ? string.Empty : camera.CameraId,
                enabled = controller.IsOperationalEnabled,
                disable_pending = controller.IsDisablePending,
                streaming = camera != null && camera.IsStreaming,
                vision_connected = camera != null && camera.IsConnected,
                workflow_state = controller.ArmState.ToString(),
                motion_state = controller.PrototypeRobotController == null
                    ? string.Empty
                    : controller.PrototypeRobotController.MotionState,
                current_target = string.IsNullOrEmpty(controller.CycleTargetClass)
                    ? "No Target"
                    : controller.CycleTargetClass,
                decision = evaluation == null ? string.Empty : evaluation.Decision.ToString(),
                successful_grasps = controller.SuccessfulGraspCount,
                failed_grasps = controller.FailedGraspCount,
                total_attempts = controller.TotalGraspAttemptCount,
                skipped_objects = controller.SkipCount,
                success_rate = controller.SuccessRate,
                average_grasp_time_s = controller.AverageGraspDurationSeconds,
                average_cycle_time_s = controller.AverageCycleDurationSeconds,
                throughput_per_minute = sessionDuration <= 0.001f
                    ? 0f
                    : controller.SuccessfulGraspCount * 60f / sessionDuration,
                utilization = sessionDuration <= 0.001f
                    ? 0f
                    : Mathf.Clamp01((float)(busySeconds / sessionDuration)),
                last_failure_reason = controller.LastFailureReason,
                camera_frames = camera == null ? 0 : camera.CapturedFrameCount
            };
        }

        private void RefreshReferences()
        {
            cameras = FindObjectsByType<WorkstationCameraController>(FindObjectsSortMode.None);
            decisionControllers = FindObjectsByType<WorkstationPickDecisionController>(
                FindObjectsSortMode.None);
            recorders = FindObjectsByType<So101CsvRecorder>(FindObjectsSortMode.None);
        }

        private bool AllArmsIdle()
        {
            return decisionControllers.All(controller =>
                controller == null || controller.ArmState == WorkstationArmState.Idle);
        }

        private float GetSessionDuration()
        {
            return sessionState == SessionRuntimeState.Inactive || sessionStartedAt <= 0d
                ? 0f
                : Mathf.Max(0f, (float)(Time.unscaledTimeAsDouble - sessionStartedAt));
        }

        private void WriteSessionMetadata()
        {
            try
            {
                string folder = Path.Combine(So101CsvRecorder.OutputRoot, sessionId);
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, "metadata.csv");
                string[] lines =
                {
                    "key,value",
                    $"session_id,{sessionId}",
                    $"started_utc,{sessionStartedUtc}",
                    $"conveyor_running,{(sceneSetup != null && sceneSetup.IsConveyorRunning).ToString().ToLowerInvariant()}",
                    $"conveyor_speed,{Format(sceneSetup == null ? 0f : sceneSetup.ConfiguredConveyorObjectSpeed)}",
                    $"arm_count,{decisionControllers.Length}",
                    $"camera_count,{cameras.Length}"
                };
                List<string> metadata = new List<string>(lines);
                foreach (WorkstationPickDecisionController controller in
                    decisionControllers.OrderBy(GetArmId))
                {
                    string armId = GetArmId(controller);
                    WorkstationCameraController camera = Array.Find(
                        cameras,
                        candidate => candidate != null && candidate.RobotArmId == armId);
                    metadata.Add($"{armId}_enabled,{controller.IsOperationalEnabled.ToString().ToLowerInvariant()}");
                    metadata.Add($"{armId}_camera_id,{camera?.CameraId ?? string.Empty}");
                    metadata.Add($"{armId}_streaming,{(camera != null && camera.IsStreaming).ToString().ToLowerInvariant()}");
                    metadata.Add($"{armId}_vision_connected,{(camera != null && camera.IsConnected).ToString().ToLowerInvariant()}");
                }
                File.WriteAllLines(path, metadata, new UTF8Encoding(false));
            }
            catch (Exception exception)
            {
                lastError = $"Metadata write failed: {exception.Message}";
            }
        }

        private void WriteSessionSummary()
        {
            if (string.IsNullOrEmpty(sessionId))
            {
                return;
            }

            try
            {
                string folder = Path.Combine(So101CsvRecorder.OutputRoot, sessionId);
                Directory.CreateDirectory(folder);
                int attempts = decisionControllers.Sum(controller => controller.TotalGraspAttemptCount);
                int successes = decisionControllers.Sum(controller => controller.SuccessfulGraspCount);
                int failures = decisionControllers.Sum(controller => controller.FailedGraspCount);
                int skips = decisionControllers.Sum(controller => controller.SkipCount);
                float duration = GetSessionDuration();
                float averageGrasp = decisionControllers.Sum(
                    controller => controller.TotalGraspDurationSeconds) /
                    Mathf.Max(1, decisionControllers.Sum(controller => controller.CompletedEpisodeCount));
                float averageCycle = decisionControllers.Sum(
                    controller => controller.TotalCycleDurationSeconds) /
                    Mathf.Max(1, decisionControllers.Sum(controller => controller.CompletedEpisodeCount));
                string path = Path.Combine(folder, "session_summary.csv");
                string header =
                    "session_id,started_utc,ended_utc,duration_s,total_attempts," +
                    "successful_grasps,failed_grasps,skipped_objects,success_rate," +
                    "average_grasp_time_s,average_cycle_time_s,throughput_per_minute";
                string values = string.Join(",", new[]
                {
                    sessionId,
                    sessionStartedUtc,
                    DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    Format(duration),
                    attempts.ToString(CultureInfo.InvariantCulture),
                    successes.ToString(CultureInfo.InvariantCulture),
                    failures.ToString(CultureInfo.InvariantCulture),
                    skips.ToString(CultureInfo.InvariantCulture),
                    Format(attempts <= 0 ? 0f : (float)successes / attempts),
                    Format(averageGrasp),
                    Format(averageCycle),
                    Format(duration <= 0.001f ? 0f : successes * 60f / duration)
                });
                File.WriteAllLines(path, new[] { header, values }, new UTF8Encoding(false));
            }
            catch (Exception exception)
            {
                lastError = $"Session summary write failed: {exception.Message}";
            }
        }

        private static string GetArmId(WorkstationPickDecisionController controller)
        {
            WorkstationCameraController camera = controller == null
                ? null
                : controller.GetComponent<WorkstationCameraController>();
            return camera == null ? string.Empty : camera.RobotArmId;
        }

        private static string Format(float value)
        {
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }
    }
}
