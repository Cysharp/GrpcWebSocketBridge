using System.Collections.Immutable;
using Grpc.Net.Client;
using GrpcWebSocketBridge.Client;
using MagicOnion.Client;
using MagicOnionSampleApp.Shared.Hubs;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace MagicOnionSampleApp.Client.BlazorWasm.Pages;

public partial class Index : IChatHubReceiver, IAsyncDisposable
{
    private string _nickname = GetRandomNickname();
    private string _inputMessage = "";

    private IChatHub? _chatHub;
    private Exception? _exception;
    private bool _isConnecting;
    private ImmutableArray<(Guid Id, DateTimeOffset ReceivedAt, string Nickname, string Message)> _messages = ImmutableArray<(Guid Id, DateTimeOffset ReceivedAt, string Nickname, string Message)>.Empty;

    [Inject]
    public NavigationManager NavigationManager { get; set; } = default!;

    private async Task ConnectAndJoinAsync()
    {
        if (_isConnecting || string.IsNullOrWhiteSpace(_nickname)) return;

        _isConnecting = true;
        try
        {
#if FALSE
            var endpoint = "https://localhost:7069";
#else
            var builder = new UriBuilder(NavigationManager.Uri);
            builder.Path = "/";
            var endpoint = builder.ToString();
#endif
            var channel = GrpcChannel.ForAddress(endpoint, new GrpcChannelOptions()
            {
                // NOTE: Use `GrpcWebSocketBridgeHandler` instead of HttpSocketHandler.
                HttpHandler = new GrpcWebSocketBridgeHandler(),
            });
            _chatHub = await StreamingHubClient.ConnectAsync<IChatHub, IChatHubReceiver>(channel, this);
            await _chatHub.JoinAsync(_nickname);
            await _chatHub.WaitForDisconnect();
        }
        catch (Exception e)
        {
            _exception = e;
        }
        finally
        {
            _chatHub = null;
            _isConnecting = false;
        }
    }

    private async Task SendAsync()
    {
        if (!_isConnecting || _chatHub is null || string.IsNullOrWhiteSpace(_inputMessage)) return;

        await _chatHub.SendAsync(_inputMessage);
        _inputMessage = "";
    }

    private async Task DisconnectAsync()
    {
        if (!_isConnecting || _chatHub is null) return;
        await _chatHub.LeaveAsync();
        await _chatHub.DisposeAsync();
    }

    private static string GetRandomNickname()
        => new[] { "Normal", "Rare", "SuperRare", "UltraRare" }.OrderBy(x => Guid.NewGuid()).First() +
           new[] { "Dog", "Cat", "Fish", "Monkey", "Bird" }.OrderBy(x => Guid.NewGuid()).First();

    void IChatHubReceiver.OnMemberJoined(string nickName)
    {
        _messages = _messages.Add((Guid.NewGuid(), DateTimeOffset.Now, nickName, $"[Joined]"));
        _ = InvokeAsync(StateHasChanged);
    }

    void IChatHubReceiver.OnMessageReceived(string nickName, string message)
    {
        _messages = _messages.Add((Guid.NewGuid(), DateTimeOffset.Now, nickName, message));
        _ = InvokeAsync(StateHasChanged);
    }

    void IChatHubReceiver.OnMemberLeft(string nickName)
    {
        _messages = _messages.Add((Guid.NewGuid(), DateTimeOffset.Now, nickName, $"[Left]"));
        _ = InvokeAsync(StateHasChanged);
    }

    public async ValueTask DisposeAsync()
    {
        if (_chatHub is not null)
        {
            await _chatHub.DisposeAsync();
        }
    }
}
