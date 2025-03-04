using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using GrpcWebSocketBridge.Client;
using GrpcWebSocketBridge.Tests.Helpers;
using GrpcWebSocketBridge.Tests.Protos;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace GrpcWebSocketBridge.Tests.FunctionalTests
{
    // NOTE: Currently, GrpcWebSocketBridge.Client only supports ServerStreaming over WebSocket.
    //       To run the test, `forceWebSocketMode: true` must be specified.
    public class ServerStreamingTest : UseTestServerTestBase
    {
        public ServerStreamingTest(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
        {
        }

        [Fact]
        public async Task NoHeaders_NoTrailers_NoResponses()
        {
            await using var host = CreateTestServer<StartupWithGrpcService<GreeterServiceNoHeadersNoTrailersNoResponses>>();
            using var channel = host.CreateChannel();

            var client = new Greeter.GreeterClient(channel);
            var serverStreaming = client.SayHelloServerStreaming(new HelloRequest());

            var responses = await serverStreaming.ResponseStream.ReadAllAsync(TimeoutToken).ToArrayAsync(TimeoutToken);

            responses.ShouldBeEmpty();
        }

        [Fact]
        public async Task NoHeaders_NoTrailers_NoResponses_WebSocket()
        {
            await using var host = CreateTestServer<StartupWithGrpcService<GreeterServiceNoHeadersNoTrailersNoResponses>>();
            using var channel = host.CreateChannel(ChannelKind.InsecureHttp1, forceWebSocketMode: true); // Force WebSocket

            var client = new Greeter.GreeterClient(channel);
            var serverStreaming = client.SayHelloServerStreaming(new HelloRequest());

            var responses = await serverStreaming.ResponseStream.ReadAllAsync(TimeoutToken).ToArrayAsync(TimeoutToken);

            responses.ShouldBeEmpty();

            await host.LastRequest.Completed;
            host.LastRequest.RequestHeaders.ShouldContainKey("Upgrade");
            host.LastRequest.RequestHeaders["Upgrade"].ToString().ShouldBe("websocket");
            host.LastRequest.Protocol.ShouldBe("HTTP/2"); // Fake HTTP/2
            host.LastRequest.StatusCode.ShouldBe(101); // 101 Switch Protocol (upgrade to WebSocket)
        }

        class GreeterServiceNoHeadersNoTrailersNoResponses : Greeter.GreeterBase
        {
            public override Task SayHelloServerStreaming(HelloRequest request, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
            {
                return Task.CompletedTask;
            }
        }

        [Fact]
        public async Task NoHeaders_NoTrailers_Response()
        {
            await using var host = CreateTestServer<StartupWithGrpcService<GreeterServiceNoHeadersNoTrailersResponseAfterRequest>>();
            using var channel = host.CreateChannel(forceWebSocketMode: true);

            var client = new Greeter.GreeterClient(channel);
            var serverStreaming = client.SayHelloServerStreaming(new HelloRequest { Name = "Req#1" });

            await Task.WhenAny(serverStreaming.ResponseHeadersAsync, Task.Delay(100) /* wait for 100ms */);
            serverStreaming.ResponseHeadersAsync.IsCompleted.ShouldBeTrue();

            // Read from the response stream.
            var response = await serverStreaming.ResponseStream.ReadAllAsync(TimeoutToken).FirstAsync(TimeoutToken);
            response.Message.ShouldBe("#1");
            serverStreaming.ResponseHeadersAsync.IsCompleted.ShouldBeTrue();

            // The response stream has ended.
            (await serverStreaming.ResponseStream.MoveNext(TimeoutToken)).ShouldBeFalse();

            await host.LastRequest.Completed;
            host.LastRequest.Items["RequestStream:First.Name"].ShouldBe("Req#1");
        }

        class GreeterServiceNoHeadersNoTrailersResponseAfterRequest : Greeter.GreeterBase
        {
            public override async Task SayHelloServerStreaming(HelloRequest request, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
            {
                context.GetTestStorageItems()["RequestStream:First.Name"] = request.Name;

                // Write a response to the response stream.
                await responseStream.WriteAsync(new HelloReply() { Message = "#1" });
            }
        }

        [Fact]
        public async Task WithHeaders_NoTrailers_Response_Before_Request()
        {
            await using var host = CreateTestServer<StartupWithGrpcService<GreeterServiceWithHeadersNoTrailersResponseBeforeRequest>>();
            using var channel = host.CreateChannel(forceWebSocketMode: true);

            var client = new Greeter.GreeterClient(channel);
            var serverStreaming = client.SayHelloServerStreaming(new HelloRequest { Name = "Req#1" });

            // 1. The server pushes the response headers.
            var responseHeaders = await serverStreaming.ResponseHeadersAsync.WithCancellation(TimeoutToken);
            responseHeaders.ShouldContain(x => x.Key == "x-header-1");
            responseHeaders.ShouldContain(x => x.Key == "x-header-2-bin" && x.IsBinary);
            responseHeaders.GetValueBytes("x-header-2-bin").ShouldBe(new byte[] { 1, 2, 3, 4 });

            // 2. Read from the response stream.
            var response = await serverStreaming.ResponseStream.ReadAllAsync(TimeoutToken).FirstAsync(TimeoutToken);
            response.Message.ShouldBe("#1");

            // The response stream has ended.
            (await serverStreaming.ResponseStream.MoveNext(TimeoutToken)).ShouldBeFalse();
        }

        class GreeterServiceWithHeadersNoTrailersResponseBeforeRequest : Greeter.GreeterBase
        {
            public override async Task SayHelloServerStreaming(HelloRequest request, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
            {
                var headers = new Metadata();
                headers.Add("x-header-1", "value1");
                headers.Add("x-header-2-bin", new byte[] { 1, 2, 3, 4 });

                await context.WriteResponseHeadersAsync(headers);
                await responseStream.WriteAsync(new HelloReply() { Message = "#1" });
            }
        }

        [Fact]
        public async Task WithRequestHeaders_NoTrailers_Response_Before_Request()
        {
            await using var host = CreateTestServer<StartupWithGrpcService<GreeterServiceWithRequestHeadersNoTrailersResponseBeforeRequest>>();
            using var channel = host.CreateChannel(forceWebSocketMode: true);

            var headers = new Metadata();
            headers.Add("x-header-1", "value1");
            headers.Add("x-header-2-bin", new byte[] { 1, 2, 3, 4 });

            var client = new Greeter.GreeterClient(channel);
            var serverStreaming = client.SayHelloServerStreaming(new HelloRequest { Name = "Req#1" }, headers);

            // The response stream has ended.
            (await serverStreaming.ResponseStream.MoveNext(TimeoutToken)).ShouldBeFalse();

            await host.LastRequest.Completed;

            host.LastRequest.Items["Server:x-header-1:Value"].ShouldBe("value1");
            host.LastRequest.Items["Server:x-header-1:IsBinary"].ShouldBe(false);
            host.LastRequest.Items["Server:x-header-2-bin:ValueBytes"].ShouldBe(new byte[] {1, 2, 3, 4});
            host.LastRequest.Items["Server:x-header-2-bin:IsBinary"].ShouldBe(true);
        }

        class GreeterServiceWithRequestHeadersNoTrailersResponseBeforeRequest : Greeter.GreeterBase
        {
            public override Task SayHelloServerStreaming(HelloRequest request, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
            {
                context.GetTestStorageItems()["Server:x-header-1:Value"] = context.RequestHeaders.Get("x-header-1").Value;
                context.GetTestStorageItems()["Server:x-header-1:IsBinary"] = context.RequestHeaders.Get("x-header-1").IsBinary;
                context.GetTestStorageItems()["Server:x-header-2-bin:ValueBytes"] = context.RequestHeaders.Get("x-header-2-bin").ValueBytes;
                context.GetTestStorageItems()["Server:x-header-2-bin:IsBinary"] = context.RequestHeaders.Get("x-header-2-bin").IsBinary;

                return Task.CompletedTask;
            }
        }

        [Fact]
        public async Task NoHeaders_WithResponseTrailers()
        {
            await using var host = CreateTestServer<StartupWithGrpcService<GreeterServiceNoHeadersWithResponseTrailers>>();
            using var channel = host.CreateChannel(forceWebSocketMode: true);

            var client = new Greeter.GreeterClient(channel);
            var serverStreaming = client.SayHelloServerStreaming(new HelloRequest { Name = "Req#1" });

            Should.Throw<InvalidOperationException>(() => serverStreaming.GetStatus());
            Should.Throw<InvalidOperationException>(() => serverStreaming.GetTrailers());

            // The response stream has ended.
            (await serverStreaming.ResponseStream.MoveNext(TimeoutToken)).ShouldBeFalse();

            var status = serverStreaming.GetStatus();
            var responseTrailers = serverStreaming.GetTrailers();
            responseTrailers.ShouldContain(x => x.Key == "x-trailer-1");
            responseTrailers.ShouldContain(x => x.Key == "x-trailer-2-bin" && x.IsBinary);
            responseTrailers.GetValueBytes("x-trailer-2-bin").ShouldBe(new byte[] { 5, 4, 3, 2, 1 });
        }

        class GreeterServiceNoHeadersWithResponseTrailers : Greeter.GreeterBase
        {
            public override async Task SayHelloServerStreaming(HelloRequest request, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
            {
                await Task.Delay(250);
                context.ResponseTrailers.Add("x-trailer-1", "trailerValue");
                context.ResponseTrailers.Add("x-trailer-2-bin", new byte[] { 5, 4, 3, 2, 1 });
            }
        }

        [Fact]
        public async Task NoRequest_ResponseImmediately()
        {
            await using var host = CreateTestServer<StartupWithGrpcService<GreeterServiceNoRequestResponseImmediately>>();
            using var channel = host.CreateChannel(forceWebSocketMode: true);

            var client = new Greeter.GreeterClient(channel);
            var serverStreaming = client.SayHelloServerStreaming(new HelloRequest { Name = "Req#1" });

            // Executing SayHelloServerStreaming will be complete immediately. And the response stream will be also done immediately.
            await serverStreaming.ResponseHeadersAsync.WithCancellation(TimeoutToken);
            var responses = await serverStreaming.ResponseStream.ReadAllAsync(TimeoutToken).ToArrayAsync(TimeoutToken);
            responses.ShouldBeEmpty();
        }

        class GreeterServiceNoRequestResponseImmediately : Greeter.GreeterBase
        {
            public override Task SayHelloServerStreaming(HelloRequest request, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
            {
                return Task.CompletedTask;
            }
        }


        [Fact]
        public async Task Incomplete_Response()
        {
            await using var host = CreateTestServer<StartupWithGrpcService<GreeterServiceIncompleteResponse>>();
            using var channel = host.CreateChannel(forceWebSocketMode: true);

            var client = new Greeter.GreeterClient(channel);
            var serverStreaming = client.SayHelloServerStreaming(new HelloRequest { Name = "Req#1" });

            await Should.ThrowAsync<TimeoutException>(async () => 
                await serverStreaming.ResponseStream.ReadAllAsync(TimeoutToken).ToArrayAsync(TimeoutToken).WithTimeout(TimeSpan.FromSeconds(1))
            );
        }

        class GreeterServiceIncompleteResponse : Greeter.GreeterBase
        {
            public override async Task SayHelloServerStreaming(HelloRequest request, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
            {
                // Never
                await Task.Delay(-1, context.CancellationToken);
            }
        }


        [Fact]
        public async Task LargePayload_Response()
        {
            await using var host = CreateTestServer<StartupWithGrpcService<GreeterServiceLargePayloadResponse>>();
            using var channel = host.CreateChannel(forceWebSocketMode: true);

            var client = new Greeter.GreeterClient(channel);
            var serverStreaming = client.SayHelloServerStreaming(new HelloRequest { Name = "Req#1" });

            var sb = new StringBuilder(100_000);
            for (var i = 0; i < 100_000; i++)
            {
                sb.Append(i);
            }

            (await serverStreaming.ResponseStream.MoveNext(TimeoutToken)).ShouldBeTrue();
            serverStreaming.ResponseStream.Current.Message.ShouldBe(sb.ToString());

            (await serverStreaming.ResponseStream.MoveNext(TimeoutToken)).ShouldBeTrue();
            serverStreaming.ResponseStream.Current.Message.ShouldBe("#2");
        }

        class GreeterServiceLargePayloadResponse : Greeter.GreeterBase
        {
            public override async Task SayHelloServerStreaming(HelloRequest request, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
            {
                var sb = new StringBuilder(100_000);
                for (var i = 0; i < 100_000; i++)
                {
                    sb.Append(i);
                }

                await responseStream.WriteAsync(new HelloReply { Message = sb.ToString() });
                await responseStream.WriteAsync(new HelloReply { Message = "#2" });
            }
        }
    }
}
