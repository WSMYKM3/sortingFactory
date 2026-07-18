using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace SortingFactory.Step2
{
    [Serializable]
    public sealed class VisionFrameMetadata
    {
        public int protocol_version = 1;
        public string robot_arm_id;
        public string camera_id;
        public long frame_id;
        public long captured_at_unix_ms;
        public int width;
        public int height;
        public string image_format = "jpeg";
    }

    public sealed class VisionFrameWebSocket : IDisposable
    {
        private const int TimeoutMilliseconds = 2500;

        private readonly Uri serverUri;
        private readonly CancellationTokenSource lifetimeCancellation = new CancellationTokenSource();
        private ClientWebSocket socket;

        public bool IsConnected => socket != null && socket.State == WebSocketState.Open;

        public VisionFrameWebSocket(string serverUrl)
        {
            serverUri = new Uri(serverUrl);
        }

        public async Task<string> SendFrameAsync(VisionFrameMetadata metadata, byte[] jpegBytes)
        {
            await EnsureConnectedAsync();

            byte[] packet = BuildPacket(metadata, jpegBytes);
            using (CancellationTokenSource timeout = CreateTimeoutToken())
            {
                await socket.SendAsync(
                    new ArraySegment<byte>(packet),
                    WebSocketMessageType.Binary,
                    true,
                    timeout.Token);
                return await ReceiveTextAsync(timeout.Token);
            }
        }

        public void Dispose()
        {
            lifetimeCancellation.Cancel();
            DisposeSocket();
            lifetimeCancellation.Dispose();
        }

        private async Task EnsureConnectedAsync()
        {
            if (IsConnected)
            {
                return;
            }

            DisposeSocket();
            socket = new ClientWebSocket();
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);

            try
            {
                using (CancellationTokenSource timeout = CreateTimeoutToken())
                {
                    await socket.ConnectAsync(serverUri, timeout.Token);
                }
            }
            catch
            {
                DisposeSocket();
                throw;
            }
        }

        private async Task<string> ReceiveTextAsync(CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[4096];
            using (MemoryStream message = new MemoryStream())
            {
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        throw new WebSocketException("The vision server closed the connection.");
                    }

                    message.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                if (result.MessageType != WebSocketMessageType.Text)
                {
                    throw new InvalidDataException("The vision server acknowledgement must be UTF-8 text.");
                }

                return Encoding.UTF8.GetString(message.ToArray());
            }
        }

        private CancellationTokenSource CreateTimeoutToken()
        {
            CancellationTokenSource timeout =
                CancellationTokenSource.CreateLinkedTokenSource(lifetimeCancellation.Token);
            timeout.CancelAfter(TimeoutMilliseconds);
            return timeout;
        }

        private void DisposeSocket()
        {
            if (socket == null)
            {
                return;
            }

            socket.Dispose();
            socket = null;
        }

        private static byte[] BuildPacket(VisionFrameMetadata metadata, byte[] jpegBytes)
        {
            byte[] header = Encoding.UTF8.GetBytes(JsonUtility.ToJson(metadata));
            byte[] packet = new byte[4 + header.Length + jpegBytes.Length];
            int headerLength = header.Length;

            packet[0] = (byte)(headerLength >> 24);
            packet[1] = (byte)(headerLength >> 16);
            packet[2] = (byte)(headerLength >> 8);
            packet[3] = (byte)headerLength;
            Buffer.BlockCopy(header, 0, packet, 4, header.Length);
            Buffer.BlockCopy(jpegBytes, 0, packet, 4 + header.Length, jpegBytes.Length);
            return packet;
        }
    }
}
