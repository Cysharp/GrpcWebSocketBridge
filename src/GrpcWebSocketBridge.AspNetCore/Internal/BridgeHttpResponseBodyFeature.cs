using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Grpc.AspNetCore.Web.Internal;
using Microsoft.AspNetCore.Http.Features;

namespace GrpcWebSocketBridge.AspNetCore.Internal
{
    internal class BridgeHttpResponseBodyFeature : IHttpResponseBodyFeature
    {
        private readonly WebSocketBridgeContext _bridgeContext;
        private readonly IHttpResponseFeature _responseFeature;
        private readonly IHttpResponseTrailersFeature _responseTrailerFeature;
        private readonly IHttpResponseBodyFeature _origHttpResponseBodyFeature;
        private readonly ResponsePipeWriter _pipeWriter;

        public BridgeHttpResponseBodyFeature(WebSocketBridgeContext bridgeContext, IHttpResponseFeature responseFeature, IHttpResponseTrailersFeature responseTrailerFeature,  IHttpResponseBodyFeature origHttpResponseBodyFeature)
        {
            _bridgeContext = bridgeContext ?? throw new ArgumentNullException(nameof(bridgeContext));
            _responseFeature = responseFeature ?? throw new ArgumentNullException(nameof(responseFeature));
            _responseTrailerFeature = responseTrailerFeature ?? throw new ArgumentNullException(nameof(responseTrailerFeature));
            _origHttpResponseBodyFeature = origHttpResponseBodyFeature ?? throw new ArgumentNullException(nameof(origHttpResponseBodyFeature));
            _pipeWriter = new ResponsePipeWriter(_bridgeContext.Writer, _responseFeature);
        }

        public void DisableBuffering()
        {
            // No-op
        }

        public async Task StartAsync(CancellationToken cancellationToken = new CancellationToken())
        {
            // StartAsync (and OnStarting) will be invoked by BridgeMiddleware
            if (_responseFeature is BridgeHttpResponseFeature bridgeHttpResponseFeature)
            {
                await bridgeHttpResponseFeature.TryStartResponseAsync(cancellationToken);
            }
        }

        public Task SendFileAsync(string path, long offset, long? count, CancellationToken cancellationToken = new CancellationToken())
        {
            throw new NotSupportedException();
        }

        public async Task CompleteAsync()
        {
            // Flush unsent data in the body-pipe.
            await _pipeWriter.FlushAsync();
            try
            {
                await GrpcWebProtocolHelpers.WriteTrailersAsync(_responseTrailerFeature.Trailers, _pipeWriter);
                await _pipeWriter.CompleteAsync();
                await _bridgeContext.WriterTask;
            }
            catch (OperationCanceledException)
            {
            }

            await _origHttpResponseBodyFeature.CompleteAsync();
        }

        public Stream Stream => Writer.AsStream();
        public PipeWriter Writer => _pipeWriter;
    }

    internal class ResponsePipeWriter : PipeWriter
    {
        private readonly PipeWriter _underlyingPipeWriter;
        private readonly BridgeHttpResponseFeature _bridgeHttpResponseFeature;

        public ResponsePipeWriter(PipeWriter underlyingPipeWriter, IHttpResponseFeature httpResponseFeature)
        {
            _underlyingPipeWriter = underlyingPipeWriter;
            _bridgeHttpResponseFeature = (httpResponseFeature as BridgeHttpResponseFeature) ?? throw new ArgumentException("IHttpResponseFeature must be type of 'BridgeHttpResponseFeature'", nameof(httpResponseFeature));
        }

        public override void Advance(int bytes)
        {
            _underlyingPipeWriter.Advance(bytes);
        }

        public override Memory<byte> GetMemory(int sizeHint = 0)
        {
            return _underlyingPipeWriter.GetMemory(sizeHint);
        }

        public override Span<byte> GetSpan(int sizeHint = 0)
        {
            return _underlyingPipeWriter.GetSpan(sizeHint);
        }

        public override void CancelPendingFlush()
        {
            _underlyingPipeWriter.CancelPendingFlush();
        }

        public override void Complete(Exception? exception = null)
        {
            _underlyingPipeWriter.Complete(exception);
        }

        public override async ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = new CancellationToken())
        {
            await _bridgeHttpResponseFeature.TryStartResponseAsync(cancellationToken);

            return await _underlyingPipeWriter.FlushAsync(cancellationToken);
        }

        public override ValueTask CompleteAsync(Exception? exception = null)
        {
            return _underlyingPipeWriter.CompleteAsync(exception);
        }

        public override Stream AsStream(bool leaveOpen = false)
        {
            return _underlyingPipeWriter.AsStream(leaveOpen);
        }

        public override async ValueTask<FlushResult> WriteAsync(ReadOnlyMemory<byte> source, CancellationToken cancellationToken = new CancellationToken())
        {
            await _bridgeHttpResponseFeature.TryStartResponseAsync(cancellationToken);

            return await _underlyingPipeWriter.WriteAsync(source, cancellationToken);
        }
    }
}
