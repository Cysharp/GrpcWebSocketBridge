using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Grpc.AspNetCore.Web.Internal;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace GrpcWebSocketBridge.AspNetCore.Internal
{
    internal class BridgeHttpResponseFeature : IHttpResponseFeature
    {
        private readonly WebSocketBridgeContext _bridgeContext;
        private readonly IHttpResponseFeature _origHttpResponseFeature;
        private int _statusCode;
        private IHeaderDictionary _headers;
        private string? _reasonPhrase;
        private bool _hasStarted;
        private readonly List<(Func<object, Task> Callback, object State)> _onStarting = new List<(Func<object, Task> Callback, object State)>();

        public BridgeHttpResponseFeature(WebSocketBridgeContext bridgeContext, IHttpResponseFeature origHttpResponseFeature)
        {
            _bridgeContext = bridgeContext;
            _origHttpResponseFeature = origHttpResponseFeature ?? throw new ArgumentNullException(nameof(origHttpResponseFeature));

            // The HTTP response header is already sent at this moment.
            // Create new HeaderDictionary from original headers.
            _headers = new HeaderDictionary(_origHttpResponseFeature.Headers.ToDictionary(k => k.Key, v => v.Value));
            _statusCode = _origHttpResponseFeature.StatusCode;
            _reasonPhrase = _origHttpResponseFeature.ReasonPhrase;
        }

        public void OnStarting(Func<object, Task> callback, object state)
        {
            _onStarting.Add((callback, state));
        }

        public void OnCompleted(Func<object, Task> callback, object state)
        {
            _origHttpResponseFeature.OnCompleted(callback, state);
        }

        public int StatusCode
        {
            get => _statusCode;
            set => _statusCode = value;
        }

        public string? ReasonPhrase
        {
            get => _reasonPhrase;
            set => _reasonPhrase = value;
        }

        public IHeaderDictionary Headers
        {
            get => _headers;
            set => _headers = value;
        }

        public Stream Body
        {
            get => _bridgeContext.Writer.AsStream();
            set => throw new NotSupportedException();
        }

        public bool HasStarted
        {
            get => _hasStarted;
        }

        public async ValueTask TryStartResponseAsync(CancellationToken cancellationToken)
        {
            if (_hasStarted) return;

            // Invoke 'OnStarting' callbacks
            foreach (var onStarting in _onStarting)
            {
                await onStarting.Callback(onStarting.State);
            }

            // Send response headers
            var writer = new ArrayBufferWriter<byte>();
            GrpcWebProtocolHelpers.WriteTrailers(Headers, writer);
            await _bridgeContext.WebSocket.SendAsync(writer.WrittenMemory, WebSocketMessageType.Binary, true, cancellationToken);

            _hasStarted = true;
        }
    }
}
