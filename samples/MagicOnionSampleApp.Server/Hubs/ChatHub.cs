using MagicOnion.Server.Hubs;
using MagicOnionSampleApp.Shared.Hubs;

#pragma warning disable CS1998

namespace MagicOnionSampleApp.Server.Hubs;

public sealed class ChatHub : StreamingHubBase<IChatHub, IChatHubReceiver>, IChatHub
{
    private IGroup<IChatHubReceiver>? _group;
    private string _nickName = default!;


    protected override async ValueTask OnDisconnected()
    {
        if (_group is {})
        {
            _group.All.OnMemberLeft(_nickName);
            _group = null;
        }
    }

    public async Task JoinAsync(string nickName)
    {
        if (_group is not null) throw new InvalidOperationException("The user has already joined.");

        _nickName = nickName ?? "Anonymous";

        _group = await Group.AddAsync("ChatRoom");
        _group.All.OnMemberJoined(nickName);
    }

    public async Task LeaveAsync()
    {
        if (_group is null) return;
        _group.All.OnMemberLeft(_nickName);
        await _group.RemoveAsync(this.Context);
        _group = null;
    }

    public async Task SendAsync(string message)
    {
        if (_group is null) return;
        _group.All.OnMessageReceived(_nickName, message);
    }
}
