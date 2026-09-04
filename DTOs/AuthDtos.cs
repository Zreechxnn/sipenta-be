namespace SIAP.Api.DTOs;

public class LoginRequest
{
    public string Username { get; set; } = null!;
    public string Password { get; set; } = null!;
}

public class RegisterRequest
{
    public string Username { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string? FullName { get; set; }
    public string Password { get; set; } = null!;
}

public class AuthResponse
{
    public string Token { get; set; } = null!;
    public string? RefreshToken { get; set; }
    public UserDto User { get; set; } = null!;
    public bool IsNewUser { get; set; }
}

public class RefreshTokenRequest
{
    public string? RefreshToken { get; set; }
}

public class GoogleLoginRequest
{
    public string IdToken { get; set; } = string.Empty;
}

public class LoginErrorResponse
{
    public string Message { get; set; } = string.Empty;
    public bool IsLockedOut { get; set; }
    public int RetryAfterSeconds { get; set; }
    public int RemainingAttempts { get; set; }
}

