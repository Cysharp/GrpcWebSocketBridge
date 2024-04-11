using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#if !NON_UNITY
#if UNITY_WEBGL
using GrpcWebSocketBridge.Client.Unity;
#endif
#endif

namespace GrpcWebSocketBridge.Client.WebSockets
{

#if UNITY_WEBGL
    internal class JsWebSocketsClientWebSocket : IClientWebSocket
    {
        private readonly GrpcWebSocketBridge.Client.Unity.JsClientWebSocket _clientWebSocket;

        public JsWebSocketsClientWebSocket()
        {
            _clientWebSocket = new JsClientWebSocket();
        }

        public WebSocketState State => _clientWebSocket.State;

        public void AddSubProtocol(string subProtocol)
            => _clientWebSocket.Options.AddSubProtocol(subProtocol);

        public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
            => _clientWebSocket.ConnectAsync(uri, cancellationToken);

        public Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
            => _clientWebSocket.SendAsync(buffer, messageType, endOfMessage, cancellationToken);

        public Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
            => _clientWebSocket.ReceiveAsync(buffer, cancellationToken);

        public void Dispose()
            => _clientWebSocket.Dispose();
    }
#endif
}
