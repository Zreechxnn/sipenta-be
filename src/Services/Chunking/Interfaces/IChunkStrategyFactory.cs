using SIAP.Api.Services.Chunking.Interfaces;

namespace SIAP.Api.Services.Chunking.Interfaces;

public interface IChunkStrategyFactory
{
    IChunkStrategy GetStrategy(string? strategyName = null);
}
