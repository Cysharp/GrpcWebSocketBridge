using Grpc.Core;
using GrpcWebSocketBridge.Tests.Helpers;
using GrpcWebSocketBridge.Tests.Protos;

namespace GrpcWebSocketBridge.Tests.FunctionalTests;

public class ServerCompatibilityTest(ITestOutputHelper testOutputHelper) : UseTestServerTestBase(testOutputHelper)
{
    [Fact]
    public async Task StartServer()
    {
        await using var host = CreateTestServer<StartupWithGrpcService<GreeterService>>();
    }

    [Fact]
    public async Task Http2_Insecure_Unary()
    {
        await using var host = CreateTestServer<StartupWithGrpcService<GreeterService>>();
        using var channel = host.CreateChannel(ChannelKind.InsecureHttp2);
        var client = new Greeter.GreeterClient(channel);
        var response = await client.SayHelloAsync(new HelloRequest() {Name = "Alice"}, cancellationToken: TimeoutToken);
        response.Message.ShouldBe("Hello Alice");

        await host.LastRequest.Completed;
        host.LastRequest.Protocol.ShouldBe("HTTP/2"); // Real HTTP/2
        host.LastRequest.StatusCode.ShouldBe(200); // OK
    }

    [Fact]
    public async Task Http2_Insecure_Duplex()
    {
        await using var host = CreateTestServer<StartupWithGrpcService<GreeterService>>();
        using var channel = host.CreateChannel(ChannelKind.InsecureHttp2);
        var client = new Greeter.GreeterClient(channel);
        var duplex = client.SayHelloDuplex();
            
        await duplex.RequestStream.WriteAsync(new HelloRequest() {Name = "Alice"});
        (await duplex.ResponseStream.MoveNext(TimeoutToken)).ShouldBeTrue();
        duplex.ResponseStream.Current.Message.ShouldBe("Hello Alice");

        await duplex.RequestStream.WriteAsync(new HelloRequest() {Name = "Karen"});
        (await duplex.ResponseStream.MoveNext(TimeoutToken)).ShouldBeTrue();
        duplex.ResponseStream.Current.Message.ShouldBe("Hello Karen");

        await duplex.RequestStream.CompleteAsync();
        (await duplex.ResponseStream.MoveNext(TimeoutToken)).ShouldBeFalse();

        await host.LastRequest.Completed;
        host.LastRequest.Protocol.ShouldBe("HTTP/2"); // Real HTTP/2
        host.LastRequest.StatusCode.ShouldBe(200); // OK
    }

    class GreeterService : Greeter.GreeterBase
    {
        public override Task<HelloReply> SayHello(HelloRequest request, ServerCallContext context)
        {
            return Task.FromResult(new HelloReply {Message = "Hello " + request.Name});
        }

        public override async Task SayHelloDuplex(IAsyncStreamReader<HelloRequest> requestStream, IServerStreamWriter<HelloReply> responseStream, ServerCallContext context)
        {
            await foreach (var request in requestStream.ReadAllAsync())
            {
                await responseStream.WriteAsync(new HelloReply() {Message = $"Hello {request.Name}" });
            }
        }
    }
}
