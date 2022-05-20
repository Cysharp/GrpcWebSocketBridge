using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Grpc.AspNetCore.Web.Internal;
using GrpcWebSocketBridge.AspNetCore.Internal;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;

namespace GrpcWebSocketBridge.AspNetCore
{
    public class GrpcWebBridgeMiddleware
    {
        private readonly RequestDelegate _next;

        public GrpcWebBridgeMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (!HttpMethods.IsPost(context.Request.Method) || context.Request.ContentType != "application/grpc-web")
            {
                await _next(context);
                return;
            }

            var feature = new GrpcWebFeature(context);

            var originalProtocol = context.Request.Protocol;
            context.Request.Headers["content-type"] = new StringValues("application/grpc");
            context.Request.Protocol = HttpProtocol.Http2;

            context.Response.OnStarting(() =>
            {
                context.Request.Protocol = originalProtocol;
                return Task.CompletedTask;
            });

            try
            {
                await _next(context);
                await feature.WriteTrailersAsync();
            }
            finally
            {
                feature.DetachFromContext(context);
            }
        }
    }
}
