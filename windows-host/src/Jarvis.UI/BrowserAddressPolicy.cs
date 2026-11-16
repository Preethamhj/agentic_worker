using System.Text.RegularExpressions;

namespace Jarvis.UI;

/// <summary>Validates addresses that may be loaded by the embedded browser.</summary>
public static class BrowserAddressPolicy
{
    private static readonly Regex ExplicitScheme = new(
        @"^[A-Za-z][A-Za-z0-9+.-]*:",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex HostPort = new(
        @"^[^:/?#\s]+:\d+(?:[/;?#].*)?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Identifies an explicit URL or hostname rather than a natural-language site name.</summary>
    public static bool LooksLikeExplicitAddress(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var candidate = value.Trim();
        if (ExplicitScheme.IsMatch(candidate))
            return true;
        if (candidate.Any(char.IsWhiteSpace))
            return false;

        var endOfHost = candidate.IndexOfAny(['/', '?', '#', ':']);
        var host = endOfHost < 0 ? candidate : candidate[..endOfHost];
        return host.Contains('.', StringComparison.Ordinal)
            || string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Normalizes a user-entered address to an absolute HTTP or HTTPS URI.
    /// Addresses without a scheme use HTTPS.
    /// </summary>
    public static bool TryNormalize(string? value, out Uri? address, out string error)
    {
        address = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "Enter a website address.";
            return false;
        }

        var candidate = value.Trim();
        if (!candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (ExplicitScheme.IsMatch(candidate) && !HostPort.IsMatch(candidate))
            {
                error = "Only HTTP and HTTPS website addresses are supported.";
                return false;
            }

            candidate = $"https://{candidate}";
        }

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var parsed)
            || string.IsNullOrWhiteSpace(parsed.Host))
        {
            error = "The website address is invalid.";
            return false;
        }

        if (parsed.Scheme is not ("http" or "https"))
        {
            error = "Only HTTP and HTTPS website addresses are supported.";
            return false;
        }

        if (!string.IsNullOrEmpty(parsed.UserInfo))
        {
            error = "Website addresses cannot include embedded credentials.";
            return false;
        }

        address = parsed;
        return true;
    }

    /// <summary>Returns whether a navigation target is a permitted web URI.</summary>
    public static bool IsWebAddress(string? value) =>
        TryNormalize(value, out var address, out _)
        && address is not null
        && (value!.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
}
