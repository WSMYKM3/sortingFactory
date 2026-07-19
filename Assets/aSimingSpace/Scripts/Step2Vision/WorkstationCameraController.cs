using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using SortingFactory.Phase1;
using SplineMeshTools.Core;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Splines;

namespace SortingFactory.Step2
{
    [RequireComponent(typeof(Camera))]
    public sealed class WorkstationCameraController : MonoBehaviour
    {
        private const int CurrentVisionFramingVersion = 4;

        [Header("Identity")]
        [SerializeField] private string robotArmId = "arm_1";
        [SerializeField] private string cameraId = "arm_1_camera";

        [Header("Capture")]
        [SerializeField] private Camera stationCamera;
        [SerializeField] private BoxCollider workspace;
        [SerializeField, Min(1)] private int captureWidth = 1280;
        [SerializeField, Min(1)] private int captureHeight = 720;
        [SerializeField, Range(1, 100)] private int jpegQuality = 85;
        [SerializeField, Range(1f, 30f)] private float streamFramesPerSecond = 10f;
        [SerializeField] private string captureFolder =
            "/Users/simon/Documents/WsmFiles/SortingFactoryScreenshots";

        [Header("Vision Framing")]
        [SerializeField] private bool autoFrameWorkspace = true;
        [SerializeField] private bool useOverheadCamera = true;
        [SerializeField, Min(2f)] private float overheadHeightAboveBelt = 5f;
        [SerializeField, Min(0f)] private float overheadSideOffsetAwayFromRobot;
        [SerializeField] private float overheadAlongBeltOffset = -0.25f;
        [SerializeField, Min(0.5f)] private float cameraSideDistance = 3.65f;
        [SerializeField, Min(0.5f)] private float cameraHeightAboveBelt = 3.2f;
        [SerializeField] private float cameraAlongBeltOffset = -1.6f;
        [SerializeField, Min(0f)] private float targetHeightAboveBelt = 0.55f;
        [SerializeField] private float targetAlongBeltOffset = 0.45f;
        [SerializeField, Range(25f, 80f)] private float visionFieldOfView = 46f;
        [SerializeField, Range(0.6f, 1.2f)] private float cameraViewDistanceScale = 0.78f;
        [SerializeField, HideInInspector] private int visionFramingVersion;

        [Header("Vision Server")]
        [SerializeField] private string serverUrl = "ws://127.0.0.1:8000/ws/camera";

        [Header("Overlay Smoothing")]
        [SerializeField, Range(1f, 40f)] private float boundingBoxSmoothingSpeed = 14f;

        [Header("Persistent Tracking")]
        [SerializeField, Min(1)] private int targetConfirmationHits = 3;
        [SerializeField, Range(0.05f, 0.5f)] private float coastingDelaySeconds = 0.18f;
        [SerializeField, Range(0.2f, 1f)] private float tentativeTimeoutSeconds = 0.45f;
        [SerializeField, Range(0.3f, 2f)] private float targetLostTimeoutSeconds = 0.85f;
        [SerializeField, Range(0.5f, 5f)] private float lostTargetRetentionSeconds = 1.5f;
        [SerializeField, Range(0.02f, 0.4f)] private float reassociationImageDistance = 0.16f;
        [SerializeField, Min(0.1f)] private float reassociationPathDistance = 1f;

        private RenderTexture cameraTexture;
        private VisionFrameWebSocket webSocket;
        private bool previewEnabled;
        private bool streaming;
        private bool captureInProgress;
        private float nextStreamTime;
        private long frameId;
        private PersistentVisionTargetRegistry targetRegistry;
        private Phase1SceneSetup phase1SceneSetup;
        private SplineContainer conveyorPath;
        private readonly Dictionary<int, SmoothedDetectionState> smoothedDetections =
            new Dictionary<int, SmoothedDetectionState>();

