using System.Text;
using Grpc.Core;
using GrpcWebSocketBridge.Tests.Helpers;
using GrpcWebSocketBridge.Tests.Protos;

namespace GrpcWebSocketBridge.Tests.FunctionalTests;

public class ClientStreamingTest(ITestOutputHelper testOutputHelper) : UseTestServerTestBase(testOutputHelper)
{
    [Fact]
    public async Task NoHeaders_NoTrailers_Response_Before_Request()
    {
        await using var host = CreateTestServer<StartupWithGrpcService<GreeterServiceNoHeadersNoTrailersResponseBeforeRequest>>();
        using var channel = host.CreateChannel();

        var client = new Greeter.GreeterClient(channel);
        var clientStreaming = client.SayHelloClientStreaming();

        // The response headers will be sent before responses.
        await clientStreaming.ResponseHeadersAsync.WithCancellation(TimeoutToken);

        // Notify the request stream has ended.
        await clientStreaming.RequestStream.CompleteAsync();

        var response = await clientStreaming.ResponseAsync;
        response.Message.ShouldBe("#1");
    }

    class GreeterServiceNoHeadersNoTrailersResponseBeforeRequest : Greeter.GreeterBase
    {
        public override async Task<HelloReply> SayHelloClientStreaming(IAsyncStreamReader<HelloRequest> requestStream, ServerCallContext context)
        {
            return new HelloReply() { Message = "#1" };
        }
    }


    [Fact]
    public async Task NoHeaders_NoTrailers_Response_After_Request()
    {
        await using var host = CreateTestServer<StartupWithGrpcService<GreeterServiceNoHeadersNoTrailersResponseAfterRequest>>();
        using var channel = host.CreateChannel();

        var client = new Greeter.GreeterClient(channel);
        var clientStreaming = client.SayHelloClientStreaming();

        await Task.WhenAny(clientStreaming.ResponseHeadersAsync, Task.Delay(100) /* wait for 100ms */);
        clientStreaming.ResponseHeadersAsync.IsCompleted.ShouldBeFalse();

        // 1. Write to the request stream
        await clientStreaming.RequestStream.WriteAsync(new HelloRequest() { Name = "Req#1"});

        // 2. Notify the request stream has ended.
        await clientStreaming.RequestStream.CompleteAsync();

        // 3. Read from the response stream.
        var response = await clientStreaming.ResponseAsync;
        response.Message.ShouldBe("#1");

        await host.LastRequest.Completed;
        host.LastRequest.EnsureLastStates();
        host.LastRequest.Items["RequestStream:First.Name"].ShouldBe("Req#1");
    }

    class GreeterServiceNoHeadersNoTrailersResponseAfterRequest : Greeter.GreeterBase
    {
        public override async Task<HelloReply> SayHelloClientStreaming(IAsyncStreamReader<HelloRequest> requestStream, ServerCallContext context)
        {
            // Read a request from the request stream.
            await requestStream.MoveNext(context.CancellationToken);
            context.GetTestStorageItems()["RequestStream:First.Name"] = requestStream.Current.Name;

            // Write a response to the response stream.
            return new HelloReply() { Message = "#1" };
        }
    }

    [Fact]
    public async Task WithHeaders_NoTrailers_Response_Before_Request()
    {
        await using var host = CreateTestServer<StartupWithGrpcService<GreeterServiceWithHeadersNoTrailersResponseBeforeRequest>>();
        using var channel = host.CreateChannel();

        var client = new Greeter.GreeterClient(channel);
        var clientStreaming = client.SayHelloClientStreaming();

        // 1. The server pushes the response headers.
        var responseHeaders = await clientStreaming.ResponseHeadersAsync.WithCancellation(TimeoutToken);
        responseHeaders.ShouldContain(x => x.Key == "x-header-1");
        responseHeaders.ShouldContain(x => x.Key == "x-header-2-bin" && x.IsBinary);
        responseHeaders.GetValueBytes("x-header-2-bin").ShouldBe(new byte[] { 1, 2, 3, 4 });

        // Notify the request stream has ended.
        await clientStreaming.RequestStream.CompleteAsync();

        // The response will be received.
        var response = await clientStreaming.ResponseAsync;
        response.Message.ShouldBe("#1");
    }

