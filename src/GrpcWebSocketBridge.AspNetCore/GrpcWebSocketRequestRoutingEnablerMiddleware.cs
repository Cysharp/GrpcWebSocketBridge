using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace GrpcWebSocketBridge.AspNetCore
{
    public class GrpcWebSocketRequestRoutingEnablerMiddleware
    {
        private readonly RequestDelegate _next;

        public GrpcWebSocketRequestRoutingEnablerMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.WebSockets.IsWebSocketRequest && context.WebSockets.WebSocketRequestedProtocols.Contains(GrpcWebSocketBridgeSubProtocol.Protocol))
            {
                // HACK: Make ASP.NET Core Routing aware that the request is gRPC (POST method).
                context.Request.Method = HttpMethod.Post.Method;
                context.Features.Set(new GrpcWebSocketBridgeFeature());
            }

            await _next(context);
        }
    }
}
