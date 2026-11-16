using System.Text.Json.Nodes;
using Jarvis.Contracts;
using Jarvis.Infrastructure;
using Jarvis.Safety;
using Jarvis.Verification;

namespace Jarvis.Composition;

/// <summary>Outcome of asking the user to approve a typed browser operation.</summary>
public sealed record BrowserActionAuthorizationResult(
    bool Approved,
    AuthorizationRecordDto? Record,
    string Message);

/// <summary>Opaque pre-action browser state handed back to the verifier.</summary>
public sealed class BrowserBaseline
{
    internal BrowserBaseline(BrowserSearchBaseline inner) => Inner = inner;
    internal BrowserSearchBaseline Inner { get; }
}

/// <summary>Opaque pre-action browser state for a signed link operation.</summary>
public sealed class BrowserLinkBaseline
{
    internal BrowserLinkBaseline(BrowserOpenLinkBaseline inner) => Inner = inner;
    internal BrowserOpenLinkBaseline Inner { get; }
}

/// <summary>Minimal verification outcome exposed to the UI without verifier implementation types.</summary>
public sealed record BrowserActionVerificationResult(string Status, IReadOnlyList<string> Reasons);

/// <summary>Arguments recovered from a consumed signed record; the only input an executor may act on.</summary>
public sealed record ConsumedBrowserSearch(string AuthId, string Query);

/// <summary>Arguments recovered from a consumed signed link-opening record.</summary>
public sealed class ConsumedBrowserLink
{
    internal ConsumedBrowserLink(
        string authId,
        string linkId,
        string observationId,
        string tabId,
        string targetHash,
        string targetHost,
        string mode)
    {
        AuthId = authId;
        LinkId = linkId;
        ObservationId = observationId;
        TabId = tabId;
        TargetHash = targetHash;
        TargetHost = targetHost;
        Mode = mode;
    }

    /// <summary>Gets the consumed authorization identifier.</summary>
    public string AuthId { get; }

    /// <summary>Gets the approved temporary link identifier.</summary>
    public string LinkId { get; }

    /// <summary>Gets the observation that the user approved.</summary>
    public string ObservationId { get; }

    /// <summary>Gets the browser tab that the user approved.</summary>
    public string TabId { get; }

    /// <summary>Gets the approved target fingerprint.</summary>
    public string TargetHash { get; }

    /// <summary>Gets the approved destination host.</summary>
    public string TargetHost { get; }

    /// <summary>Gets the signed destination mode.</summary>
    public string Mode { get; }
}

/// <summary>
/// Routes registered browser operations through ToolPolicy, user approval and signed
/// AuthorizationRecords. Holds the one AuthorizationService that issued each record so
/// the later consume step is checked against the same single-use lifecycle.
/// </summary>
public sealed class BrowserActionAuthority : IDisposable
{
    private const string SearchToolId = "browser.search";
    private const string OpenLinkToolId = "browser.open_link";
    private readonly SafetyGate _gate;
    private readonly AuthorizationService _authorization;
    private readonly BrowserSearchVerifier _verifier;
    private readonly BrowserOpenLinkVerifier _linkVerifier;
    private readonly IDisposable _auditStore;

    internal BrowserActionAuthority(
        SafetyGate gate,
        AuthorizationService authorization,
        BrowserSearchVerifier verifier,
        BrowserOpenLinkVerifier linkVerifier,
        IDisposable auditStore)
    {
        _gate = gate;
        _authorization = authorization;
        _verifier = verifier;
        _linkVerifier = linkVerifier;
        _auditStore = auditStore;
    }

    /// <summary>Captures the pre-action browser address used as the verification baseline.</summary>
    public async Task<BrowserBaseline> BeginSearchVerificationAsync(
        IBrowserEvidenceSource source,
        CancellationToken cancellationToken = default) =>
        new BrowserBaseline(await _verifier.BeginAsync(source, cancellationToken).ConfigureAwait(false));

    /// <summary>Verifies a consumed search record from browser-reported evidence only.</summary>
    public async Task<BrowserActionVerificationResult> VerifySearchAsync(
        AuthorizationRecordDto record,
        IBrowserEvidenceSource source,
        BrowserBaseline baseline,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        var result = await _verifier.VerifyAsync(record, source, baseline.Inner, cancellationToken)
            .ConfigureAwait(false);
        return new BrowserActionVerificationResult(result.Status, result.Reasons);
    }

    /// <summary>Captures the browser address before an authorized link is opened.</summary>
    public async Task<BrowserLinkBaseline> BeginOpenLinkVerificationAsync(
        IBrowserEvidenceSource source,
        CancellationToken cancellationToken = default) =>
        new(await _linkVerifier.BeginAsync(source, cancellationToken).ConfigureAwait(false));

    /// <summary>Captures all existing browser tab identities before creating the destination tab.</summary>
    public async Task<BrowserLinkBaseline> BeginOpenLinkVerificationAsync(
        IBrowserOpenLinkEvidenceSource source,
        CancellationToken cancellationToken = default) =>
        new(await _linkVerifier.BeginAsync(source, cancellationToken).ConfigureAwait(false));

