using Microsoft.Extensions.Caching.Memory;
using SIAP.Api.Services.Interfaces;

namespace SIAP.Api.Services.Implementations;

public class LoginRateLimiter : ILoginRateLimiter
{
    private readonly IMemoryCache _cache;
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan AttemptWindow = TimeSpan.FromMinutes(15);

    public LoginRateLimiter(IMemoryCache cache)
    {
        _cache = cache;
    }

    public Task<LockoutStatus> CheckLockoutAsync(string identifier, string ipAddress)
    {
        var (attemptKey, lockoutKey) = GetKeys(identifier, ipAddress);

        // Check if actively locked out
        if (_cache.TryGetValue(lockoutKey, out DateTimeOffset lockoutExpiry))
        {
            var remaining = (int)Math.Max(0, (lockoutExpiry - DateTimeOffset.UtcNow).TotalSeconds);
            if (remaining > 0)
            {
                return Task.FromResult(new LockoutStatus
                {
                    IsLockedOut = true,
                    RemainingSeconds = remaining,
                    RemainingAttempts = 0,
                    FailedAttempts = MaxFailedAttempts
                });
            }
            else
            {
                // Lockout expired
                _cache.Remove(lockoutKey);
                _cache.Remove(attemptKey);
            }
        }

        // Check current attempts count
        int currentAttempts = 0;
        if (_cache.TryGetValue(attemptKey, out int attempts))
        {
            currentAttempts = attempts;
        }

        return Task.FromResult(new LockoutStatus
        {
            IsLockedOut = false,
            RemainingSeconds = 0,
            RemainingAttempts = Math.Max(0, MaxFailedAttempts - currentAttempts),
            FailedAttempts = currentAttempts
        });
    }

    public Task<LockoutStatus> RecordFailedAttemptAsync(string identifier, string ipAddress)
    {
        var (attemptKey, lockoutKey) = GetKeys(identifier, ipAddress);

        int currentAttempts = 0;
        if (_cache.TryGetValue(attemptKey, out int attempts))
        {
            currentAttempts = attempts;
        }

        currentAttempts++;

        if (currentAttempts >= MaxFailedAttempts)
        {
            var lockoutExpiry = DateTimeOffset.UtcNow.Add(LockoutDuration);
            _cache.Set(lockoutKey, lockoutExpiry, LockoutDuration);
            _cache.Set(attemptKey, currentAttempts, LockoutDuration);

            return Task.FromResult(new LockoutStatus
            {
                IsLockedOut = true,
                RemainingSeconds = (int)LockoutDuration.TotalSeconds,
                RemainingAttempts = 0,
                FailedAttempts = currentAttempts
            });
        }
        else
        {
            _cache.Set(attemptKey, currentAttempts, AttemptWindow);

            return Task.FromResult(new LockoutStatus
            {
                IsLockedOut = false,
                RemainingSeconds = 0,
                RemainingAttempts = MaxFailedAttempts - currentAttempts,
                FailedAttempts = currentAttempts
            });
        }
    }

    public Task ResetAttemptsAsync(string identifier, string ipAddress)
    {
        var (attemptKey, lockoutKey) = GetKeys(identifier, ipAddress);
        _cache.Remove(attemptKey);
        _cache.Remove(lockoutKey);
        return Task.CompletedTask;
    }

    private static (string attemptKey, string lockoutKey) GetKeys(string identifier, string ipAddress)
    {
        var cleanId = (identifier ?? string.Empty).Trim().ToLowerInvariant();
        var cleanIp = (ipAddress ?? "unknown").Trim().ToLowerInvariant();
        return ($"auth:attempts:{cleanId}:{cleanIp}", $"auth:lockout:{cleanId}:{cleanIp}");
    }
}
