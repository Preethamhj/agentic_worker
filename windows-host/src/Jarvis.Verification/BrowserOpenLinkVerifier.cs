using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Jarvis.Contracts;
using Jarvis.Infrastructure;

namespace Jarvis.Verification;

/// <summary>Browser state captured before an authorized link is opened.</summary>
public sealed record BrowserOpenLinkBaseline(string? Url)
{
    private string[]? _tabIds;

    internal static BrowserOpenLinkBaseline FromTopology(IReadOnlyList<string>? tabIds) =>
        new((string?)null) { _tabIds = tabIds?.ToArray() };

    internal bool HasValidTopology(string sourceTabId) =>
        _tabIds is { Length: > 0 }
        && _tabIds.All(id => !string.IsNullOrWhiteSpace(id))
        && _tabIds.Distinct(StringComparer.Ordinal).Count() == _tabIds.Length
        && _tabIds.Contains(sourceTabId, StringComparer.Ordinal);

    internal bool ContainsTab(string tabId) =>
        _tabIds is not null && _tabIds.Contains(tabId, StringComparer.Ordinal);
}

/// <summary>
/// Verifies a signed browser.open_link action using only independent browser-engine evidence.
/// The link destination is represented by a one-way fingerprint in the authorization record.
/// </summary>
public sealed class BrowserOpenLinkVerifier
{
    private const string ToolId = "browser.open_link";
    private readonly IAuthorizationVerifier _authorization;
    private readonly IAuditStore _auditStore;
    private readonly IClock _clock;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _interval;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    /// <summary>Creates a verifier with injectable timing for deterministic tests.</summary>
    public BrowserOpenLinkVerifier(
        IAuthorizationVerifier authorization,
        IAuditStore auditStore,
        IClock clock,
        TimeSpan? timeout = null,
        TimeSpan? interval = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _auditStore = auditStore ?? throw new ArgumentNullException(nameof(auditStore));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _timeout = timeout ?? TimeSpan.FromSeconds(10);
        _interval = interval ?? TimeSpan.FromMilliseconds(250);
        _delay = delay ?? ((span, token) => Task.Delay(span, token));
    }

    /// <summary>Captures the browser-engine address before the action.</summary>
    public async Task<BrowserOpenLinkBaseline> BeginAsync(
        IBrowserEvidenceSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var evidence = await source.CaptureAsync(cancellationToken).ConfigureAwait(false);
        return new BrowserOpenLinkBaseline(evidence.Url);
    }

    /// <summary>Captures the complete tab topology before creating the destination tab.</summary>
    public async Task<BrowserOpenLinkBaseline> BeginAsync(
        IBrowserOpenLinkEvidenceSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var evidence = await source.CaptureTopologyAsync(cancellationToken).ConfigureAwait(false);
        return BrowserOpenLinkBaseline.FromTopology(evidence?.TabIds);
    }

    /// <summary>Verifies the consumed authorization and resulting browser address.</summary>
    public Task<VerificationResultDto> VerifyAsync(
        AuthorizationRecordDto record,
        IBrowserEvidenceSource source,
        BrowserOpenLinkBaseline baseline,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        return VerifyCoreAsync(record, baseline,
            (hash, _, token) => PollAsync(hash, source, token), cancellationToken);
    }

    /// <summary>Verifies a new destination tab against the pre-action topology and signed target.</summary>
    public Task<VerificationResultDto> VerifyAsync(
        AuthorizationRecordDto record,
        IBrowserOpenLinkEvidenceSource source,
        BrowserOpenLinkBaseline baseline,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        return VerifyCoreAsync(record, baseline,
            (hash, sourceId, token) => PollTopologyAsync(hash, sourceId, source, baseline, token),
            cancellationToken);
    }

