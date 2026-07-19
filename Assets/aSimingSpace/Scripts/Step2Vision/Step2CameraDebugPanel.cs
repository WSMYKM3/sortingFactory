using System;
using SortingFactory.Phase1;
using SortingFactory.Step4;
using UnityEngine;

namespace SortingFactory.Step2
{
    public sealed class Step2CameraDebugPanel : MonoBehaviour
    {
        private const float PanelWidth = 500f;
        private const float PanelHeight = 604f;
        private const float DetectionPanelWidth = 380f;
        private const float DetectionPanelHeight = 604f;
        private const float PanelGap = 12f;

        private WorkstationCameraController[] cameras = Array.Empty<WorkstationCameraController>();
        private Phase1SceneSetup phase1SceneSetup;
        private int selectedIndex;
        private Vector2 detectionScrollPosition;

        private WorkstationCameraController SelectedCamera =>
            cameras.Length == 0 ? null : cameras[Mathf.Clamp(selectedIndex, 0, cameras.Length - 1)];

        private void Start()
        {
            cameras = FindObjectsByType<WorkstationCameraController>(FindObjectsSortMode.None);
            Array.Sort(cameras, (left, right) => string.CompareOrdinal(left.CameraId, right.CameraId));
            phase1SceneSetup = FindFirstObjectByType<Phase1SceneSetup>();
            SelectCamera(0);
        }

        private void OnDisable()
        {
            if (SelectedCamera != null)
            {
                SelectedCamera.SetPreviewEnabled(false);
            }
        }

        private void OnGUI()
        {
            float totalWidth = PanelWidth + PanelGap + DetectionPanelWidth;
            float totalHeight = Mathf.Max(PanelHeight, DetectionPanelHeight);
            float scale = Mathf.Min(
                1f,
                Screen.width / (totalWidth + 32f),
                Screen.height / (totalHeight + 32f));
            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            Rect panel = new Rect(16f, 16f, PanelWidth, PanelHeight);
            GUI.Box(panel, GUIContent.none);
            GUI.Label(new Rect(32f, 28f, 360f, 24f), "STEP 4  PICK WINDOW DECISION");

            if (SelectedCamera == null)
            {
                GUI.Label(new Rect(32f, 64f, 430f, 24f), "No workstation cameras found.");
                GUI.matrix = previousMatrix;
                return;
            }

            DrawCameraSummaries();

            WorkstationCameraController camera = SelectedCamera;
            Rect previewRect = new Rect(32f, 94f, 450f, 253f);
            GUI.Box(previewRect, GUIContent.none);
            if (camera.PreviewTexture != null)
            {
                GUI.DrawTexture(previewRect, camera.PreviewTexture, ScaleMode.ScaleToFit, false);
            }
            DrawVisionOverlay(previewRect, camera);

            GUI.Label(new Rect(32f, 354f, 220f, 22f), $"Arm: {camera.RobotArmId}");
            GUI.Label(new Rect(252f, 354f, 230f, 22f), $"Camera: {camera.CameraId}");

            using (new GUIEnabledScope(!camera.IsCaptureInProgress))
            {
                if (GUI.Button(new Rect(32f, 380f, 126f, 32f), "Capture JPEG"))
                {
                    camera.CaptureStill();
                }
            }

            string streamLabel = camera.IsStreaming
                ? $"Stop Arm {selectedIndex + 1}"
                : $"Start Arm {selectedIndex + 1}";
            if (GUI.Button(new Rect(166f, 380f, 146f, 32f), streamLabel))
            {
                camera.SetStreaming(!camera.IsStreaming);
            }

            bool allStreaming = AreAllCamerasStreaming();
            string allStreamsLabel = allStreaming ? "Stop All Streams" : "Start All Streams";
            if (GUI.Button(new Rect(320f, 380f, 162f, 32f), allStreamsLabel))
            {
                SetAllStreams(!allStreaming);
            }

            string connection = camera.IsConnected ? "connected" : "disconnected";
            GUI.Label(
                new Rect(32f, 418f, 450f, 20f),
                $"1280 x 720 | JPEG 85 | Server {connection} | Frames {camera.CapturedFrameCount}");
            string visionInfo = string.IsNullOrEmpty(camera.ActiveModelName)
                ? "Model waiting for first frame"
                : $"{camera.ActiveModelName} | {camera.EffectiveServerFramesPerSecond:0.0} FPS | " +
                    $"T {camera.TrackedDetectionCount} / P {camera.PredictedDetectionCount} | " +
                    $"{camera.LastInferenceMilliseconds:0} ms";
            GUI.Label(new Rect(32f, 440f, 450f, 20f), visionInfo);
            GUI.Label(new Rect(32f, 462f, 450f, 20f), camera.Status);
            DrawStep4Status(camera);
            DrawConveyorSpeedControl();

            DrawDetectionPanel(new Rect(
                16f + PanelWidth + PanelGap,
                16f,
                DetectionPanelWidth,
                DetectionPanelHeight));

            GUI.matrix = previousMatrix;
        }

