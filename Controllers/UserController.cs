using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using SIAP.Api.DTOs;
using SIAP.Api.Hubs;
using SIAP.Api.Services.Interfaces;

namespace SIAP.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class UserController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly IHubContext<AppHub> _hubContext;

    public UserController(IUserService userService, IHubContext<AppHub> hubContext)
    {
        _userService = userService;
        _hubContext = hubContext;
    }

    private async Task<(Guid? userId, int? bidangId, bool isSuperAdmin, bool isBidangAdmin)> GetCurrentCallerInfoAsync()
    {
        var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdStr, out var userId))
        {
            return (null, null, false, false);
        }

        var isSuperAdmin = User.IsInRole("admin");
        var isBidangAdmin = User.IsInRole("admin") || User.IsInRole("kasubag");

        int? bidangId = null;
        var bidangClaim = User.FindFirst("bidangId")?.Value;
        if (int.TryParse(bidangClaim, out var bId))
        {
            bidangId = bId;
        }
        else
        {
            try
            {
                var dbUser = await _userService.GetUserByIdAsync(userId);
                bidangId = dbUser?.BidangId;
            }
            catch
            {
                // ignore
            }
        }

        return (userId, bidangId, isSuperAdmin, isBidangAdmin);
    }

    [HttpGet("profile")]
    [HttpGet("me")]
    public async Task<IActionResult> GetProfile()
    {
        try
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
                return Unauthorized(new { message = "Pengguna tidak terautentikasi" });

            var user = await _userService.GetProfileAsync(userId);
            return Ok(user);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("profile")]
    [HttpPut("me")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
    {
        try
        {
            var userIdString = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
                return Unauthorized(new { message = "Pengguna tidak terautentikasi" });

            var user = await _userService.UpdateProfileAsync(userId, request);

            // Broadcast SignalR event
            await _hubContext.Clients.All.SendAsync("UserUpdated", user);

            return Ok(user);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet]
    [Authorize(Roles = "admin,kasubag")]
    public async Task<IActionResult> GetAllUsers()
    {
        var users = await _userService.GetAllUsersAsync();
        var (_, bidangId, isSuperAdmin, _) = await GetCurrentCallerInfoAsync();
        
        if (!isSuperAdmin)
        {
            if (bidangId.HasValue)
            {
                // Admin bidang / Kasubag hanya melihat Tenaga Ahli di bidangnya sendiri serta calon pendaftar yang belum diapprove
                users = users.Where(u => u.BidangId == bidangId.Value || (!u.IsApproved && (u.BidangId == null || u.BidangId == bidangId.Value)));
            }
            else
            {
                return Forbid();
            }
        }
        
        return Ok(users);
    }

    [HttpGet("{id}")]
    [Authorize(Roles = "admin,kasubag")]
    public async Task<IActionResult> GetUserById(Guid id)
    {
        try
        {
            var user = await _userService.GetUserByIdAsync(id);
            var (_, bidangId, isSuperAdmin, _) = await GetCurrentCallerInfoAsync();
            if (!isSuperAdmin)
            {
                if (!bidangId.HasValue || (user.BidangId != bidangId.Value && user.IsApproved))
                {
                    return Forbid();
                }
            }
            return Ok(user);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpPost]
    [Authorize(Roles = "admin,kasubag")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
    {
        try
        {
            var (_, bidangId, isSuperAdmin, _) = await GetCurrentCallerInfoAsync();
            if (!isSuperAdmin)
            {
                if (!bidangId.HasValue)
                {
                    return Forbid("Admin/Kasubag belum memiliki bidang terdaftar.");
                }

                // Lock created user to Admin's bidang and 'user' (Tenaga Ahli) role
                request.BidangId = bidangId.Value;
                request.Bidang = null;
                request.RoleId = 3;
            }
            else if (request.RoleId == 2 || request.RoleId == 4)
            {
                // Super Admin can create admin or super-admin
            }

            var user = await _userService.CreateUserAsync(request);

            // Broadcast SignalR event
            await _hubContext.Clients.All.SendAsync("UserCreated", user);

            return CreatedAtAction(nameof(GetUserById), new { id = user.Id }, user);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "admin,kasubag")]
    public async Task<IActionResult> UpdateUser(Guid id, [FromBody] UpdateUserRequest request)
    {
        try
        {
            var existingUser = await _userService.GetUserByIdAsync(id);
            var (_, bidangId, isSuperAdmin, _) = await GetCurrentCallerInfoAsync();
            if (!isSuperAdmin)
            {
                if (!bidangId.HasValue)
                {
                    return Forbid("Admin/Kasubag belum memiliki bidang terdaftar.");
                }

                if (existingUser.IsApproved && existingUser.BidangId != bidangId.Value)
                {
                    return Forbid("Admin/Kasubag hanya bisa mengubah Tenaga Ahli di bidangnya sendiri.");
                }

                // Enforce Admin's bidang
                request.BidangId = bidangId.Value;
                request.Bidang = null;

                if (request.RoleId.HasValue && request.RoleId.Value != 3)
                {
                    return Forbid("Admin/Kasubag hanya dapat mengelola Tenaga Ahli.");
                }
            }

            var user = await _userService.UpdateUserAsync(id, request);

            // Broadcast SignalR event
            await _hubContext.Clients.All.SendAsync("UserUpdated", user);

            return Ok(user);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("{id}/approve")]
    [Authorize(Roles = "admin,kasubag")]
    public async Task<IActionResult> ApproveUser(Guid id, [FromBody] ApproveUserRequest request)
    {
        try
        {
            var (_, bidangId, isSuperAdmin, _) = await GetCurrentCallerInfoAsync();
            if (!isSuperAdmin)
            {
                if (!bidangId.HasValue)
                {
                    return Forbid("Admin/Kasubag belum memiliki bidang terdaftar.");
                }

                // When Admin/Kasubag approves, the user AUTOMATICALLY becomes Tenaga Ahli in Admin's bidang!
                request.BidangId = bidangId.Value;
                request.Bidang = null;
            }

            var user = await _userService.ApproveUserAsync(id, request);

            // Broadcast SignalR event
            await _hubContext.Clients.All.SendAsync("UserUpdated", user);

            return Ok(user);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "admin,kasubag")]
    public async Task<IActionResult> DeleteUser(Guid id)
    {
        try
        {
            var existingUser = await _userService.GetUserByIdAsync(id);
            var (_, bidangId, isSuperAdmin, _) = await GetCurrentCallerInfoAsync();
            if (!isSuperAdmin)
            {
                if (!bidangId.HasValue)
                {
                    return Forbid();
                }

                if ((existingUser.IsApproved && existingUser.BidangId != bidangId.Value) || 
                    existingUser.Role == "admin" || existingUser.Role == "admin" || existingUser.Role == "kasubag")
                {
                    return Forbid("Admin/Kasubag hanya bisa menghapus Tenaga Ahli di bidangnya sendiri.");
                }
            }

            await _userService.DeleteUserAsync(id);

            // Broadcast SignalR event
            await _hubContext.Clients.All.SendAsync("UserDeleted", id);

            return NoContent();
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    [HttpGet("search")]
    public async Task<IActionResult> SearchUsers([FromQuery] string query)
    {
        try
        {
            var users = await _userService.SearchUsersAsync(query);
            return Ok(users);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
