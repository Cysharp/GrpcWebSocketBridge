using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace GrpcWebSocketBridge.Client.Internal
{
    internal class GrpcWebSocketRequestContent : HttpContent
    {
        private readonly HttpContent _inner;
        public GrpcWebSocketRequestContent(HttpContent inner)
        {
            _inner = inner;
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext context)
        {
            await _inner.CopyToAsync(stream).ConfigureAwait(false);
            stream.Close(); // Finish PipeWriter
        }

        protected override bool TryComputeLength(out long length)
        {
            length = -1;
            return false;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
