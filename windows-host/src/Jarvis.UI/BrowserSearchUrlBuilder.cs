namespace Jarvis.UI;

/// <summary>Creates a Google search URL without making an HTTP request from the host.</summary>
public static class BrowserSearchUrlBuilder
{
    /// <summary>Builds a Google results-page address with the query safely URL-encoded.</summary>
    public static Uri Build(string query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        return new Uri(
            $"https://www.google.com/search?q={Uri.EscapeDataString(query.Trim())}",
            UriKind.Absolute);
    }
}
