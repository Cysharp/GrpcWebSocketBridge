using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GrpcWebSocketBridge.AspNetCore;

// ReSharper disable once CheckNamespace
namespace Microsoft.AspNetCore.Builder
{
    public static class GrpcWebSocketBridgeExtensions
    {
        public static IApplicationBuilder UseGrpcWebSocketRequestRoutingEnabler(this IApplicationBuilder app)
        {
            app.UseMiddleware<GrpcWebSocketRequestRoutingEnablerMiddleware>();
            return app;
        }

        public static IApplicationBuilder UseGrpcWebSocketBridge(this IApplicationBuilder app)
        {
            app.UseMiddleware<GrpcWebSocketBridgeMiddleware>();
            app.UseMiddleware<GrpcWebBridgeMiddleware>();
            return app;
        }
    }
}
