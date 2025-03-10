using Grpc.Core;
using GrpcWebSocketBridge.Tests.Helpers;
using GrpcWebSocketBridge.Tests.Protos;

namespace GrpcWebSocketBridge.Tests.FunctionalTests;

public class ClientConnectionBehaviorTest(ITestOutputHelper testOutputHelper) : UseTestServerTestBase(testOutputHelper)
{
    [Fact]
    public async Task CannotConnectToServer_Unary()
    {
        // To get the server ports, launch and shutdown the server immediately.
        var host = CreateTestServer<StartupWithGrpcService<GreeterServiceCannotConnectToServer>>(new AspNetCoreServerTestHostOptions() { ShutdownTimeout = TimeSpan.FromMilliseconds(100) });
        await host.DisposeAsync();

        using var channel = host.CreateChannel();
        var client = new Greeter.GreeterClient(channel);
        await Should.ThrowAsync<RpcException>(async () => 
            await client.SayHelloAsync(new HelloRequest(), cancellationToken: TimeoutToken)
        );
    }

    [Fact]
    public async Task CannotConnectToServer_Duplex_Header()
    {
        // To get the server ports, launch and shutdown the server immediately.
        var host = CreateTestServer<StartupWithGrpcService<GreeterServiceCannotConnectToServer>>(new AspNetCoreServerTestHostOptions() { ShutdownTimeout = TimeSpan.FromMilliseconds(100) });
        await host.DisposeAsync();

        using var channel = host.CreateChannel();
        var client = new Greeter.GreeterClient(channel);
        var duplex = client.SayHelloDuplex();

        await Should.ThrowAsync<RpcException>(async () => await duplex.ResponseHeadersAsync);
    }

    [Fact]
    public async Task CannotConnectToServer_Duplex_Response()
    {
        // To get the server ports, launch and shutdown the server immediately.
        var host = CreateTestServer<StartupWithGrpcService<GreeterServiceCannotConnectToServer>>(new AspNetCoreServerTestHostOptions() { ShutdownTimeout = TimeSpan.FromMilliseconds(100) });
        await host.DisposeAsync();

        using var channel = host.CreateChannel();
        var client = new Greeter.GreeterClient(channel);
        var duplex = client.SayHelloDuplex();

        await Should.ThrowAsync<RpcException>(async () => await duplex.ResponseStream.MoveNext(TimeoutToken));
    }

    [Fact]
    public async Task CannotConnectToServer_Duplex_Request()
    {
        // To get the server ports, launch and shutdown the server immediately.
        var host = CreateTestServer<StartupWithGrpcService<GreeterServiceCannotConnectToServer>>(new AspNetCoreServerTestHostOptions() { ShutdownTimeout = TimeSpan.FromMilliseconds(100) });
        await host.DisposeAsync();

        using var channel = host.CreateChannel();
        var client = new Greeter.GreeterClient(channel);
        var duplex = client.SayHelloDuplex();

        await Should.ThrowAsync<RpcException>(async () => await duplex.RequestStream.WriteAsync(new HelloRequest()));
    }


    class GreeterServiceCannotConnectToServer : Greeter.GreeterBase
    {
        public override async Task SayHelloDuplex(IAsyncStreamReader<HelloRequest> requestStream, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
            => throw new NotImplementedException();
    }

    //[Fact]
    //public async Task DisconnectFromServer_Duplex_WaitForHeader()
    //{
    //    var host = CreateTestServer<StartupWithGrpcService<GreeterServiceDisconnectFromServerDuplexWaitForResponses>>(new AspNetCoreServerTestHostOptions() { ShutdownTimeout = TimeSpan.FromMilliseconds(100) });
    //    using var channel = host.CreateChannel();
    //    var client = new Greeter.GreeterClient(channel);
    //    var duplex = client.SayHelloDuplex();

    //    var headerTask = duplex.ResponseHeadersAsync;
    //    headerTask.IsCompleted.ShouldBeFalse();

