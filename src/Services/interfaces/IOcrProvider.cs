namespace SIPENTA.Api.Services.Interfaces;

public interface IOcrProvider
{
    Task<OcrResult> ExtractTextAsync(Stream stream, CancellationToken cancellationToken = default);
}