        public string RobotArmId => robotArmId;
        public string CameraId => cameraId;
        public BoxCollider Workspace => workspace;
        public RenderTexture PreviewTexture => cameraTexture;
        public bool IsStreaming => streaming;
        public bool IsConnected => webSocket != null && webSocket.IsConnected;
        public bool IsCaptureInProgress => captureInProgress;
        public long CapturedFrameCount => frameId;
        public double LastRoundTripMilliseconds { get; private set; }
        public float LastInferenceMilliseconds { get; private set; }
        public float EffectiveServerFramesPerSecond { get; private set; }
        public int TrackedDetectionCount { get; private set; }
        public int PredictedDetectionCount { get; private set; }
        public string ActiveModelName { get; private set; } = string.Empty;
        public string ActiveTrackerName { get; private set; } = string.Empty;
        public VisionRoiResult LatestRoi { get; private set; } = new VisionRoiResult();
        public VisionDetectionResult[] LatestDetections { get; private set; } =
            Array.Empty<VisionDetectionResult>();
        public VisionDetectionResult[] DisplayDetections { get; private set; } =
            Array.Empty<VisionDetectionResult>();
        public PersistentVisionTarget[] PersistentTargets => targetRegistry == null
            ? Array.Empty<PersistentVisionTarget>()
            : targetRegistry.Snapshot();
        public PersistentVisionTarget LockedTarget => targetRegistry?.LockedTarget;
        public string LastCapturePath { get; private set; } = string.Empty;
        public string Status { get; private set; } = "Ready";

        public void Configure(
            string newRobotArmId,
            string newCameraId,
            Camera newStationCamera,
            BoxCollider newWorkspace,
            int width,
            int height,
            float framesPerSecond,
            int quality)
        {
            robotArmId = newRobotArmId;
            cameraId = newCameraId;
            stationCamera = newStationCamera;
            workspace = newWorkspace;
            captureWidth = width;
            captureHeight = height;
            streamFramesPerSecond = framesPerSecond;
            jpegQuality = quality;
            ApplyVisionFraming();
        }

        public void SetPreviewEnabled(bool enabled)
        {
            previewEnabled = enabled;
            EnsureResources();
            ApplyCameraState();
        }

        public void SetStreaming(bool enabled)
        {
            streaming = enabled;
            nextStreamTime = Time.unscaledTime;
            Status = enabled ? "Streaming requested" : "Stream stopped";
            if (!enabled)
            {
                ClearDetections();
            }
            EnsureResources();
            ApplyCameraState();
        }

        public void SetOverheadHeightAboveBelt(float height)
        {
            overheadHeightAboveBelt = Mathf.Max(2f, height);
            overheadSideOffsetAwayFromRobot = 0f;
            visionFramingVersion = CurrentVisionFramingVersion;
            ApplyVisionFraming();
        }

        public void CaptureStill()
        {
            RequestCapture(true, false);
        }

        private void Awake()
        {
            ApplyVisionFraming();
            EnsurePersistentTracking();
            EnsureResources();
            ApplyCameraState();
            EnsureStep4DecisionController();
        }

        private void OnValidate()
        {
            ApplyVisionFraming();
            ConfigureTargetRegistry();
        }

        private void Update()
        {
            UpdatePersistentTargets();
            UpdateSmoothedDetections();
            if (!streaming || captureInProgress || Time.unscaledTime < nextStreamTime)
            {
                return;
            }

            nextStreamTime = Time.unscaledTime + 1f / streamFramesPerSecond;
            RequestCapture(false, true);
        }

        private void OnDestroy()
        {
            if (webSocket != null)
            {
                webSocket.Dispose();
                webSocket = null;
            }

            if (cameraTexture != null)
            {
                cameraTexture.Release();
                Destroy(cameraTexture);
                cameraTexture = null;
            }
        }

        private void RequestCapture(bool saveToDisk, bool sendToServer)
        {
            if (captureInProgress)
            {
                Status = "Capture already in progress";
                return;
            }

            EnsureResources();
            if (stationCamera == null || cameraTexture == null)
            {
                Status = "Camera resources unavailable";
                return;
            }

            captureInProgress = true;
            Status = sendToServer ? "Capturing stream frame" : "Capturing JPEG";
            stationCamera.Render();
            AsyncGPUReadback.Request(
                cameraTexture,
                0,
                TextureFormat.RGBA32,
                request => OnReadbackComplete(request, saveToDisk, sendToServer));
        }

