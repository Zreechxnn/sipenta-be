using SIAP.Api.DTOs;
using SIAP.Api.Entities;
using SIAP.Api.Repositories.Interfaces;
using SIAP.Api.Services.Interfaces;

namespace SIAP.Api.Services.Implementations;

public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IBidangRepository _bidangRepository;

    public UserService(IUserRepository userRepository, IRoleRepository roleRepository, IBidangRepository bidangRepository)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _bidangRepository = bidangRepository;
    }

    public async Task<IEnumerable<UserDto>> GetAllUsersAsync()
    {
        var users = await _userRepository.GetAllAsync();
        return users.Select(user => MapToDto(user));
    }

    public async Task<UserDto> GetUserByIdAsync(Guid id)
    {
        var user = await _userRepository.GetByIdAsync(id);
        if (user == null)
            throw new KeyNotFoundException("User tidak ditemukan.");

        return MapToDto(user);
    }

    public async Task<UserDto> CreateUserAsync(CreateUserRequest request)
    {
        if (await _userRepository.GetByUsernameAsync(request.Username) != null)
            throw new Exception("Username sudah digunakan.");

        if (await _userRepository.GetByEmailAsync(request.Email) != null)
            throw new Exception("Email sudah terdaftar.");

        var role = await _roleRepository.GetByIdAsync(request.RoleId);
        if (role == null)
            throw new Exception("Peran (Role) tidak ditemukan.");

        int? bidangId = null;
        if (request.BidangId.HasValue)
        {
            var b = await _bidangRepository.GetByIdAsync(request.BidangId.Value);
            if (b != null) bidangId = b.Id;
        }
        else if (!string.IsNullOrWhiteSpace(request.Bidang))
        {
            var b = await _bidangRepository.GetByNamaAsync(request.Bidang);
            if (b != null) bidangId = b.Id;
        }

        var user = new User
        {
            Username = request.Username,
            Email = request.Email,
            FullName = request.FullName,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            RoleId = request.RoleId,
            BidangId = bidangId,
            IsApproved = request.IsApproved
        };

        await _userRepository.AddAsync(user);
        user = await _userRepository.GetByIdAsync(user.Id);

        return MapToDto(user!);
    }

    public async Task<UserDto> UpdateUserAsync(Guid id, UpdateUserRequest request)
    {
        var user = await _userRepository.GetByIdAsync(id);
        if (user == null)
            throw new KeyNotFoundException("User tidak ditemukan.");

        if (!string.IsNullOrEmpty(request.Username) && request.Username != user.Username)
        {
            if (await _userRepository.GetByUsernameAsync(request.Username) != null)
                throw new Exception("Username sudah digunakan.");
            user.Username = request.Username;
        }

        if (!string.IsNullOrEmpty(request.Email) && request.Email != user.Email)
        {
            if (await _userRepository.GetByEmailAsync(request.Email) != null)
                throw new Exception("Email sudah terdaftar.");
            user.Email = request.Email;
        }

        if (request.FullName != null)
        {
            user.FullName = request.FullName;
        }

        if (!string.IsNullOrEmpty(request.Password))
        {
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
        }

        if (request.RoleId.HasValue && request.RoleId.Value != user.RoleId)
        {
            var role = await _roleRepository.GetByIdAsync(request.RoleId.Value);
            if (role == null)
                throw new Exception("Peran (Role) tidak ditemukan.");
            user.RoleId = request.RoleId.Value;
        }

        if (request.BidangId.HasValue)
        {
            var b = await _bidangRepository.GetByIdAsync(request.BidangId.Value);
            if (b != null) user.BidangId = b.Id;
        }
        else if (request.Bidang != null)
        {
            if (string.IsNullOrWhiteSpace(request.Bidang))
            {
                user.BidangId = null;
            }
            else
            {
                var b = await _bidangRepository.GetByNamaAsync(request.Bidang);
                if (b != null) user.BidangId = b.Id;
            }
        }

        if (request.IsApproved.HasValue)
        {
            user.IsApproved = request.IsApproved.Value;
        }

        user.UpdatedAt = DateTime.UtcNow;

        await _userRepository.UpdateAsync(user);
        user = await _userRepository.GetByIdAsync(user.Id);

        return MapToDto(user!);
    }

    public async Task<UserDto> ApproveUserAsync(Guid id, ApproveUserRequest request)
    {
        var user = await _userRepository.GetByIdAsync(id);
        if (user == null)
            throw new KeyNotFoundException("User tidak ditemukan.");

        int? bidangId = null;
        if (request.BidangId.HasValue)
        {
            var b = await _bidangRepository.GetByIdAsync(request.BidangId.Value);
            if (b != null) bidangId = b.Id;
        }
        else if (!string.IsNullOrWhiteSpace(request.Bidang))
        {
            var b = await _bidangRepository.GetByNamaAsync(request.Bidang);
            if (b != null) bidangId = b.Id;
        }

        if (!bidangId.HasValue)
            throw new Exception("Bidang wajib dipilih saat menyetujui pengguna.");

        user.BidangId = bidangId.Value;
        user.IsApproved = true;
        user.UpdatedAt = DateTime.UtcNow;

        await _userRepository.UpdateAsync(user);
        user = await _userRepository.GetByIdAsync(user.Id);

        return MapToDto(user!);
    }

    public async Task DeleteUserAsync(Guid id)
    {
        var user = await _userRepository.GetByIdAsync(id);
        if (user == null)
            throw new KeyNotFoundException("User tidak ditemukan.");

        await _userRepository.DeleteAsync(user);
    }

    public async Task<UserDto> GetProfileAsync(Guid userId)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
            throw new KeyNotFoundException("User tidak ditemukan.");

        return MapToDto(user);
    }

    public async Task<UserDto> UpdateProfileAsync(Guid userId, UpdateProfileRequest request)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
            throw new KeyNotFoundException("User tidak ditemukan.");

        if (!string.IsNullOrWhiteSpace(request.Username) && request.Username != user.Username)
        {
            var existing = await _userRepository.GetByUsernameAsync(request.Username);
            if (existing != null && existing.Id != user.Id)
                throw new Exception("Username sudah digunakan.");
            user.Username = request.Username;
        }

        if (!string.IsNullOrWhiteSpace(request.Email) && request.Email != user.Email)
        {
            var existing = await _userRepository.GetByEmailAsync(request.Email);
            if (existing != null && existing.Id != user.Id)
                throw new Exception("Email sudah terdaftar.");
            user.Email = request.Email;
        }

        if (request.FullName != null)
        {
            user.FullName = request.FullName;
        }

        if (!string.IsNullOrWhiteSpace(request.NewPassword))
        {
            if (string.IsNullOrWhiteSpace(request.CurrentPassword) || !BCrypt.Net.BCrypt.Verify(request.CurrentPassword, user.PasswordHash))
            {
                throw new Exception("Password saat ini tidak valid.");
            }
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        }

        user.UpdatedAt = DateTime.UtcNow;

        await _userRepository.UpdateAsync(user);
        user = await _userRepository.GetByIdAsync(user.Id);

        return MapToDto(user!);
    }

    private static UserDto MapToDto(User user)
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
}
