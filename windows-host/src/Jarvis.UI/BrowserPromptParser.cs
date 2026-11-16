using System.Text.RegularExpressions;

namespace Jarvis.UI;

/// <summary>Recognizes explicit browser-only open requests from the main prompt box.</summary>
public static partial class BrowserPromptParser
{
    private const int MaximumTargetLength = 512;
    private static readonly Regex SearchIntentPattern = new(
        @"\b(?:internet|online|web)\b|\bbrowser\b|\bgoogle\s+search\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex InternetSuffixPattern = new(
        @"\s+(?:on|in)\s+(?:the\s+)?(?:internet|web)\b.*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex BrowserInstructionPattern = new(
        @"\s+(?:use|using|with)\s+(?:the\s+)?browser(?:\s+feature)?\b.*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex SearchPrefixPattern = new(
        @"^\s*(?:(?:please\s+)?(?:google\s+search|find|search|look\s+up)\s+(?:me\s+)?(?:for\s+)?|(?:use|using)\s+(?:the\s+)?browser(?:\s+feature)?\s+to\s+(?:find|search|look\s+up)\s+(?:for\s+)?|browser\s+(?:search|find)\s+(?:for\s+)?)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex TrailingPunctuationPattern = new(
        @"[\s.!?]+$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    [GeneratedRegex(
        @"^\s*in\s+(?:the\s+)?browser(?:\s*,)?\s+open\s+(?<target>.+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BrowserPrefixPattern();

    [GeneratedRegex(
        @"^\s*(?:use\s+(?:the\s+)?browser(?:\s+to)?|browser)\s+open\s+(?<target>.+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BrowserOpenPattern();

    [GeneratedRegex(
        @"^\s*open\s+(?<target>.+?)\s+in\s+(?:the\s+)?browser\s*[.!?]?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BrowserSuffixPattern();

    /// <summary>Extracts the website or web-app name from an explicit browser-open prompt.</summary>
    public static bool TryParse(string? prompt, out string target)
    {
        target = string.Empty;
        if (string.IsNullOrWhiteSpace(prompt))
            return false;

        var match = BrowserPrefixPattern().Match(prompt);
        if (!match.Success)
            match = BrowserOpenPattern().Match(prompt);
        if (!match.Success)
            match = BrowserSuffixPattern().Match(prompt);
        if (!match.Success)
            return false;

        var candidate = match.Groups["target"].Value.Trim().Trim('"', '\'');
        var websiteMatch = ExplicitWebsitePattern().Match(candidate);
        if (websiteMatch.Success
            && BrowserAddressPolicy.LooksLikeExplicitAddress(websiteMatch.Groups["address"].Value))
            candidate = websiteMatch.Groups["address"].Value;
        if (candidate.Length is 0 or > MaximumTargetLength)
            return false;

        target = candidate;
        return true;
    }

    [GeneratedRegex(
        @"^(?:the\s+)?(?<address>\S+)\s+(?:website|web\s+site)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ExplicitWebsitePattern();

    /// <summary>Extracts a query only when the prompt clearly requests an internet/browser search.</summary>
    public static bool TryParseSearch(string? prompt, out string query)
    {
        query = string.Empty;
        if (string.IsNullOrWhiteSpace(prompt) || !SearchIntentPattern.IsMatch(prompt))
            return false;

        var candidate = InternetSuffixPattern.Replace(prompt.Trim(), string.Empty);
        candidate = BrowserInstructionPattern.Replace(candidate, string.Empty);
        candidate = SearchPrefixPattern.Replace(candidate, string.Empty);
        candidate = TrailingPunctuationPattern.Replace(candidate, string.Empty).Trim();
        if (candidate.Length is 0 or > MaximumTargetLength)
            return false;
        if (TryParse(candidate, out _))
            return false;

        query = candidate;
        return true;
    }
}