    class GreeterServiceWithHeadersNoTrailersResponseBeforeRequest : Greeter.GreeterBase
    {
        public override async Task<HelloReply> SayHelloClientStreaming(IAsyncStreamReader<HelloRequest> requestStream, ServerCallContext context)
        {
            var headers = new Metadata();
            headers.Add("x-header-1", "value1");
            headers.Add("x-header-2-bin", new byte[] { 1, 2, 3, 4 });

            await context.WriteResponseHeadersAsync(headers);

            await foreach (var _ in requestStream.ReadAllAsync()) { }

            return new HelloReply() { Message = "#1" };
        }
    }

    [Fact]
    public async Task WithHeaders_NoTrailers_Response_After_Request()
    {
        await using var host = CreateTestServer<StartupWithGrpcService<GreeterServiceWithHeadersNoTrailersResponseAfterRequest>>();
        using var channel = host.CreateChannel();

        var client = new Greeter.GreeterClient(channel);
        var clientStreaming = client.SayHelloClientStreaming();

        // 0. The server doesn't send the response headers before sending responses.
        await Task.WhenAny(clientStreaming.ResponseHeadersAsync, Task.Delay(100) /* wait for 100ms */);
        clientStreaming.ResponseHeadersAsync.IsCompleted.ShouldBeFalse();

        // 1. Write to the request stream
        await clientStreaming.RequestStream.WriteAsync(new HelloRequest() { Name = "Req#1" });

        // 2. The server pushes the response headers.
        var responseHeaders = await clientStreaming.ResponseHeadersAsync.WithCancellation(TimeoutToken);
        responseHeaders.ShouldContain(x => x.Key == "x-header-1");
        responseHeaders.ShouldContain(x => x.Key == "x-header-2-bin" && x.IsBinary);
        responseHeaders.GetValueBytes("x-header-2-bin").ShouldBe(new byte[] { 1, 2, 3, 4 });

        // Notify the request stream has ended.
        await clientStreaming.RequestStream.CompleteAsync();

        // 3. Read from the response stream.
        var response = await clientStreaming.ResponseAsync;
        response.Message.ShouldBe("#1");
    }

    class GreeterServiceWithHeadersNoTrailersResponseAfterRequest : Greeter.GreeterBase
    {
        public override async Task<HelloReply> SayHelloClientStreaming(IAsyncStreamReader<HelloRequest> requestStream, ServerCallContext context)
        {
            await requestStream.MoveNext(context.CancellationToken);

            var headers = new Metadata();
            headers.Add("x-header-1", "value1");
            headers.Add("x-header-2-bin", new byte[] { 1, 2, 3, 4 });

            await context.WriteResponseHeadersAsync(headers);
            return new HelloReply() { Message = "#1" };
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
        var clientStreaming = client.SayHelloClientStreaming(headers);

        // Notify the request stream has ended.
        await clientStreaming.RequestStream.CompleteAsync();
        await clientStreaming.ResponseAsync;

        await host.LastRequest.Completed;

        host.LastRequest.EnsureLastStates();
        host.LastRequest.Items["Server:x-header-1:Value"].ShouldBe("value1");
        host.LastRequest.Items["Server:x-header-1:IsBinary"].ShouldBe(false);
        host.LastRequest.Items["Server:x-header-2-bin:ValueBytes"].ShouldBe(new byte[] {1, 2, 3, 4});
        host.LastRequest.Items["Server:x-header-2-bin:IsBinary"].ShouldBe(true);
    }

    class GreeterServiceWithRequestHeadersNoTrailersResponseBeforeRequest : Greeter.GreeterBase
    {
        public override async Task<HelloReply> SayHelloClientStreaming(IAsyncStreamReader<HelloRequest> requestStream, ServerCallContext context)
        {
            context.GetTestStorageItems()["Server:x-header-1:Value"] = context.RequestHeaders.Get("x-header-1")!.Value;
            context.GetTestStorageItems()["Server:x-header-1:IsBinary"] = context.RequestHeaders.Get("x-header-1")!.IsBinary;
            context.GetTestStorageItems()["Server:x-header-2-bin:ValueBytes"] = context.RequestHeaders.Get("x-header-2-bin")!.ValueBytes;
            context.GetTestStorageItems()["Server:x-header-2-bin:IsBinary"] = context.RequestHeaders.Get("x-header-2-bin")!.IsBinary;

            return new HelloReply() { Message = "#1" };
        }
    }

