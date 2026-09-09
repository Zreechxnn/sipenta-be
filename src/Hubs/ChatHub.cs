using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SIAP.Api.Data;

namespace SIAP.Api.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private readonly AppDbContext _dbContext;
    private readonly ILogger<ChatHub> _logger;

    public ChatHub(AppDbContext dbContext, ILogger<ChatHub> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

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
        if (!await ValidateSessionAccessAsync(sessionId))
        {
            throw new HubException("Akses ditolak: Anda tidak memiliki akses ke sesi percakapan ini.");
        }

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
        if (!await ValidateSessionAccessAsync(sessionId))
        {
            throw new HubException("Akses ditolak: Anda tidak memiliki akses ke sesi percakapan ini.");
        }

        await Clients.Group(sessionId).SendAsync("ReceiveSessionMessage", user, message);
    }

    public async Task SendTypingStatus(string sessionId, string user, bool isTyping)
    {
        if (!await ValidateSessionAccessAsync(sessionId))
        {
            throw new HubException("Akses ditolak: Anda tidak memiliki akses ke sesi percakapan ini.");
        }

        await Clients.Group(sessionId).SendAsync("UserTypingStatus", user, isTyping);
    }

    private async Task<bool> ValidateSessionAccessAsync(string sessionId)
    {
        if (!Guid.TryParse(sessionId, out var sessionGuid))
        {
            return false;
        }

        var userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId))
        {
            return false;
        }

        var role = Context.User?.FindFirst(ClaimTypes.Role)?.Value;
        var isAdmin = string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase);

        var session = await _dbContext.ChatSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == sessionGuid);

        if (session == null)
        {
            return false;
        }

        if (session.UserId != userId && !isAdmin)
        {
            _logger.LogWarning("Percobaan akses sesi chat ilegal oleh user {UserId} pada sesi {SessionId}", userId, sessionGuid);
            return false;
        }

        return true;
    }
}
