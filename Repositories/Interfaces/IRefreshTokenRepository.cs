using SIAP.Api.Entities;

namespace SIAP.Api.Repositories.Interfaces;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByTokenAsync(string token);
    Task<IEnumerable<RefreshToken>> GetActiveByUserIdAsync(Guid userId);
    Task AddAsync(RefreshToken refreshToken);
    Task UpdateAsync(RefreshToken refreshToken);
    Task RevokeTokenAsync(string token, string? ipAddress = null);
    Task RevokeAllUserTokensAsync(Guid userId, string? ipAddress = null);
    Task<int> CleanupStaleTokensAsync(TimeSpan? revokedGracePeriod = null, CancellationToken cancellationToken = default);
}
