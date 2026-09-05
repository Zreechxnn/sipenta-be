using Microsoft.Extensions.Options;
using SIAP.Api.Configurations;
using SIAP.Api.Services.Chunking.Interfaces;

namespace SIAP.Api.Services.Chunking.Implementations;

public class ChunkStrategyFactory : IChunkStrategyFactory
{
    private readonly IEnumerable<IChunkStrategy> _strategies;
    private readonly ChunkOptions _options;

    public ChunkStrategyFactory(IEnumerable<IChunkStrategy> strategies, IOptions<ChunkOptions> options)
    {
        _strategies = strategies;
        _options = options.Value;
    }

    public IChunkStrategy GetStrategy(string? strategyName = null)
    {
        var targetStrategy = string.IsNullOrWhiteSpace(strategyName) ? _options.DefaultStrategy : strategyName;

        var strategy = _strategies.FirstOrDefault(s => s.StrategyName.Equals(targetStrategy, StringComparison.OrdinalIgnoreCase));
        
        if (strategy == null)
        {
            throw new ArgumentException($"Chunk strategy '{targetStrategy}' not found.");
        }

        return strategy;
    }
}
