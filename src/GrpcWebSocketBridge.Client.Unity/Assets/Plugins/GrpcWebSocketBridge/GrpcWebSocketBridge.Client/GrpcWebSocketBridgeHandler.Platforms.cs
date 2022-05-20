using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Text;
using System.Threading.Tasks;
#if !NON_UNITY
using Cysharp.Threading.Tasks;
#endif
using GrpcWebSocketBridge.Client.WebSockets;

namespace GrpcWebSocketBridge.Client
{
#if NON_UNITY
    public partial class GrpcWebSocketBridgeHandler
    {
        private static PipeOptions PipeOptions { get; } = new PipeOptions();

        private static IClientWebSocket CreateClientWebSocket() => new SystemNetWebSocketsClientWebSocket();
        private static TaskCompletionSource<bool> CreateHeadersTaskCompletionSource() => new TaskCompletionSource<bool>();
    }
#else
    public partial class GrpcWebSocketBridgeHandler
    {
        private static PipeOptions PipeOptions { get; } = new PipeOptions(readerScheduler: PipeScheduler.Inline, writerScheduler: PipeScheduler.Inline, useSynchronizationContext: false);

        private static IClientWebSocket CreateClientWebSocket() =>
#if UNITY_WEBGL && !UNITY_EDITOR
            new JsWebSocketsClientWebSocket();
#else
            new SystemNetWebSocketsClientWebSocket();
#endif
        private static UniTaskCompletionSource<bool> CreateHeadersTaskCompletionSource() => new UniTaskCompletionSource<bool>();
    }
#endif
}
