using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using MagicOnion;

namespace MagicOnionSampleApp.Shared.Hubs
{
    public interface IChatHub : IStreamingHub<IChatHub, IChatHubReceiver>
    {
        Task JoinAsync(string nickName);
        Task LeaveAsync();
        Task SendAsync(string message);
    }

    public interface IChatHubReceiver
    {
        void OnMemberJoined(string nickName);
        void OnMessageReceived(string nickName, string message);
        void OnMemberLeft(string nickName);
    }
}
