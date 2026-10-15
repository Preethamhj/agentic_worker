using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using System.Security.Cryptography;
using System.Text.Json;
using Jarvis.Contracts;
using Jarvis.Infrastructure;

namespace Jarvis.Verification;

/// <summary>
/// Independently verifies filesystem, process, search, and scheduled-task effects using OS evidence.
/// Persistence and drift stages are deferred to Phase 6.
/// </summary>
public sealed class VerificationEngine : IVerifier
{
    private const string Verified = "VERIFIED";
    private const string Failed = "FAILED";
    private const string Escalate = "ESCALATE";
    private const string NotRun = "NOT_RUN";
    private static readonly TimeSpan FileTimeResolutionTolerance = TimeSpan.FromMilliseconds(100);

    private readonly IAuthorizationVerifier _authorizationVerifier;
    private readonly IAuditStore _auditStore;
    private readonly IClock _clock;
    private readonly SnapshotService _snapshots;
    private readonly FileEvidenceCollector _fileCollector;
    private readonly SearchEvidenceCollector _searchCollector;
    private readonly ProcessEvidenceCollector _processCollector;
    private readonly ScheduledTaskEvidenceCollector _scheduledTaskCollector;
    private readonly TimeSpan _evidenceWindow;
    private readonly object _nonceLock = new();
    private readonly HashSet<string> _verifiedNonces = new(StringComparer.Ordinal);

    public VerificationEngine(
        IAuthorizationVerifier authorizationVerifier,
        IAuditStore auditStore,
        IClock? clock = null,
        int maxSnapshotEntries = 10_000,
        TimeSpan? evidenceWindow = null,
        IEnumerable<string>? noiseExclusions = null)
    {
        _authorizationVerifier = authorizationVerifier ?? throw new ArgumentNullException(nameof(authorizationVerifier));
        _auditStore = auditStore ?? throw new ArgumentNullException(nameof(auditStore));
        _clock = clock ?? SystemClock.Instance;
        _snapshots = new SnapshotService(maxSnapshotEntries, noiseExclusions);
        _fileCollector = new FileEvidenceCollector();
        _searchCollector = new SearchEvidenceCollector(_fileCollector);
        _processCollector = new ProcessEvidenceCollector();
        _scheduledTaskCollector = new ScheduledTaskEvidenceCollector();
        _evidenceWindow = evidenceWindow ?? TimeSpan.FromSeconds(30);
        if (_evidenceWindow <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(evidenceWindow));
    }

    public Task<StepToken> BeginStepAsync(
        AuthorizationRecordDto authorization,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        cancellationToken.ThrowIfCancellationRequested();
        var isFilesystemMutation = authorization.ToolId is "fs.create_file" or "fs.create_folder";
        var target = isFilesystemMutation
            && authorization.CanonicalArgs.TryGetValue("path", out var path)
            && path is string targetPath
                ? targetPath
                : null;
        var snapshot = isFilesystemMutation
            ? _snapshots.Capture(
                authorization.WatchScopes,
                cancellationToken,
                authorizedTarget: target)
            : new SnapshotCaptureResult([], false, null);
        var beforeSchedulerTasks = authorization.ToolId == "task.schedule_reminder"
            ? _scheduledTaskCollector.CaptureTaskPaths(cancellationToken)
            : (IReadOnlyList<string>?)null;
        return Task.FromResult(new StepToken(
            authorization,
            authorization.Nonce,
            snapshot.Entries,
            snapshot.EntryLimitExceeded,
            snapshot.FailureReason,
            _snapshots.NoiseExclusions,
            beforeSchedulerTasks));
    }

