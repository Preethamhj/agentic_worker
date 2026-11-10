using System.Text.RegularExpressions;

namespace Jarvis.UI;

/// <summary>A parsed natural-language request to list Gmail messages.</summary>
public sealed record GmailPromptRequest(string Query, int MaxResults, bool TodayOnly);

/// <summary>Recognizes explicit mail-list and selected-mail explanation requests.</summary>
public static class GmailPromptParser
{
    private static readonly Regex ListVerb = new(
        @"\b(fetch|find|show|get|read|search|open|give|list|retrieve|check|look)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex MailNoun = new(
        @"\b(e-?mails?|gmail|mails?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Today = new(
        @"\b(today|today['’]s|todays)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Plural = new(
        @"\be-?mails\b|\bmails\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ExplainVerb = new(
        @"\b(explain|summari[sz]e|describe)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ExplainTarget = new(
        @"\b(mail|e-?mail|it|this|that|selected)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex FromClause = new(
        @"\bfrom\s+(?<v>""[^""]+""|\S+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex TopicClause = new(
        @"\b(?:about|regarding|subject|containing|with|on)\s+(?<v>.+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Returns true when the prompt asks Jarvis to list Gmail messages.</summary>
    public static bool TryParse(string prompt, out GmailPromptRequest request)
    {
        request = new GmailPromptRequest(string.Empty, 1, false);
        if (string.IsNullOrWhiteSpace(prompt)
            || !MailNoun.IsMatch(prompt)
            || !ListVerb.IsMatch(prompt))
            return false;

        var parts = new List<string>();
        var rest = prompt;
        var from = FromClause.Match(rest);
        if (from.Success)
        {
            var value = from.Groups["v"].Value.Trim('"', '.', ',', '?');
            if (value.Length > 0)
                parts.Add($"from:\"{EscapeQueryValue(value)}\"");
            rest = rest.Remove(from.Index, from.Length);
        }

        var topic = TopicClause.Match(rest);
        if (topic.Success)
        {
            var value = topic.Groups["v"].Value.Trim().Trim('"', '.', '?');
            if (value.Length > 0)
                parts.Add($"\"{EscapeQueryValue(value)}\"");
        }

        var todayOnly = Today.IsMatch(prompt);
        request = new GmailPromptRequest(
            string.Join(' ', parts),
            todayOnly ? 100 : Plural.IsMatch(prompt) ? 10 : 1,
            todayOnly);
        return true;
    }

    /// <summary>Returns true for an explicit request to explain or summarize a selected mail.</summary>
    public static bool IsExplainPrompt(string prompt) =>
        !string.IsNullOrWhiteSpace(prompt)
        && ExplainVerb.IsMatch(prompt)
        && ExplainTarget.IsMatch(prompt);

    private static string EscapeQueryValue(string value) =>
        value.Replace("\\", " ", StringComparison.Ordinal)
            .Replace("\"", " ", StringComparison.Ordinal)
            .Trim();
}
