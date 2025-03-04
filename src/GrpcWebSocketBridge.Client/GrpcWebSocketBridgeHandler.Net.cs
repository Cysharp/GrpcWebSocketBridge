#if !UNITY_2021_1_OR_NEWER
using System.IO.Pipelines;
using System.Net.Http;
using GrpcWebSocketBridge.Client.WebSockets;

namespace GrpcWebSocketBridge.Client
{
    public partial class GrpcWebSocketBridgeHandler
    {
        private static PipeOptions PipeOptions { get; } = new PipeOptions();

        private static IClientWebSocket CreateClientWebSocket() => new SystemNetWebSocketsClientWebSocket();


        public GrpcWebSocketBridgeHandler(bool forceWebSocketMode = false)
            : base(new HttpClientHandler())
        {
            _forceWebSocketMode = forceWebSocketMode;
        }
    }
}
#endif