        private void DrawConveyorSpeedControl()
        {
            GUI.Label(new Rect(32f, 546f, 170f, 22f), "CONVEYOR OBJECT SPEED");
            if (phase1SceneSetup == null)
            {
                GUI.Label(new Rect(210f, 546f, 272f, 22f), "Phase1SceneSetup unavailable");
                return;
            }

            float currentSpeed = phase1SceneSetup.ConveyorObjectSpeed;
            float requestedSpeed = GUI.HorizontalSlider(
                new Rect(206f, 551f, 210f, 20f),
                currentSpeed,
                0.1f,
                2f);
            requestedSpeed = Mathf.Round(requestedSpeed * 20f) / 20f;
            GUI.Label(new Rect(428f, 546f, 54f, 22f), $"{currentSpeed:0.00}");
            GUI.Label(new Rect(206f, 572f, 80f, 18f), "0.10 u/s");
            GUI.Label(new Rect(362f, 572f, 54f, 18f), "2.00 u/s");

            if (!Mathf.Approximately(requestedSpeed, currentSpeed))
            {
                phase1SceneSetup.SetConveyorObjectSpeed(requestedSpeed);
            }
        }

        private static void DrawStep4Status(WorkstationCameraController camera)
        {
            WorkstationPickDecisionController decisionController =
                camera.GetComponent<WorkstationPickDecisionController>();
            if (decisionController == null || !decisionController.HasPhysicalPickWindow)
            {
                GUI.Label(new Rect(32f, 484f, 450f, 20f), "Step 4: building physical pick window");
                GUI.Label(new Rect(32f, 506f, 450f, 20f), "Latest Pick Line unavailable");
                return;
            }

            GUI.Label(
                new Rect(32f, 484f, 450f, 20f),
                $"Arm {ArmStateLabel(decisionController.ArmState)} | " +
                    $"Required {decisionController.RequiredDecisionTime:0.0}s | " +
                    $"Cycle {decisionController.CompleteCycleTime:0.0}s");
            PickTargetEvaluation active = decisionController.ActiveEvaluation;
            string activeStatus = active == null
                ? "No locked target"
                : $"Locked L#{active.LogicalTargetId} {active.ClassName} | " +
                    $"{DecisionLabel(active.Decision)}";
            GUI.Label(
                new Rect(32f, 506f, 450f, 20f),
                $"{decisionController.WorkspaceSpan:0.00}u | " +
                    $"{decisionController.MotionStatus} | {activeStatus}");
        }

        private void SelectCamera(int index)
        {
            if (SelectedCamera != null)
            {
                SelectedCamera.SetPreviewEnabled(false);
            }

            selectedIndex = Mathf.Clamp(index, 0, Mathf.Max(0, cameras.Length - 1));
            if (SelectedCamera != null)
            {
                SelectedCamera.SetPreviewEnabled(true);
            }
        }

        private void DrawCameraSummaries()
        {
            const float buttonWidth = 144f;
            Color previousColor = GUI.color;
            for (int index = 0; index < cameras.Length; index++)
            {
                GUI.color = index == selectedIndex
                    ? new Color(0.65f, 0.9f, 1f, 1f)
                    : Color.white;
                Rect buttonRect = new Rect(32f + index * 153f, 56f, buttonWidth, 28f);
                string status = cameras[index].IsStreaming ? "LIVE" : "OFF";
                if (GUI.Button(buttonRect, $"A{index + 1} {status}"))
                {
                    SelectCamera(index);
                }
            }
            GUI.color = previousColor;
        }

