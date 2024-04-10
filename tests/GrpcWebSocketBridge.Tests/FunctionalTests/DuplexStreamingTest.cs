using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using FluentAssertions.Common;
using Grpc.Core;
using Grpc.Net.Client;
using GrpcWebSocketBridge.Client;
using GrpcWebSocketBridge.Tests.Helpers;
using GrpcWebSocketBridge.Tests.Protos;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace GrpcWebSocketBridge.Tests.FunctionalTests
{
    public class DuplexStreamingTest : UseTestServerTestBase
    {
        public DuplexStreamingTest(ITestOutputHelper testOutputHelper) : base(testOutputHelper)
        {
        }

        [Fact]
        public async Task NoHeaders_NoTrailers_NoResponses()
        {
            await using var host = CreateTestServer<StartupWithGrpcService<GreeterServiceNoHeadersNoTrailersNoResponses>>();
            using var channel = host.CreateChannel();

            var client = new Greeter.GreeterClient(channel);
            var duplex = client.SayHelloDuplex();

            await duplex.RequestStream.CompleteAsync();
            var responses = await duplex.ResponseStream.ReadAllAsync(TimeoutToken).ToArrayAsync(TimeoutToken);

            responses.Should().BeEmpty();
        }

        [Fact]
        public async Task NoHeaders_NoTrailers_NoResponses_WebSocket()
        {
            await using var host = CreateTestServer<StartupWithGrpcService<GreeterServiceNoHeadersNoTrailersNoResponses>>();
            using var channel = host.CreateChannel(ChannelKind.InsecureHttp1); // Force WebSocket

            var client = new Greeter.GreeterClient(channel);
            var duplex = client.SayHelloDuplex();

            await duplex.RequestStream.CompleteAsync();
            var responses = await duplex.ResponseStream.ReadAllAsync(TimeoutToken).ToArrayAsync(TimeoutToken);

            responses.Should().BeEmpty();

            await host.LastRequest.Completed;
            host.LastRequest.RequestHeaders.Should().Contain("Upgrade", "websocket");
            host.LastRequest.Protocol.Should().Be("HTTP/2"); // Fake HTTP/2
            host.LastRequest.StatusCode.Should().Be(101); // 101 Switch Protocol (upgrade to WebSocket)
        }

        class GreeterServiceNoHeadersNoTrailersNoResponses : Greeter.GreeterBase
        {
            public override Task SayHelloDuplex(IAsyncStreamReader<HelloRequest> requestStream, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
            {
                return Task.CompletedTask;
            }
        }

        [Fact]
        public async Task NoHeaders_NoTrailers_Response_Before_Request()
        {
            await using var host = CreateTestServer<StartupWithGrpcService<GreeterServiceNoHeadersNoTrailersResponseBeforeRequest>>();
            using var channel = host.CreateChannel();

            var client = new Greeter.GreeterClient(channel);
            var duplex = client.SayHelloDuplex();

            // The response headers will be sent before responses.
            await duplex.ResponseHeadersAsync.WithCancellation(TimeoutToken);

            // First
            var response = await duplex.ResponseStream.ReadAllAsync(TimeoutToken).FirstAsync(TimeoutToken);
            response.Message.Should().Be("#1");

            // Notify the request stream has ended.
            await duplex.RequestStream.CompleteAsync();

            // The response stream has ended.
            (await duplex.ResponseStream.MoveNext(TimeoutToken)).Should().BeFalse();
        }

        class GreeterServiceNoHeadersNoTrailersResponseBeforeRequest : Greeter.GreeterBase
        {
            public override async Task SayHelloDuplex(IAsyncStreamReader<HelloRequest> requestStream, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
            {
                await responseStream.WriteAsync(new HelloReply() { Message = "#1" });
            }
        }


        [Fact]
        public async Task NoHeaders_NoTrailers_Response_After_Request()
        {
            await using var host = CreateTestServer<StartupWithGrpcService<GreeterServiceNoHeadersNoTrailersResponseAfterRequest>>();
            using var channel = host.CreateChannel();

            var client = new Greeter.GreeterClient(channel);
            var duplex = client.SayHelloDuplex();

            await Task.WhenAny(duplex.ResponseHeadersAsync, Task.Delay(100) /* wait for 100ms */);
            duplex.ResponseHeadersAsync.IsCompleted.Should().BeFalse();

            // 1. Write to the request stream
            await duplex.RequestStream.WriteAsync(new HelloRequest() { Name = "Req#1"});

            // 2. Read from the response stream.
            var response = await duplex.ResponseStream.ReadAllAsync(TimeoutToken).FirstAsync(TimeoutToken);
            response.Message.Should().Be("#1");
            duplex.ResponseHeadersAsync.IsCompleted.Should().BeTrue();

            // Notify the request stream has ended.
            await duplex.RequestStream.CompleteAsync();

            // The response stream has ended.
            (await duplex.ResponseStream.MoveNext(TimeoutToken)).Should().BeFalse();

            await host.LastRequest.Completed;
            host.LastRequest.Items["RequestStream:First.Name"].Should().Be("Req#1");
        }

        class GreeterServiceNoHeadersNoTrailersResponseAfterRequest : Greeter.GreeterBase
        {
            public override async Task SayHelloDuplex(IAsyncStreamReader<HelloRequest> requestStream, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
            {
                // Read a request from the request stream.
                await requestStream.MoveNext(context.CancellationToken);
                context.GetTestStorageItems()["RequestStream:First.Name"] = requestStream.Current.Name;

                // Write a response to the response stream.
                await responseStream.WriteAsync(new HelloReply() { Message = "#1" });
            }
        }

        [Fact]
        public async Task WithHeaders_NoTrailers_Response_Before_Request()
        {
            await using var host = CreateTestServer<StartupWithGrpcService<GreeterServiceWithHeadersNoTrailersResponseBeforeRequest>>();
            using var channel = host.CreateChannel();

            var client = new Greeter.GreeterClient(channel);
            var duplex = client.SayHelloDuplex();

            // 1. The server pushes the response headers.
            var responseHeaders = await duplex.ResponseHeadersAsync.WithCancellation(TimeoutToken);
            responseHeaders.Should().Contain(x => x.Key == "x-header-1");
            responseHeaders.Should().Contain(x => x.Key == "x-header-2-bin" && x.IsBinary);
            responseHeaders.GetValueBytes("x-header-2-bin").Should().Equal(new byte[] { 1, 2, 3, 4 });

            // 2. Read from the response stream.
            var response = await duplex.ResponseStream.ReadAllAsync(TimeoutToken).FirstAsync(TimeoutToken);
            response.Message.Should().Be("#1");

            // Notify the request stream has ended.
            await duplex.RequestStream.CompleteAsync();

            // The response stream has ended.
            (await duplex.ResponseStream.MoveNext(TimeoutToken)).Should().BeFalse();
        }

        class GreeterServiceWithHeadersNoTrailersResponseBeforeRequest : Greeter.GreeterBase
        {
            public override async Task SayHelloDuplex(IAsyncStreamReader<HelloRequest> requestStream, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
            {
                var headers = new Metadata();
                headers.Add("x-header-1", "value1");
                headers.Add("x-header-2-bin", new byte[] { 1, 2, 3, 4 });

                await context.WriteResponseHeadersAsync(headers);
                await responseStream.WriteAsync(new HelloReply() { Message = "#1" });
            }
        }

        [Fact]
        public async Task WithHeaders_NoTrailers_Response_After_Request()
        {
            await using var host = CreateTestServer<StartupWithGrpcService<GreeterServiceWithHeadersNoTrailersResponseAfterRequest>>();
            using var channel = host.CreateChannel();

            var client = new Greeter.GreeterClient(channel);
            var duplex = client.SayHelloDuplex();

            // 0. The server doesn't send the response headers before sending responses.
            await Task.WhenAny(duplex.ResponseHeadersAsync, Task.Delay(100) /* wait for 100ms */);
            duplex.ResponseHeadersAsync.IsCompleted.Should().BeFalse();

            // 1. Write to the request stream
            await duplex.RequestStream.WriteAsync(new HelloRequest() { Name = "Req#1" });

            // 2. The server pushes the response headers.
            var responseHeaders = await duplex.ResponseHeadersAsync.WithCancellation(TimeoutToken);
            responseHeaders.Should().Contain(x => x.Key == "x-header-1");
            responseHeaders.Should().Contain(x => x.Key == "x-header-2-bin" && x.IsBinary);
            responseHeaders.GetValueBytes("x-header-2-bin").Should().Equal(new byte[] { 1, 2, 3, 4 });

            // 3. Read from the response stream.
            var response = await duplex.ResponseStream.ReadAllAsync(TimeoutToken).FirstAsync(TimeoutToken);
            response.Message.Should().Be("#1");

            // Notify the request stream has ended.
            await duplex.RequestStream.CompleteAsync();

            // The response stream has ended.
            (await duplex.ResponseStream.MoveNext(TimeoutToken)).Should().BeFalse();
        }

        class GreeterServiceWithHeadersNoTrailersResponseAfterRequest : Greeter.GreeterBase
        {
            public override async Task SayHelloDuplex(IAsyncStreamReader<HelloRequest> requestStream, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
            {
                await requestStream.MoveNext(context.CancellationToken);

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
            using var channel = host.CreateChannel();

            var headers = new Metadata();
            headers.Add("x-header-1", "value1");
            headers.Add("x-header-2-bin", new byte[] { 1, 2, 3, 4 });

            var client = new Greeter.GreeterClient(channel);
            var duplex = client.SayHelloDuplex(headers);

            // Notify the request stream has ended.
            await duplex.RequestStream.CompleteAsync();

            // The response stream has ended.
            (await duplex.ResponseStream.MoveNext(TimeoutToken)).Should().BeFalse();

            await host.LastRequest.Completed;

            host.LastRequest.Items["Server:x-header-1:Value"].Should().Be("value1");
            host.LastRequest.Items["Server:x-header-1:IsBinary"].Should().Be(false);
            host.LastRequest.Items["Server:x-header-2-bin:ValueBytes"].Should().BeEquivalentTo(new byte[] {1, 2, 3, 4});
            host.LastRequest.Items["Server:x-header-2-bin:IsBinary"].Should().Be(true);
        }

        class GreeterServiceWithRequestHeadersNoTrailersResponseBeforeRequest : Greeter.GreeterBase
        {
            public override async Task SayHelloDuplex(IAsyncStreamReader<HelloRequest> requestStream, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
            {
                context.GetTestStorageItems()["Server:x-header-1:Value"] = context.RequestHeaders.Get("x-header-1").Value;
                context.GetTestStorageItems()["Server:x-header-1:IsBinary"] = context.RequestHeaders.Get("x-header-1").IsBinary;
                context.GetTestStorageItems()["Server:x-header-2-bin:ValueBytes"] = context.RequestHeaders.Get("x-header-2-bin").ValueBytes;
                context.GetTestStorageItems()["Server:x-header-2-bin:IsBinary"] = context.RequestHeaders.Get("x-header-2-bin").IsBinary;
            }
        }

        [Fact]
        public async Task NoHeaders_WithResponseTrailers()
        {
            await using var host = CreateTestServer<StartupWithGrpcService<GreeterServiceNoHeadersWithResponseTrailers>>();
            using var channel = host.CreateChannel();

            var client = new Greeter.GreeterClient(channel);
            var duplex = client.SayHelloDuplex();

            Assert.Throws<InvalidOperationException>(() => duplex.GetStatus());
            Assert.Throws<InvalidOperationException>(() => duplex.GetTrailers());

            // Notify the request stream has ended.
            await duplex.RequestStream.CompleteAsync();

            // The response stream has ended.
            (await duplex.ResponseStream.MoveNext(TimeoutToken)).Should().BeFalse();

            var status = duplex.GetStatus();
            var responseTrailers = duplex.GetTrailers();
            responseTrailers.Should().Contain(x => x.Key == "x-trailer-1");
            responseTrailers.Should().Contain(x => x.Key == "x-trailer-2-bin" && x.IsBinary);
            responseTrailers.GetValueBytes("x-trailer-2-bin").Should().Equal(new byte[] { 5, 4, 3, 2, 1 });
        }

        class GreeterServiceNoHeadersWithResponseTrailers : Greeter.GreeterBase
        {
            public override async Task SayHelloDuplex(IAsyncStreamReader<HelloRequest> requestStream, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
            {
                await requestStream.MoveNext(context.CancellationToken);
                context.ResponseTrailers.Add("x-trailer-1", "trailerValue");
                context.ResponseTrailers.Add("x-trailer-2-bin", new byte[] { 5, 4, 3, 2, 1 });
            }
        }

        [Fact]
        public async Task RequestCompleteImmediately()
        {
            await using var host = CreateTestServer<StartupWithGrpcService<GreeterServiceRequestCompleteImmediately>>();
            using var channel = host.CreateChannel();

            var client = new Greeter.GreeterClient(channel);
            var duplex = client.SayHelloDuplex();
            await duplex.RequestStream.CompleteAsync();

            // Executing SayHelloDuplex will be complete immediately. And the response stream will be also done immediately.
            await duplex.ResponseHeadersAsync.WithCancellation(TimeoutToken);
            var responses = await duplex.ResponseStream.ReadAllAsync(TimeoutToken).ToArrayAsync(TimeoutToken);
            responses.Should().BeEmpty();
        }

        class GreeterServiceRequestCompleteImmediately : Greeter.GreeterBase
        {
            public override Task SayHelloDuplex(IAsyncStreamReader<HelloRequest> requestStream, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
            {
                return Task.CompletedTask;
            }
        }

        [Fact]
        public async Task NoRequest_ResponseImmediately()
        {
            await using var host = CreateTestServer<StartupWithGrpcService<GreeterServiceNoRequestResponseImmediately>>();
            using var channel = host.CreateChannel();

            var client = new Greeter.GreeterClient(channel);
            var duplex = client.SayHelloDuplex();

            // Executing SayHelloDuplex will be complete immediately. And the response stream will be also done immediately.
            await duplex.ResponseHeadersAsync.WithCancellation(TimeoutToken);
            var responses = await duplex.ResponseStream.ReadAllAsync(TimeoutToken).ToArrayAsync(TimeoutToken);
            responses.Should().BeEmpty();
        }

        class GreeterServiceNoRequestResponseImmediately : Greeter.GreeterBase
        {
            public override Task SayHelloDuplex(IAsyncStreamReader<HelloRequest> requestStream, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
            {
                return Task.CompletedTask;
            }
        }

        [Fact]
        public async Task Incomplete_Request()
        {
            await using var host = CreateTestServer<StartupWithGrpcService<GreeterServiceIncompleteRequest>>();
            using var channel = host.CreateChannel();

            var client = new Greeter.GreeterClient(channel);
            var duplex = client.SayHelloDuplex();

            await Assert.ThrowsAsync<TimeoutException>(async () => await duplex.ResponseHeadersAsync.WithTimeout(TimeSpan.FromSeconds(1)));
            await Assert.ThrowsAsync<TimeoutException>(async () => await duplex.ResponseStream.ReadAllAsync(TimeoutToken).ToArrayAsync(TimeoutToken).WithTimeout(TimeSpan.FromSeconds(1)));
        }

        class GreeterServiceIncompleteRequest : Greeter.GreeterBase
        {
            public override async Task SayHelloDuplex(IAsyncStreamReader<HelloRequest> requestStream, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
            {
                await requestStream.MoveNext(context.CancellationToken);
            }
        }


        [Fact]
        public async Task Incomplete_Response()
        {
            await using var host = CreateTestServer<StartupWithGrpcService<GreeterServiceIncompleteResponse>>();
            using var channel = host.CreateChannel();

            var client = new Greeter.GreeterClient(channel);
            var duplex = client.SayHelloDuplex();

            await Assert.ThrowsAsync<TimeoutException>(async () => await duplex.ResponseStream.ReadAllAsync(TimeoutToken).ToArrayAsync(TimeoutToken).WithTimeout(TimeSpan.FromSeconds(1)));
        }

        class GreeterServiceIncompleteResponse : Greeter.GreeterBase
        {
            public override async Task SayHelloDuplex(IAsyncStreamReader<HelloRequest> requestStream, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
            {
                // Never
                await Task.Delay(-1, context.CancellationToken);
            }
        }


        [Fact]
        public async Task LargePayload_Response()
        {
            await using var host = CreateTestServer<StartupWithGrpcService<GreeterServiceLargePayloadResponse>>();
            using var channel = host.CreateChannel();

            var client = new Greeter.GreeterClient(channel);
            var duplex = client.SayHelloDuplex();

            var sb = new StringBuilder(100_000);
            for (var i = 0; i < 100_000; i++)
            {
                sb.Append(i);
            }

            (await duplex.ResponseStream.MoveNext(TimeoutToken)).Should().BeTrue();
            duplex.ResponseStream.Current.Message.Should().Be(sb.ToString());

            (await duplex.ResponseStream.MoveNext(TimeoutToken)).Should().BeTrue();
            duplex.ResponseStream.Current.Message.Should().Be("#2");
        }

        class GreeterServiceLargePayloadResponse : Greeter.GreeterBase
        {
            public override async Task SayHelloDuplex(IAsyncStreamReader<HelloRequest> requestStream, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
            {
                var sb = new StringBuilder(100_000);
                for (var i = 0; i < 100_000; i++)
                {
                    sb.Append(i);
                }

                await responseStream.WriteAsync(new HelloReply {Message = sb.ToString()});
                await responseStream.WriteAsync(new HelloReply {Message = "#2"});
            }
        }

        [Fact]
        public async Task LargePayload_Request()
        {
            await using var host = CreateTestServer<StartupWithGrpcService<GreeterServiceLargePayloadRequest>>();
            using var channel = host.CreateChannel();

            var sb = new StringBuilder(100_000);
            for (var i = 0; i < 100_000; i++)
            {
                sb.Append(i);
            }

            var client = new Greeter.GreeterClient(channel);
            var duplex = client.SayHelloDuplex();

            await duplex.RequestStream.WriteAsync(new HelloRequest() { Name = sb.ToString() });
            (await duplex.ResponseStream.MoveNext(TimeoutToken)).Should().BeTrue();
            duplex.ResponseStream.Current.Message.Should().Be(sb.Length.ToString());

            await duplex.RequestStream.WriteAsync(new HelloRequest() { Name = "#2" });
            (await duplex.ResponseStream.MoveNext(TimeoutToken)).Should().BeTrue();
            duplex.ResponseStream.Current.Message.Should().Be("2");
        }

        class GreeterServiceLargePayloadRequest : Greeter.GreeterBase
        {
            public override Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)
            {
                var sb = new StringBuilder(100_000);
                for (var i = 0; i < 100_000; i++)
                {
                    sb.Append(i);
                }

                return Task.FromResult(new HelloReply { Message = request.Name.Length.ToString() });
            }

            public override async Task SayHelloDuplex(IAsyncStreamReader<HelloRequest> requestStream, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
            {
                await foreach (var request in requestStream.ReadAllAsync(context.CancellationToken))
                {
                    await responseStream.WriteAsync(new HelloReply() {Message = request.Name.Length.ToString()});
                }
            }
        }

        [Fact]
        public async Task RepeatRequest()
        {
            await using var host = CreateTestServer<StartupWithGrpcService<GreeterServiceRepeatRequest>>();
            using var channel = host.CreateChannel();

            var client = new Greeter.GreeterClient(channel);
            var duplex = client.SayHelloDuplex();

            for (var i = 0; i < 10000; i++)
            {
                await duplex.RequestStream.WriteAsync(new HelloRequest() { Name = i.ToString() });
                (await duplex.ResponseStream.MoveNext(TimeoutToken)).Should().BeTrue();
                duplex.ResponseStream.Current.Message.Should().Be($"Response#{i}");
            }

            await duplex.RequestStream.CompleteAsync();
        }

        class GreeterServiceRepeatRequest : Greeter.GreeterBase
        {
            public override Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)
            {
                return Task.FromResult(new HelloReply { Message = request.Name.Length.ToString() });
            }

            public override async Task SayHelloDuplex(IAsyncStreamReader<HelloRequest> requestStream, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
            {
                await foreach (var request in requestStream.ReadAllAsync(context.CancellationToken))
                {
                    await responseStream.WriteAsync(new HelloReply() { Message = $"Response#{request.Name}" });
                }
            }
        }

        [Fact]
        public async Task Concurrent()
        {
            await using var host = CreateTestServer<StartupWithGrpcService<GreeterServiceConcurrent>>();
            using var channel = host.CreateChannel();

            var client = new Greeter.GreeterClient(channel);
            var duplex = client.SayHelloDuplex();

            var writeTask = Task.Run(async () =>
            {
                for (var i = 0; i < 10000; i++)
                {
                    await duplex.RequestStream.WriteAsync(new HelloRequest() { Name = i.ToString() });
                    if (i % 100 == 0)
                    {
                        await Task.Delay(10);
                    }
                }
            });

            var readTask = Task.Run(async () =>
            {
                for (var i = 0; i < 10000; i++)
                {
                    (await duplex.ResponseStream.MoveNext(TimeoutToken)).Should().BeTrue();
                    duplex.ResponseStream.Current.Message.Should().Be($"Response#{i}");
                }
            });

            await Task.WhenAll(writeTask, readTask);

            await duplex.RequestStream.CompleteAsync();
        }

        class GreeterServiceConcurrent : Greeter.GreeterBase
        {
            public override Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)
            {
                return Task.FromResult(new HelloReply { Message = request.Name.Length.ToString() });
            }

            public override async Task SayHelloDuplex(IAsyncStreamReader<HelloRequest> requestStream, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
            {
                await foreach (var request in requestStream.ReadAllAsync(context.CancellationToken))
                {
                    await responseStream.WriteAsync(new HelloReply() { Message = $"Response#{request.Name}" });
                }
            }
        }
    }
}
