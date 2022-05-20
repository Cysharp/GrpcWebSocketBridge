using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

#if !NON_UNITY
using Cysharp.Threading.Tasks;
#endif

namespace GrpcWebSocketBridge.Client.WebSockets
{
    public interface IClientWebSocket : IDisposable
    {
        WebSocketState State { get; }

        void AddSubProtocol(string subProtocol);

#if NON_UNITY
        Task ConnectAsync(Uri uri, CancellationToken cancellationToken);
        Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken);
        Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken);
#else
        UniTask ConnectAsync(Uri uri, CancellationToken cancellationToken);
        UniTask SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken);
        UniTask<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken);
#endif
    }
}
