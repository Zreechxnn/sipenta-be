using Microsoft.Extensions.Caching.Memory;
using SIAP.Api.Services.Interfaces;

namespace SIAP.Api.Services.Implementations;

public class LoginRateLimiter : ILoginRateLimiter
{
    private readonly IMemoryCache _cache;
    private const int MaxFailedAttempts = 5;
    private const int MaxIpFailedAttempts = 15;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan AttemptWindow = TimeSpan.FromMinutes(15);

    public LoginRateLimiter(IMemoryCache cache)
    {
        _cache = cache;
    }

    public Task<LockoutStatus> CheckLockoutAsync(string identifier, string ipAddress)
    {
        var (userAttemptKey, userLockoutKey, ipAttemptKey, ipLockoutKey) = GetKeys(identifier, ipAddress);

        // 1. Check if user is locked out
        if (_cache.TryGetValue(userLockoutKey, out DateTimeOffset userLockExpiry))
        {
            var remaining = (int)Math.Max(0, (userLockExpiry - DateTimeOffset.UtcNow).TotalSeconds);
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
            _cache.Remove(userLockoutKey);
            _cache.Remove(userAttemptKey);
        }

        // 2. Check if IP is locked out
        if (!string.IsNullOrEmpty(ipAddress) && _cache.TryGetValue(ipLockoutKey, out DateTimeOffset ipLockExpiry))
        {
            var remaining = (int)Math.Max(0, (ipLockExpiry - DateTimeOffset.UtcNow).TotalSeconds);
            if (remaining > 0)
            {
                return Task.FromResult(new LockoutStatus
                {
                    IsLockedOut = true,
                    RemainingSeconds = remaining,
                    RemainingAttempts = 0,
                    FailedAttempts = MaxIpFailedAttempts
                });
            }
            _cache.Remove(ipLockoutKey);
            _cache.Remove(ipAttemptKey);
        }

        // Check current user attempts count
        int currentAttempts = 0;
        if (_cache.TryGetValue(userAttemptKey, out int attempts))
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
        var (userAttemptKey, userLockoutKey, ipAttemptKey, ipLockoutKey) = GetKeys(identifier, ipAddress);

        // Record User Attempt
        int userAttempts = 0;
        if (_cache.TryGetValue(userAttemptKey, out int uAttempts))
        {
            userAttempts = uAttempts;
        }
        userAttempts++;

        // Record IP Attempt
        int ipAttempts = 0;
        if (_cache.TryGetValue(ipAttemptKey, out int iAttempts))
        {
            ipAttempts = iAttempts;
        }
        ipAttempts++;

        bool isUserLocked = userAttempts >= MaxFailedAttempts;
        bool isIpLocked = ipAttempts >= MaxIpFailedAttempts;

        if (isUserLocked)
        {
            var lockoutExpiry = DateTimeOffset.UtcNow.Add(LockoutDuration);
            _cache.Set(userLockoutKey, lockoutExpiry, LockoutDuration);
            _cache.Set(userAttemptKey, userAttempts, LockoutDuration);
        }
        else
        {
            _cache.Set(userAttemptKey, userAttempts, AttemptWindow);
        }

        if (isIpLocked)
        {
            var lockoutExpiry = DateTimeOffset.UtcNow.Add(LockoutDuration);
            _cache.Set(ipLockoutKey, lockoutExpiry, LockoutDuration);
            _cache.Set(ipAttemptKey, ipAttempts, LockoutDuration);
        }
        else
        {
            _cache.Set(ipAttemptKey, ipAttempts, AttemptWindow);
        }

        if (isUserLocked || isIpLocked)
        {
            return Task.FromResult(new LockoutStatus
            {
                IsLockedOut = true,
                RemainingSeconds = (int)LockoutDuration.TotalSeconds,
                RemainingAttempts = 0,
                FailedAttempts = userAttempts
            });
        }

        return Task.FromResult(new LockoutStatus
        {
            IsLockedOut = false,
            RemainingSeconds = 0,
            RemainingAttempts = Math.Max(0, MaxFailedAttempts - userAttempts),
            FailedAttempts = userAttempts
        });
    }

    public Task ResetAttemptsAsync(string identifier, string ipAddress)
    {
        var (userAttemptKey, userLockoutKey, ipAttemptKey, _) = GetKeys(identifier, ipAddress);
        _cache.Remove(userAttemptKey);
        _cache.Remove(userLockoutKey);
        _cache.Remove(ipAttemptKey);
        return Task.CompletedTask;
    }

    private static (string userAttemptKey, string userLockoutKey, string ipAttemptKey, string ipLockoutKey) GetKeys(string identifier, string ipAddress)
    {
        var cleanId = (identifier ?? string.Empty).Trim().ToLowerInvariant();
        var cleanIp = (ipAddress ?? "unknown").Trim().ToLowerInvariant();
        return (
            $"auth:attempts:user:{cleanId}",
            $"auth:lockout:user:{cleanId}",
            $"auth:attempts:ip:{cleanIp}",
            $"auth:lockout:ip:{cleanIp}"
        );
    }
}
