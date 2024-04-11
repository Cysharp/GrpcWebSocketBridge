// NET_STANDARD is .NET Standard 2.1 on Unity
#if NET_STANDARD_2_0
#define NETSTANDARD2_0
#endif
#if NET_STANDARD || NET_STANDARD_2_1
#define NETSTANDARD2_1
#undef NETSTANDARD2_0 // NOTE: Same symbols defined as in .NET SDK
#pragma warning disable CS8632 // The annotation for nullable reference types should only be used in code within a '#nullable' annotations context.
#endif

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Grpc.AspNetCore.Web.Internal;
using Grpc.Net.Client.Internal;
using Grpc.Shared;
using GrpcWebSocketBridge.Client.Internal;
using GrpcWebSocketBridge.Client.WebSockets;

namespace GrpcWebSocketBridge.Client
{
    public partial class GrpcWebSocketBridgeHandler : DelegatingHandler
    {
        private readonly HashSet<IClientWebSocket> _ongoingWebSockets = new HashSet<IClientWebSocket>();
        private readonly bool _forceWebSocketMode = false;

        public GrpcWebSocketBridgeHandler(bool forceWebSocketMode = false)
#if UNITY_2018_1_OR_NEWER
            : base(new GrpcWebSocketBridge.Client.Unity.UnityWebRequestHttpHandler())
#else
            : base(new HttpClientHandler())
#endif
        {
            _forceWebSocketMode = forceWebSocketMode;
        }

        private void AddToOngoing(IClientWebSocket webSocket)
        {
            lock (_ongoingWebSockets)
            {
                _ongoingWebSockets.Add(webSocket);
            }
        }

        private void RemoveFromOngoing(IClientWebSocket webSocket)
        {
            lock (_ongoingWebSockets)
            {
                _ongoingWebSockets.Remove(webSocket);
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // If the request invokes Unary method, use HTTP/1 transport instead of WebSocket.
            return (request.Content.GetType().Name.Contains("Unary") && !_forceWebSocketMode)
                ? SendWithHttpHandlerAsync(request, cancellationToken)
                : SendWithWebSocketAsync(request, cancellationToken);
        }

        private async Task<HttpResponseMessage> SendWithHttpHandlerAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Version = HttpVersion.Version11; // NOTE: Force downgrade to HTTP/1.1
            request.Content = new GrpcWebRequestContent(request.Content);

            // WORKAROUND: Suppress `The header <header> is managed automatically, setting it may have no effect or result in unexpected behavior.`
            request.Headers.Remove("TE");
            request.Headers.Remove("User-Agent");

            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.Content != null)
            {
#if NETSTANDARD2_0
                response.EnsureTrailingHeaders();
#endif
            }

            response.Content = new GrpcWebResponseContent(response.Content, response.TrailingHeaders());
            response.Version = GrpcProtocolConstants.Http2Version;

            return response;
        }

        private async Task<HttpResponseMessage> SendWithWebSocketAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Content = new GrpcWebSocketRequestContent(request.Content);

            // WORKAROUND: Suppress `The header <header> is managed automatically, setting it may have no effect or result in unexpected behavior.`
            request.Headers.Remove("TE");
            request.Headers.Remove("User-Agent");

            // NOTE: .NET Framework's System.Net.Http.HttpRequestMessage don't allow 'ws' and 'wss' scheme.
            var uri = new Uri(request.RequestUri.ToString().Replace("http://", "ws://").Replace("https://", "wss://"));

            var clientWebSocket = CreateClientWebSocket();
            AddToOngoing(clientWebSocket);

            clientWebSocket.AddSubProtocol(GrpcWebSocketBridgeSubProtocol.Protocol);
            await clientWebSocket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);


            var arrayBufferWriter = new ArrayBufferWriter<byte>();
            GrpcWebProtocolHelpers.WriteTrailers(request.Headers, arrayBufferWriter);
            await clientWebSocket.SendAsync(new ArraySegment<byte>(arrayBufferWriter.WrittenMemory.ToArray()), WebSocketMessageType.Binary, true, cancellationToken).ConfigureAwait(false);

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Version = GrpcProtocolConstants.Http2Version,
                RequestMessage = request,
            };
#if NETSTANDARD2_0
            response.EnsureTrailingHeaders();
