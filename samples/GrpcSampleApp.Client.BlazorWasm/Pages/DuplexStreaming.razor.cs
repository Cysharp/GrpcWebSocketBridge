using System.Collections.Immutable;
using Grpc.Core;
using Grpc.Net.Client;
using GrpcSampleApp.Server;
using GrpcWebSocketBridge.Client;

namespace GrpcSampleApp.Client.BlazorWasm.Pages
{
    public partial class DuplexStreaming
    {
        private ImmutableArray<HelloReply> _responses = ImmutableArray<HelloReply>.Empty;
        private bool _connecting;
        private Exception? _exception;

        private string _name = "User";

        private async Task ConnectAsync()
        {
            if (_connecting) return;

            _connecting = true;
            _exception = null;
            _responses = ImmutableArray<HelloReply>.Empty;
            await InvokeAsync(StateHasChanged);

            try
            {
                var channel = GrpcChannel.ForAddress("http://localhost:5172", new GrpcChannelOptions()
                {
                    // NOTE: Use `GrpcWebSocketBridgeHandler` instead of HttpSocketHandler.
                    HttpHandler = new GrpcWebSocketBridgeHandler(),
                });

                var greeter = new Greeter.GreeterClient(channel);
                var duplex = greeter.SayHelloDuplex();
                var writerTask = Task.Run(async () =>
                {
                    for (var i = 0; i < 10; i++)
                    {
                        await duplex.RequestStream.WriteAsync(new HelloRequest() { Name = $"{_name}@{DateTimeOffset.Now}/{i}" });
                        await Task.Delay(1000);
                    }

                    await duplex.RequestStream.CompleteAsync();
                });
                var readerTask = Task.Run(async () =>
                {
                    await foreach (var res in duplex.ResponseStream.ReadAllAsync())
                    {
                        _responses = _responses.Add(res);
                        await InvokeAsync(StateHasChanged);
                    }
                });

                await Task.WhenAll(readerTask, writerTask);
            }
            catch (Exception e)
            {
                _exception = e;
            }
            finally
            {
                _connecting = false;
            }

            await InvokeAsync(StateHasChanged);
        }
    }
}
