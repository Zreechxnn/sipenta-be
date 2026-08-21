using SIAP.Api.DTOs;

namespace SIAP.Api.Services.Interfaces;

public interface IAuthService
{
    Task<AuthResponse> LoginAsync(LoginRequest request);
    Task<AuthResponse> RegisterAsync(RegisterRequest request);
    Task<AuthResponse> GoogleLoginAsync(GoogleLoginRequest request);
}
