namespace SIAP.Api.Configurations;

public class ChunkOptions
{
    public string DefaultStrategy { get; set; } = "Paragraph";
    public int MaxCharacters { get; set; } = 1000;
    public int MaxTokens { get; set; } = 250;
    public int OverlapCharacters { get; set; } = 100;
}
