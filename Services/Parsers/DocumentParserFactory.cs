namespace SIAP.Api.Services.Parsers;

public interface IDocumentParserFactory
{
    IDocumentParser? GetParser(string extension, string mimeType);
}

public class DocumentParserFactory : IDocumentParserFactory
{
    private readonly IEnumerable<IDocumentParser> _parsers;

    public DocumentParserFactory(IEnumerable<IDocumentParser> parsers)
    {
        _parsers = parsers;
    }

    public IDocumentParser? GetParser(string extension, string mimeType)
    {
        return _parsers.FirstOrDefault(p => p.CanParse(extension, mimeType));
    }
}
