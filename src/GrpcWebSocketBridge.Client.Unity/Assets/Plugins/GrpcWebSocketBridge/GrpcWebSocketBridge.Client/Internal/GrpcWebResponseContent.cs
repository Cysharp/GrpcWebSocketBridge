using System;
using System.IO;
using System.IO.Pipelines;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Grpc.Net.Client.Internal;
using Grpc.Net.Client.Web.Internal;

namespace GrpcWebSocketBridge.Client.Internal
{
    internal class GrpcWebResponseContent : HttpContent
    {
        private readonly HttpContent _innerContent;
        private readonly HttpHeaders _responseTrailers;
        private Stream _grpcWebStream;

        public GrpcWebResponseContent(HttpContent innerContent, HttpHeaders responseTrailers)
        {
            _innerContent = innerContent;
            _responseTrailers = responseTrailers;

            foreach (var header in innerContent.Headers)
            {
                Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            Headers.ContentType = GrpcProtocolConstants.GrpcContentTypeHeaderValue;
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext context)
        {
            var innerStream = await _innerContent.ReadAsStreamAsync().ConfigureAwait(false);
            _grpcWebStream = new GrpcWebResponseStream(innerStream, _responseTrailers);
            await _grpcWebStream.CopyToAsync(stream).ConfigureAwait(false);
        }

        protected override async Task<Stream> CreateContentReadStreamAsync()
        {
            var innerStream = await _innerContent.ReadAsStreamAsync().ConfigureAwait(false);
            return new GrpcWebResponseStream(innerStream, _responseTrailers);
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
                _grpcWebStream?.Dispose();
                _innerContent.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
