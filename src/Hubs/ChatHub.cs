using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;

namespace SIAP.Api.Hubs;

[Authorize]
public class ChatHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userName = Context.User?.Identity?.Name ?? Context.ConnectionId;
        await Clients.All.SendAsync("UserConnected", userName);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userName = Context.User?.Identity?.Name ?? Context.ConnectionId;
        await Clients.All.SendAsync("UserDisconnected", userName);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task JoinSession(string sessionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, sessionId);
        await Clients.Group(sessionId).SendAsync("UserJoinedSession", Context.ConnectionId, sessionId);
    }

    public async Task LeaveSession(string sessionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, sessionId);
        await Clients.Group(sessionId).SendAsync("UserLeftSession", Context.ConnectionId, sessionId);
    }

    public async Task SendMessage(string user, string message)
    {
        await Clients.All.SendAsync("ReceiveMessage", user, message);
    }

    public async Task SendSessionMessage(string sessionId, string user, string message)
    {
        await Clients.Group(sessionId).SendAsync("ReceiveSessionMessage", user, message);
    }

    public async Task SendTypingStatus(string sessionId, string user, bool isTyping)
    {
        await Clients.Group(sessionId).SendAsync("UserTypingStatus", user, isTyping);
    }
}
