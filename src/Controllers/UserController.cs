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
        var isBidangAdmin = User.IsInRole("admin") || User.IsInRole("kepala bagian");

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
    [Authorize(Roles = "admin,kepala bagian")]
    public async Task<IActionResult> GetAllUsers()
    {
        var users = await _userService.GetAllUsersAsync();
        var (_, bidangId, isSuperAdmin, _) = await GetCurrentCallerInfoAsync();
        
        if (!isSuperAdmin)
        {
            if (bidangId.HasValue)
            {
                // Admin bidang / Kepala Bidang hanya melihat Tenaga Ahli di bidangnya sendiri serta calon pendaftar yang belum diapprove
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
    [Authorize(Roles = "admin,kepala bagian")]
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
    [Authorize(Roles = "admin,kepala bagian")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request)
    {
        try
        {
            var (_, bidangId, isSuperAdmin, _) = await GetCurrentCallerInfoAsync();
            if (!isSuperAdmin)
            {
                if (!bidangId.HasValue)
                {
                    return Forbid("Admin/Kepala Bagian belum memiliki bidang terdaftar.");
                }

                request.BidangId = bidangId.Value;
                request.Bidang = null;
                request.RoleId = 3;
            }

            var user = await _userService.CreateUserAsync(request);

            await _hubContext.Clients.All.SendAsync("UserCreated", user);

            return CreatedAtAction(nameof(GetUserById), new { id = user.Id }, user);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "admin,kepala bagian")]
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
                    return Forbid("Admin/Kepala Bagian belum memiliki bidang terdaftar.");
                }

                if (existingUser.IsApproved && existingUser.BidangId != bidangId.Value)
                {
                    return Forbid("Admin/Kepala Bagian hanya bisa mengubah Tenaga Ahli di bidangnya sendiri.");
                }

                request.BidangId = bidangId.Value;
                request.Bidang = null;

                if (request.RoleId.HasValue && request.RoleId.Value != 3)
                {
                    return Forbid("Admin/Kepala Bagian hanya dapat mengelola Tenaga Ahli.");
                }
            }

            var user = await _userService.UpdateUserAsync(id, request);

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
    [Authorize(Roles = "admin,kepala bagian")]
    public async Task<IActionResult> ApproveUser(Guid id, [FromBody] ApproveUserRequest request)
    {
        try
        {
            var (_, bidangId, isSuperAdmin, _) = await GetCurrentCallerInfoAsync();
            if (!isSuperAdmin)
            {
                if (!bidangId.HasValue)
                {
                    return Forbid("Admin/Kepala Bagian belum memiliki bidang terdaftar.");
                }

                request.BidangId = bidangId.Value;
                request.Bidang = null;
            }

            var user = await _userService.ApproveUserAsync(id, request);

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
    [Authorize(Roles = "admin,kepala bagian")]
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
                    existingUser.Role == "admin" || existingUser.Role == "kepala bagian")
                {
                    return Forbid("Admin/Kepala Bagian hanya bisa menghapus Tenaga Ahli di bidangnya sendiri.");
                }
            }

            await _userService.DeleteUserAsync(id);

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
            var (userId, _, isSuperAdmin, isBidangAdmin) = await GetCurrentCallerInfoAsync();
            if (!userId.HasValue) return Unauthorized(new { message = "Pengguna tidak terautentikasi" });

            if (!isSuperAdmin && !isBidangAdmin)
            {
                var profile = await _userService.GetUserByIdAsync(userId.Value);
                if (!profile.IsApproved)
                {
                    return StatusCode(403, new { message = "Akun Anda sedang menunggu persetujuan dari Admin/Kepala Bidang." });
                }
            }

            var users = await _userService.SearchUsersAsync(query);
            return Ok(users);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }
}
