// See https://aka.ms/new-console-template for more information

using Grpc.Core;
using Grpc.Net.Client;
using GrpcSampleApp.Server;
using GrpcWebSocketBridge.Client;

var channel = GrpcChannel.ForAddress("http://localhost:5172", new GrpcChannelOptions()
{
    // NOTE: Use `GrpcWebSocketBridgeHandler` instead of HttpSocketHandler.
    HttpHandler = new GrpcWebSocketBridgeHandler(),
});

var greeter = new Greeter.GreeterClient(channel);

// Unary method call
Console.WriteLine("Call SayHelloAsync: HelloRequest(Name = Alice)");
var response = await greeter.SayHelloAsync(new HelloRequest() { Name = "Alice" });
Console.WriteLine($"Response: {response.Message}");

Console.WriteLine();

// Duplex Streaming method call
Console.WriteLine("Call SayHelloDuplex");
var duplex = greeter.SayHelloDuplex();
var writerTask = Task.Run(async () =>
{
    for (var i = 0; i < 10; i++)
    {
        Console.WriteLine($"Send: HelloRequest(Message=User{i})");
        await duplex.RequestStream.WriteAsync(new HelloRequest() { Name = $"User{i}" });
        await Task.Delay(1000);
    }

    await duplex.RequestStream.CompleteAsync();
});
var readerTask = Task.Run(async () =>
{
    await foreach (var res in duplex.ResponseStream.ReadAllAsync())
    {
        Console.WriteLine($"Response: {res.Message}");
    }
});

await Task.WhenAll(readerTask, writerTask);
Console.WriteLine("Call SayHelloDuplex: Completed");
