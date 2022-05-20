using System;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace GrpcWebSocketBridge.Client.Internal
{
    internal class GrpcWebSocketResponseContent : HttpContent
    {
        private readonly PipeReader _pipeReader;
        private readonly Action _onDispose;

        public GrpcWebSocketResponseContent(PipeReader pipeReader, Action onDispose)
        {
            _pipeReader = pipeReader;
            _onDispose = onDispose;
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext context)
        {
            await _pipeReader.CopyToAsync(stream).ConfigureAwait(false);
            await _pipeReader.CompleteAsync().ConfigureAwait(false);
        }

        protected override Task<Stream> CreateContentReadStreamAsync()
        {
            return Task.FromResult(_pipeReader.AsStream());
        }

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            _onDispose();
            base.Dispose(disposing);
        }
    }
}