        private void DrawDetectionPanel(Rect panel)
        {
            GUI.Box(panel, GUIContent.none);
            GUI.Label(
                new Rect(panel.x + 16f, panel.y + 12f, panel.width - 32f, 24f),
                "TARGETS + PICK DECISIONS");

            Rect viewport = new Rect(
                panel.x + 10f,
                panel.y + 40f,
                panel.width - 20f,
                panel.height - 50f);
            float contentHeight = CalculateDetectionContentHeight();
            Rect content = new Rect(0f, 0f, viewport.width - 18f, contentHeight);
            detectionScrollPosition = GUI.BeginScrollView(
                viewport,
                detectionScrollPosition,
                content);

            float y = 4f;
            for (int cameraIndex = 0; cameraIndex < cameras.Length; cameraIndex++)
            {
                WorkstationCameraController camera = cameras[cameraIndex];
                WorkstationPickDecisionController decisionController =
                    camera.GetComponent<WorkstationPickDecisionController>();
                string status = camera.IsStreaming ? "LIVE" : "OFF";
                string armState = decisionController == null
                    ? "WAITING"
                    : ArmStateLabel(decisionController.ArmState);
                GUI.Label(
                    new Rect(6f, y, content.width - 12f, 22f),
                    $"ARM {cameraIndex + 1}   {status}   {armState}");
                y += 24f;

                if (!camera.IsStreaming)
                {
                    GUI.Label(new Rect(14f, y, content.width - 20f, 20f), "Stream is off");
                    y += 22f;
                }
                else if (camera.PersistentTargets.Length == 0)
                {
                    GUI.Label(new Rect(14f, y, content.width - 20f, 20f), "No persistent targets");
                    y += 22f;
                }
                else
                {
                    foreach (PersistentVisionTarget target in camera.PersistentTargets)
                    {
                        string sourceTrack = target.SourceTrackId >= 0
                            ? $"T#{target.SourceTrackId}"
                            : "T#--";
                        string state = target.IsLocked
                            ? "LOCKED"
                            : target.State.ToString().ToUpperInvariant();
                        GUI.Label(
                            new Rect(14f, y, content.width - 20f, 20f),
                            $"L#{target.LogicalId} / {sourceTrack}  {target.ClassName}  {state}");
                        y += 19f;
                        GUI.Label(
                            new Rect(22f, y, content.width - 28f, 20f),
                            $"hits {target.TotalObservedFrames} | miss {target.MissedFrames} | " +
                                $"gap {target.ObservationGapSeconds * 1000f:0} ms");
                        y += 19f;
                        string path = target.HasConveyorPosition
                            ? $"s {target.PredictedSplineDistance:0.00} | " +
                                $"x,z {target.PredictedBeltPosition.x:0.0}," +
                                $"{target.PredictedBeltPosition.z:0.0}"
                            : "s -- | conveyor projection unavailable";
                        GUI.Label(
                            new Rect(22f, y, content.width - 28f, 20f),
                            $"{path} | ID changes {target.TrackIdSwitches}");
                        y += 19f;
                        PickTargetEvaluation evaluation = decisionController == null
                            ? null
                            : decisionController.GetEvaluation(target.LogicalId);
                        string decision = evaluation == null
                            ? "pick: waiting for Step 4 evaluation"
                            : $"pick: {FormatRemainingTime(evaluation.RemainingTime)} / " +
                                $"{evaluation.RequiredTime:0.0}s | " +
                                $"{DecisionLabel(evaluation.Decision)} | {evaluation.Reason}";
                        GUI.Label(
                            new Rect(22f, y, content.width - 28f, 20f),
                            decision);
                        y += 23f;
                    }
                }

                DrawSeparator(new Rect(6f, y + 2f, content.width - 12f, 1f));
                y += 14f;
            }

            GUI.EndScrollView();
        }

        private float CalculateDetectionContentHeight()
        {
            float height = 4f;
            foreach (WorkstationCameraController camera in cameras)
            {
                int targetRows = Mathf.Max(1, camera.PersistentTargets.Length);
                int resultRows = camera.IsStreaming
                    ? targetRows * 4
                    : 1;
                height += 24f + resultRows * 20f + 14f;
            }
            return Mathf.Max(height, DetectionPanelHeight - 50f);
        }

        private static void DrawSeparator(Rect rect)
        {
            Color previousColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.25f);
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previousColor;
        }

        private bool AreAllCamerasStreaming()
        {
            if (cameras.Length == 0)
            {
                return false;
            }

            foreach (WorkstationCameraController camera in cameras)
            {
                if (!camera.IsStreaming)
                {
                    return false;
                }
            }

            return true;
        }

