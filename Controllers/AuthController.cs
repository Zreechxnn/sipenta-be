using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using SIAP.Api.DTOs;
using SIAP.Api.Hubs;
using SIAP.Api.Services.Interfaces;

namespace SIAP.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IHubContext<AppHub> _hubContext;

    public AuthController(IAuthService authService, IHubContext<AppHub> hubContext)
    {
        _authService = authService;
        _hubContext = hubContext;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        try
        {
            var response = await _authService.LoginAsync(request);
            return Ok(response);
        }
        catch (Exception ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        try
        {
            var response = await _authService.RegisterAsync(request);

            // Broadcast SignalR event to admins for real-time user registration
            await _hubContext.Clients.All.SendAsync("UserRegistered", new { username = request.Username, email = request.Email });

            return Ok(response);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("google-login")]
    public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginRequest request)
    {
        try
        {
            var response = await _authService.GoogleLoginAsync(request);
            
            if (response.IsNewUser)
            {
                // Broadcast SignalR event to admins for real-time user registration
                await _hubContext.Clients.All.SendAsync("UserRegistered", new { username = response.User.Username, email = response.User.Email });
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }
}
