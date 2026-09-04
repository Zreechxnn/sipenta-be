using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using SIAP.Api.Configurations;
using SIAP.Api.DTOs;
using SIAP.Api.Hubs;
using SIAP.Api.Services.Interfaces;
using System.Security.Claims;

namespace SIAP.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly ILoginRateLimiter _rateLimiter;
    private readonly IHubContext<AppHub> _hubContext;
    private readonly IDataProtector _dataProtector;
    private readonly JwtOptions _jwtOptions;

    public AuthController(
        IAuthService authService, 
        ILoginRateLimiter rateLimiter,
        IHubContext<AppHub> hubContext,
        IDataProtectionProvider dataProtectionProvider,
        IOptions<JwtOptions> jwtOptions)
    {
        _authService = authService;
        _rateLimiter = rateLimiter;
        _hubContext = hubContext;
        _dataProtector = dataProtectionProvider.CreateProtector("SIAP.Auth.CookieProtection");
        _jwtOptions = jwtOptions.Value;
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
            var response = await _authService.LoginAsync(request, ip);

            // Reset failed attempt counter on success
            await _rateLimiter.ResetAttemptsAsync(request.Username, ip);

            // Set secure HttpOnly session cookies for encrypted JWT & encrypted Refresh Token (cleared when browser closes)
            SetAuthCookies(response.Token, response.RefreshToken);

            var expiry = DateTime.UtcNow.AddMinutes(_jwtOptions.ExpiryMinutes > 0 ? _jwtOptions.ExpiryMinutes : 30);
            return Ok(new AuthResponse
            {
                Token = "hidden-httponly-token",
                RefreshToken = null,
                User = response.User,
                IsNewUser = false,
                ExpiresAt = expiry
            });
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
        var ip = GetClientIp();
        try
        {
            var response = await _authService.RegisterAsync(request, ip);

            // Set secure HttpOnly session cookies for encrypted JWT & Refresh Token
            SetAuthCookies(response.Token, response.RefreshToken);

            // Broadcast SignalR event to admins for real-time user registration
            await _hubContext.Clients.All.SendAsync("UserRegistered", new { username = request.Username, email = request.Email });

            var expiry = DateTime.UtcNow.AddMinutes(_jwtOptions.ExpiryMinutes > 0 ? _jwtOptions.ExpiryMinutes : 30);
            return Ok(new AuthResponse
            {
                Token = "hidden-httponly-token",
                RefreshToken = null,
                User = response.User,
                IsNewUser = true,
                ExpiresAt = expiry
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("google-login")]
    public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginRequest request)
    {
        var ip = GetClientIp();
        try
        {
            var response = await _authService.GoogleLoginAsync(request, ip);
            
            // Set secure HttpOnly session cookies for encrypted JWT & Refresh Token
            SetAuthCookies(response.Token, response.RefreshToken);

            if (response.IsNewUser)
            {
                // Broadcast SignalR event to admins for real-time user registration
                await _hubContext.Clients.All.SendAsync("UserRegistered", new { username = response.User.Username, email = response.User.Email });
            }

            var expiry = DateTime.UtcNow.AddMinutes(_jwtOptions.ExpiryMinutes > 0 ? _jwtOptions.ExpiryMinutes : 30);
            return Ok(new AuthResponse
            {
                Token = "hidden-httponly-token",
                RefreshToken = null,
                User = response.User,
                IsNewUser = response.IsNewUser,
                ExpiresAt = expiry
            });
        }
        catch (Exception ex)
        {
            return Unauthorized(new { message = ex.Message });
        }
    }

    [HttpPost("refresh-token")]
    [HttpPost("refresh")]
    public async Task<IActionResult> RefreshToken([FromBody] RefreshTokenRequest? request)
    {
        var refreshToken = request?.RefreshToken;
        if (string.IsNullOrEmpty(refreshToken) && Request.Cookies.TryGetValue("sipenta_refresh_token", out var protectedRefreshToken) && !string.IsNullOrEmpty(protectedRefreshToken))
        {
            try
            {
                refreshToken = _dataProtector.Unprotect(protectedRefreshToken);
            }
            catch
            {
                refreshToken = protectedRefreshToken;
            }
        }

        if (string.IsNullOrEmpty(refreshToken))
        {
            return Unauthorized(new { message = "Refresh token tidak ditemukan atau telah kedaluwarsa." });
        }

        var ip = GetClientIp();

        try
        {
            var response = await _authService.RefreshTokenAsync(refreshToken, ip);

            // Set new encrypted session cookies for rotated tokens
            SetAuthCookies(response.Token, response.RefreshToken);

            var expiry = DateTime.UtcNow.AddMinutes(_jwtOptions.ExpiryMinutes > 0 ? _jwtOptions.ExpiryMinutes : 30);
            return Ok(new AuthResponse
            {
                Token = "hidden-httponly-token",
                RefreshToken = null,
                User = response.User,
                IsNewUser = false,
                ExpiresAt = expiry
            });
        }
        catch (Exception ex)
        {
            ClearAuthCookies();
            return Unauthorized(new { message = ex.Message });
        }
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] RefreshTokenRequest? request)
    {
        var ip = GetClientIp();

        var refreshToken = request?.RefreshToken;
        if (string.IsNullOrEmpty(refreshToken) && Request.Cookies.TryGetValue("sipenta_refresh_token", out var protectedRefreshToken) && !string.IsNullOrEmpty(protectedRefreshToken))
        {
            try
            {
                refreshToken = _dataProtector.Unprotect(protectedRefreshToken);
            }
            catch
            {
                refreshToken = protectedRefreshToken;
            }
        }

        if (!string.IsNullOrEmpty(refreshToken))
        {
            await _authService.RevokeTokenAsync(refreshToken, ip);
        }
        else if (User.Identity?.IsAuthenticated == true)
        {
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
            if (Guid.TryParse(userIdStr, out var userId))
            {
                await _authService.RevokeAllUserTokensAsync(userId, ip);
            }
        }

        ClearAuthCookies();

        return Ok(new { message = "Logout berhasil dan refresh token dinonaktifkan." });
    }

    private void SetAuthCookies(string token, string? refreshToken = null)
    {
        if (string.IsNullOrEmpty(token)) return;

        var isHttps = Request.IsHttps 
            || (Request.Headers.TryGetValue("X-Forwarded-Proto", out var proto) && proto.ToString().Equals("https", StringComparison.OrdinalIgnoreCase))
            || !Request.Host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);

        // Encrypt the token using ASP.NET Core Data Protection (AES-256) so it cannot be read in DevTools Cookies
        var protectedToken = _dataProtector.Protect(token);

        // Omitting Expires and MaxAge creates a browser Session Cookie.
        // The browser discards session cookies as soon as the browser / window is closed.
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = isHttps,
            SameSite = isHttps ? SameSiteMode.None : SameSiteMode.Lax,
            Path = "/"
        };

        Response.Cookies.Append("sipenta_token", protectedToken, cookieOptions);

        if (!string.IsNullOrEmpty(refreshToken))
        {
            var protectedRefreshToken = _dataProtector.Protect(refreshToken);
            Response.Cookies.Append("sipenta_refresh_token", protectedRefreshToken, cookieOptions);
        }

        // Issue Double-Submit CSRF Cookie (readable by frontend script for X-CSRF-Token header)
        var csrfToken = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var csrfCookieOptions = new CookieOptions
        {
            HttpOnly = false, // Client JavaScript reads this to attach X-CSRF-Token header
            Secure = isHttps,
            SameSite = isHttps ? SameSiteMode.None : SameSiteMode.Lax,
            Path = "/"
        };
        Response.Cookies.Append("sipenta_csrf", csrfToken, csrfCookieOptions);
    }

    private void ClearAuthCookies()
    {
        var isHttps = Request.IsHttps 
            || (Request.Headers.TryGetValue("X-Forwarded-Proto", out var proto) && proto.ToString().Equals("https", StringComparison.OrdinalIgnoreCase))
            || !Request.Host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);

        var deleteOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = isHttps,
            SameSite = isHttps ? SameSiteMode.None : SameSiteMode.Lax,
            Path = "/"
        };

        Response.Cookies.Delete("sipenta_token", deleteOptions);
        Response.Cookies.Delete("sipenta_refresh_token", deleteOptions);

        var csrfDeleteOptions = new CookieOptions
        {
            HttpOnly = false,
            Secure = isHttps,
            SameSite = isHttps ? SameSiteMode.None : SameSiteMode.Lax,
            Path = "/"
        };
        Response.Cookies.Delete("sipenta_csrf", csrfDeleteOptions);
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
