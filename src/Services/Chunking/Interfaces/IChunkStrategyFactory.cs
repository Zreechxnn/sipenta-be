using SIPENTA.Api.Services.Chunking.Interfaces;

namespace SIPENTA.Api.Services.Chunking.Interfaces;

public interface IChunkStrategyFactory
{
    IChunkStrategy GetStrategy(string? strategyName = null);
}
