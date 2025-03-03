#if UNITY_2022_1_OR_NEWER
using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using GrpcWebSocketBridge.Client.WebSockets;

namespace GrpcWebSocketBridge.Client
{
    public partial class GrpcWebSocketBridgeHandler
    {
        private static PipeOptions PipeOptions { get; } = new PipeOptions(readerScheduler: PipeScheduler.Inline, writerScheduler: PipeScheduler.Inline, useSynchronizationContext: false);

        private static IClientWebSocket CreateClientWebSocket() =>
#if UNITY_WEBGL && !UNITY_EDITOR
            new JsWebSocketsClientWebSocket();
#else
            new SystemNetWebSocketsClientWebSocket();
#endif

       public GrpcWebSocketBridgeHandler(bool forceWebSocketMode = false)
            : base(new GrpcWebSocketBridge.Client.Unity.UnityWebRequestHttpHandler())
        {
            _forceWebSocketMode = forceWebSocketMode;
        }
    }
}
#endif