        private void OnReadbackComplete(
            AsyncGPUReadbackRequest request,
            bool saveToDisk,
            bool sendToServer)
        {
            if (this == null)
            {
                return;
            }

            if (request.hasError)
            {
                captureInProgress = false;
                Status = "GPU readback failed";
                return;
            }

            byte[] rgba = request.GetData<byte>().ToArray();
            FlipRows(rgba, captureWidth, captureHeight, 4);

            Texture2D readableTexture = new Texture2D(
                captureWidth,
                captureHeight,
                TextureFormat.RGBA32,
                false,
                false);
            readableTexture.LoadRawTextureData(rgba);
            readableTexture.Apply(false, false);
            byte[] jpegBytes = readableTexture.EncodeToJPG(jpegQuality);
            Destroy(readableTexture);

            frameId++;
            VisionRoiResult workspaceRoi = CalculateWorkspaceRoi();
            VisionFrameMetadata metadata = new VisionFrameMetadata
            {
                robot_arm_id = robotArmId,
                camera_id = cameraId,
                frame_id = frameId,
                captured_at_unix_ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                width = captureWidth,
                height = captureHeight,
                roi_x_min = workspaceRoi.x_min,
                roi_y_min = workspaceRoi.y_min,
                roi_x_max = workspaceRoi.x_max,
                roi_y_max = workspaceRoi.y_max
            };

            if (saveToDisk)
            {
                SaveStill(jpegBytes);
            }

            if (sendToServer)
            {
                SendFrame(jpegBytes, metadata);
                return;
            }

            captureInProgress = false;
            Status = saveToDisk
                ? $"Saved {Path.GetFileName(LastCapturePath)}"
                : $"Captured frame {frameId}";
        }

        private async void SendFrame(byte[] jpegBytes, VisionFrameMetadata metadata)
        {
            Stopwatch timer = Stopwatch.StartNew();
            try
            {
                if (webSocket == null)
                {
                    webSocket = new VisionFrameWebSocket(serverUrl);
                }

                string acknowledgement = await webSocket.SendFrameAsync(metadata, jpegBytes);
                timer.Stop();
                LastRoundTripMilliseconds = timer.Elapsed.TotalMilliseconds;
                ApplyVisionResponse(acknowledgement, metadata);
            }
            catch (Exception exception)
            {
                Status = $"Vision server unavailable: {exception.Message}";
                if (webSocket != null)
                {
                    webSocket.Dispose();
                    webSocket = null;
                }
            }
            finally
            {
                captureInProgress = false;
            }
        }

        private void SaveStill(byte[] jpegBytes)
        {
            Directory.CreateDirectory(captureFolder);
            string fileName = $"{cameraId}_{DateTime.Now:yyyyMMdd_HHmmss_fff}.jpg";
            LastCapturePath = Path.Combine(captureFolder, fileName);
            File.WriteAllBytes(LastCapturePath, jpegBytes);
        }

        private void ApplyVisionResponse(string json, VisionFrameMetadata sentMetadata)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidDataException("The vision server returned an empty response.");
            }

            VisionFrameResponse response = JsonUtility.FromJson<VisionFrameResponse>(json);
            if (response == null || !response.received)
            {
                string reason = response == null || string.IsNullOrEmpty(response.error)
                    ? "unknown server error"
                    : response.error;
                throw new InvalidDataException($"Vision frame rejected: {reason}");
            }

            if (response.protocol_version != 2 ||
                response.robot_arm_id != sentMetadata.robot_arm_id ||
                response.camera_id != sentMetadata.camera_id ||
                response.frame_id != sentMetadata.frame_id)
            {
                throw new InvalidDataException("Vision response identity does not match the sent frame.");
            }

