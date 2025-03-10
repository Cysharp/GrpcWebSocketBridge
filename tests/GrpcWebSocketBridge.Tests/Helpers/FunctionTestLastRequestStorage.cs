using System.Diagnostics.CodeAnalysis;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace GrpcWebSocketBridge.Tests.Helpers;

public static class FunctionTestLastRequestStorageExtensions
{
    public static IDictionary<string, object> GetTestStorageItems(this ServerCallContext serverCallContext)
    {
        return serverCallContext.GetHttpContext().Features.GetRequiredFeature<FunctionTestLastRequestStorageFeature>().Items;
    }
}

public class FunctionTestLastRequestStorage
{
    private readonly TaskCompletionSource<bool> _tcsCompleted = new();

    public IReadOnlyDictionary<string, object>? Items { get; private set; }

    public Task Completed => _tcsCompleted.Task;

    public string? Path { get; private set; }
    public string? Protocol { get; private set; }
    public int StatusCode { get; private set; }
    public IHeaderDictionary? RequestHeaders { get; private set; }
    public IHeaderDictionary? ResponseHeaders { get; private set; }
    public IHeaderDictionary? ResponseTrailers { get; private set; }

    [MemberNotNull(nameof(Items))]
    [MemberNotNull(nameof(Path))]
    [MemberNotNull(nameof(Protocol))]
    [MemberNotNull(nameof(RequestHeaders))]
    [MemberNotNull(nameof(ResponseHeaders))]
    [MemberNotNull(nameof(ResponseTrailers))]
    public void EnsureLastStates()
    {
        if (Items is null || Path is null || Protocol is null || RequestHeaders is null || ResponseHeaders is null || ResponseTrailers is null)
        {
            throw new InvalidOperationException();
        }
    }

    public void SetLastStates(HttpContext context, IReadOnlyDictionary<string, object> items)
    {
        Path = context.Request.Path;
        Protocol = context.Request.Protocol;
        StatusCode = context.Response.StatusCode;
        RequestHeaders = context.Request.Headers;
        ResponseHeaders = context.Response.Headers;
        ResponseTrailers = context.Features.GetRequiredFeature<IHttpResponseTrailersFeature>().Trailers;
        Items = items;

        _tcsCompleted.TrySetResult(true);
    }
}

public class FunctionTestLastRequestStorageMiddleware(RequestDelegate next, FunctionTestLastRequestStorage storage)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var storageFeature = new FunctionTestLastRequestStorageFeature();
        context.Features.Set(storageFeature);
        try
        {
            await next(context);
        }
        finally
        {
            storage.SetLastStates(context, storageFeature.Items);
        }
    }
}

public class FunctionTestLastRequestStorageFeature
{
    public Dictionary<string, object> Items { get; } = new();
}
