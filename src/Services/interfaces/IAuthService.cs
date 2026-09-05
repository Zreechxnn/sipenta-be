using SIAP.Api.DTOs;

namespace SIAP.Api.Services.Interfaces;

public interface IAuthService
{
    Task<AuthResponse> LoginAsync(LoginRequest request, string? ipAddress = null);
    Task<AuthResponse> RegisterAsync(RegisterRequest request, string? ipAddress = null);
    Task<AuthResponse> GoogleLoginAsync(GoogleLoginRequest request, string? ipAddress = null);
    Task<AuthResponse> RefreshTokenAsync(string refreshToken, string? ipAddress = null);
    Task RevokeTokenAsync(string refreshToken, string? ipAddress = null);
    Task RevokeAllUserTokensAsync(Guid userId, string? ipAddress = null);
}
