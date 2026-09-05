using System.Threading.Channels;
using SIAP.Api.Services.Interfaces;

namespace SIAP.Api.Services.Implementations;

public class DocumentProcessingQueue : IDocumentProcessingQueue
{
    private readonly Channel<Guid> _queue;

    public DocumentProcessingQueue()
    {
        var options = new BoundedChannelOptions(1000)
        {
            FullMode = BoundedChannelFullMode.Wait
        };
        _queue = Channel.CreateBounded<Guid>(options);
    }

    public async ValueTask EnqueueAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        await _queue.Writer.WriteAsync(documentId, cancellationToken);
    }

    public async ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken = default)
    {
        return await _queue.Reader.ReadAsync(cancellationToken);
    }

    public int Count()
    {
        return _queue.Reader.Count;
    }
}