        private void SetAllStreams(bool enabled)
        {
            foreach (WorkstationCameraController camera in cameras)
            {
                camera.SetStreaming(enabled);
            }
        }

        private static void DrawVisionOverlay(
            Rect previewRect,
            WorkstationCameraController camera)
        {
            VisionRoiResult roi = camera.LatestRoi;
            if (roi != null)
            {
                Rect roiRect = NormalizedToGuiRect(
                    previewRect,
                    (roi.x_min + roi.x_max) * 0.5f,
                    ImageToPreviewY((roi.y_min + roi.y_max) * 0.5f),
                    roi.x_max - roi.x_min,
                    roi.y_max - roi.y_min);
                DrawOutline(roiRect, new Color(0.2f, 1f, 0.55f, 0.9f), 2f);
            }

            WorkstationPickDecisionController decisionController =
                camera.GetComponent<WorkstationPickDecisionController>();
            DrawLatestPickLine(previewRect, camera, decisionController);

            foreach (VisionDetectionResult detection in camera.DisplayDetections)
            {
                Rect detectionRect = NormalizedToGuiRect(
                    previewRect,
                    detection.bbox_center_x,
                    ImageToPreviewY(detection.bbox_center_y),
                    detection.bbox_width,
                    detection.bbox_height);
                Color color = DetectionColor(detection.track_id, detection.class_id);
                bool predicted = detection.tracking_status == "predicted" ||
                    detection.tracking_status == "coasting";
                bool tentative = detection.tracking_status == "tentative";
                if (predicted)
                {
                    color.a = 0.48f;
                }
                else if (tentative)
                {
                    color.a = 0.7f;
                }
                DrawOutline(detectionRect, color, predicted ? 2f : 3f);

                string trackLabel = detection.track_id >= 0
                    ? $"L#{detection.track_id} "
                    : string.Empty;
                string stateLabel = DetectionStateLabel(detection.tracking_status);
                PickTargetEvaluation evaluation = decisionController == null
                    ? null
                    : decisionController.GetEvaluation(detection.track_id);
                string pickLabel = evaluation == null
                    ? string.Empty
                    : $" | {FormatRemainingTime(evaluation.RemainingTime)} " +
                        DecisionShortLabel(evaluation.Decision);
                string label = $"{trackLabel}{detection.class_name} " +
                    $"{detection.confidence:P0}{stateLabel}{pickLabel}";
                float labelWidth = Mathf.Clamp(label.Length * 7.2f + 10f, 110f, 245f);
                float labelX = Mathf.Clamp(
                    detectionRect.x,
                    previewRect.x,
                    previewRect.xMax - labelWidth);
                Rect labelRect = new Rect(
                    labelX,
                    Mathf.Max(previewRect.y, detectionRect.y - 20f),
                    labelWidth,
                    20f);
                Color previousColor = GUI.color;
                GUI.color = new Color(color.r, color.g, color.b, 0.92f);
                GUI.Box(labelRect, GUIContent.none);
                GUI.color = Color.black;
                GUI.Label(new Rect(labelRect.x + 4f, labelRect.y, labelRect.width - 6f, 20f), label);
                GUI.color = previousColor;
            }
        }

        private static void DrawLatestPickLine(
            Rect previewRect,
            WorkstationCameraController cameraController,
            WorkstationPickDecisionController decisionController)
        {
            if (decisionController == null || !decisionController.HasPhysicalPickWindow)
            {
                return;
            }

            Camera camera = cameraController.GetComponent<Camera>();
            if (camera == null)
            {
                return;
            }

            Vector3 startViewport = camera.WorldToViewportPoint(
                decisionController.LatestPickLineStartWorld);
            Vector3 endViewport = camera.WorldToViewportPoint(
                decisionController.LatestPickLineEndWorld);
            if (startViewport.z <= 0f || endViewport.z <= 0f)
            {
                return;
            }

            Vector2 start = ViewportToGuiPoint(previewRect, startViewport);
            Vector2 end = ViewportToGuiPoint(previewRect, endViewport);
            Color lineColor = new Color(1f, 0.18f, 0.12f, 0.95f);
            DrawGuiLine(start, end, lineColor, 3f);

            Vector2 midpoint = (start + end) * 0.5f;
            const float labelWidth = 126f;
            Rect labelRect = new Rect(
                Mathf.Clamp(midpoint.x - labelWidth * 0.5f, previewRect.x, previewRect.xMax - labelWidth),
                Mathf.Clamp(midpoint.y - 24f, previewRect.y, previewRect.yMax - 18f),
                labelWidth,
                18f);
            Color previousColor = GUI.color;
            GUI.color = lineColor;
            GUI.Box(labelRect, GUIContent.none);
            GUI.color = Color.white;
            GUI.Label(
                new Rect(labelRect.x + 4f, labelRect.y, labelRect.width - 8f, 18f),
                "LATEST PICK LINE");
            GUI.color = previousColor;
        }