    private async Task<VerificationResultDto> VerifyCoreAsync(
        AuthorizationRecordDto record,
        BrowserOpenLinkBaseline baseline,
        Func<string, string, CancellationToken, Task<(string Status, string Detail)>> poll,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(baseline);
        cancellationToken.ThrowIfCancellationRequested();
        var timer = Stopwatch.GetTimestamp();
        var stages = new List<VerificationStageResultDto>();
        var reasons = new List<string>();
        if (record.ToolId != ToolId
            || !record.CanonicalArgs.TryGetValue("mode", out var rawMode)
            || rawMode is not string mode
            || mode != "new_tab"
            || !record.CanonicalArgs.TryGetValue("tab_id", out var rawTabId)
            || rawTabId is not string sourceTabId
            || string.IsNullOrWhiteSpace(sourceTabId)
            || !record.CanonicalArgs.TryGetValue("target_hash", out var rawHash)
            || rawHash is not string targetHash
            || !IsFingerprint(targetHash))
        {
            stages.Add(new(1, "TriggerValidation", false, "Record is not a new-tab browser.open_link authorization with a target fingerprint.", "FAILED"));
            return await FinishAsync(record, "FAILED", stages, reasons, "Invalid browser.open_link record.", timer, cancellationToken)
                .ConfigureAwait(false);
        }

        stages.Add(new(1, "TriggerValidation", true, "Signed new-tab browser.open_link record with a target fingerprint."));
        try
        {
            await _authorization.VerifyAndMarkVerifiedAsync(record, cancellationToken).ConfigureAwait(false);
            stages.Add(new(2, "Authorization", true, "Signed authorization was consumed, unexpired, and marked verified."));
        }
        catch (AuthorizationVerificationException exception)
        {
            stages.Add(new(2, "Authorization", false, exception.Message, "FAILED"));
            return await FinishAsync(record, "FAILED", stages, reasons, exception.Message, timer, cancellationToken)
                .ConfigureAwait(false);
        }

        var status = await poll(targetHash, sourceTabId, cancellationToken).ConfigureAwait(false);
        stages.Add(new(
            3,
            "IndependentBrowserEvidence",
            status.Status == "VERIFIED",
            status.Detail,
            status.Status == "VERIFIED" ? "PASSED" : status.Status));
        return await FinishAsync(
                record,
                status.Status,
                stages,
                reasons,
                status.Status == "VERIFIED" ? null : status.Detail,
                timer,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<(string Status, string Detail)> PollTopologyAsync(
        string targetHash,
        string sourceTabId,
        IBrowserOpenLinkEvidenceSource source,
        BrowserOpenLinkBaseline baseline,
        CancellationToken cancellationToken)
    {
        if (!baseline.HasValidTopology(sourceTabId))
            return ("ESCALATE", "The pre-action tab topology is missing or does not include the signed source tab.");

        var deadline = Stopwatch.GetTimestamp() + (long)(_timeout.TotalSeconds * Stopwatch.Frequency);
        var last = ("ESCALATE", "No destination tab evidence was captured.");
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var evidence = await source.CaptureDestinationAsync(cancellationToken).ConfigureAwait(false);
                var tabId = evidence?.TabId;
                var current = TryWebUri(evidence?.Url);
                if (string.IsNullOrWhiteSpace(tabId)
                    || string.Equals(tabId, sourceTabId, StringComparison.Ordinal)
                    || baseline.ContainsTab(tabId))
                    last = ("ESCALATE", "The destination tab identity is missing or was present before the action.");
                else if (evidence!.IsLoading || current is null
                    || string.IsNullOrEmpty(current.Host) || !string.IsNullOrEmpty(current.UserInfo))
                    last = ("ESCALATE", "The destination tab did not report a stable HTTP or HTTPS address.");
                else if (Fingerprint(current) == targetHash)
                    return ("VERIFIED", $"A new browser tab reports the approved destination on {current.Host}.");
                else
                    last = ("ESCALATE", $"The destination tab reports {current.Host}, but the approved destination could not be confirmed.");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                last = ("ESCALATE", $"Browser evidence could not be read ({exception.GetType().Name}).");
            }

            if (Stopwatch.GetTimestamp() >= deadline)
                return last;
            await _delay(_interval, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<(string Status, string Detail)> PollAsync(
        string targetHash,
        IBrowserEvidenceSource source,
        CancellationToken cancellationToken)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(_timeout.TotalSeconds * Stopwatch.Frequency);
        var last = ("ESCALATE", "No browser evidence was captured.");
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var evidence = await source.CaptureAsync(cancellationToken).ConfigureAwait(false);
                var current = TryWebUri(evidence.Url);
                if (!evidence.IsLoading && current is not null
                    && !string.IsNullOrEmpty(current.Host)
                    && string.IsNullOrEmpty(current.UserInfo))
                {
                    if (Fingerprint(current) == targetHash)
                        last = ("ESCALATE", "The approved destination was observed, but new-tab identity evidence is unavailable.");
                    else
                        last = ("ESCALATE", $"The browser moved to {current.Host}, but the approved destination could not be confirmed.");
                }
                else
                {
                    last = ("ESCALATE", "The browser did not report a stable HTTP or HTTPS address.");
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                last = ("ESCALATE", $"Browser evidence could not be read ({exception.GetType().Name}).");
            }

            if (Stopwatch.GetTimestamp() >= deadline)
                return last;
            await _delay(_interval, cancellationToken).ConfigureAwait(false);
        }
    }

    private static Uri? TryWebUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri
            : null;

    private static string Fingerprint(Uri uri) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uri.AbsoluteUri))).ToLowerInvariant();

    private static bool IsFingerprint(string value) =>
        value.Length == 64 && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');

    private async Task<VerificationResultDto> FinishAsync(
        AuthorizationRecordDto authorization,
        string status,
        List<VerificationStageResultDto> stages,
        List<string> reasons,
        string? reason,
        long timer,
        CancellationToken cancellationToken)
    {
        if (reason is not null)
            reasons.Add(reason);
        await _auditStore.RecordVerificationAsync(
            new VerificationAuditRecord(
                authorization.StepId,
                status,
                JsonSerializer.Serialize(new { Stages = stages, Reasons = reasons }),
                _clock.UtcNow,
                authorization.TaskId),
            cancellationToken).ConfigureAwait(false);
        return new VerificationResultDto(
            1, authorization.StepId, status, stages, [], [], reasons, Stopwatch.GetElapsedTime(timer).TotalMilliseconds);
    }
}
