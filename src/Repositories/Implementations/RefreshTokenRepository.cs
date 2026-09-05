using Microsoft.EntityFrameworkCore;
using SIAP.Api.Data;
using SIAP.Api.Entities;
using SIAP.Api.Repositories.Interfaces;

namespace SIAP.Api.Repositories.Implementations;

public class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly AppDbContext _context;

    public RefreshTokenRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<RefreshToken?> GetByTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        return await _context.RefreshTokens
            .Include(r => r.User)
                .ThenInclude(u => u.Role)
            .Include(r => r.User)
                .ThenInclude(u => u.Bidang)
            .FirstOrDefaultAsync(r => r.Token == token);
    }

    public async Task<IEnumerable<RefreshToken>> GetActiveByUserIdAsync(Guid userId)
    {
        var now = DateTime.UtcNow;
        return await _context.RefreshTokens
            .Where(r => r.UserId == userId && r.RevokedAt == null && r.ExpiresAt > now)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
    }

    public async Task AddAsync(RefreshToken refreshToken)
    {
        await _context.RefreshTokens.AddAsync(refreshToken);
        await _context.SaveChangesAsync();
    }

    public async Task UpdateAsync(RefreshToken refreshToken)
    {
        _context.RefreshTokens.Update(refreshToken);
        await _context.SaveChangesAsync();
    }

    public async Task RevokeTokenAsync(string token, string? ipAddress = null)
    {
        if (string.IsNullOrWhiteSpace(token)) return;

        var existingToken = await _context.RefreshTokens
            .FirstOrDefaultAsync(r => r.Token == token);

        if (existingToken != null && existingToken.RevokedAt == null)
        {
            existingToken.RevokedAt = DateTime.UtcNow;
            existingToken.RevokedByIp = ipAddress;
            _context.RefreshTokens.Update(existingToken);
            await _context.SaveChangesAsync();
        }
    }

    public async Task RevokeAllUserTokensAsync(Guid userId, string? ipAddress = null)
    {
        var now = DateTime.UtcNow;
        await _context.RefreshTokens
            .Where(r => r.UserId == userId && r.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.RevokedAt, now)
                .SetProperty(r => r.RevokedByIp, ipAddress));
    }

    public async Task<int> CleanupStaleTokensAsync(TimeSpan? revokedGracePeriod = null, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var grace = revokedGracePeriod ?? TimeSpan.FromDays(1);
        var revokedCutoff = now - grace;

        return await _context.RefreshTokens
            .Where(r => r.ExpiresAt <= now || (r.RevokedAt != null && r.RevokedAt <= revokedCutoff))
            .ExecuteDeleteAsync(cancellationToken);
    }
}
