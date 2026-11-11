using System.Diagnostics;
using System.Text.Json;
using Jarvis.Contracts;
using Jarvis.Infrastructure;

namespace Jarvis.Verification;

/// <summary>Address observed before a browser search ran.</summary>
public sealed record BrowserSearchBaseline(string? Url);

/// <summary>
/// Verifies a signed browser.search record from browser-reported page state only. The executor's
/// completion is never read. Anything short of a same-host navigation whose URL carries the signed
/// query is FAILED or ESCALATE, never VERIFIED.
/// </summary>
public sealed class BrowserSearchVerifier
{
    private const string ToolId = "browser.search";
    private readonly IAuthorizationVerifier _authorization;
    private readonly IAuditStore _auditStore;
    private readonly IClock _clock;
    private readonly TimeSpan _timeout;
    private readonly TimeSpan _interval;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    /// <summary>Creates a verifier; timeout and delay are injectable for deterministic tests.</summary>
    public BrowserSearchVerifier(
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
        _interval = interval ?? TimeSpan.FromMilliseconds(500);
        _delay = delay ?? ((span, token) => Task.Delay(span, token));
    }

    /// <summary>Captures the pre-action address. Call before the executor runs.</summary>
    public async Task<BrowserSearchBaseline> BeginAsync(
        IBrowserEvidenceSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var evidence = await source.CaptureAsync(cancellationToken).ConfigureAwait(false);
        return new BrowserSearchBaseline(evidence.Url);
    }

    /// <summary>Marks the consumed authorization verified and judges the page evidence.</summary>
    public async Task<VerificationResultDto> VerifyAsync(
        AuthorizationRecordDto authorization,
        IBrowserEvidenceSource source,
        BrowserSearchBaseline baseline,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(baseline);
        var timer = Stopwatch.GetTimestamp();
        var stages = new List<VerificationStageResultDto>();
        var reasons = new List<string>();

        if (authorization.ToolId != ToolId
            || !authorization.CanonicalArgs.TryGetValue("query", out var raw)
            || raw is not string query
            || string.IsNullOrWhiteSpace(query))
        {
            stages.Add(new(1, "TriggerValidation", false, "Record is not a browser.search authorization with a query.", "FAILED"));
            return await FinishAsync(authorization, "FAILED", stages, reasons, "Invalid browser.search record.", timer, cancellationToken)
                .ConfigureAwait(false);
        }
        stages.Add(new(1, "TriggerValidation", true, "Signed browser.search record with a query."));

        try
        {
            await _authorization.VerifyAndMarkVerifiedAsync(authorization, cancellationToken).ConfigureAwait(false);
            stages.Add(new(2, "Authorization", true, "Signed authorization was consumed, unexpired, and marked verified."));
        }
        catch (AuthorizationVerificationException exception)
        {
            stages.Add(new(2, "Authorization", false, exception.Message, "FAILED"));
            return await FinishAsync(authorization, "FAILED", stages, reasons, exception.Message, timer, cancellationToken)
                .ConfigureAwait(false);
        }

        var (status, detail) = await PollAsync(query, source, baseline, cancellationToken).ConfigureAwait(false);
        stages.Add(new(3, "IndependentBrowserEvidence", status == "VERIFIED", detail, status == "VERIFIED" ? "PASSED" : status));
        return await FinishAsync(authorization, status, stages, reasons, status == "VERIFIED" ? null : detail, timer, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<(string Status, string Detail)> PollAsync(
        string query,
        IBrowserEvidenceSource source,
        BrowserSearchBaseline baseline,
        CancellationToken cancellationToken)
    {
        var baselineUri = TryWebUri(baseline.Url);
        if (baselineUri is not null && UrlCarriesQuery(baselineUri, query))
            return ("ESCALATE", "The page address already contained the query before the action, so it cannot prove the action.");

        var deadline = Stopwatch.GetTimestamp() + (long)(_timeout.TotalSeconds * Stopwatch.Frequency);
        (string Status, string Detail) last = ("ESCALATE", "No browser evidence was captured.");
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var evidence = await source.CaptureAsync(cancellationToken).ConfigureAwait(false);
                last = Judge(query, baselineUri, evidence);
                if (last.Status == "VERIFIED")
                    return last;
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

    private static (string Status, string Detail) Judge(string query, Uri? baseline, BrowserPageEvidence evidence)
    {
        if (evidence.IsLoading)
            return ("ESCALATE", "The page was still loading.");
        var current = TryWebUri(evidence.Url);
        if (current is null)
            return ("ESCALATE", "The browser did not report an HTTP or HTTPS address.");
        if (baseline is not null && Uri.Compare(baseline, current, UriComponents.AbsoluteUri, UriFormat.UriEscaped, StringComparison.Ordinal) == 0)
            return ("FAILED", $"The page address on {current.Host} did not change after the search.");
        if (baseline is not null && !string.Equals(baseline.Host, current.Host, StringComparison.OrdinalIgnoreCase))
            return ("ESCALATE", $"The page moved from {baseline.Host} to {current.Host}; a cross-host move is not proof of the search.");
        return UrlCarriesQuery(current, query)
            ? ("VERIFIED", $"The page on {current.Host} navigated to an address carrying the signed query.")
            : ("ESCALATE", $"The page on {current.Host} changed but its address does not carry the query.");
    }

    internal static bool UrlCarriesQuery(Uri uri, string query)
    {
        var wanted = Normalize(query, false);
        if (wanted.Length == 0)
            return false;
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator < 0)
                continue;
            if (Normalize(Decode(pair[(separator + 1)..]), false) == wanted)
                return true;
        }

        foreach (var segment in uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Normalize(Decode(segment), true) == wanted)
                return true;
        }
        return false;
    }

    private static string Decode(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value.Replace('+', ' '));
        }
        catch (UriFormatException)
        {
            return string.Empty;
        }
    }

    private static string Normalize(string value, bool dashesAsSpaces)
    {
        if (dashesAsSpaces)
            value = value.Replace('-', ' ').Replace('_', ' ');
        return string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();
    }

    private static Uri? TryWebUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? uri
            : null;

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
