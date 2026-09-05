namespace SIAP.Api.Configurations;

public class JwtOptions
{
    public string Key { get; set; } = null!;
    public string Issuer { get; set; } = null!;
    public string Audience { get; set; } = null!;
    public int ExpiryMinutes { get; set; } = 30;
    public int RefreshTokenExpiryDays { get; set; } = 7;
}
