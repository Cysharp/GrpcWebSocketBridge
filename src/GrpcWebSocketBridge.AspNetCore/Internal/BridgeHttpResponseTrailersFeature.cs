using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace GrpcWebSocketBridge.AspNetCore.Internal
{
    internal class BridgeHttpResponseTrailersFeature : IHttpResponseTrailersFeature
    {
        private readonly IHttpResponseTrailersFeature? _origHttpResponseTrailersFeature;

        public IHeaderDictionary Trailers { get; set; } = new HeaderDictionary();

        public BridgeHttpResponseTrailersFeature(IHttpResponseTrailersFeature? origHttpResponseTrailersFeature)
        {
            _origHttpResponseTrailersFeature = origHttpResponseTrailersFeature;
        }
    }
}
