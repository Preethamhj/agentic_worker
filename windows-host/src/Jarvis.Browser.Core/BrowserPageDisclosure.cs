using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Jarvis.Browser.Core;

/// <summary>Reasons a disclosed link can or cannot be used from a fresh observation.</summary>
public enum BrowserLinkFreshness
{
    /// <summary>The page and link match the disclosed observation.</summary>
    Current,
    /// <summary>The tab, document, or document version changed or is unavailable.</summary>
    PageChanged,
    /// <summary>The link does not belong to the approved disclosure.</summary>
    UnknownLink,
    /// <summary>The original element is no longer present in the observation.</summary>
    TargetMissing,
    /// <summary>The element is no longer visible or enabled.</summary>
    TargetUnavailable,
    /// <summary>The element's role, label, or destination changed.</summary>
    TargetChanged
}

/// <summary>A temporary page link with local-only target and observation details.</summary>
public sealed record BrowserDisclosureLink(
    string Id,
    string Label,
    Uri Target,
    string ElementId,
    string FrameId,
    string DocumentToken,
    long DomVersion,
    string SourceLabelFingerprint);

/// <summary>A bounded page projection that contains no page URLs or element attributes.</summary>
public sealed record BrowserPageDisclosure(
    Guid ObservationId,
    BrowserTabId TabId,
    string MainFrameId,
    string DocumentToken,
    long DomVersion,
    string Title,
    string VisibleText,
    IReadOnlyList<BrowserDisclosureLink> Links,
    IReadOnlyList<string> Warnings)
{
    private static readonly Regex SensitiveParameterName = new(
        @"(?i)^(?:password|passwd|passcode|secret|client[_-]?secret|access[_-]?token|refresh[_-]?token|id[_-]?token|token|jwt|api[_-]?key|authorization|cookie|session(?:[_-]?id)?|auth(?:entication)?[_-]?code|verification[_-]?code|code)$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex CredentialMarker = new(
        @"(?i)\b(?:password|passwd|passcode|secret|client[_ -]?secret|credential|access[_ -]?token|refresh[_ -]?token|id[_ -]?token|token|jwt|api[_ -]?key|authorization|cookie|session[_ -]?id|auth(?:entication)?[_ -]?code|verification[_ -]?code)\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex OpaqueSecret = new(
        @"\b(?:(?=[A-Za-z0-9_+/=-]{24,}\b)(?=[A-Za-z0-9_+/=-]*[A-Z])(?=[A-Za-z0-9_+/=-]*[a-z])(?=[A-Za-z0-9_+/=-]*\d)[A-Za-z0-9_+/=-]{24,}|(?=[A-Za-z0-9_-]{32,}\b)(?=[A-Za-z0-9_-]*\d)[A-Za-z0-9_-]{32,})\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex UrlLike = new(
        @"(?i)\b(?:https?://|www\.)\S+|\b(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,}(?::\d+)?(?:/[^\s]*)?(?:\?[^\s]*)?(?:#[^\s]*)?|\b(?:\d{1,3}\.){3}\d{1,3}(?::\d+)?(?:/[^\s]*)?",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>Builds a bounded disclosure projection from one active observation.</summary>
    public static BrowserPageDisclosure Create(ObservationResult observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        var snapshot = observation.Snapshot;
        if (!snapshot.IsActive)
            throw new InvalidOperationException("The page observation is no longer active.");
        if (snapshot.IsLoading)
            throw new InvalidOperationException("The page is still loading.");

        if (LooksCredentialLike(snapshot.Title)
            || LooksCredentialLike(snapshot.VisibleText)
            || snapshot.Elements.Any(element =>
                string.Equals(element.Role, "link", StringComparison.OrdinalIgnoreCase)
                && LooksCredentialLike(element.Name)))
            throw new InvalidOperationException("Potential credential content was found; page disclosure was blocked.");

        var title = Sanitize(snapshot.Title, 500);
        var text = Sanitize(snapshot.VisibleText);
        var mainFrame = snapshot.Frames.Single(frame => frame.IsMainFrame);
        var warnings = new List<string>();
        if (snapshot.IsTruncated)
            warnings.Add("The snapshot is truncated; only part of the page is included in the disclosure.");
        if (observation.Issues.Count != 0)
            warnings.Add("Some page frames could not be captured and are not included in the disclosure.");
        var links = snapshot.Elements
            .Where(element =>
                element.FrameId == mainFrame.FrameId
                && string.Equals(element.Role, "link", StringComparison.OrdinalIgnoreCase)
                && element.IsVisible
                && element.IsEnabled)
            .Select(element => (Element: element, Target: GetWebTarget(element), Label: Sanitize(element.Name, 500)))
            .Where(item => item.Target is not null && !string.IsNullOrWhiteSpace(item.Label))
            .Select(item => new BrowserDisclosureLink(
                CreateTemporaryId(),
                item.Label,
                item.Target!,
                item.Element.ElementId,
                item.Element.FrameId,
                item.Element.DocumentToken,
                item.Element.DomVersion,
                FingerprintText(item.Element.Name)))
            .ToArray();

        if (ContainsCredentialMarker(title)
            || ContainsCredentialMarker(text)
            || links.Any(link => ContainsCredentialMarker(link.Label)))
            throw new InvalidOperationException("Potential credential content was found; page disclosure was blocked.");

        return new BrowserPageDisclosure(
            snapshot.ObservationId,
            snapshot.TabId,
            mainFrame.FrameId,
            mainFrame.DocumentToken,
            mainFrame.DomVersion,
            title,
            text,
            Array.AsReadOnly(links),
            Array.AsReadOnly(warnings.ToArray()));
    }

    /// <summary>Returns the exact text-and-link-label projection suitable for a user preview.</summary>
    public string FormatPreview()
    {
        var output = new StringBuilder();
        foreach (var warning in Warnings)
            output.AppendLine($"Notice: {warning}");
        output.AppendLine($"Title: {Title}");
        output.AppendLine("Visible text:");
        output.AppendLine(VisibleText);
        output.AppendLine("Visible links (temporary ID and label only):");
        foreach (var link in Links)
            output.AppendLine($"[{link.Id}] {link.Label}");
        return output.ToString();
    }

    /// <summary>Returns the temporary link selected by the planner, if it belongs to this observation.</summary>
    public BrowserDisclosureLink? FindLink(string id) =>
        Links.SingleOrDefault(link => string.Equals(link.Id, id, StringComparison.Ordinal));

    /// <summary>Reports whether the selected label maps to multiple different destinations.</summary>
    public bool IsAmbiguous(BrowserDisclosureLink link)
    {
        ArgumentNullException.ThrowIfNull(link);
        if (FindLink(link.Id) != link)
            return true;
        var normalizedLabel = NormalizeLabel(link.Label);
        return Links.Any(candidate =>
            candidate.Id != link.Id
            && NormalizeLabel(candidate.Label) == normalizedLabel
            && !string.Equals(candidate.Target.AbsoluteUri, link.Target.AbsoluteUri, StringComparison.Ordinal));
    }

    /// <summary>Checks that a temporary link still resolves to its exact visible target.</summary>
    public bool IsLinkCurrent(ObservationResult observation, BrowserDisclosureLink link)
        => CheckLinkFreshness(observation, link) == BrowserLinkFreshness.Current;

    /// <summary>Checks link freshness and reports which validation category failed.</summary>
    public BrowserLinkFreshness CheckLinkFreshness(ObservationResult observation, BrowserDisclosureLink link)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(link);
        if (!IsPageCurrent(observation))
            return BrowserLinkFreshness.PageChanged;
        if (FindLink(link.Id) != link)
            return BrowserLinkFreshness.UnknownLink;

        var frame = observation.Snapshot.Frames.SingleOrDefault(candidate =>
            candidate.FrameId == link.FrameId && candidate.IsMainFrame);
        var element = observation.Snapshot.Elements.SingleOrDefault(candidate =>
            candidate.ElementId == link.ElementId
            && candidate.FrameId == link.FrameId
            && candidate.DocumentToken == link.DocumentToken
            && candidate.DomVersion == link.DomVersion);
        if (frame is null || frame.DocumentToken != link.DocumentToken || frame.DomVersion != link.DomVersion)
            return BrowserLinkFreshness.PageChanged;
        if (element is null)
            return BrowserLinkFreshness.TargetMissing;
        if (!element.IsVisible || !element.IsEnabled)
            return BrowserLinkFreshness.TargetUnavailable;
        if (!string.Equals(element.Role, "link", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Sanitize(element.Name, 500), link.Label, StringComparison.Ordinal)
            || !string.Equals(FingerprintText(element.Name), link.SourceLabelFingerprint, StringComparison.Ordinal)
            || GetWebTarget(element) is not { } target
            || !string.Equals(target.AbsoluteUri, link.Target.AbsoluteUri, StringComparison.Ordinal))
            return BrowserLinkFreshness.TargetChanged;
        return BrowserLinkFreshness.Current;
    }

    /// <summary>Checks an element whose live identity was independently bound by the browser adapter.</summary>
    public BrowserLinkFreshness CheckBoundLink(
        ObservationResult observation,
        BrowserDisclosureLink link,
        ElementReference element)
    {
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(element);
        var snapshot = observation.Snapshot;
        if (!snapshot.IsActive || snapshot.IsLoading || snapshot.TabId != TabId
            || !snapshot.Frames.Any(frame => frame.IsMainFrame && frame.FrameId == MainFrameId
                && frame.DocumentToken == DocumentToken))
            return BrowserLinkFreshness.PageChanged;
        if (FindLink(link.Id) != link)
            return BrowserLinkFreshness.UnknownLink;
        if (!snapshot.ContainsReference(element) || element.FrameId != MainFrameId
            || element.DocumentToken != DocumentToken)
            return BrowserLinkFreshness.TargetMissing;
        if (!element.IsVisible || !element.IsEnabled)
            return BrowserLinkFreshness.TargetUnavailable;
        if (!string.Equals(element.Role, "link", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Sanitize(element.Name, 500), link.Label, StringComparison.Ordinal)
            || !string.Equals(FingerprintText(element.Name), link.SourceLabelFingerprint, StringComparison.Ordinal)
            || GetWebTarget(element) is not { } target
            || !string.Equals(target.AbsoluteUri, link.Target.AbsoluteUri, StringComparison.Ordinal))
            return BrowserLinkFreshness.TargetChanged;
        return BrowserLinkFreshness.Current;
    }

    /// <summary>Checks that the captured main document and DOM version still match the active page.</summary>
    public bool IsPageCurrent(ObservationResult observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (observation.Snapshot.IsLoading
            || !observation.Snapshot.IsActive
            || observation.Snapshot.TabId != TabId)
            return false;

        var frame = observation.Snapshot.Frames.SingleOrDefault(candidate =>
            candidate.IsMainFrame && candidate.FrameId == MainFrameId);
        return frame is not null
            && frame.DocumentToken == DocumentToken
            && frame.DomVersion == DomVersion;
    }

    /// <summary>Hashes a target without placing its URL in planner content or authorization arguments.</summary>
    public static string Fingerprint(Uri target)
    {
        ArgumentNullException.ThrowIfNull(target);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(target.AbsoluteUri));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static Uri? GetWebTarget(ElementReference element)
    {
        if (!element.Attributes.TryGetValue("href", out var value)
            || !Uri.TryCreate(value, UriKind.Absolute, out var target)
            || (target.Scheme != Uri.UriSchemeHttp && target.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrWhiteSpace(target.Host)
            || !string.IsNullOrEmpty(target.UserInfo)
            || HasCredentialParameter(target.Query)
            || HasCredentialParameter(target.Fragment))
            return null;
        return target;
    }

    private static bool HasCredentialParameter(string value)
    {
        return value.TrimStart('?', '#')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(parameter => Uri.UnescapeDataString(parameter.Split('=', 2)[0]))
            .Any(name => SensitiveParameterName.IsMatch(name));
    }

    private static string Sanitize(string value, int maximumLength = BrowserObservationMapper.MaximumVisibleTextLength)
    {
        var normalized = Regex.Replace(value, @"\s+", " ").Trim();
        normalized = Regex.Replace(normalized, @"(?i)\bBearer\s+[A-Za-z0-9._~+/-]+=*", "[REDACTED]");
        normalized = Regex.Replace(
            normalized,
            @"(?i)\b(?<key>password|passwd|passcode|secret|access[_ -]?token|refresh[_ -]?token|api[_ -]?key|authorization|cookie|session[_ -]?id)\b\s*[:=]\s*[^\s,;]+",
            match => $"{match.Groups["key"].Value}=[REDACTED]");
        normalized = Regex.Replace(
            normalized,
            @"\b[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\b",
            "[REDACTED]");
        normalized = UrlLike.Replace(normalized, "[URL REDACTED]");
        if (normalized.Length <= maximumLength)
            return normalized;
        var length = maximumLength;
        if (char.IsHighSurrogate(normalized[length - 1]) && char.IsLowSurrogate(normalized[length]))
            length--;
        return normalized[..length];
    }

    private static bool ContainsCredentialMarker(string value) =>
        CredentialMarker.IsMatch(value) || OpaqueSecret.IsMatch(value);

    private static bool LooksCredentialLike(string value) =>
        CredentialMarker.IsMatch(value) || OpaqueSecret.IsMatch(value);

    private static string CreateTemporaryId() => $"l{Guid.NewGuid():N}"[..13];

    private static string FingerprintText(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string NormalizeLabel(string label) =>
        string.Join(' ', label.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();
}
