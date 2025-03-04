using System.Text;
using Grpc.Core;
using GrpcWebSocketBridge.Tests.Helpers;
using GrpcWebSocketBridge.Tests.Protos;

namespace GrpcWebSocketBridge.Tests.FunctionalTests;

public class UnaryTest(ITestOutputHelper testOutputHelper) : UseTestServerTestBase(testOutputHelper)
{
    [Fact]
    public async Task NoHeaders_NoTrailers()
    {
        await using var host = CreateTestServer<StartupWithGrpcService<GreeterServiceNoHeadersNoTrailers>>();
        using var channel = host.CreateChannel();

        var client = new Greeter.GreeterClient(channel);
        var response = await client.SayHelloAsync(new HelloRequest() {Name = "Alice"}, cancellationToken: TimeoutToken);
        response.Message.ShouldBe("Hello Alice");
    }

    [Fact]
    public async Task NoHeaders_NoTrailers_WebSocket()
    {
        await using var host = CreateTestServer<StartupWithGrpcService<GreeterServiceNoHeadersNoTrailers>>();
        using var channel = host.CreateChannel(ChannelKind.InsecureHttp1, forceWebSocketMode: true); // Force WebSocket

        var client = new Greeter.GreeterClient(channel);
        var response = await client.SayHelloAsync(new HelloRequest() { Name = "Alice" }, cancellationToken: TimeoutToken);
        response.Message.ShouldBe("Hello Alice");

        await host.LastRequest.Completed;

        host.LastRequest.EnsureLastStates();
        host.LastRequest.RequestHeaders.ShouldContainKey("Upgrade");
        host.LastRequest.RequestHeaders["Upgrade"].ToString().ShouldBe("websocket");
        host.LastRequest.Protocol.ShouldBe("HTTP/2"); // Fake HTTP/2
        host.LastRequest.StatusCode.ShouldBe(101); // 101 Switch Protocol (upgrade to WebSocket)
    }

