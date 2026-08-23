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
    [Authorize(Roles = "super-admin,admin,kasubag")]
    public async Task<IActionResult> GetAllUsers()
    {
        var users = await _userService.GetAllUsersAsync();
        
        if (User.IsInRole("kasubag"))
        {
            var bidangClaim = User.FindFirst("bidangId")?.Value;
            if (int.TryParse(bidangClaim, out int bidangId))
            {
                users = users.Where(u => u.BidangId == bidangId);
            }
            else
            {
                return Forbid();
            }
        }
        
        return Ok(users);
    }

    [HttpGet("{id}")]
    [Authorize(Roles = "super-admin,admin,kasubag")]
    public async Task<IActionResult> GetUserById(Guid id)
    {
        try
        {
            var user = await _userService.GetUserByIdAsync(id);
            if (User.IsInRole("kasubag"))
            {
                var bidangClaim = User.FindFirst("bidangId")?.Value;
                if (!int.TryParse(bidangClaim, out int bidangId) || user.BidangId != bidangId)
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
    [Authorize(Roles = "super-admin,admin,kasubag")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
    {
        try
        {
            if (User.IsInRole("kasubag"))
            {
                var bidangClaim = User.FindFirst("bidangId")?.Value;
                if (!int.TryParse(bidangClaim, out int bidangId) || request.BidangId != bidangId)
                {
                    return Forbid("Kasubag hanya bisa membuat user di bidangnya sendiri.");
                }
            }

            // Only super-admin can assign admin (2) or super-admin (4) role
            if (request.RoleId == 2 || request.RoleId == 4)
            {
                if (!User.IsInRole("super-admin"))
                {
                    return Forbid("Hanya super-admin yang bisa menetapkan hak akses sebagai admin.");
                }
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
    [Authorize(Roles = "super-admin,admin,kasubag")]
    public async Task<IActionResult> UpdateUser(Guid id, [FromBody] UpdateUserRequest request)
    {
        try
        {
            var existingUser = await _userService.GetUserByIdAsync(id);
            if (User.IsInRole("kasubag"))
            {
                var bidangClaim = User.FindFirst("bidangId")?.Value;
                if (!int.TryParse(bidangClaim, out int bidangId) || existingUser.BidangId != bidangId || (request.BidangId.HasValue && request.BidangId != bidangId))
                {
                    return Forbid("Kasubag hanya bisa mengubah user di bidangnya sendiri.");
                }
            }

            // Only super-admin can assign admin (2) or super-admin (4) role
            if (request.RoleId.HasValue && (request.RoleId.Value == 2 || request.RoleId.Value == 4))
            {
                if (!User.IsInRole("super-admin"))
                {
                    return Forbid("Hanya super-admin yang bisa menetapkan hak akses sebagai admin.");
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
    [Authorize(Roles = "super-admin,admin,kasubag")]
    public async Task<IActionResult> ApproveUser(Guid id, [FromBody] ApproveUserRequest request)
    {
        try
        {
            if (User.IsInRole("kasubag"))
            {
                var bidangClaim = User.FindFirst("bidangId")?.Value;
                if (!int.TryParse(bidangClaim, out int bidangId) || request.BidangId != bidangId)
                {
                    return Forbid("Kasubag hanya bisa menyetujui user untuk bidangnya sendiri.");
                }
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
    [Authorize(Roles = "super-admin,admin,kasubag")]
    public async Task<IActionResult> DeleteUser(Guid id)
    {
        try
        {
            var existingUser = await _userService.GetUserByIdAsync(id);
            if (User.IsInRole("kasubag"))
            {
                var bidangClaim = User.FindFirst("bidangId")?.Value;
                if (!int.TryParse(bidangClaim, out int bidangId) || existingUser.BidangId != bidangId)
                {
                    return Forbid();
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
