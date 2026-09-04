using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using SIAP.Api.Repositories.Interfaces;

namespace SIAP.Api.Services.Implementations;

/// <summary>
/// Background Service yang secara otomatis dan berkala membersihkan refresh token yang telah
/// kedaluwarsa (expired) atau telah dicabut (revoked) di luar batas waktu retensi keamanan (grace period).
/// Mencegah penumpukan data sampah pada tabel RefreshTokens sekaligus mempertahankan jendela deteksi serangan (Token Reuse Detection).
/// </summary>
public class TokenCleanupBackgroundService : BackgroundService
{
    private readonly ILogger<TokenCleanupBackgroundService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeSpan _checkInterval = TimeSpan.FromHours(6); // Dijalankan tiap 6 jam
    private readonly TimeSpan _initialDelay = TimeSpan.FromSeconds(15); // Jeda startup aplikasi
    private readonly TimeSpan _revokedGracePeriod = TimeSpan.FromHours(24); // Retensi 24 jam untuk token revoked

    public TokenCleanupBackgroundService(
        ILogger<TokenCleanupBackgroundService> logger,
        IServiceScopeFactory scopeFactory)
    {
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TokenCleanupBackgroundService diinisialisasi (Interval pembersihan: setiap {Hours} jam, Grace Period: {GraceHours} jam).", 
            _checkInterval.TotalHours, _revokedGracePeriod.TotalHours);

        try
        {
            // Beri jeda sejenak setelah startup aplikasi selesai
            await Task.Delay(_initialDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PerformCleanupAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Terjadi kesalahan saat menjalankan background cleanup refresh token.");
            }

            try
            {
                await Task.Delay(_checkInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("TokenCleanupBackgroundService telah dihentikan.");
    }

    private async Task PerformCleanupAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Memulai pembersihan berkala refresh token kedaluwarsa & revoked...");

        using var scope = _scopeFactory.CreateScope();
        var refreshTokenRepository = scope.ServiceProvider.GetRequiredService<IRefreshTokenRepository>();

        var deletedCount = await refreshTokenRepository.CleanupStaleTokensAsync(_revokedGracePeriod, stoppingToken);

        if (deletedCount > 0)
        {
            _logger.LogInformation("Pembersihan token selesai. Berhasil menghapus {Count} baris refresh token basi dari database.", deletedCount);
        }
        else
        {
            _logger.LogInformation("Pembersihan token selesai. Tidak ada refresh token basi yang memenuhi kriteria penghapusan.");
        }
    }
}
