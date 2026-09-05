namespace SIAP.Api.Entities;

public class ChunkMetadata
{
    // Local Metadata
    public string? Bab { get; set; }
    public string? Pasal { get; set; }
    public string? Ayat { get; set; }
    public string? Heading { get; set; }
    public int? StartPage { get; set; }
    public int? EndPage { get; set; }

    public ChunkMetadata Clone()
    {
        return (ChunkMetadata)this.MemberwiseClone();
    }
}