    [Fact]
    public async Task NoHeaders_WithResponseTrailers()
    {
        await using var host = CreateTestServer<StartupWithGrpcService<GreeterServiceNoHeadersWithResponseTrailers>>();
        using var channel = host.CreateChannel();

        var client = new Greeter.GreeterClient(channel);
        var duplex = client.SayHelloDuplex();

        Should.Throw<InvalidOperationException>(() => duplex.GetStatus());
        Should.Throw<InvalidOperationException>(() => duplex.GetTrailers());

        // Notify the request stream has ended.
        await duplex.RequestStream.CompleteAsync();

        // The response stream has ended.
        (await duplex.ResponseStream.MoveNext(TimeoutToken)).ShouldBeFalse();

        var status = duplex.GetStatus();
        var responseTrailers = duplex.GetTrailers();
        responseTrailers.ShouldContain(x => x.Key == "x-trailer-1");
        responseTrailers.ShouldContain(x => x.Key == "x-trailer-2-bin" && x.IsBinary);
        responseTrailers.GetValueBytes("x-trailer-2-bin").ShouldBe(new byte[] { 5, 4, 3, 2, 1 });
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
    public async Task WithRequestHeaders_RequestCompleteImmediately()
    {
        await using var host = CreateTestServer<StartupWithGrpcService<GreeterServiceWithRequestHeadersRequestCompleteImmediately>>();
        using var channel = host.CreateChannel();

        var headers = new Metadata();
        headers.Add("x-header-1", "value1");
        headers.Add("x-header-2-bin", new byte[] { 1, 2, 3, 4 });

        var client = new Greeter.GreeterClient(channel);
        var clientStreaming = client.SayHelloClientStreaming(headers);

        // Complete the request stream immediately.
        await clientStreaming.RequestStream.CompleteAsync();
        await clientStreaming.ResponseAsync;

        await host.LastRequest.Completed;

        host.LastRequest.EnsureLastStates();
        host.LastRequest.Items["Server:x-header-1:Value"].ShouldBe("value1");
        host.LastRequest.Items["Server:x-header-1:IsBinary"].ShouldBe(false);
        host.LastRequest.Items["Server:x-header-2-bin:ValueBytes"].ShouldBe(new byte[] { 1, 2, 3, 4 });
        host.LastRequest.Items["Server:x-header-2-bin:IsBinary"].ShouldBe(true);
    }

    class GreeterServiceWithRequestHeadersRequestCompleteImmediately : Greeter.GreeterBase
    {
        public override async Task<HelloReply> SayHelloClientStreaming(IAsyncStreamReader<HelloRequest> requestStream, ServerCallContext context)
        {
            System.Diagnostics.Debug.WriteLine("SayHelloClientStreaming: Begin");
            context.GetTestStorageItems()["Server:x-header-1:Value"] = context.RequestHeaders.Get("x-header-1")!.Value;
            context.GetTestStorageItems()["Server:x-header-1:IsBinary"] = context.RequestHeaders.Get("x-header-1")!.IsBinary;
            context.GetTestStorageItems()["Server:x-header-2-bin:ValueBytes"] = context.RequestHeaders.Get("x-header-2-bin")!.ValueBytes;
            context.GetTestStorageItems()["Server:x-header-2-bin:IsBinary"] = context.RequestHeaders.Get("x-header-2-bin")!.IsBinary;

            return new HelloReply() { Message = "#1" };
        }
    }

    [Fact]
    public async Task RequestCompleteImmediately()
    {
        await using var host = CreateTestServer<StartupWithGrpcService<GreeterServiceRequestCompleteImmediately>>();
        using var channel = host.CreateChannel();

        var client = new Greeter.GreeterClient(channel);
        var clientStreaming = client.SayHelloClientStreaming();
        await clientStreaming.RequestStream.CompleteAsync();

        // Executing SayHelloDuplex will be complete immediately. And the response stream will be also done immediately.
        await clientStreaming.ResponseHeadersAsync.WithCancellation(TimeoutToken);
        var response = await clientStreaming.ResponseAsync;
        response.Message.ShouldBe("#1");
    }

    class GreeterServiceRequestCompleteImmediately : Greeter.GreeterBase
    {
        public override Task<HelloReply> SayHelloClientStreaming(IAsyncStreamReader<HelloRequest> requestStream, ServerCallContext context)
        {
            return Task.FromResult(new HelloReply() { Message = "#1" });
        }
    }

    [Fact]
    public async Task NoRequest_ResponseImmediately()
    {
        await using var host = CreateTestServer<StartupWithGrpcService<GreeterServiceNoRequestResponseImmediately>>();
        using var channel = host.CreateChannel();

        var client = new Greeter.GreeterClient(channel);
        var clientStreaming = client.SayHelloClientStreaming();

        // Executing SayHelloClientStreaming will be complete immediately. And the response stream will be also done immediately.
        await clientStreaming.ResponseHeadersAsync.WithCancellation(TimeoutToken);
        var response = await clientStreaming.ResponseAsync;
        response.ShouldNotBeNull();
        response.Message.ShouldBe("#1");
    }

    class GreeterServiceNoRequestResponseImmediately : Greeter.GreeterBase
    {
        public override Task<HelloReply> SayHelloClientStreaming(IAsyncStreamReader<HelloRequest> requestStream, ServerCallContext context)
        {
            return Task.FromResult(new HelloReply() { Message = "#1" });
        }
    }

    [Fact]
    public async Task Incomplete_Request()
    {
        await using var host = CreateTestServer<StartupWithGrpcService<GreeterServiceIncompleteRequest>>();
        using var channel = host.CreateChannel();

        var client = new Greeter.GreeterClient(channel);
        var clientStreaming = client.SayHelloClientStreaming();

        await Should.ThrowAsync<TimeoutException>(async () => 
            await clientStreaming.ResponseHeadersAsync.WithTimeout(TimeSpan.FromSeconds(1))
        );
        await Should.ThrowAsync<TimeoutException>(async () => 
            await clientStreaming.ResponseAsync.WithTimeout(TimeSpan.FromSeconds(1))
        );
    }

    class GreeterServiceIncompleteRequest : Greeter.GreeterBase
    {
        public override async Task<HelloReply> SayHelloClientStreaming(IAsyncStreamReader<HelloRequest> requestStream, ServerCallContext context)
        {
            await requestStream.MoveNext(context.CancellationToken);
            return new HelloReply() { Message = "#1" };
        }
    }

    [Fact]
    public async Task Incomplete_Response()
    {
        await using var host = CreateTestServer<StartupWithGrpcService<GreeterServiceIncompleteResponse>>();
        using var channel = host.CreateChannel();

        var client = new Greeter.GreeterClient(channel);
        var clientStreaming = client.SayHelloClientStreaming();

        await Should.ThrowAsync<TimeoutException>(async () => 
            await clientStreaming.ResponseAsync.WithTimeout(TimeSpan.FromSeconds(1))
        );
    }

    class GreeterServiceIncompleteResponse : Greeter.GreeterBase
    {
        public override async Task<HelloReply> SayHelloClientStreaming(IAsyncStreamReader<HelloRequest> requestStream, ServerCallContext context)
        {
            // Never
            await Task.Delay(-1, context.CancellationToken);
            return new HelloReply() { Message = "#1" };
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
        var clientStreaming = client.SayHelloClientStreaming();

        await clientStreaming.RequestStream.WriteAsync(new HelloRequest() { Name = sb.ToString() });
        await clientStreaming.RequestStream.WriteAsync(new HelloRequest() { Name = "#2" });
        await clientStreaming.RequestStream.CompleteAsync();

        var response = await clientStreaming.ResponseAsync;
        response.Message.ShouldBe((sb.Length + "#2".Length).ToString());
    }

    class GreeterServiceLargePayloadRequest : Greeter.GreeterBase
    {
        public override async Task<HelloReply> SayHelloClientStreaming(IAsyncStreamReader<HelloRequest> requestStream, ServerCallContext context)
        {
            var totalLength = 0;

            await foreach (var request in requestStream.ReadAllAsync(context.CancellationToken))
            {
                totalLength += request.Name.Length;
            }

            return new HelloReply() { Message = totalLength.ToString() };
        }
    }

    [Fact]
    public async Task RepeatRequest()
    {
        await using var host = CreateTestServer<StartupWithGrpcService<GreeterServiceRepeatRequest>>();
        using var channel = host.CreateChannel();

        var client = new Greeter.GreeterClient(channel);
        var clientStreaming = client.SayHelloClientStreaming();

        for (var i = 0; i < 10000; i++)
        {
            await clientStreaming.RequestStream.WriteAsync(new HelloRequest() { Name = i.ToString() });
        }

        await clientStreaming.RequestStream.CompleteAsync();
        var response = await clientStreaming.ResponseAsync;
        response.Message.ShouldBe(string.Join(",", Enumerable.Range(0, 10000).Select(x => x.ToString())));
    }

    class GreeterServiceRepeatRequest : Greeter.GreeterBase
    {
        public override async Task<HelloReply> SayHelloClientStreaming(IAsyncStreamReader<HelloRequest> requestStream, ServerCallContext context)
        {
            var names = new List<string>();
            await foreach (var request in requestStream.ReadAllAsync(context.CancellationToken))
            {
                names.Add(request.Name);
            }

            return new HelloReply() { Message = string.Join(",", names) };
        }
    }
}
