using System;
using System.IO.Pipelines;
using Microsoft.AspNetCore.Http.Features;

namespace GrpcWebSocketBridge.AspNetCore.Internal
{
    internal class BridgeRequestBodyPipeFeature : IRequestBodyPipeFeature
    {
        private readonly WebSocketBridgeContext _bridgeContext;
        public PipeReader Reader => _bridgeContext.Reader;

        public BridgeRequestBodyPipeFeature(WebSocketBridgeContext bridgeCtx, IRequestBodyPipeFeature origRequestBodyPipeFeature)
        {
            _bridgeContext = bridgeCtx ?? throw new ArgumentNullException(nameof(bridgeCtx));
        }
    }
}