    //    // Shutdown the server.
    //    await host.DisposeAsync();

    //    await Should.ThrowAsync<RpcException>(async () => await headerTask);
    //}

    //class GreeterServiceDisconnectFromServerDuplexWaitForHeader : Greeter.GreeterBase
    //{
    //    public override async Task SayHelloDuplex(IAsyncStreamReader<HelloRequest> requestStream, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
    //    {
    //        await Task.Delay(-1); // Never
    //    }
    //}

    [Fact]
    public async Task DisconnectFromServer_Duplex_BeforeSendResponses()
    {
        var host = CreateTestServer<StartupWithGrpcService<GreeterServiceDisconnectFromServerDuplexWaitForResponses>>(new AspNetCoreServerTestHostOptions() { ShutdownTimeout = TimeSpan.FromMilliseconds(100) });
        using var channel = host.CreateChannel();
        var client = new Greeter.GreeterClient(channel);
        var duplex = client.SayHelloDuplex();

        await duplex.ResponseHeadersAsync;

        // Shutdown the server immediately.
        await host.DisposeAsync();

        await Should.ThrowAsync<RpcException>(async () => await duplex.ResponseStream.MoveNext(TimeoutToken));
    }

    [Fact]
    public async Task DisconnectFromServer_Duplex_BeforeSendResponses_MoveNext()
    {
        var host = CreateTestServer<StartupWithGrpcService<GreeterServiceDisconnectFromServerDuplexWaitForResponses>>(new AspNetCoreServerTestHostOptions() { ShutdownTimeout = TimeSpan.FromMilliseconds(100) });
        using var channel = host.CreateChannel();
        var client = new Greeter.GreeterClient(channel);
        var duplex = client.SayHelloDuplex();

        await duplex.ResponseHeadersAsync;

        var moveNextTask = duplex.ResponseStream.MoveNext(TimeoutToken);

        // Shutdown the server immediately.
        await host.DisposeAsync();

        await Should.ThrowAsync<RpcException>(async () => await moveNextTask);
    }

    [Fact]
    public async Task DisconnectFromServer_Duplex_BeforeSendResponses_Request()
    {
        var host = CreateTestServer<StartupWithGrpcService<GreeterServiceDisconnectFromServerDuplexWaitForResponses>>(new AspNetCoreServerTestHostOptions() { ShutdownTimeout = TimeSpan.FromMilliseconds(100) });
        using var channel = host.CreateChannel();
        var client = new Greeter.GreeterClient(channel);
        var duplex = client.SayHelloDuplex();

        await duplex.ResponseHeadersAsync;

        // Shutdown the server immediately.
        await host.DisposeAsync();

        var ex = await Should.ThrowAsync<IOException>(async () => await duplex.RequestStream.WriteAsync(new HelloRequest()));
        ex.Message.ShouldBe("The request was aborted.");
    }

    [Fact]
    public async Task DisconnectFromServer_Duplex_BeforeSendResponses_Status()
    {
        var host = CreateTestServer<StartupWithGrpcService<GreeterServiceDisconnectFromServerDuplexWaitForResponses>>(new AspNetCoreServerTestHostOptions() { ShutdownTimeout = TimeSpan.FromMilliseconds(100) });
        using var channel = host.CreateChannel();
        var client = new Greeter.GreeterClient(channel);
        var duplex = client.SayHelloDuplex();

        await duplex.ResponseHeadersAsync;

        // Shutdown the server immediately.
        await host.DisposeAsync();

        Should.Throw<InvalidOperationException>(() => duplex.GetStatus())
            .Message.ShouldBe("Unable to get the status because the call is not complete.");
    }