        private static Vector2 ViewportToGuiPoint(Rect previewRect, Vector3 viewport)
        {
            return new Vector2(
                previewRect.x + Mathf.Clamp01(viewport.x) * previewRect.width,
                previewRect.y + (1f - Mathf.Clamp01(viewport.y)) * previewRect.height);
        }

        private static void DrawGuiLine(Vector2 start, Vector2 end, Color color, float thickness)
        {
            Vector2 difference = end - start;
            float length = difference.magnitude;
            if (length <= 0.01f)
            {
                return;
            }

            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            GUI.color = color;
            GUIUtility.RotateAroundPivot(
                Mathf.Atan2(difference.y, difference.x) * Mathf.Rad2Deg,
                start);
            GUI.DrawTexture(
                new Rect(start.x, start.y - thickness * 0.5f, length, thickness),
                Texture2D.whiteTexture);
            GUI.matrix = previousMatrix;
            GUI.color = previousColor;
        }

        private static Rect NormalizedToGuiRect(
            Rect previewRect,
            float centerX,
            float centerY,
            float width,
            float height)
        {
            float xMin = Mathf.Clamp01(centerX - width * 0.5f);
            float yMin = Mathf.Clamp01(centerY - height * 0.5f);
            float xMax = Mathf.Clamp01(centerX + width * 0.5f);
            float yMax = Mathf.Clamp01(centerY + height * 0.5f);
            return new Rect(
                previewRect.x + xMin * previewRect.width,
                previewRect.y + yMin * previewRect.height,
                (xMax - xMin) * previewRect.width,
                (yMax - yMin) * previewRect.height);
        }

        private static float ImageToPreviewY(float imageY)
        {
            return 1f - imageY;
        }

        private static string DetectionStateLabel(string status)
        {
            switch (status)
            {
                case "coasting":
                case "predicted":
                    return " COAST";
                case "tentative":
                    return " TENT";
                case "locked":
                    return " LOCK";
                default:
                    return string.Empty;
            }
        }

        private static string ArmStateLabel(WorkstationArmState state)
        {
            switch (state)
            {
                case WorkstationArmState.SecuringObject:
                    return "SECURING";
                case WorkstationArmState.CompletingCycle:
                    return "BUSY";
                default:
                    return "IDLE";
            }
        }

        private static string DecisionLabel(PickDecision decision)
        {
            return decision.ToString().ToUpperInvariant();
        }

        private static string DecisionShortLabel(PickDecision decision)
        {
            switch (decision)
            {
                case PickDecision.Execute:
                    return "EXEC";
                case PickDecision.Completed:
                    return "DONE";
                default:
                    return decision.ToString().ToUpperInvariant();
            }
        }

        private static string FormatRemainingTime(float seconds)
        {
            return float.IsPositiveInfinity(seconds) ? "INF" : $"{seconds:0.0}s";
        }

        private static void DrawOutline(Rect rect, Color color, float thickness)
        {
            Color previousColor = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(
                new Rect(rect.x, rect.yMax - thickness, rect.width, thickness),
                Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), Texture2D.whiteTexture);
            GUI.DrawTexture(
                new Rect(rect.xMax - thickness, rect.y, thickness, rect.height),
                Texture2D.whiteTexture);
            GUI.color = previousColor;
        }

        private static Color DetectionColor(int trackId, int classId)
        {
            int stableId = trackId >= 0 ? trackId : classId + 37;
            float hue = Mathf.Repeat(stableId * 0.173f, 1f);
            return Color.HSVToRGB(hue, 0.72f, 1f);
        }

        private readonly struct GUIEnabledScope : IDisposable
        {
            private readonly bool previousEnabled;

            public GUIEnabledScope(bool enabled)
            {
                previousEnabled = GUI.enabled;
                GUI.enabled = enabled;
            }

            public void Dispose()
            {
                GUI.enabled = previousEnabled;
            }
        }
    }
}
