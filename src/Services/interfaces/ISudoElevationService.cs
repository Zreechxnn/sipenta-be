namespace SIAP.Api.Services.Interfaces;

public interface ISudoElevationService
{
    Task<(bool Success, string SudoToken, DateTime ExpiresAt, string Message)> ElevateAsync(Guid userId, string password);
    bool ValidateSudoToken(Guid userId, string? sudoToken);
    (bool Elevated, int SecondsRemaining, DateTime? ExpiresAt) CheckElevation(Guid userId, string? sudoToken);
    void RevokeSudoToken(string? sudoToken);
}