            LatestRoi = response.roi ?? new VisionRoiResult();
            LatestDetections = response.detections ?? Array.Empty<VisionDetectionResult>();
            LastInferenceMilliseconds = response.inference_ms;
            EffectiveServerFramesPerSecond = response.effective_fps;
            TrackedDetectionCount = response.tracked_count;
            PredictedDetectionCount = response.predicted_count;
            ActiveModelName = response.model_name ?? string.Empty;
            ActiveTrackerName = response.tracker_name ?? string.Empty;
            EnsurePersistentTracking();
            double now = Time.unscaledTimeAsDouble;
            double captureAgeSeconds = Math.Max(
                0d,
                (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() -
                    sentMetadata.captured_at_unix_ms) / 1000d);
            double observationTime = now - Math.Min(2d, captureAgeSeconds);
            targetRegistry.ProcessFrame(
                LatestDetections,
                observationTime,
                now,
                GetConveyorLength(),
                ProjectDetectionToConveyor);
            UpdatePersistentTargets();
            Status = $"Frame {response.frame_id}: {TrackedDetectionCount} tracked, " +
                $"{PredictedDetectionCount} predicted";
        }

        public bool TryLockBestTarget(out PersistentVisionTarget target)
        {
            EnsurePersistentTracking();
            return targetRegistry.TryLockBestTarget(out target);
        }

        public bool TryLockTarget(int logicalId, out PersistentVisionTarget target)
        {
            EnsurePersistentTracking();
            return targetRegistry.TryLockTarget(logicalId, out target);
        }

        public void ReleaseLockedTarget()
        {
            targetRegistry?.ReleaseLockedTarget();
        }

        private void EnsureStep4DecisionController()
        {
            if (Application.isPlaying &&
                GetComponent<SortingFactory.Step4.WorkstationPickDecisionController>() == null)
            {
                gameObject.AddComponent<SortingFactory.Step4.WorkstationPickDecisionController>();
            }
        }

        private void EnsurePersistentTracking()
        {
            if (targetRegistry == null)
            {
                targetRegistry = new PersistentVisionTargetRegistry();
            }

            if (phase1SceneSetup == null)
            {
                phase1SceneSetup = FindFirstObjectByType<Phase1SceneSetup>();
            }
            if (conveyorPath == null && phase1SceneSetup != null)
            {
                conveyorPath = phase1SceneSetup.ConveyorPath;
            }
            ConfigureTargetRegistry();
        }

        private void ConfigureTargetRegistry()
        {
            if (targetRegistry == null)
            {
                return;
            }

            targetRegistry.ConfirmationHits = Mathf.Max(1, targetConfirmationHits);
            targetRegistry.CoastingDelaySeconds = Mathf.Max(0.01f, coastingDelaySeconds);
            targetRegistry.TentativeTimeoutSeconds = Mathf.Max(
                targetRegistry.CoastingDelaySeconds,
                tentativeTimeoutSeconds);
            targetRegistry.LostTimeoutSeconds = Mathf.Max(
                targetRegistry.TentativeTimeoutSeconds,
                targetLostTimeoutSeconds);
            targetRegistry.LostRetentionSeconds = Mathf.Max(0.1f, lostTargetRetentionSeconds);
            targetRegistry.ReassociationImageDistance = Mathf.Max(
                0.001f,
                reassociationImageDistance);
            targetRegistry.ReassociationPathDistance = Mathf.Max(
                0.01f,
                reassociationPathDistance);
        }

        private void UpdatePersistentTargets()
        {
            EnsurePersistentTracking();
            float speed = phase1SceneSetup == null
                ? 0f
                : phase1SceneSetup.ConveyorObjectSpeed;
            targetRegistry.Tick(
                Time.unscaledTimeAsDouble,
                speed,
                GetConveyorLength(),
                EvaluateConveyorPosition);
            UpdateDetectionTargets(targetRegistry.BuildDisplayDetections());
        }

        private void UpdateDetectionTargets(VisionDetectionResult[] detections)
        {
            HashSet<int> receivedKeys = new HashSet<int>();
            for (int index = 0; index < detections.Length; index++)
            {
                VisionDetectionResult detection = detections[index];
                int key = detection.track_id >= 0
                    ? detection.track_id
                    : int.MinValue + index;
                receivedKeys.Add(key);

                if (!smoothedDetections.TryGetValue(key, out SmoothedDetectionState state))
                {
                    state = new SmoothedDetectionState(detection);
                    smoothedDetections.Add(key, state);
                }
                else
                {
                    state.SetTarget(detection);
                }
            }

            List<int> removedKeys = new List<int>();
            foreach (int key in smoothedDetections.Keys)
            {
                if (!receivedKeys.Contains(key))
                {
                    removedKeys.Add(key);
                }
            }
            foreach (int key in removedKeys)
            {
                smoothedDetections.Remove(key);
            }

            RebuildDisplayDetections();
        }

        private void UpdateSmoothedDetections()
        {
            if (smoothedDetections.Count == 0)
            {
                return;
            }

            float blend = 1f - Mathf.Exp(-boundingBoxSmoothingSpeed * Time.unscaledDeltaTime);
            foreach (SmoothedDetectionState state in smoothedDetections.Values)
            {
                state.Step(blend);
            }
        }

        private void RebuildDisplayDetections()
        {
            List<VisionDetectionResult> display = new List<VisionDetectionResult>(
                smoothedDetections.Count);
            foreach (SmoothedDetectionState state in smoothedDetections.Values)
            {
                display.Add(state.Display);
            }
            display.Sort((left, right) => left.track_id.CompareTo(right.track_id));
            DisplayDetections = display.ToArray();
        }

        private void ClearDetections()
        {
            LatestDetections = Array.Empty<VisionDetectionResult>();
            DisplayDetections = Array.Empty<VisionDetectionResult>();
            smoothedDetections.Clear();
            targetRegistry?.Clear();
            TrackedDetectionCount = 0;
            PredictedDetectionCount = 0;
        }

        private ConveyorProjection? ProjectDetectionToConveyor(VisionDetectionResult detection)
        {
            if (stationCamera == null || workspace == null || conveyorPath == null ||
                conveyorPath.Spline == null)
            {
                return null;
            }

            float imageProjectionY = useOverheadCamera
                ? Mathf.Clamp01(detection.bbox_center_y)
                : Mathf.Clamp01(
                    detection.bbox_center_y + detection.bbox_height * 0.5f);
            Ray ray = stationCamera.ViewportPointToRay(new Vector3(
                Mathf.Clamp01(detection.bbox_center_x),
                1f - imageProjectionY,
                0f));
            Plane beltPlane = new Plane(Vector3.up, new Vector3(0f, workspace.bounds.min.y, 0f));
            if (!beltPlane.Raycast(ray, out float enter) || enter <= 0f)
            {
                return null;
            }

            Vector3 beltPoint = ray.GetPoint(enter);
            (Spline closestSpline, float closestDistance) =
                SplineMeshUtils.FindClosestSplineAndPosition(conveyorPath, beltPoint);
            if (closestSpline == null || closestSpline.GetLength() <= 0.001f)
            {
                return null;
            }

            float normalizedPosition = SplineUtility.GetNormalizedInterpolation(
                closestSpline,
                closestDistance,
                PathIndexUnit.Distance);
            Vector3 pathPosition = conveyorPath.EvaluatePosition(normalizedPosition);
            Vector3 tangent = conveyorPath.EvaluateTangent(normalizedPosition);
            tangent.y = 0f;
            tangent = tangent.sqrMagnitude > 0.0001f
                ? tangent.normalized
                : transform.forward;
            Vector3 side = Vector3.Cross(Vector3.up, tangent).normalized;
            float lateralOffset = Vector3.Dot(beltPoint - pathPosition, side);
            return new ConveyorProjection(closestDistance, lateralOffset, beltPoint);
        }

        private float GetConveyorLength()
        {
            return conveyorPath == null || conveyorPath.Spline == null
                ? 0f
                : conveyorPath.Spline.GetLength();
        }

        private Vector3 EvaluateConveyorPosition(float distance, float lateralOffset)
        {
            if (conveyorPath == null || conveyorPath.Spline == null)
            {
                return Vector3.zero;
            }

            float length = conveyorPath.Spline.GetLength();
            if (length <= 0.001f)
            {
                return Vector3.zero;
            }

            float normalizedPosition = SplineUtility.GetNormalizedInterpolation(
                conveyorPath.Spline,
                Mathf.Repeat(distance, length),
                PathIndexUnit.Distance);
            Vector3 pathPosition = conveyorPath.EvaluatePosition(normalizedPosition);
            Vector3 tangent = conveyorPath.EvaluateTangent(normalizedPosition);
            tangent.y = 0f;
            tangent = tangent.sqrMagnitude > 0.0001f
                ? tangent.normalized
                : transform.forward;
            Vector3 side = Vector3.Cross(Vector3.up, tangent).normalized;
            pathPosition.y = workspace == null ? pathPosition.y : workspace.bounds.min.y;
            return pathPosition + side * lateralOffset;
        }

        private VisionRoiResult CalculateWorkspaceRoi()
        {
            if (stationCamera == null || workspace == null)
            {
                return new VisionRoiResult();
            }

            Vector3 halfSize = workspace.size * 0.5f;
            float minX = 1f;
            float minY = 1f;
            float maxX = 0f;
            float maxY = 0f;
            int visibleCornerCount = 0;

            for (int x = -1; x <= 1; x += 2)
            {
                for (int y = -1; y <= 1; y += 2)
                {
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 localCorner = workspace.center + Vector3.Scale(
                            halfSize,
                            new Vector3(x, y, z));
                        Vector3 viewport = stationCamera.WorldToViewportPoint(
                            workspace.transform.TransformPoint(localCorner));
                        if (viewport.z <= 0f)
                        {
                            continue;
                        }

                        float imageY = 1f - viewport.y;
                        minX = Mathf.Min(minX, viewport.x);
                        minY = Mathf.Min(minY, imageY);
                        maxX = Mathf.Max(maxX, viewport.x);
                        maxY = Mathf.Max(maxY, imageY);
                        visibleCornerCount++;
                    }
                }
            }

            if (visibleCornerCount == 0)
            {
                return new VisionRoiResult();
            }

            const float margin = 0.015f;
            minX = Mathf.Clamp01(minX - margin);
            minY = Mathf.Clamp01(minY - margin);
            maxX = Mathf.Clamp01(maxX + margin);
            maxY = Mathf.Clamp01(maxY + margin);
            if (maxX - minX < 0.01f || maxY - minY < 0.01f)
            {
                return new VisionRoiResult();
            }

            return new VisionRoiResult
            {
                x_min = minX,
                y_min = minY,
                x_max = maxX,
                y_max = maxY
            };
        }

        private void ApplyVisionFraming()
        {
            UpgradeVisionFramingIfNeeded();
            if (!autoFrameWorkspace || stationCamera == null || workspace == null)
            {
                return;
            }

            Transform station = transform.parent;
            if (station == null)
            {
                return;
            }

            Transform robotMount = station.Find("RobotMount");
            float robotSide = robotMount == null || Mathf.Approximately(robotMount.localPosition.x, 0f)
                ? Mathf.Sign(transform.localPosition.x)
                : Mathf.Sign(robotMount.localPosition.x);
            if (Mathf.Approximately(robotSide, 0f))
            {
                robotSide = 1f;
            }

            float beltSurfaceY = workspace.transform.localPosition.y - workspace.size.y * 0.5f;
            if (useOverheadCamera)
            {
                Vector3 overheadCameraPosition = new Vector3(
                    -robotSide * overheadSideOffsetAwayFromRobot,
                    beltSurfaceY + overheadHeightAboveBelt,
                    overheadAlongBeltOffset);
                Vector3 overheadTargetPosition = new Vector3(
                    0f,
                    beltSurfaceY + 0.15f,
                    targetAlongBeltOffset);
                transform.localPosition = overheadCameraPosition;
                transform.localRotation = Quaternion.LookRotation(
                    overheadTargetPosition - overheadCameraPosition,
                    Vector3.forward);
                stationCamera.fieldOfView = visionFieldOfView;
                HideWorkspaceVisualsFromCamera();
                return;
            }

            Vector3 baseCameraLocalPosition = new Vector3(
                -robotSide * cameraSideDistance,
                beltSurfaceY + cameraHeightAboveBelt,
                cameraAlongBeltOffset);
            Vector3 targetLocalPosition = new Vector3(
                0f,
                beltSurfaceY + targetHeightAboveBelt,
                targetAlongBeltOffset);
            Vector3 cameraLocalPosition = Vector3.LerpUnclamped(
                targetLocalPosition,
                baseCameraLocalPosition,
                cameraViewDistanceScale);

            transform.localPosition = cameraLocalPosition;
            transform.localRotation = Quaternion.LookRotation(
                targetLocalPosition - cameraLocalPosition,
                Vector3.up);
            stationCamera.fieldOfView = visionFieldOfView;

            HideWorkspaceVisualsFromCamera();
        }

        private void UpgradeVisionFramingIfNeeded()
        {
            if (visionFramingVersion >= CurrentVisionFramingVersion)
            {
                return;
            }

            overheadHeightAboveBelt = 5f;
            overheadSideOffsetAwayFromRobot = 0f;
            visionFramingVersion = CurrentVisionFramingVersion;
        }

        private void HideWorkspaceVisualsFromCamera()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            int workspaceVisualLayer = LayerMask.NameToLayer("Ignore Raycast");
            if (workspaceVisualLayer < 0)
            {
                return;
            }

            foreach (Renderer workspaceRenderer in workspace.GetComponentsInChildren<Renderer>(true))
            {
                workspaceRenderer.gameObject.layer = workspaceVisualLayer;
            }

            Transform station = transform.parent;
            Transform robotMount = station == null ? null : station.Find("RobotMount");
            if (robotMount != null)
            {
                foreach (Renderer robotRenderer in
                    robotMount.GetComponentsInChildren<Renderer>(true))
                {
                    robotRenderer.gameObject.layer = workspaceVisualLayer;
                }
            }
            stationCamera.cullingMask &= ~(1 << workspaceVisualLayer);
        }

        private void EnsureResources()
        {
            if (stationCamera == null)
            {
                stationCamera = GetComponent<Camera>();
            }

            if (cameraTexture != null &&
                (cameraTexture.width != captureWidth || cameraTexture.height != captureHeight))
            {
                cameraTexture.Release();
                Destroy(cameraTexture);
                cameraTexture = null;
            }

            if (cameraTexture == null)
            {
                cameraTexture = new RenderTexture(
                    captureWidth,
                    captureHeight,
                    24,
                    RenderTextureFormat.ARGB32,
                    RenderTextureReadWrite.sRGB)
                {
                    name = $"{cameraId}_{captureWidth}x{captureHeight}",
                    antiAliasing = 1,
                    useMipMap = false,
                    autoGenerateMips = false
                };
                cameraTexture.Create();
            }

            if (stationCamera != null)
            {
                stationCamera.targetTexture = cameraTexture;
                stationCamera.aspect = captureWidth / (float)captureHeight;
            }
        }

        private void ApplyCameraState()
        {
            if (stationCamera != null)
            {
                stationCamera.enabled = previewEnabled || streaming;
            }
        }

        private static void FlipRows(byte[] pixels, int width, int height, int bytesPerPixel)
        {
            int rowLength = width * bytesPerPixel;
            byte[] row = new byte[rowLength];

            for (int y = 0; y < height / 2; y++)
            {
                int top = y * rowLength;
                int bottom = (height - y - 1) * rowLength;
                Buffer.BlockCopy(pixels, top, row, 0, rowLength);
                Buffer.BlockCopy(pixels, bottom, pixels, top, rowLength);
                Buffer.BlockCopy(row, 0, pixels, bottom, rowLength);
            }
        }

        private sealed class SmoothedDetectionState
        {
            private float targetCenterX;
            private float targetCenterY;
            private float targetWidth;
            private float targetHeight;

            public VisionDetectionResult Display { get; }

            public SmoothedDetectionState(VisionDetectionResult source)
            {
                Display = new VisionDetectionResult();
                CopyMetadata(source);
                Display.bbox_center_x = source.bbox_center_x;
                Display.bbox_center_y = source.bbox_center_y;
                Display.bbox_width = source.bbox_width;
                Display.bbox_height = source.bbox_height;
                SetTarget(source);
            }

            public void SetTarget(VisionDetectionResult source)
            {
                CopyMetadata(source);
                targetCenterX = source.bbox_center_x;
                targetCenterY = source.bbox_center_y;
                targetWidth = source.bbox_width;
                targetHeight = source.bbox_height;
            }

            public void Step(float blend)
            {
                Display.bbox_center_x = Mathf.Lerp(Display.bbox_center_x, targetCenterX, blend);
                Display.bbox_center_y = Mathf.Lerp(Display.bbox_center_y, targetCenterY, blend);
                Display.bbox_width = Mathf.Lerp(Display.bbox_width, targetWidth, blend);
                Display.bbox_height = Mathf.Lerp(Display.bbox_height, targetHeight, blend);
            }

            private void CopyMetadata(VisionDetectionResult source)
            {
                Display.track_id = source.track_id;
                Display.class_id = source.class_id;
                Display.class_name = source.class_name;
                Display.confidence = source.confidence;
                Display.tracking_status = source.tracking_status;
                Display.prediction_age_ms = source.prediction_age_ms;
            }
        }
    }
}
