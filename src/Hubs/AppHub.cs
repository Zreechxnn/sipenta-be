using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Authorization;

namespace SIAP.Api.Hubs;

[Authorize]
public class AppHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await base.OnDisconnectedAsync(exception);
    }
}
