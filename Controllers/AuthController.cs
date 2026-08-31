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
    private readonly ILoginRateLimiter _rateLimiter;
    private readonly IHubContext<AppHub> _hubContext;

    public AuthController(
        IAuthService authService, 
        ILoginRateLimiter rateLimiter,
        IHubContext<AppHub> hubContext)
    {
        _authService = authService;
        _rateLimiter = rateLimiter;
        _hubContext = hubContext;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var ip = GetClientIp();

        // 1. Check if user/IP is currently locked out
        var lockoutStatus = await _rateLimiter.CheckLockoutAsync(request.Username, ip);
        if (lockoutStatus.IsLockedOut)
        {
            var minutes = (int)Math.Ceiling(lockoutStatus.RemainingSeconds / 60.0);
            return StatusCode(StatusCodes.Status429TooManyRequests, new LoginErrorResponse
            {
                Message = $"Terlalu banyak percobaan login yang gagal. Akun dikunci sementara demi keamanan. Silakan tunggu {minutes} menit ({lockoutStatus.RemainingSeconds} detik) sebelum mencoba kembali.",
                IsLockedOut = true,
                RetryAfterSeconds = lockoutStatus.RemainingSeconds,
                RemainingAttempts = 0
            });
        }

        try
        {
            var response = await _authService.LoginAsync(request);

            // Reset failed attempt counter on success
            await _rateLimiter.ResetAttemptsAsync(request.Username, ip);

            // Set secure HttpOnly cookie for JWT
            SetTokenCookie(response.Token);

            return Ok(response);
        }
        catch (Exception)
        {
            // Record failed attempt
            var failStatus = await _rateLimiter.RecordFailedAttemptAsync(request.Username, ip);

            if (failStatus.IsLockedOut)
            {
                return StatusCode(StatusCodes.Status429TooManyRequests, new LoginErrorResponse
                {
                    Message = "Batas percobaan login tercapai (5 kali salah kata sandi). Akun Anda dikunci sementara selama 5 menit untuk mencegah serangan brute force / DDoS.",
                    IsLockedOut = true,
                    RetryAfterSeconds = failStatus.RemainingSeconds,
                    RemainingAttempts = 0
                });
            }

            return Unauthorized(new LoginErrorResponse
            {
                Message = $"Username atau kata sandi tidak valid. Sisa kesempatan: {failStatus.RemainingAttempts} kali sebelum akun dikunci sementara.",
                IsLockedOut = false,
                RetryAfterSeconds = 0,
                RemainingAttempts = failStatus.RemainingAttempts
            });
        }
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        try
        {
            var response = await _authService.RegisterAsync(request);

            // Set secure HttpOnly cookie for JWT
            SetTokenCookie(response.Token);

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
            
            // Set secure HttpOnly cookie for JWT
            SetTokenCookie(response.Token);

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

    [HttpPost("logout")]
    public IActionResult Logout()
    {
        var isHttps = Request.IsHttps || (Request.Headers.TryGetValue("X-Forwarded-Proto", out var proto) && proto.ToString().Equals("https", StringComparison.OrdinalIgnoreCase));

        // Clear HttpOnly token cookie
        Response.Cookies.Delete("sipenta_token", new CookieOptions
        {
            HttpOnly = true,
            Secure = isHttps,
            SameSite = isHttps ? SameSiteMode.None : SameSiteMode.Lax,
            Path = "/"
        });

        return Ok(new { message = "Logout berhasil." });
    }

    private void SetTokenCookie(string token)
    {
        if (string.IsNullOrEmpty(token)) return;

        var isHttps = Request.IsHttps || (Request.Headers.TryGetValue("X-Forwarded-Proto", out var proto) && proto.ToString().Equals("https", StringComparison.OrdinalIgnoreCase));

        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = isHttps,
            SameSite = isHttps ? SameSiteMode.None : SameSiteMode.Lax,
            Path = "/",
            Expires = DateTimeOffset.UtcNow.AddDays(7)
        };

        Response.Cookies.Append("sipenta_token", token, cookieOptions);
    }

    private string GetClientIp()
    {
        if (Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor) && !string.IsNullOrEmpty(forwardedFor))
        {
            var ips = forwardedFor.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (ips.Length > 0) return ips[0].Trim();
        }

        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