    [Fact]
    public async Task DisconnectFromServer_Duplex_BeforeSendResponses_Trailers()
    {
        var host = CreateTestServer<StartupWithGrpcService<GreeterServiceDisconnectFromServerDuplexWaitForResponses>>(new AspNetCoreServerTestHostOptions(){ ShutdownTimeout = TimeSpan.FromMilliseconds(100) });
        using var channel = host.CreateChannel();
        var client = new Greeter.GreeterClient(channel);
        var duplex = client.SayHelloDuplex();

        await duplex.ResponseHeadersAsync;

        // Shutdown the server immediately.
        await host.DisposeAsync();

        Should.Throw<InvalidOperationException>(() => duplex.GetTrailers())
            .Message.ShouldBe("Can't get the call trailers because the call has not completed successfully.");
    }

    class GreeterServiceDisconnectFromServerDuplexWaitForResponses : Greeter.GreeterBase
    {
        public override async Task SayHelloDuplex(IAsyncStreamReader<HelloRequest> requestStream, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
        {
            await context.WriteResponseHeadersAsync(new Metadata());
            await Task.Delay(-1); // Never
        }
    }

    [Fact]
    public async Task DisconnectFromServer_Duplex_DuringSendResponses()
    {
        var host = CreateTestServer<StartupWithGrpcService<GreeterServiceDisconnectFromServerDuplexDuringSendResponses>>(new AspNetCoreServerTestHostOptions() { ShutdownTimeout = TimeSpan.FromMilliseconds(100) });
        using var channel = host.CreateChannel();
        var client = new Greeter.GreeterClient(channel);
        var duplex = client.SayHelloDuplex();

        (await duplex.ResponseHeadersAsync).ShouldNotBeNull();

        await duplex.RequestStream.WriteAsync(new HelloRequest());
        await duplex.ResponseStream.MoveNext(TimeoutToken);
        duplex.ResponseStream.Current.Message.ShouldBe("#1");

        // Shutdown the server immediately.
        await host.DisposeAsync();

        await Should.ThrowAsync<RpcException>(async () => await duplex.ResponseStream.MoveNext(TimeoutToken));
    }

    [Fact]
    public async Task DisconnectFromServer_Duplex_DuringSendResponses_MoveNext()
    {
        var host = CreateTestServer<StartupWithGrpcService<GreeterServiceDisconnectFromServerDuplexDuringSendResponses>>(new AspNetCoreServerTestHostOptions() { ShutdownTimeout = TimeSpan.FromMilliseconds(100) });
        using var channel = host.CreateChannel();
        var client = new Greeter.GreeterClient(channel);
        var duplex = client.SayHelloDuplex();

        (await duplex.ResponseHeadersAsync).ShouldNotBeNull();

        await duplex.RequestStream.WriteAsync(new HelloRequest());
        await duplex.ResponseStream.MoveNext(TimeoutToken);
        duplex.ResponseStream.Current.Message.ShouldBe("#1");

        var moveTask = duplex.ResponseStream.MoveNext(TimeoutToken);

        // Shutdown the server immediately.
        await host.DisposeAsync();

        await Should.ThrowAsync<RpcException>(async () => await moveTask);
    }

    [Fact]
    public async Task DisconnectFromServer_Duplex_DuringSendResponses_Request()
    {
        var host = CreateTestServer<StartupWithGrpcService<GreeterServiceDisconnectFromServerDuplexDuringSendResponses>>(new AspNetCoreServerTestHostOptions() { ShutdownTimeout = TimeSpan.FromMilliseconds(100) });
        using var channel = host.CreateChannel();
        var client = new Greeter.GreeterClient(channel);
        var duplex = client.SayHelloDuplex();

        (await duplex.ResponseHeadersAsync).ShouldNotBeNull();

        await duplex.RequestStream.WriteAsync(new HelloRequest());
        await duplex.ResponseStream.MoveNext(TimeoutToken);
        duplex.ResponseStream.Current.Message.ShouldBe("#1");

        // Shutdown the server immediately.
        await host.DisposeAsync();

        var ex = await Should.ThrowAsync<IOException>(async () => await duplex.RequestStream.WriteAsync(new HelloRequest()));
        ex.Message.ShouldBe("The request was aborted.");
    }

