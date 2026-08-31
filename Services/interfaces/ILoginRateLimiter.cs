namespace SIAP.Api.Services.Interfaces;

public class LockoutStatus
{
    public bool IsLockedOut { get; set; }
    public int RemainingSeconds { get; set; }
    public int RemainingAttempts { get; set; }
    public int FailedAttempts { get; set; }
}

public interface ILoginRateLimiter
{
    Task<LockoutStatus> CheckLockoutAsync(string identifier, string ipAddress);
    Task<LockoutStatus> RecordFailedAttemptAsync(string identifier, string ipAddress);
    Task ResetAttemptsAsync(string identifier, string ipAddress);
}