#endif

            var ctx = new ConnectionContext(new Pipe(PipeOptions), new Pipe(PipeOptions));
            var requestPushTask = ProcessRequestAsync(clientWebSocket, request, ctx, cancellationToken);
            var responseTask = ProcessResponseAsync(clientWebSocket, response, ctx, cancellationToken);

            var responseContent = new GrpcWebSocketResponseContent(ctx.ResponsePipe.Reader, () => RemoveFromOngoing(clientWebSocket));
            responseContent.Headers.ContentType = new MediaTypeHeaderValue(GrpcProtocolConstants.GrpcContentType);
            response.Content = responseContent;

            await ctx.ResponseHeaderReceived.ConfigureAwait(false);

            return response;
        }


        private async Task ProcessRequestAsync(IClientWebSocket clientWebSocket, HttpRequestMessage request, ConnectionContext ctx, CancellationToken cancellationToken)
        {
            cancellationToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ctx.ConnectionAborted).Token;

            _ = request.Content.CopyToAsync(ctx.RequestPipe.Writer.AsStream()).ConfigureAwait(false);

            try
            {
                while (clientWebSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    var result = await ctx.RequestPipe.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                    if (result.Buffer.Length > 0)
                    {
                        var buffer = ArrayPool<byte>.Shared.Rent((int)result.Buffer.Length);
                        try
                        {
                            result.Buffer.CopyTo(buffer);
                            await clientWebSocket.SendAsync(new ArraySegment<byte>(buffer, 0, (int)result.Buffer.Length), WebSocketMessageType.Binary, true, cancellationToken).ConfigureAwait(false);
                        }
                        finally
                        {
                            ArrayPool<byte>.Shared.Return(buffer);
                        }
                    }

                    if (result.IsCompleted || result.IsCanceled)
                    {
                        return;
                    }

                    ctx.RequestPipe.Reader.AdvanceTo(result.Buffer.End);
                }
            }
            catch (Exception e)
            {
                await ctx.RequestAbortedAsync(e).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    // Send a empty trailer for completion.
                    await clientWebSocket.SendAsync(new ArraySegment<byte>(new byte[] { 0b10000000, 0x00, 0x00, 0x00, 0x00 }), WebSocketMessageType.Binary, true, cancellationToken).ConfigureAwait(false);
                }
                catch (WebSocketException e) when (e.WebSocketErrorCode == WebSocketError.InvalidState)
                {
                    // ignore errors when trying to send to already closed web socket
                }
                finally
                {
                    await ctx.CompleteRequestAsync().ConfigureAwait(false);
                }
            }
        }

        private async Task ProcessResponseAsync(IClientWebSocket clientWebSocket, HttpResponseMessage response, ConnectionContext ctx, CancellationToken cancellationToken)
        {
            cancellationToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ctx.ConnectionAborted).Token;

            var reader = new GrpcWebSocketBufferReader();
            var buffer = ArrayPool<byte>.Shared.Rent(minimumLength: 32 * 1024);
            var bufferWriter = new ArrayBufferWriter<byte>();
            var readOffset = 0;
            try
            {
                while (clientWebSocket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
                {
                    var result = await clientWebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);
                    if (result.Count > 0)
                    {
                        bufferWriter.Write(buffer.AsSpan(0, result.Count));
                    }

                    if (result.EndOfMessage)
                    {
                        while (reader.TryRead(bufferWriter.WrittenMemory.Slice(readOffset), out var readResult))
                        {
                            switch (readResult.Type)
                            {
                                case GrpcWebSocketBufferReader.BufferReadResultType.Header:
                                    foreach (var keyValue in readResult.HeadersOrTrailers)
                                    {
                                        response.Headers.TryAddWithoutValidation(keyValue.Key, keyValue.Value);
                                    }
                                    ctx.SignalResponseHeaderHasReceived();
                                    break;
                                case GrpcWebSocketBufferReader.BufferReadResultType.Trailer:
                                    var trailers = response.TrailingHeaders();
                                    foreach (var keyValue in readResult.HeadersOrTrailers)
                                    {
                                        trailers.TryAddWithoutValidation(keyValue.Key, keyValue.Value);
                                    }

                                    // The response is completed.
                                    await ctx.CompleteResponseAsync().ConfigureAwait(false);
                                    return;
                                case GrpcWebSocketBufferReader.BufferReadResultType.Content:
                                    await ctx.ResponsePipe.Writer.WriteAsync(readResult.Data, cancellationToken).ConfigureAwait(false);
                                    break;
                            }

                            readOffset += readResult.Consumed;
                        }

                        if (readOffset == bufferWriter.WrittenCount)
                        {
                            bufferWriter.Clear();
                            readOffset = 0;
                        }
                    }
                }

                if (clientWebSocket.State == WebSocketState.Aborted && !ctx.ResponseCompleted)
                {
                    await ctx.RequestAbortedAsync().ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                await ctx.RequestAbortedAsync(e).ConfigureAwait(false);
            }
            finally
            {
                await ctx.CompleteResponseAsync().ConfigureAwait(false);
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (_ongoingWebSockets)
                {
                    foreach (var clientWebSocket in _ongoingWebSockets)
                    {
                        clientWebSocket.Dispose();
                    }
                    _ongoingWebSockets.Clear();
                }
            }

            base.Dispose(disposing);
        }
    }
}
