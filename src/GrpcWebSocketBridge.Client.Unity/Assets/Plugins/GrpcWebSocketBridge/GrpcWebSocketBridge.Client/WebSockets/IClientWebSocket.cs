using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace GrpcWebSocketBridge.Client.WebSockets
{
    public interface IClientWebSocket : IDisposable
    {
        WebSocketState State { get; }

        void AddSubProtocol(string subProtocol);

        Task ConnectAsync(Uri uri, CancellationToken cancellationToken);
        Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken);
        Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken);
    }
}
