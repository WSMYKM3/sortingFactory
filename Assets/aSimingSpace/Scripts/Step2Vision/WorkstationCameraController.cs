using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace SortingFactory.Step2
{
    [RequireComponent(typeof(Camera))]
    public sealed class WorkstationCameraController : MonoBehaviour
    {
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

        [Header("Vision Server")]
        [SerializeField] private string serverUrl = "ws://127.0.0.1:8000/ws/camera";

        private RenderTexture cameraTexture;
        private VisionFrameWebSocket webSocket;
        private bool previewEnabled;
        private bool streaming;
        private bool captureInProgress;
        private float nextStreamTime;
        private long frameId;

        public string RobotArmId => robotArmId;
        public string CameraId => cameraId;
        public BoxCollider Workspace => workspace;
        public RenderTexture PreviewTexture => cameraTexture;
        public bool IsStreaming => streaming;
        public bool IsConnected => webSocket != null && webSocket.IsConnected;
        public bool IsCaptureInProgress => captureInProgress;
        public long CapturedFrameCount => frameId;
        public double LastRoundTripMilliseconds { get; private set; }
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
            EnsureResources();
            ApplyCameraState();
        }

        public void CaptureStill()
        {
            RequestCapture(true, false);
        }

        private void Awake()
        {
            EnsureResources();
            ApplyCameraState();
        }

        private void Update()
        {
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
            VisionFrameMetadata metadata = new VisionFrameMetadata
            {
                robot_arm_id = robotArmId,
                camera_id = cameraId,
                frame_id = frameId,
                captured_at_unix_ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                width = captureWidth,
                height = captureHeight
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
                Status = string.IsNullOrEmpty(acknowledgement)
                    ? $"Frame {metadata.frame_id} acknowledged"
                    : $"Frame {metadata.frame_id} acknowledged by server";
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
    }
}
