using Grpc.Net.Client;
using GrpcWebSocketBridge.Client;
using MagicOnion.Client;
using MagicOnionSampleApp.Shared.Hubs;

await Task.Run(() => new ChatClient().RunAsync());

class ChatClient : IChatHubReceiver
{
    public async Task RunAsync()
    {
        var endpoint = "https://localhost:7069";
        var channel = GrpcChannel.ForAddress(endpoint, new GrpcChannelOptions()
        {
            // NOTE: Use `GrpcWebSocketBridgeHandler` instead of HttpSocketHandler.
            HttpHandler = new GrpcWebSocketBridgeHandler(),
        });

        Console.WriteLine($"Connecting to {endpoint}...");
        var client = await StreamingHubClient.ConnectAsync<IChatHub, IChatHubReceiver>(channel, this);
        Console.WriteLine($"StreamingHub connection has been established.");

        Console.Write("NickName: ");
        var nickName = Console.ReadLine()!.Trim();
        await client.JoinAsync(nickName);
        await Task.Yield(); // NOTE: Release the gRPC's worker thread here.

        while (true)
        {
            Console.Write("> ");
            var line = await Console.In.ReadLineAsync();
            await client.SendAsync(line);
        }
    }

    public void OnMemberJoined(string nickName)
    {
        Console.WriteLine($"Join: {nickName}");
    }

    public void OnMessageReceived(string nickName, string message)
    {
        Console.WriteLine($"@{nickName}: {message}");
    }

    public void OnMemberLeft(string nickName)
    {
        Console.WriteLine($"Left: {nickName}");
    }
}
