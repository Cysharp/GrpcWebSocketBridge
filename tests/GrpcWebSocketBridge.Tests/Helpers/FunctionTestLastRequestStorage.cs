using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace GrpcWebSocketBridge.Tests.Helpers
{
    public static class FunctionTestLastRequestStorageExtensions
    {
        public static IDictionary<string, object> GetTestStorageItems(this ServerCallContext serverCallContext)
        {
            return serverCallContext.GetHttpContext().Features.Get<FunctionTestLastRequestStorageFeature>().Items;
        }
    }

    public class FunctionTestLastRequestStorage
    {
        private readonly TaskCompletionSource<bool> _tcsCompleted = new TaskCompletionSource<bool>();

        public IReadOnlyDictionary<string, object> Items { get; private set; }

        public Task Completed => _tcsCompleted.Task;

        public string Path { get; private set; }
        public string Protocol { get; private set; }
        public int StatusCode { get; private set; }
        public IHeaderDictionary RequestHeaders { get; private set; }
        public IHeaderDictionary ResponseHeaders { get; private set; }
        public IHeaderDictionary ResponseTrailers { get; private set; }


        public void SetLastStates(HttpContext context, IReadOnlyDictionary<string, object> items)
        {
            Path = context.Request.Path;
            Protocol = context.Request.Protocol;
            StatusCode = context.Response.StatusCode;
            RequestHeaders = context.Request.Headers;
            ResponseHeaders = context.Response.Headers;
            ResponseTrailers = context.Features.Get<IHttpResponseTrailersFeature>().Trailers;
            Items = items;

            _tcsCompleted.TrySetResult(true);
        }
    }

    public class FunctionTestLastRequestStorageMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly FunctionTestLastRequestStorage _storage;

        public FunctionTestLastRequestStorageMiddleware(RequestDelegate next, FunctionTestLastRequestStorage storage)
        {
            _next = next;
            _storage = storage;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var storageFeature = new FunctionTestLastRequestStorageFeature();
            context.Features.Set(storageFeature);
            try
            {
                await _next(context);
            }
            finally
            {
                _storage.SetLastStates(context, storageFeature.Items);
            }
        }
    }

    public class FunctionTestLastRequestStorageFeature
    {
        public Dictionary<string, object> Items { get; } = new Dictionary<string, object>();
    }
}
