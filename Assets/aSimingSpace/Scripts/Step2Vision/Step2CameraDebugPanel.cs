using System;
using UnityEngine;

namespace SortingFactory.Step2
{
    public sealed class Step2CameraDebugPanel : MonoBehaviour
    {
        private const float PanelWidth = 500f;
        private const float PanelHeight = 440f;

        private WorkstationCameraController[] cameras = Array.Empty<WorkstationCameraController>();
        private int selectedIndex;

        private WorkstationCameraController SelectedCamera =>
            cameras.Length == 0 ? null : cameras[Mathf.Clamp(selectedIndex, 0, cameras.Length - 1)];

        private void Start()
        {
            cameras = FindObjectsByType<WorkstationCameraController>(FindObjectsSortMode.None);
            Array.Sort(cameras, (left, right) => string.CompareOrdinal(left.CameraId, right.CameraId));
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
            float scale = Mathf.Min(1f, Screen.width / (PanelWidth + 32f), Screen.height / (PanelHeight + 32f));
            Matrix4x4 previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            Rect panel = new Rect(16f, 16f, PanelWidth, PanelHeight);
            GUI.Box(panel, GUIContent.none);
            GUI.Label(new Rect(32f, 28f, 300f, 24f), "STEP 2  CAMERA PIPELINE");

            if (SelectedCamera == null)
            {
                GUI.Label(new Rect(32f, 64f, 430f, 24f), "No workstation cameras found.");
                GUI.matrix = previousMatrix;
                return;
            }

            if (GUI.Button(new Rect(330f, 24f, 44f, 28f), "<"))
            {
                SelectCamera((selectedIndex - 1 + cameras.Length) % cameras.Length);
            }

            GUI.Label(
                new Rect(380f, 28f, 68f, 24f),
                $"{selectedIndex + 1} / {cameras.Length}");
            if (GUI.Button(new Rect(448f, 24f, 44f, 28f), ">"))
            {
                SelectCamera((selectedIndex + 1) % cameras.Length);
            }

            WorkstationCameraController camera = SelectedCamera;
            Rect previewRect = new Rect(32f, 64f, 450f, 253f);
            GUI.Box(previewRect, GUIContent.none);
            if (camera.PreviewTexture != null)
            {
                GUI.DrawTexture(previewRect, camera.PreviewTexture, ScaleMode.ScaleToFit, false);
            }

            GUI.Label(new Rect(32f, 324f, 220f, 22f), $"Arm: {camera.RobotArmId}");
            GUI.Label(new Rect(252f, 324f, 230f, 22f), $"Camera: {camera.CameraId}");

            using (new GUIEnabledScope(!camera.IsCaptureInProgress))
            {
                if (GUI.Button(new Rect(32f, 350f, 210f, 32f), "Capture JPEG"))
                {
                    camera.CaptureStill();
                }
            }

            string streamLabel = camera.IsStreaming ? "Stop 10 FPS Stream" : "Start 10 FPS Stream";
            if (GUI.Button(new Rect(252f, 350f, 230f, 32f), streamLabel))
            {
                camera.SetStreaming(!camera.IsStreaming);
            }

            string connection = camera.IsConnected ? "connected" : "disconnected";
            GUI.Label(
                new Rect(32f, 388f, 450f, 20f),
                $"1280 x 720 | JPEG 85 | Server {connection} | Frames {camera.CapturedFrameCount}");
            GUI.Label(new Rect(32f, 410f, 450f, 20f), camera.Status);

            GUI.matrix = previousMatrix;
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
