using System;
using System.Buffers;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using GrpcWebSocketBridge.AspNetCore.Internal;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;

namespace GrpcWebSocketBridge.AspNetCore
{
    public class GrpcWebSocketBridgeMiddleware
    {
        private readonly RequestDelegate _next;

        public GrpcWebSocketBridgeMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (context.Features.Get<GrpcWebSocketBridgeFeature>() is null)
            {
                await _next(context);
                return;
            }

            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync("Bad Request");
                return;
            }

            context.Request.Headers["content-type"] = new StringValues("application/grpc");
            context.Request.Protocol = HttpProtocol.Http2;
            context.Response.StatusCode = 200;

            var readyToRunTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var socket = await context.WebSockets.AcceptWebSocketAsync(GrpcWebSocketBridgeSubProtocol.Protocol);
            var bridgeContext = CreatePipeFromWebSocket(context, socket, readyToRunTcs.Task, context.RequestAborted);

            var origHttpResponseTrailersFeature = context.Features.Get<IHttpResponseTrailersFeature>(); // NOTE: In most cases, IHttpResponseTrailersFeature will be null.
            var origHttpResponseFeature = context.Features.Get<IHttpResponseFeature>() ?? throw new InvalidOperationException($"Could not get {nameof(IHttpResponseFeature)} from HttpContext.Features.");
            var origRequestBodyPipeFeature = context.Features.Get<IRequestBodyPipeFeature>() ?? throw new InvalidOperationException($"Could not get {nameof(IRequestBodyPipeFeature)} from HttpContext.Features.");
            var origHttpResponseBodyFeature = context.Features.Get<IHttpResponseBodyFeature>() ?? throw new InvalidOperationException($"Could not get {nameof(IHttpResponseBodyFeature)} from HttpContext.Features.");
            var origHttpRequestFeature = context.Features.Get<IHttpRequestFeature>() ?? throw new InvalidOperationException($"Could not get {nameof(IHttpRequestFeature)} from HttpContext.Features.");

            context.Features.Set<IHttpRequestFeature>(new BridgeHttpRequestFeature(origHttpRequestFeature));
            context.Features.Set<IRequestBodyPipeFeature>(new BridgeRequestBodyPipeFeature(bridgeContext, origRequestBodyPipeFeature));
            var newHttpResponseFeature = new BridgeHttpResponseFeature(bridgeContext, origHttpResponseFeature);
            context.Features.Set<IHttpResponseFeature>(newHttpResponseFeature);
            var newHttpResponseTrailersFeature = new BridgeHttpResponseTrailersFeature(origHttpResponseTrailersFeature);
            context.Features.Set<IHttpResponseTrailersFeature>(newHttpResponseTrailersFeature);
            var newHttpResponseBodyFeature = new BridgeHttpResponseBodyFeature(bridgeContext, newHttpResponseFeature, newHttpResponseTrailersFeature, origHttpResponseBodyFeature);
            context.Features.Set<IHttpResponseBodyFeature>(newHttpResponseBodyFeature);

            // All features are ready to run the reader/writer loop.
            readyToRunTcs.TrySetResult();

            await bridgeContext.RequestHeaderReceivedTask;

            await _next(context);

            await newHttpResponseBodyFeature.CompleteAsync();
        }

        private WebSocketBridgeContext CreatePipeFromWebSocket(HttpContext context, WebSocket webSocket, Task readyToRunTask, CancellationToken cancellationToken)
        {
            var requestHeaderReceivedTcs = new TaskCompletionSource();

            // Client --(WebSocket)--> Reader
            var readerPipe = new Pipe();
            var readerTask = RunReadFromClientLoopAsync(readerPipe.Writer, webSocket, context, requestHeaderReceivedTcs, readyToRunTask, cancellationToken);

            // Server --(WebSocket)--> Client
            var writerPipe = new Pipe();
            var writerTask = RunWriteToClientLoopAsync(writerPipe, webSocket, readyToRunTask, cancellationToken);

            return new WebSocketBridgeContext(webSocket, writerPipe.Writer, readerPipe.Reader, writerTask, readerTask, requestHeaderReceivedTcs.Task);
        }

        private async Task RunReadFromClientLoopAsync(PipeWriter websocketPipeWriter, WebSocket webSocket, HttpContext context, TaskCompletionSource requestHeaderReceivedTcs, Task readyToRunTask, CancellationToken cancellationToken)
        {
            // Wait until the features are ready to run.
            await readyToRunTask;

            var bufferArray = ArrayPool<byte>.Shared.Rent(minimumLength: 32 * 1024);
            var isRequestCompleted = false;
            var isPipeCompleted = false;

            var reader = new GrpcWebSocketBufferReader();
            var bufferWriter = new ArrayBufferWriter<byte>();
            var consumed = 0;
            try
            {
                while (webSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested && !isRequestCompleted)
                {
                    var result = await webSocket.ReceiveAsync(bufferArray, cancellationToken);
                    if (result.MessageType != WebSocketMessageType.Binary) continue;

                    bufferWriter.Write(bufferArray.AsSpan(0, result.Count));

                    while (reader.TryRead(bufferWriter.WrittenMemory.Slice(consumed), out var readResult))
                    {
                        switch (readResult.Type)
                        {
                            case GrpcWebSocketBufferReader.BufferReadResultType.Header:
                                foreach (var (key, value) in readResult.HeadersOrTrailers!)
                                {
                                    context.Request.Headers[key] = new StringValues(value.ToArray());
                                }

                                requestHeaderReceivedTcs.TrySetResult();
                                break;
                            case GrpcWebSocketBufferReader.BufferReadResultType.Content:
                                await websocketPipeWriter.WriteAsync(readResult.Data, cancellationToken);
                                await websocketPipeWriter.FlushAsync(cancellationToken);
                                break;
                            case GrpcWebSocketBufferReader.BufferReadResultType.Trailer:
                                isRequestCompleted = true;
                                break;
                        }

                        consumed += readResult.Consumed;

                        if (consumed == bufferWriter.WrittenCount)
                        {
                            consumed = 0;
                            bufferWriter.Clear();
                        }
                    }
                }

                if (webSocket.State == WebSocketState.CloseReceived)
                {
                    await webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Complete", cancellationToken);
                }
            }
            catch (Exception e) when (e is ConnectionAbortedException || e is WebSocketException)
            {
                // When the WebSocket connection has been closed, Ignore ConnectionAbortedException and WebSocketException.
                if (!isRequestCompleted)
                {
                    await websocketPipeWriter.CompleteAsync(new IOException("The request was aborted.", e));
                    isPipeCompleted = true;
                }
            }
            finally
            {
                if (!isPipeCompleted)
                {
                    await websocketPipeWriter.CompleteAsync();
                }

                requestHeaderReceivedTcs.TrySetCanceled();

                ArrayPool<byte>.Shared.Return(bufferArray);
            }
        }

        private async Task RunWriteToClientLoopAsync(Pipe writerPipe, WebSocket webSocket, Task readyToRunTask, CancellationToken cancellationToken)
        {
            // Wait until the features are ready to run.
            await readyToRunTask;

            using var stream = writerPipe.Reader.AsStream();
            var bufferArray = ArrayPool<byte>.Shared.Rent(minimumLength: 32 * 1024);
            try
            {
                var readLen = 0;
                while ((readLen = await stream.ReadAsync(bufferArray, cancellationToken)) > 0)
                {
                    if (webSocket.State == WebSocketState.Open)
                    {
                        await webSocket.SendAsync(bufferArray.AsMemory(0, readLen), WebSocketMessageType.Binary, true, cancellationToken);
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(bufferArray);
            }
        }
    }
}
