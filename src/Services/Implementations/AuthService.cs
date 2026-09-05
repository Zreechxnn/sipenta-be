using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SIAP.Api.Configurations;
using SIAP.Api.DTOs;
using SIAP.Api.Entities;
using SIAP.Api.Repositories.Interfaces;
using SIAP.Api.Services.Interfaces;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Google.Apis.Auth;

namespace SIAP.Api.Services.Implementations;

public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly ITokenCipherService _tokenCipherService;
    private readonly JwtOptions _jwtOptions;
    private readonly GoogleOptions _googleOptions;

    public AuthService(
        IUserRepository userRepository, 
        IRoleRepository roleRepository, 
        IRefreshTokenRepository refreshTokenRepository,
        ITokenCipherService tokenCipherService,
        IOptions<JwtOptions> jwtOptions, 
        IOptions<GoogleOptions> googleOptions)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _tokenCipherService = tokenCipherService;
        _jwtOptions = jwtOptions.Value;
        _googleOptions = googleOptions.Value;
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, string? ipAddress = null)
    {
        var user = await _userRepository.GetByUsernameAsync(request.Username);
        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            throw new Exception("Username atau kata sandi tidak valid.");
        }

        // Invalidate previous active sessions to prevent token accumulation in DB
        await _refreshTokenRepository.RevokeAllUserTokensAsync(user.Id, ipAddress);

        var token = GenerateJwtToken(user);
        var (refreshToken, rawRefreshToken) = CreateRefreshToken(user.Id, ipAddress);
        await _refreshTokenRepository.AddAsync(refreshToken);

        return new AuthResponse
        {
            Token = token,
            RefreshToken = rawRefreshToken,
            User = MapToUserDto(user)
        };
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, string? ipAddress = null)
    {
        if (await _userRepository.GetByUsernameAsync(request.Username) != null)
        {
            throw new Exception("Username sudah digunakan.");
        }

        if (await _userRepository.GetByEmailAsync(request.Email) != null)
        {
            throw new Exception("Email sudah terdaftar.");
        }

        var userRole = await _roleRepository.GetByNameAsync("user");
        if (userRole == null)
        {
            throw new Exception("Default role 'user' tidak ditemukan.");
        }

        var user = new User
        {
            Username = request.Username,
            Email = request.Email,
            FullName = request.FullName,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            RoleId = userRole.Id,
            BidangId = null,
            IsApproved = false
        };

        await _userRepository.AddAsync(user);
        user = await _userRepository.GetByIdAsync(user.Id);

        var token = GenerateJwtToken(user!);
        var (refreshToken, rawRefreshToken) = CreateRefreshToken(user!.Id, ipAddress);
        await _refreshTokenRepository.AddAsync(refreshToken);

        return new AuthResponse
        {
            Token = token,
            RefreshToken = rawRefreshToken,
            User = MapToUserDto(user),
            IsNewUser = true
        };
    }

    public async Task<AuthResponse> GoogleLoginAsync(GoogleLoginRequest request, string? ipAddress = null)
    {
        GoogleJsonWebSignature.ValidationSettings settings = new GoogleJsonWebSignature.ValidationSettings
        {
            Audience = new List<string>() { _googleOptions.ClientId }
        };

        GoogleJsonWebSignature.Payload payload;
        try
        {
            payload = await GoogleJsonWebSignature.ValidateAsync(request.IdToken, settings);
        }
        catch (InvalidJwtException ex)
        {
            throw new Exception("Invalid Google Token: " + ex.Message);
        }

        var user = await _userRepository.GetByEmailAsync(payload.Email);
        bool isNewUser = false;

        if (user == null)
        {
            isNewUser = true;
            var userRole = await _roleRepository.GetByNameAsync("user");
            if (userRole == null)
            {
                throw new Exception("Default role 'user' tidak ditemukan.");
            }

            user = new User
            {
                Username = payload.Email.Split('@')[0] + "_" + Guid.NewGuid().ToString("N").Substring(0, 5),
                Email = payload.Email,
                FullName = payload.Name ?? "Google User",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString()),
                RoleId = userRole.Id,
                BidangId = null,
                IsApproved = false
            };

            await _userRepository.AddAsync(user);
            user = await _userRepository.GetByIdAsync(user.Id);
        }

        // Invalidate previous active sessions to prevent token accumulation in DB
        await _refreshTokenRepository.RevokeAllUserTokensAsync(user!.Id, ipAddress);

        var token = GenerateJwtToken(user!);
        var (refreshToken, rawRefreshToken) = CreateRefreshToken(user!.Id, ipAddress);
        await _refreshTokenRepository.AddAsync(refreshToken);

        return new AuthResponse
        {
            Token = token,
            RefreshToken = rawRefreshToken,
            User = MapToUserDto(user),
            IsNewUser = isNewUser
        };
    }

    public async Task<AuthResponse> RefreshTokenAsync(string refreshTokenString, string? ipAddress = null)
    {
        if (string.IsNullOrWhiteSpace(refreshTokenString))
        {
            throw new Exception("Refresh token tidak valid.");
        }

        // Cari token di database menggunakan versi terenkripsi (AES-256)
        var cipherToken = _tokenCipherService.Encrypt(refreshTokenString);
        var existingToken = await _refreshTokenRepository.GetByTokenAsync(cipherToken);

        // Fallback backward-compatibility: jika token di database dibuat sebelum migrasi cipher
        if (existingToken == null && !refreshTokenString.StartsWith("ENC_"))
        {
            existingToken = await _refreshTokenRepository.GetByTokenAsync(refreshTokenString);
        }

        if (existingToken == null)
        {
            throw new Exception("Refresh token tidak ditemukan.");
        }

        // Token reuse detection: if token is already revoked, check for rotation grace window (e.g. concurrent client requests)
        if (existingToken.IsRevoked)
        {
            if (existingToken.RevokedAt.HasValue &&
                DateTime.UtcNow - existingToken.RevokedAt.Value < TimeSpan.FromSeconds(30) &&
                !string.IsNullOrEmpty(existingToken.ReplacedByToken))
            {
                var replacementToken = await _refreshTokenRepository.GetByTokenAsync(existingToken.ReplacedByToken);
                if (replacementToken != null && !replacementToken.IsRevoked && !replacementToken.IsExpired)
                {
                    var replacementUser = replacementToken.User ?? await _userRepository.GetByIdAsync(replacementToken.UserId);
                    if (replacementUser != null)
                    {
                        var replacementJwt = GenerateJwtToken(replacementUser);
                        var rawReplacementToken = _tokenCipherService.Decrypt(replacementToken.Token);
                        return new AuthResponse
                        {
                            Token = replacementJwt,
                            RefreshToken = rawReplacementToken,
                            User = MapToUserDto(replacementUser)
                        };
                    }
                }
            }

            await _refreshTokenRepository.RevokeAllUserTokensAsync(existingToken.UserId, ipAddress);
            throw new Exception("Refresh token sudah pernah digunakan atau dinonaktifkan.");
        }

        if (existingToken.IsExpired)
        {
            throw new Exception("Refresh token telah kedaluwarsa. Silakan login kembali.");
        }

        var user = existingToken.User ?? await _userRepository.GetByIdAsync(existingToken.UserId);
        if (user == null)
        {
            throw new Exception("Pengguna tidak ditemukan.");
        }

        // Rotate refresh token: generate new 30-min JWT and new 1-day/7-day refresh token
        var newJwt = GenerateJwtToken(user);
        var (newRefreshToken, newRawToken) = CreateRefreshToken(user.Id, ipAddress);

        // Mark old token revoked and link to replacement (stores cipher token in DB)
        existingToken.RevokedAt = DateTime.UtcNow;
        existingToken.RevokedByIp = ipAddress;
        existingToken.ReplacedByToken = newRefreshToken.Token;

        await _refreshTokenRepository.UpdateAsync(existingToken);
        await _refreshTokenRepository.AddAsync(newRefreshToken);

        return new AuthResponse
        {
            Token = newJwt,
            RefreshToken = newRawToken,
            User = MapToUserDto(user)
        };
    }

    public async Task RevokeTokenAsync(string refreshTokenString, string? ipAddress = null)
    {
        if (!string.IsNullOrWhiteSpace(refreshTokenString))
        {
            var cipherToken = _tokenCipherService.Encrypt(refreshTokenString);
            await _refreshTokenRepository.RevokeTokenAsync(cipherToken, ipAddress);

            // Jika token mentah lama sebelum migrasi enkripsi
            if (!refreshTokenString.StartsWith("ENC_"))
            {
                await _refreshTokenRepository.RevokeTokenAsync(refreshTokenString, ipAddress);
            }
        }
    }

    public async Task RevokeAllUserTokensAsync(Guid userId, string? ipAddress = null)
    {
        await _refreshTokenRepository.RevokeAllUserTokensAsync(userId, ipAddress);
    }

    private (RefreshToken entity, string rawToken) CreateRefreshToken(Guid userId, string? ipAddress)
    {
        var randomBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        var rawTokenString = Convert.ToBase64String(randomBytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');

        // Enkripsi token sebelum disimpan ke entity database
        var cipherToken = _tokenCipherService.Encrypt(rawTokenString);
        var days = _jwtOptions.RefreshTokenExpiryDays > 0 ? _jwtOptions.RefreshTokenExpiryDays : 1;

        var entity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Token = cipherToken,
            ExpiresAt = DateTime.UtcNow.AddDays(days),
            CreatedAt = DateTime.UtcNow,
            CreatedByIp = ipAddress
        };

        return (entity, rawTokenString);
    }

    private static UserDto MapToUserDto(User user)
    {
        return new UserDto
        {
            Id = user.Id,
            Username = user.Username,
            Email = user.Email,
            FullName = user.FullName,
            Role = user.Role?.Name ?? "user",
            BidangId = user.BidangId,
            Bidang = user.Bidang?.Nama,
            IsApproved = user.IsApproved,
            CreatedAt = user.CreatedAt
        };
    }

    private string GenerateJwtToken(User user)
    {
        var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtOptions.Key));
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        var roleName = user.Role?.Name ?? "user";
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(JwtRegisteredClaimNames.UniqueName, user.Username),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(ClaimTypes.Role, roleName),
            new Claim("role", roleName),
            new Claim("isApproved", user.IsApproved.ToString().ToLower()),
            new Claim("bidangId", user.BidangId?.ToString() ?? ""),
            new Claim("bidang", user.Bidang?.Nama ?? "")
        };

        var token = new JwtSecurityToken(
            issuer: _jwtOptions.Issuer,
            audience: _jwtOptions.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_jwtOptions.ExpiryMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
