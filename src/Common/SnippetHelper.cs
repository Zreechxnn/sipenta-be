namespace SIAP.Api.Common;

public static class SnippetHelper
{
    public static string GetSnippet(string? rawText, string? keyword, int charsBefore = 150, int charsAfter = 250, int defaultLength = 500)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return string.Empty;

        if (string.IsNullOrWhiteSpace(keyword))
        {
            return rawText.Length > defaultLength ? rawText.Substring(0, defaultLength) : rawText;
        } 

        int index = rawText.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);

        if (index == -1)
        {
            return rawText.Length > defaultLength ? rawText.Substring(0, defaultLength) : rawText;
        }

        int startIndex = Math.Max(0, index - charsBefore);
        int endIndex = Math.Min(rawText.Length, index + keyword.Length + charsAfter);
        int length = endIndex - startIndex;

        string snippet = rawText.Substring(startIndex, length);

        if (startIndex > 0)
        {
            snippet = "..." + snippet;
        }

        if (endIndex < rawText.Length)
        {
            snippet = snippet + "...";
        }

        return snippet;
    }
}