    public async Task<VerificationResultDto> VerifyAsync(
        ExecutionReceiptDto receipt,
        ExecutionObservation observation,
        StepToken token,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(observation);
        ArgumentNullException.ThrowIfNull(token);
        cancellationToken.ThrowIfCancellationRequested();

        var timer = Stopwatch.GetTimestamp();
        var authorization = token.Authorization;
        var stages = new List<VerificationStageResultDto>(10);
        var evidence = new List<EvidenceDto>(1);
        var sideEffects = new List<string>();
        var reasons = new List<string>();

        var triggerIsValid = IsValidTrigger(receipt, observation, token, authorization, out var triggerFailure);
        stages.Add(Stage(1, "TriggerValidation", triggerIsValid, triggerFailure));
        var authorizationVerified = false;
        if (triggerIsValid)
        {
            try
            {
                await _authorizationVerifier.VerifyAndMarkVerifiedAsync(authorization, cancellationToken).ConfigureAwait(false);
                authorizationVerified = true;
                stages.Add(Stage(2, "Authorization", true, "Signed authorization was consumed, unexpired, and atomically marked verified."));
            }
            catch (AuthorizationVerificationException ex)
            {
                reasons.Add(ex.Message);
                stages.Add(Stage(2, "Authorization", false, ex.Message));
            }
        }
        else
        {
            stages.Add(NotRunStage(2, "Authorization", "Trigger validation failed."));
            reasons.Add(triggerFailure);
        }

        if (authorizationVerified)
        {
            try
            {
                var collected = CollectIndependentEvidence(
                    authorization,
                    receipt,
                    observation,
                    token,
                    cancellationToken);
                var item = CreateEvidence(authorization, token, collected, _clock.UtcNow);
                evidence.Add(item);
                stages.Add(Stage(3, "IndependentEvidenceCollection", true,
                    $"OS evidence was collected by {collected.Collector}."));

                var evidenceValid = ValidateEvidence(item, token, observation, out var evidenceFailure);
                stages.Add(Stage(4, "EvidenceValidation", evidenceValid, evidenceFailure));
                if (!evidenceValid)
                    reasons.Add(evidenceFailure);

                stages.Add(Stage(5, "AttributeVerification", collected.Match, collected.Detail, collected.Outcome));
                if (!collected.Match)
                    reasons.Add(collected.Detail);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (IOException ex)
            {
                reasons.Add($"Filesystem evidence collection failed: {ex.Message}");
                stages.Add(Stage(3, "IndependentEvidenceCollection", false, ex.Message, Escalate));
                stages.Add(NotRunStage(4, "EvidenceValidation", "Evidence collection did not complete."));
                stages.Add(NotRunStage(5, "AttributeVerification", "Evidence collection did not complete."));
            }
            catch (UnauthorizedAccessException ex)
            {
                reasons.Add($"Filesystem evidence collection failed: {ex.Message}");
                stages.Add(Stage(3, "IndependentEvidenceCollection", false, ex.Message, Escalate));
                stages.Add(NotRunStage(4, "EvidenceValidation", "Evidence collection did not complete."));
                stages.Add(NotRunStage(5, "AttributeVerification", "Evidence collection did not complete."));
            }
            catch (Win32Exception ex)
            {
                reasons.Add($"Filesystem evidence collection failed: {ex.Message}");
                stages.Add(Stage(3, "IndependentEvidenceCollection", false, ex.Message, Escalate));
                stages.Add(NotRunStage(4, "EvidenceValidation", "Evidence collection did not complete."));
                stages.Add(NotRunStage(5, "AttributeVerification", "Evidence collection did not complete."));
            }
            catch (SecurityException ex)
            {
                reasons.Add($"Filesystem evidence collection failed: {ex.Message}");
                stages.Add(Stage(3, "IndependentEvidenceCollection", false, ex.Message, Escalate));
                stages.Add(NotRunStage(4, "EvidenceValidation", "Evidence collection did not complete."));
                stages.Add(NotRunStage(5, "AttributeVerification", "Evidence collection did not complete."));
            }
            catch (ArgumentException ex)
            {
                reasons.Add($"Independent evidence collection failed: {ex.Message}");
                stages.Add(Stage(3, "IndependentEvidenceCollection", false, ex.Message, Escalate));
                stages.Add(NotRunStage(4, "EvidenceValidation", "Evidence collection did not complete."));
                stages.Add(NotRunStage(5, "AttributeVerification", "Evidence collection did not complete."));
            }
            catch (System.Xml.XmlException ex)
            {
                reasons.Add($"Scheduled-task evidence could not be parsed: {ex.Message}");
                stages.Add(Stage(3, "IndependentEvidenceCollection", false, ex.Message, Escalate));
                stages.Add(NotRunStage(4, "EvidenceValidation", "Evidence collection did not complete."));
                stages.Add(NotRunStage(5, "AttributeVerification", "Evidence collection did not complete."));
            }
        }
        else
        {
            stages.Add(NotRunStage(3, "IndependentEvidenceCollection", "Authorization validation failed."));
            stages.Add(NotRunStage(4, "EvidenceValidation", "Authorization validation failed."));
            stages.Add(NotRunStage(5, "AttributeVerification", "Authorization validation failed."));
        }

        var sideEffectOutcome = "PASSED";
        if (authorization.ToolId is "fs.create_file" or "fs.create_folder")
        {
            var beforeEntries = token.BeforeSnapshots.ToDictionary(entry => entry.Path, StringComparer.OrdinalIgnoreCase);
            var targetPath = authorization.CanonicalArgs.TryGetValue("path", out var path) && path is string target
                ? target
                : null;
            var afterSnapshot = _snapshots.Capture(
                authorization.WatchScopes,
                cancellationToken,
                beforeEntries,
                targetPath);
            sideEffectOutcome = CompareSnapshots(token, afterSnapshot, authorization, sideEffects, reasons);
            stages.Add(Stage(6, "SideEffectDetection", sideEffectOutcome == "PASSED",
                sideEffectOutcome == "PASSED"
                    ? "Watched filesystem changes are within allowed_effects."
                    : string.Join("; ", reasons.Where(r =>
                        r.Contains("side effect", StringComparison.OrdinalIgnoreCase)
                        || r.Contains("snapshot", StringComparison.OrdinalIgnoreCase)
                        || r.Contains("authorized creation", StringComparison.OrdinalIgnoreCase))),
                sideEffectOutcome));
        }
        else
        {
            stages.Add(Stage(6, "SideEffectDetection", true,
                "No filesystem snapshot applies; side effects for this tool are checked by its independent OS collector."));
        }

        stages.Add(NotRunStage(7, "PersistenceCheck", "Stage 7 is deferred to Phase 6."));
        stages.Add(NotRunStage(8, "DriftCheck", "Stage 8 is deferred to Phase 6."));

        var status = Aggregate(stages);
        stages.Add(Stage(9, "DecisionAggregation", status == Verified,
            $"Verification status: {status}.",
            status == Verified ? "PASSED" : status));

        var result = new VerificationResultDto(
            1,
            authorization.StepId,
            status,
            stages,
            evidence,
            sideEffects,
            reasons,
            Stopwatch.GetElapsedTime(timer).TotalMilliseconds);

        foreach (var item in evidence)
        {
            await _auditStore.RecordEvidenceAsync(
                new EvidenceAuditRecord(
                    item.EvidenceId,
                    item.StepId,
                    item.Collector,
                    item.Check.Type,
                    item.Match,
                    DateTimeOffset.Parse(item.CollectedAt),
                    item.Nonce,
                    item.RecordSha256,
                    JsonSerializer.Serialize(item)),
                cancellationToken).ConfigureAwait(false);
        }

        result = result with
        {
            Stages = result.Stages.Append(Stage(10, "AuditAndReport", true, "Verification result and evidence were persisted.")).ToList(),
            DurationMs = Stopwatch.GetElapsedTime(timer).TotalMilliseconds
        };
        await _auditStore.RecordVerificationAsync(
            new VerificationAuditRecord(
                authorization.StepId,
                status,
                JsonSerializer.Serialize(new { result.Stages, result.Reasons, result.SideEffects }),
                _clock.UtcNow),
            cancellationToken).ConfigureAwait(false);

        return result;
    }

    private static bool IsValidTrigger(
        ExecutionReceiptDto receipt,
        ExecutionObservation observation,
        StepToken token,
        AuthorizationRecordDto authorization,
        out string detail)
    {
        if (authorization.ToolId is not (
            "fs.create_file" or "fs.create_folder" or "fs.search_files" or "app.launch" or "task.schedule_reminder"))
        {
            detail = $"Unsupported verifier tool '{authorization.ToolId}'.";
            return false;
        }
        if (!string.Equals(receipt.StepId, authorization.StepId, StringComparison.Ordinal)
            || !string.Equals(receipt.AuthId, authorization.AuthId, StringComparison.Ordinal)
            || !string.Equals(token.Nonce, authorization.Nonce, StringComparison.Ordinal))
        {
            detail = "Receipt identity or StepToken nonce does not match the signed authorization.";
            return false;
        }
        if (observation.CompletedAtUtc < observation.StartedAtUtc)
        {
            detail = "Host execution observation has an invalid time range.";
            return false;
        }
        if (authorization.ToolId is "fs.create_file" or "fs.create_folder")
        {
            if (!authorization.CanonicalArgs.TryGetValue("path", out var path)
                || path is not string
                || authorization.WatchScopes.Count == 0)
            {
                detail = "Signed authorization is missing a target path or watch scope.";
                return false;
            }
        }
        else if (authorization.ToolId == "fs.search_files")
        {
            if (!authorization.CanonicalArgs.TryGetValue("root_path", out var rootPath)
                || rootPath is not string root
                || !Path.IsPathRooted(root)
                || !authorization.CanonicalArgs.TryGetValue("pattern", out var pattern)
                || pattern is not string
                || !authorization.WatchScopes.Contains(root, StringComparer.OrdinalIgnoreCase))
            {
                detail = "Signed search authorization is missing its rooted search path, pattern, or matching allowed scope.";
                return false;
            }
        }
        else if (authorization.ToolId == "app.launch")
        {
            if (!authorization.CanonicalArgs.TryGetValue("app_id", out var appId)
                || appId is not string
                || !authorization.Expected.TryGetValue("image_path", out var imagePath)
                || imagePath is not string image
                || !Path.IsPathRooted(image))
            {
                detail = "Signed app-launch authorization is missing its app ID or absolute allowlisted image path.";
                return false;
            }
        }
        else if (!authorization.CanonicalArgs.TryGetValue("task_name", out var taskName)
            || taskName is not string
            || !authorization.CanonicalArgs.TryGetValue("time_iso", out var timeValue)
            || timeValue is not string time
            || !DateTimeOffset.TryParse(time, out _)
            || !authorization.CanonicalArgs.TryGetValue("message", out var message)
            || message is not string
            || !authorization.Expected.TryGetValue("task_path", out var taskPath)
            || taskPath is not string taskPathValue
            || !string.Equals(
                taskPathValue,
                $@"\Jarvis\{authorization.AuthId}",
                StringComparison.OrdinalIgnoreCase)
            || !authorization.Expected.TryGetValue("action_path", out var actionPath)
            || actionPath is not string actionPathValue
            || !Path.IsPathRooted(actionPathValue))
        {
            detail = "Signed reminder authorization is missing its task identity, schedule, message, or action path.";
            return false;
        }

        detail = "Receipt identity and host execution observation match the authorized step.";
        return true;
    }

    private IndependentEvidenceObservation CollectIndependentEvidence(
        AuthorizationRecordDto authorization,
        ExecutionReceiptDto receipt,
        ExecutionObservation observation,
        StepToken token,
        CancellationToken cancellationToken)
    {
        if (authorization.ToolId is "fs.create_file" or "fs.create_folder")
        {
            var observedFile = _fileCollector.Collect(
                authorization,
                authorization.WatchScopes,
                cancellationToken);
            var match = MatchesExpected(authorization, observedFile, observation, out var detail);
            return new IndependentEvidenceObservation(
                authorization.ToolId == "fs.create_folder" ? "dir_exists" : "file_exists",
                nameof(FileEvidenceCollector),
                new Dictionary<string, object?>
                {
                    ["path"] = observedFile.Path,
                    ["exists"] = observedFile.Exists,
                    ["is_directory"] = observedFile.IsDirectory,
                    ["is_reparse_point"] = observedFile.IsReparsePoint,
                    ["is_inside_allowed_root"] = observedFile.IsInsideAllowedRoot,
                    ["final_path"] = observedFile.FinalPath,
                    ["size"] = observedFile.Size,
                    ["sha256"] = observedFile.Sha256,
                    ["creation_time_utc"] = observedFile.CreationTimeUtc,
                    ["last_write_time_utc"] = observedFile.LastWriteTimeUtc
                },
                match,
                detail);
        }

        return authorization.ToolId switch
        {
            "fs.search_files" => _searchCollector.Collect(authorization, receipt.Stdout, cancellationToken),
            "app.launch" => _processCollector.Collect(authorization, observation, cancellationToken),
            "task.schedule_reminder" => _scheduledTaskCollector.Collect(
                authorization,
                token.BeforeSchedulerTasks ?? [],
                cancellationToken),
            _ => throw new ArgumentException($"Unsupported verification tool '{authorization.ToolId}'.", nameof(authorization))
        };
    }

    private static bool MatchesExpected(
        AuthorizationRecordDto authorization,
        FileSystemObservation observed,
        ExecutionObservation observation,
        out string detail)
    {
        var mismatches = new List<string>();
        var expectsDirectory = authorization.ToolId == "fs.create_folder";
        if (!observed.Exists)
            mismatches.Add("target is absent");
        if (observed.IsDirectory != expectsDirectory)
            mismatches.Add("target has the wrong filesystem type");
        if (observed.IsReparsePoint)
            mismatches.Add("target is a reparse point");
        if (!observed.IsInsideAllowedRoot)
            mismatches.Add("final path is outside the allowed root");
        if (observed.CreationTimeUtc is null
            || observed.CreationTimeUtc.Value + FileTimeResolutionTolerance < observation.StartedAtUtc)
            mismatches.Add($"target creation time {observed.CreationTimeUtc:o} predates host start {observation.StartedAtUtc:o}");
        if (authorization.Expected.TryGetValue("exists", out var expectedExists) && !ReadBoolean(expectedExists))
            mismatches.Add("authorization expected the target not to exist");
        if (authorization.Expected.TryGetValue("path", out var expectedPath)
            && expectedPath is string expectedTarget
            && !string.Equals(Path.GetFullPath(expectedTarget), observed.Path, StringComparison.OrdinalIgnoreCase))
            mismatches.Add("observed target path differs from authorization.expected.path");
        if (!expectsDirectory)
        {
            if (authorization.Expected.TryGetValue("size", out var size) && observed.Size != ReadInt64(size))
                mismatches.Add($"size {observed.Size} differs from expected {ReadInt64(size)}");
            if (authorization.Expected.TryGetValue("sha256", out var hash)
                && !string.Equals(hash?.ToString(), observed.Sha256, StringComparison.OrdinalIgnoreCase))
                mismatches.Add("SHA-256 differs from authorization.expected.sha256");
        }

        detail = mismatches.Count == 0
            ? "Observed filesystem attributes match the signed authorization."
            : string.Join("; ", mismatches);
        return mismatches.Count == 0;
    }


    private bool ValidateEvidence(
        EvidenceDto evidence,
        StepToken token,
        ExecutionObservation observation,
        out string detail)
    {
        var collectedAt = DateTimeOffset.Parse(evidence.CollectedAt);
        var end = observation.CompletedAtUtc + _evidenceWindow;
        if (collectedAt < observation.CompletedAtUtc || collectedAt > end)
        {
            detail = "Evidence collection time is outside the host-observed verification window.";
            return false;
        }
        if (!string.Equals(evidence.Nonce, token.Nonce, StringComparison.Ordinal))
        {
            detail = "Evidence nonce is not bound to the current StepToken.";
            return false;
        }
        if (!string.Equals(evidence.RecordSha256, ComputeEvidenceHash(evidence), StringComparison.OrdinalIgnoreCase))
        {
            detail = "Evidence record SHA-256 did not match its canonical record.";
            return false;
        }

        lock (_nonceLock)
        {
            if (!_verifiedNonces.Add(token.Nonce))
            {
                detail = "Evidence nonce has already been used.";
                return false;
            }
        }

        detail = "Evidence timestamp, nonce binding, and SHA-256 record hash are valid.";
        return true;
    }

    private EvidenceDto CreateEvidence(
        AuthorizationRecordDto authorization,
        StepToken token,
        IndependentEvidenceObservation observed,
        DateTimeOffset collectedAt)
    {
        var unsigned = new EvidenceDto(
            1,
            $"evidence-{Guid.NewGuid():N}",
            authorization.StepId,
            new CheckDto(observed.CheckType, CreateCheckParameters(authorization)),
            observed.Collector,
            collectedAt.ToString("o"),
            new Dictionary<string, object?>(observed.Observed)
            {
                ["noise_exclusions"] = token.NoiseExclusions ?? Array.Empty<string>()
            },
            new Dictionary<string, object?>(authorization.Expected),
            observed.Match,
            token.Nonce,
            string.Empty);
        return unsigned with { RecordSha256 = ComputeEvidenceHash(unsigned) };
    }

    private static Dictionary<string, object?> CreateCheckParameters(AuthorizationRecordDto authorization)
    {
        var keys = authorization.ToolId switch
        {
            "fs.create_file" or "fs.create_folder" => new[] { "path" },
            "fs.search_files" => new[] { "root_path", "pattern" },
            "app.launch" => new[] { "app_id" },
            "task.schedule_reminder" => new[] { "task_name", "time_iso" },
            _ => Array.Empty<string>()
        };
        return keys
            .Where(authorization.CanonicalArgs.ContainsKey)
            .ToDictionary(key => key, key => authorization.CanonicalArgs[key], StringComparer.Ordinal);
    }

    private static string CompareSnapshots(
        StepToken token,
        SnapshotCaptureResult after,
        AuthorizationRecordDto authorization,
        ICollection<string> sideEffects,
        ICollection<string> reasons)
    {
        if (token.SnapshotLimitExceeded || after.EntryLimitExceeded)
        {
            reasons.Add("Snapshot entry cap exceeded; watched changes cannot be completely verified.");
            return Escalate;
        }
        if (token.SnapshotFailure is not null || after.FailureReason is not null)
        {
            var failure = token.SnapshotFailure ?? after.FailureReason!;
            reasons.Add($"Snapshot collection failed: {failure}");
            return Escalate;
        }

        var beforeEntries = token.BeforeSnapshots.ToDictionary(entry => entry.Path, StringComparer.OrdinalIgnoreCase);
        var afterEntries = after.Entries.ToDictionary(entry => entry.Path, StringComparer.OrdinalIgnoreCase);
        var allPaths = beforeEntries.Keys.Union(afterEntries.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var createdPaths = allPaths
            .Where(path => !beforeEntries.ContainsKey(path) && afterEntries.ContainsKey(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in allPaths)
        {
            var hadBefore = beforeEntries.TryGetValue(path, out var before);
            var hasAfter = afterEntries.TryGetValue(path, out var current);
            string? kind = null;
            if (!hadBefore && hasAfter)
                kind = "created";
            else if (hadBefore && !hasAfter)
                kind = "deleted";
            else if (hadBefore && hasAfter && MetadataChanged(before!, current!))
                kind = "modified";
            if (kind is null)
                continue;

            sideEffects.Add($"{kind}:{path}");
            var allowed = kind == "created"
                && authorization.AllowedEffects.Any(effect =>
                    string.Equals(effect, $"create:{path}", StringComparison.OrdinalIgnoreCase));
            if (!allowed && kind == "modified" && before!.IsDirectory && current!.IsDirectory)
            {
                allowed = authorization.AllowedEffects
                    .Where(effect => effect.StartsWith("create:", StringComparison.OrdinalIgnoreCase))
                    .Select(effect => Path.GetFullPath(effect["create:".Length..]))
                    .Any(createdPath =>
                        createdPaths.Contains(createdPath)
                        && string.Equals(Path.GetDirectoryName(createdPath), path, StringComparison.OrdinalIgnoreCase));
            }
            if (!allowed)
                reasons.Add($"Unauthorized side effect detected: {kind}:{path}");
        }

        foreach (var effect in authorization.AllowedEffects)
        {
            const string prefix = "create:";
            if (!effect.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;
            var expectedPath = Path.GetFullPath(effect[prefix.Length..]);
            if (!createdPaths.Contains(expectedPath))
                reasons.Add($"Authorized creation was not observed as a new side effect: {expectedPath}");
        }

        return reasons.Any(reason => reason.StartsWith("Unauthorized side effect", StringComparison.Ordinal))
            ? Failed
            : reasons.Any(reason => reason.StartsWith("Authorized creation was not observed", StringComparison.Ordinal))
                ? Failed
                : "PASSED";
    }

    private static string Aggregate(IReadOnlyList<VerificationStageResultDto> stages)
    {
        var executed = stages.Where(stage => stage.Stage is >= 1 and <= 6).ToArray();
        if (executed.Any(stage => stage.Outcome == Failed))
            return Failed;
        if (executed.Any(stage => stage.Outcome == Escalate))
            return Escalate;
        return executed.All(stage => stage.Outcome == "PASSED") ? Verified : Failed;
    }

    private static bool MetadataChanged(SnapshotEntryDto before, SnapshotEntryDto after) =>
        before.IsDirectory != after.IsDirectory
        || before.IsReparsePoint != after.IsReparsePoint
        || before.Size != after.Size
        || before.LastWriteTimeUtc != after.LastWriteTimeUtc;

    private static VerificationStageResultDto Stage(int number, string name, bool passed, string detail, string? outcome = null) =>
        new(number, name, passed, detail, outcome ?? (passed ? "PASSED" : Failed));

    private static VerificationStageResultDto NotRunStage(int number, string name, string detail) =>
        new(number, name, false, detail, NotRun);

    private static string ComputeEvidenceHash(EvidenceDto evidence)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(evidence with { RecordSha256 = string.Empty });
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static bool ReadBoolean(object? value) =>
        value is bool boolean || bool.TryParse(value?.ToString(), out boolean) && boolean;

    private static long ReadInt64(object? value) =>
        value is long number ? number : long.TryParse(value?.ToString(), out var parsed) ? parsed : long.MinValue;
}
