namespace SIAP.Api.DTOs;

public class UserDto
{
    public Guid Id { get; set; }
    public string Username { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string? FullName { get; set; }
    public string Role { get; set; } = null!;
    public int? BidangId { get; set; }
    public string? Bidang { get; set; }
    public bool IsApproved { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateUserRequest
{
    public string Username { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string? FullName { get; set; }
    public string Password { get; set; } = null!;
    public int RoleId { get; set; }
    public int? BidangId { get; set; }
    public string? Bidang { get; set; }
    public bool IsApproved { get; set; } = true;
}

public class UpdateUserRequest
{
    public string? Username { get; set; }
    public string? Email { get; set; }
    public string? FullName { get; set; }
    public string? Password { get; set; }
    public int? RoleId { get; set; }
    public int? BidangId { get; set; }
    public string? Bidang { get; set; }
    public bool? IsApproved { get; set; }
}

public class ApproveUserRequest
{
    public int? BidangId { get; set; }
    public string? Bidang { get; set; }
}

public class UpdateProfileRequest
{
    public string? Username { get; set; }
    public string? Email { get; set; }
    public string? FullName { get; set; }
    public string? CurrentPassword { get; set; }
    public string? NewPassword { get; set; }
}
