using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SIAP.Api.Configurations;
using SIAP.Api.DTOs;
using SIAP.Api.Entities;
using SIAP.Api.Repositories.Interfaces;
using SIAP.Api.Services.Interfaces;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Google.Apis.Auth;

namespace SIAP.Api.Services.Implementations;

public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly JwtOptions _jwtOptions;
    private readonly GoogleOptions _googleOptions;

    public AuthService(IUserRepository userRepository, IRoleRepository roleRepository, IOptions<JwtOptions> jwtOptions, IOptions<GoogleOptions> googleOptions)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _jwtOptions = jwtOptions.Value;
        _googleOptions = googleOptions.Value;
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        var user = await _userRepository.GetByUsernameAsync(request.Username);
        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            throw new Exception("Username atau kata sandi tidak valid.");
        }

        var token = GenerateJwtToken(user);
        
        return new AuthResponse
        {
            Token = token,
            User = new UserDto
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
            }
        };
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
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

        return new AuthResponse
        {
            Token = token,
            User = new UserDto
            {
                Id = user!.Id,
                Username = user.Username,
                Email = user.Email,
                FullName = user.FullName,
                Role = user.Role?.Name ?? "user",
                BidangId = user.BidangId,
                Bidang = user.Bidang?.Nama,
                IsApproved = user.IsApproved,
                CreatedAt = user.CreatedAt
            },
            IsNewUser = true
        };
    }

    public async Task<AuthResponse> GoogleLoginAsync(GoogleLoginRequest request)
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

        var token = GenerateJwtToken(user!);

        return new AuthResponse
        {
            Token = token,
            User = new UserDto
            {
                Id = user!.Id,
                Username = user.Username,
                Email = user.Email,
                FullName = user.FullName,
                Role = user.Role?.Name ?? "user",
                BidangId = user.BidangId,
                Bidang = user.Bidang?.Nama,
                IsApproved = user.IsApproved,
                CreatedAt = user.CreatedAt
            },
            IsNewUser = isNewUser
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
