using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

#if !NON_UNITY
using Cysharp.Threading.Tasks;
#endif

namespace GrpcWebSocketBridge.Client.WebSockets
{
    internal class SystemNetWebSocketsClientWebSocket : IClientWebSocket
    {
        private readonly System.Net.WebSockets.ClientWebSocket _clientWebSocket;

        public SystemNetWebSocketsClientWebSocket()
        {
            _clientWebSocket = new ClientWebSocket();
        }

        public WebSocketState State => _clientWebSocket.State;

        public void AddSubProtocol(string subProtocol)
            => _clientWebSocket.Options.AddSubProtocol(subProtocol);

#if NON_UNITY
        public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
            => _clientWebSocket.ConnectAsync(uri, cancellationToken);

        public Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
            => _clientWebSocket.SendAsync(buffer, messageType, endOfMessage, cancellationToken);

        public Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
            => _clientWebSocket.ReceiveAsync(buffer, cancellationToken);
#else
        public async UniTask ConnectAsync(Uri uri, CancellationToken cancellationToken)
            => await _clientWebSocket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);

        public async UniTask SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
            => await _clientWebSocket.SendAsync(buffer, messageType, endOfMessage, cancellationToken).ConfigureAwait(false);

        public async UniTask<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
            => await _clientWebSocket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
#endif
        public void Dispose()
            => _clientWebSocket.Dispose();
    }
}