    class GreeterServiceNoHeadersNoTrailers : Greeter.GreeterBase
    {
        public override Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)
        {
            return Task.FromResult(new HelloReply { Message = "Hello " + request.Name });
        }
    }

    [Fact]
    public async Task ClientHeaders_NoTrailers()
    {
        await using var host = CreateTestServer<StartupWithGrpcService<GreeterServiceClientHeadersNoTrailers>>();
        using var channel = host.CreateChannel();

        var client = new Greeter.GreeterClient(channel);
        var headers = new Metadata();
        headers.Add("x-header-1", "value1");
        headers.Add("x-header-2-bin", new byte[] { 1, 2, 3, 4 });

        var response = await client.SayHelloAsync(new HelloRequest() { Name = "Alice" }, new CallOptions(headers, cancellationToken: TimeoutToken));
        response.Message.ShouldBe("Hello Alice");

        await host.LastRequest.Completed;

        host.LastRequest.EnsureLastStates();
        host.LastRequest.Items["x-header-1:Exists"].ShouldBe(true);
        host.LastRequest.Items["x-header-2-bin:Exists"].ShouldBe(true);
        host.LastRequest.Items["x-header-2-bin:IsBinary"].ShouldBe(true);
        host.LastRequest.Items["x-header-2-bin:Length"].ShouldBe(4);
    }

    class GreeterServiceClientHeadersNoTrailers : Greeter.GreeterBase
    {
        public override Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)
        {
            var items = context.GetTestStorageItems();
            items["x-header-1:Exists"] = context.RequestHeaders.Any(x => x.Key == "x-header-1");
            items["x-header-2-bin:Exists"] = context.RequestHeaders.Any(x => x.Key == "x-header-2-bin");
            items["x-header-2-bin:IsBinary"] = context.RequestHeaders.FirstOrDefault(x => x.Key == "x-header-2-bin")?.IsBinary ?? false;
            items["x-header-2-bin:Length"] = context.RequestHeaders.FirstOrDefault(x => x.Key == "x-header-2-bin")?.ValueBytes?.Length ?? -1;

            return Task.FromResult(new HelloReply { Message = "Hello " + request.Name });
        }
    }

    [Fact]
    public async Task ResponseHeaders()
    {
        await using var host = CreateTestServer<StartupWithGrpcService<GreeterServiceResponseHeaders>>();
        using var channel = host.CreateChannel();

        var client = new Greeter.GreeterClient(channel);

        var request = client.SayHelloAsync(new HelloRequest() { Name = "Alice" }, cancellationToken: TimeoutToken);
        var responseHeaders = await request.ResponseHeadersAsync.WithCancellation(TimeoutToken);

        responseHeaders.ShouldContain(x => x.Key == "x-header-1");
        responseHeaders.ShouldContain(x => x.Key == "x-header-2-bin" && x.IsBinary);
        responseHeaders.GetValueBytes("x-header-2-bin").ShouldBe(new byte[] { 1, 2, 3, 4 });

        var response = await request;
        response.Message.ShouldBe("Hello Alice");
    }

    class GreeterServiceResponseHeaders : Greeter.GreeterBase
    {
        public override async Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)
        {
            var headers = new Metadata();
            headers.Add("x-header-1", "value1");
            headers.Add("x-header-2-bin", new byte[] { 1, 2, 3, 4 });

            await context.WriteResponseHeadersAsync(headers);

            return new HelloReply { Message = "Hello " + request.Name };
        }
    }

    [Fact]
    public async Task ResponseTrailers()
    {
        await using var host = CreateTestServer<StartupWithGrpcService<GreeterServiceResponseTrailers>>();
        using var channel = host.CreateChannel();

        var client = new Greeter.GreeterClient(channel);

        var request = client.SayHelloAsync(new HelloRequest() { Name = "Alice" }, cancellationToken: TimeoutToken);
        var response = await request;

        var responseTrailers = request.GetTrailers();
        responseTrailers.ShouldContain(x => x.Key == "x-header-1");
        responseTrailers.ShouldContain(x => x.Key == "x-header-2-bin" && x.IsBinary);
        responseTrailers.GetValueBytes("x-header-2-bin").ShouldBe(new byte[] { 1, 2, 3, 4 });

        response.Message.ShouldBe("Hello Alice");
    }

    class GreeterServiceResponseTrailers : Greeter.GreeterBase
    {
        public override async Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)
        {
            context.ResponseTrailers.Add("x-header-1", "value1");
            context.ResponseTrailers.Add("x-header-2-bin", new byte[] { 1, 2, 3, 4 });

            return new HelloReply { Message = "Hello " + request.Name };
        }
    }

    [Fact]
    public async Task ThrowException()
    {
        await using var host = CreateTestServer<StartupWithGrpcService<GreeterServiceThrowException>>();
        using var channel = host.CreateChannel();

        var client = new Greeter.GreeterClient(channel);
        var ex = await Should.ThrowAsync<RpcException>(async () => 
            await client.SayHelloAsync(new HelloRequest() { Name = "Alice" }, cancellationToken: TimeoutToken)
        );
        ex.Status.StatusCode.ShouldBe(StatusCode.Unknown);
        ex.Status.Detail.ShouldBe("Exception was thrown by handler.");
    }

    class GreeterServiceThrowException : Greeter.GreeterBase
    {
        public override Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)
        {
            throw new Exception("Something went wrong.");
        }
    }

    [Fact]
    public async Task ReturnStatusCode()
    {
        await using var host = CreateTestServer<StartupWithGrpcService<GreeterServiceReturnStatusCode>>();
        using var channel = host.CreateChannel();

        var client = new Greeter.GreeterClient(channel);
        var ex = await Should.ThrowAsync<RpcException>(async () => 
            await client.SayHelloAsync(new HelloRequest() { Name = "Alice" }, cancellationToken: TimeoutToken)
        );
        ex.Status.StatusCode.ShouldBe(StatusCode.AlreadyExists);
        ex.Status.Detail.ShouldBe("Something went wrong.");
    }

    class GreeterServiceReturnStatusCode : Greeter.GreeterBase
    {
        public override Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)
        {
            throw new RpcException(new Status(StatusCode.AlreadyExists, "Something went wrong."));
        }
    }

    [Fact]
    public async Task ResponseHeader_Never()
    {
        await using var host = CreateTestServer<StartupWithGrpcService<GreeterServiceResponseHeaderNever>>();
        using var channel = host.CreateChannel();

        var client = new Greeter.GreeterClient(channel);
        var request = client.SayHelloAsync(new HelloRequest() { Name = "Alice" }, cancellationToken: TimeoutToken);
        await Task.WhenAny(request.ResponseHeadersAsync, Task.Delay(100));
        request.ResponseHeadersAsync.IsCompleted.ShouldBeFalse();
    }

    class GreeterServiceResponseHeaderNever : Greeter.GreeterBase
    {
        public override async Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)
        {
            await Task.Delay(int.MaxValue, context.CancellationToken);
            return new HelloReply(); // Unreachable
        }
    }

    [Fact]
    public async Task LargePayload_Response()
    {
        await using var host = CreateTestServer<StartupWithGrpcService<GreeterServiceLargePayloadResponse>>();
        using var channel = host.CreateChannel();

        var client = new Greeter.GreeterClient(channel);
        var response = await client.SayHelloAsync(new HelloRequest() { Name = "Alice" }, cancellationToken: TimeoutToken);

        var sb = new StringBuilder(100_000);
        for (var i = 0; i < 100_000; i++)
        {
            sb.Append(i);
        }

        response.Message.ShouldBe("Alice"  + sb.ToString());
    }

    class GreeterServiceLargePayloadResponse : Greeter.GreeterBase
    {
        public override Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)
        {
            var sb = new StringBuilder(100_000);
            for (var i = 0; i < 100_000; i++)
            {
                sb.Append(i);
            }

            return Task.FromResult(new HelloReply { Message = request.Name + sb.ToString() });
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
        var response = await client.SayHelloAsync(new HelloRequest() { Name = sb.ToString() }, cancellationToken: TimeoutToken);

        response.Message.ShouldBe(sb.ToString().Length.ToString());
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
    }
}