    [Fact]
    public async Task DisconnectFromServer_Duplex_DuringSendResponses_Status()
    {
        var host = CreateTestServer<StartupWithGrpcService<GreeterServiceDisconnectFromServerDuplexDuringSendResponses>>(new AspNetCoreServerTestHostOptions() { ShutdownTimeout = TimeSpan.FromMilliseconds(100) });
        using var channel = host.CreateChannel();
        var client = new Greeter.GreeterClient(channel);
        var duplex = client.SayHelloDuplex();

        (await duplex.ResponseHeadersAsync).ShouldNotBeNull();

        await duplex.RequestStream.WriteAsync(new HelloRequest());
        await duplex.ResponseStream.MoveNext(TimeoutToken);
        duplex.ResponseStream.Current.Message.ShouldBe("#1");

        // Shutdown the server immediately.
        await host.DisposeAsync();

        Should.Throw<InvalidOperationException>(() => duplex.GetStatus())
            .Message.ShouldBe("Unable to get the status because the call is not complete.");
    }

    [Fact]
    public async Task DisconnectFromServer_Duplex_DuringSendResponses_Trailers()
    {
        var host = CreateTestServer<StartupWithGrpcService<GreeterServiceDisconnectFromServerDuplexDuringSendResponses>>(new AspNetCoreServerTestHostOptions() { ShutdownTimeout = TimeSpan.FromMilliseconds(100) });
        using var channel = host.CreateChannel();
        var client = new Greeter.GreeterClient(channel);
        var duplex = client.SayHelloDuplex();

        (await duplex.ResponseHeadersAsync).ShouldNotBeNull();

        await duplex.RequestStream.WriteAsync(new HelloRequest());
        await duplex.ResponseStream.MoveNext(TimeoutToken);
        duplex.ResponseStream.Current.Message.ShouldBe("#1");

        // Shutdown the server immediately.
        await host.DisposeAsync();

        Should.Throw<InvalidOperationException>(() => duplex.GetTrailers())
            .Message.ShouldBe("Can't get the call trailers because the call has not completed successfully.");
    }

    class GreeterServiceDisconnectFromServerDuplexDuringSendResponses : Greeter.GreeterBase
    {
        // Behavior: Connect -> SendResponse -> Receive Response -> Send Response -> Wait & Disconnect
        public override async Task SayHelloDuplex(IAsyncStreamReader<HelloRequest> requestStream, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
        {
            await context.WriteResponseHeadersAsync(new Metadata());

            await requestStream.MoveNext(context.CancellationToken);
            await responseStream.WriteAsync(new HelloReply() {Message = "#1"});

            await Task.Delay(-1); // Never
        }
    }

    [Fact]
    public async Task CancelConnectionOnClient()
    {
        var host = CreateTestServer<StartupWithGrpcService<GreeterServiceDisconnectFromServerDuplexWaitForResponses>>(new AspNetCoreServerTestHostOptions() { ShutdownTimeout = TimeSpan.FromMilliseconds(100) });
        using var channel = host.CreateChannel();
        var client = new Greeter.GreeterClient(channel);
        var duplex = client.SayHelloDuplex();

        await duplex.ResponseHeadersAsync;

        var cts = new CancellationTokenSource();
        cts.CancelAfter(200);

        var rpcException = await Should.ThrowAsync<RpcException>(async () => await duplex.ResponseStream.MoveNext(cts.Token)).WaitAsync(TimeoutToken);
        rpcException.InnerException.ShouldBeOfType<OperationCanceledException>();

        // Shutdown the server immediately.
        await host.DisposeAsync();
    }

    class GreeterServiceCancelConnectionOnClient : Greeter.GreeterBase
    {
        // Behavior: Connect -> SendResponse -> Receive Response -> Send Response -> Wait & Disconnect
        public override async Task SayHelloDuplex(IAsyncStreamReader<HelloRequest> requestStream, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
        {
            await context.WriteResponseHeadersAsync(new Metadata());

            await requestStream.MoveNext(context.CancellationToken);

            await Task.Delay(-1); // Never
        }
    }

}
