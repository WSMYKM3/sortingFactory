using System;
using SortingFactory.Phase1;
using UnityEngine;

namespace SortingFactory.Step2
{
    public sealed class Step2CameraDebugPanel : MonoBehaviour
    {
        private const float PanelWidth = 500f;
        private const float PanelHeight = 548f;
        private const float DetectionPanelWidth = 250f;
        private const float DetectionPanelHeight = 360f;
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
            GUI.Label(new Rect(32f, 28f, 300f, 24f), "STEP 3  YOLO + BYTETRACK");

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
            GUI.Label(new Rect(32f, 490f, 170f, 22f), "CONVEYOR OBJECT SPEED");
            if (phase1SceneSetup == null)
            {
                GUI.Label(new Rect(210f, 490f, 272f, 22f), "Phase1SceneSetup unavailable");
                return;
            }

            float currentSpeed = phase1SceneSetup.ConveyorObjectSpeed;
            float requestedSpeed = GUI.HorizontalSlider(
                new Rect(206f, 495f, 210f, 20f),
                currentSpeed,
                0.1f,
                2f);
            requestedSpeed = Mathf.Round(requestedSpeed * 20f) / 20f;
            GUI.Label(new Rect(428f, 490f, 54f, 22f), $"{currentSpeed:0.00}");
            GUI.Label(new Rect(206f, 516f, 80f, 18f), "0.10 u/s");
            GUI.Label(new Rect(362f, 516f, 54f, 18f), "2.00 u/s");

            if (!Mathf.Approximately(requestedSpeed, currentSpeed))
            {
                phase1SceneSetup.SetConveyorObjectSpeed(requestedSpeed);
            }
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
                "LIVE DETECTIONS");

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
                string status = camera.IsStreaming ? "LIVE" : "OFF";
                GUI.Label(
                    new Rect(6f, y, content.width - 12f, 22f),
                    $"ARM {cameraIndex + 1}   {status}");
                y += 24f;

                if (!camera.IsStreaming)
                {
                    GUI.Label(new Rect(14f, y, content.width - 20f, 20f), "Stream is off");
                    y += 22f;
                }
                else if (camera.DisplayDetections.Length == 0)
                {
                    GUI.Label(new Rect(14f, y, content.width - 20f, 20f), "No objects detected");
                    y += 22f;
                }
                else
                {
                    foreach (VisionDetectionResult detection in camera.DisplayDetections)
                    {
                        string trackLabel = detection.track_id >= 0
                            ? $"#{detection.track_id}"
                            : "--";
                        string predictionLabel = detection.tracking_status == "predicted"
                            ? "  PRED"
                            : string.Empty;
                        GUI.Label(
                            new Rect(14f, y, content.width - 20f, 20f),
                            $"{trackLabel}  {detection.class_name}  " +
                                $"{detection.confidence:P0}{predictionLabel}");
                        y += 22f;
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
                int resultRows = camera.IsStreaming
                    ? Mathf.Max(1, camera.DisplayDetections.Length)
                    : 1;
                height += 24f + resultRows * 22f + 14f;
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

            foreach (VisionDetectionResult detection in camera.DisplayDetections)
            {
                Rect detectionRect = NormalizedToGuiRect(
                    previewRect,
                    detection.bbox_center_x,
                    ImageToPreviewY(detection.bbox_center_y),
                    detection.bbox_width,
                    detection.bbox_height);
                Color color = DetectionColor(detection.track_id, detection.class_id);
                bool predicted = detection.tracking_status == "predicted";
                if (predicted)
                {
                    color.a = 0.48f;
                }
                DrawOutline(detectionRect, color, predicted ? 2f : 3f);

                string trackLabel = detection.track_id >= 0 ? $"#{detection.track_id} " : string.Empty;
                string predictionLabel = predicted ? " PRED" : string.Empty;
                string label = $"{trackLabel}{detection.class_name} " +
                    $"{detection.confidence:P0}{predictionLabel}";
                float labelWidth = Mathf.Clamp(label.Length * 7.2f + 10f, 90f, detectionRect.width);
                Rect labelRect = new Rect(
                    detectionRect.x,
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