    /// <summary>Verifies link navigation using browser-engine evidence only.</summary>
    public async Task<BrowserActionVerificationResult> VerifyOpenLinkAsync(
        AuthorizationRecordDto record,
        IBrowserEvidenceSource source,
        BrowserLinkBaseline baseline,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        var result = await _linkVerifier.VerifyAsync(record, source, baseline.Inner, cancellationToken)
            .ConfigureAwait(false);
        return new BrowserActionVerificationResult(result.Status, result.Reasons);
    }

    /// <summary>Verifies a new destination tab from independent browser topology and page evidence.</summary>
    public async Task<BrowserActionVerificationResult> VerifyOpenLinkAsync(
        AuthorizationRecordDto record,
        IBrowserOpenLinkEvidenceSource source,
        BrowserLinkBaseline baseline,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        var result = await _linkVerifier.VerifyAsync(record, source, baseline.Inner, cancellationToken)
            .ConfigureAwait(false);
        return new BrowserActionVerificationResult(result.Status, result.Reasons);
    }

    /// <summary>Validates the query against the registry, asks the user, and signs a single-use record.</summary>
    public async Task<BrowserActionAuthorizationResult> AuthorizeSearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var taskId = $"browser-{Guid.NewGuid():N}"[..20];
        var args = new JsonObject { ["query"] = query };
        var result = await _gate.EvaluateAndAuthorizeAsync(
            taskId, "step-1", SearchToolId, args, "medium", cancellationToken).ConfigureAwait(false);
        return result.Approved && result.Record is not null
            ? new BrowserActionAuthorizationResult(true, result.Record, "Approved and signed.")
            : new BrowserActionAuthorizationResult(false, null, result.ErrorMessage ?? "The action was not approved.");
    }

    /// <summary>Consumes a signed search record exactly once and returns its signed query.</summary>
    public ConsumedBrowserSearch ConsumeSearch(AuthorizationRecordDto record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!string.Equals(record.ToolId, SearchToolId, StringComparison.Ordinal))
            throw new AuthorizationSecurityException($"Record '{record.AuthId}' does not authorize {SearchToolId}.");
        var verified = _authorization.Consume(record, record.StepId);
        _ = verified;
        if (!record.CanonicalArgs.TryGetValue("query", out var value) || value is not string query)
            throw new AuthorizationSecurityException("Signed record has no search query.");
        return new ConsumedBrowserSearch(record.AuthId, query);
    }

    /// <summary>Validates, approves, and signs a one-use open-link operation.</summary>
    public async Task<BrowserActionAuthorizationResult> AuthorizeOpenLinkAsync(
        string linkId,
        string observationId,
        string tabId,
        string targetHash,
        string targetHost,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(linkId);
        ArgumentException.ThrowIfNullOrWhiteSpace(observationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tabId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetHost);

        var taskId = $"browser-{Guid.NewGuid():N}"[..20];
        var args = new JsonObject
        {
            ["link_id"] = linkId,
            ["observation_id"] = observationId,
            ["tab_id"] = tabId,
            ["target_hash"] = targetHash,
            ["target_host"] = targetHost,
            ["mode"] = "new_tab"
        };
        var result = await _gate.EvaluateAndAuthorizeAsync(
            taskId, "step-1", OpenLinkToolId, args, "medium", cancellationToken).ConfigureAwait(false);
        return result.Approved && result.Record is not null
            ? new BrowserActionAuthorizationResult(true, result.Record, "Approved and signed.")
            : new BrowserActionAuthorizationResult(false, null, result.ErrorMessage ?? "The action was not approved.");
    }

    /// <summary>Consumes a signed link-opening record exactly once.</summary>
    public ConsumedBrowserLink ConsumeOpenLink(AuthorizationRecordDto record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!string.Equals(record.ToolId, OpenLinkToolId, StringComparison.Ordinal))
            throw new AuthorizationSecurityException($"Record '{record.AuthId}' does not authorize {OpenLinkToolId}.");
        _ = _authorization.Consume(record, record.StepId);
        var args = record.CanonicalArgs;
        if (!TryRead(args, "link_id", out var linkId)
            || !TryRead(args, "observation_id", out var observationId)
            || !TryRead(args, "tab_id", out var tabId)
            || !TryRead(args, "target_hash", out var targetHash)
            || !TryRead(args, "target_host", out var targetHost)
            || !TryRead(args, "mode", out var mode)
            || !string.Equals(mode, "new_tab", StringComparison.Ordinal))
            throw new AuthorizationSecurityException("Signed link authorization is incomplete.");
        return new ConsumedBrowserLink(record.AuthId, linkId, observationId, tabId, targetHash, targetHost, mode);
    }

    /// <inheritdoc />
    public void Dispose() => _auditStore.Dispose();

    private static bool TryRead(
        IReadOnlyDictionary<string, object?> args,
        string key,
        out string value)
    {
        if (args.TryGetValue(key, out var raw) && raw is string text && !string.IsNullOrWhiteSpace(text))
        {
            value = text;
            return true;
        }
        value = string.Empty;
        return false;
    }
}
