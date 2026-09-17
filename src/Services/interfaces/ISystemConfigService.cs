using SIAP.Api.DTOs;

namespace SIAP.Api.Services.Interfaces;

public interface ISystemConfigService
{
    Task<ConfigurationOverviewDto> GetOverviewAsync();
    
    Task<LlmConfigListDto> GetLlmConfigsAsync();
    Task SaveLlmConfigsAsync(LlmConfigListDto config, string? updatedBy = null);
    Task<LlmTestResponseDto> TestLlmEndpointAsync(LlmTestRequestDto request);
    
    Task<StorageConfigDto> GetStorageConfigAsync();
    Task SaveStorageConfigAsync(StorageConfigDto config, string? updatedBy = null);
    Task<StorageTestResponseDto> TestStorageAsync(StorageConfigDto? testConfig = null);
    
    Task<DatabaseConfigDto> GetDatabaseConfigAsync();
    Task<DatabaseTestResponseDto> TestDatabaseAsync(DatabaseTestRequestDto request);
    Task SaveDatabaseConfigAsync(DatabaseConfigDto request, string? updatedBy = null);
    
    Task EnsureAllConfigurationsEncryptedAsync();
}
