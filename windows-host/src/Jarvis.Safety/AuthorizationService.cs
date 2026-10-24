using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jarvis.Contracts;
using Jarvis.Core;
using Jarvis.Infrastructure;

namespace Jarvis.Safety;

public enum AuthorizationLifecycleState
{
    Issued,
    Consumed,
    Verified
}

/// <summary>
/// Service responsible for issuing, signing, tracking, consuming, and verifying
/// AuthorizationRecords. HMAC-SHA256 keys are managed through DPAPI (IDpapiKeyStore).
/// All lifecycle transitions are atomically persisted and audited through IAuditStore.
/// </summary>
public sealed class AuthorizationService : IAuthorizationVerifier
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private readonly IDpapiKeyStore _keyStore;
    private readonly IClock _clock;
    private readonly IAuditStore? _auditStore;
    private readonly IHostPathProvider _hostPathProvider;
    private readonly IReminderDataLocation _reminderDataLocation;
    private readonly TimeSpan _defaultExecuteWindow = TimeSpan.FromSeconds(60);
    private readonly TimeSpan _defaultVerifyWindow = TimeSpan.FromSeconds(120);
    private readonly TimeSpan _defaultTotalExpiry = TimeSpan.FromMinutes(5);

    private readonly Dictionary<string, AuthorizationLifecycleState> _recordStates = new();
    private readonly HashSet<string> _consumedNonces = new();
    private readonly object _lock = new();

    /// <summary>Gets the trusted reminder payload location used for issued reminder authorizations.</summary>
    public IReminderDataLocation ReminderDataLocation => _reminderDataLocation;

    public AuthorizationService(
        IDpapiKeyStore keyStore,
        IClock? clock = null,
        IAuditStore? auditStore = null,
        IHostPathProvider? hostPathProvider = null,
        IReminderDataLocation? reminderDataLocation = null)
    {
        _keyStore = keyStore ?? throw new ArgumentNullException(nameof(keyStore));
        _clock = clock ?? SystemClock.Instance;
        _auditStore = auditStore;
        _hostPathProvider = hostPathProvider ?? new EnvironmentHostPathProvider();
        _reminderDataLocation = reminderDataLocation ?? new DefaultReminderDataLocation();

        if (_auditStore != null)
        {
            try
            {
                var existingLifecycles = _auditStore.GetAuthorizationLifecycle();
                foreach (var rec in existingLifecycles)
                {
                    if (Enum.TryParse<AuthorizationLifecycleState>(rec.State, out var state))
                    {
                        _recordStates[rec.AuthId] = state;
                    }
                    if (rec.State is "Consumed" or "Verified")
                    {
                        _consumedNonces.Add(rec.Nonce);
                    }
                }
            }
            catch
            {
                // Graceful fallback if audit store is newly initializing
            }
        }
    }

    /// <summary>
    /// Issues a signed AuthorizationRecord with deterministic canonical arguments and deadlines.
    /// </summary>
    public AuthorizationRecordDto IssueAuthorization(
        string taskId,
        string stepId,
        string toolId,
        IReadOnlyDictionary<string, object?> rawArgs,
        IReadOnlyDictionary<string, object?>? expected = null,
        IReadOnlyList<string>? allowedEffects = null,
        IReadOnlyList<string>? watchScopes = null,
        string risk = "medium",
        string approvedBy = "user")
    {
        lock (_lock)
        {
            var authorizationGuid = Guid.NewGuid();
            var authId = $"auth-{authorizationGuid:N}";
            var nonce = $"nonce-{Guid.NewGuid():N}";
            var now = _clock.UtcNow;

            var issuedAt = now.ToString("o");
            var expiresAt = now.Add(_defaultTotalExpiry).ToString("o");
            var executeBefore = now.Add(_defaultExecuteWindow).ToString("o");
            var verifyBefore = now.Add(_defaultVerifyWindow).ToString("o");

            // Canonicalize args (sort keys)
            var canonicalArgs = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (var kv in rawArgs.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                canonicalArgs[kv.Key] = kv.Value;
            }
            if (string.Equals(toolId, "task.schedule_reminder", StringComparison.OrdinalIgnoreCase)
                && !canonicalArgs.ContainsKey("task_name")
                && canonicalArgs.TryGetValue("message", out var messageValue)
                && messageValue is string message)
            {
                canonicalArgs["task_name"] = ToolPolicy.TruncateReminderTitle(message);
            }

            var exp = string.Equals(toolId, "task.schedule_reminder", StringComparison.OrdinalIgnoreCase)
                ? DeriveExpected(toolId, canonicalArgs, authId)
                : expected != null
                    ? new Dictionary<string, object?>(expected)
                    : DeriveExpected(toolId, canonicalArgs, authId);

            var effects = allowedEffects != null
                ? allowedEffects.ToList()
                : DeriveAllowedEffects(toolId, canonicalArgs);

            var scopes = watchScopes != null
                ? watchScopes.ToList()
                : DeriveWatchScopes(toolId, canonicalArgs);

            var unsignedRecord = new AuthorizationRecordDto(
                SchemaVersion: 1,
                AuthId: authId,
                TaskId: taskId,
                StepId: stepId,
                ToolId: toolId,
                CanonicalArgs: canonicalArgs,
                Expected: exp,
                AllowedEffects: effects,
                WatchScopes: scopes,
                Risk: risk,
                ApprovedBy: approvedBy,
                IssuedAt: issuedAt,
                ExpiresAt: expiresAt,
                ExecuteBefore: executeBefore,
                VerifyBefore: verifyBefore,
                Nonce: nonce,
                Hmac: string.Empty
            );

            var key = _keyStore.GetOrCreateKey();
            var payload = ComputeCanonicalRepresentation(unsignedRecord);
            var hmac = ComputeHmac(payload, key);

            var signedRecord = unsignedRecord with { Hmac = hmac };
            _recordStates[signedRecord.AuthId] = AuthorizationLifecycleState.Issued;

            _auditStore?.RecordAuthorizationLifecycleAsync(new AuthorizationLifecycleRecord(
                signedRecord.AuthId,
                signedRecord.TaskId,
                signedRecord.StepId,
                "Issued",
                signedRecord.Nonce,
                now
            ));

            return signedRecord;
        }
    }

    /// <summary>
    /// Atomically consumes the authorization record for single use by a tool executor.
    /// Thread-safe: exactly one concurrent caller succeeds; all others are rejected.
    /// Returns VerifiedAuthorization, which is required by tool executors.
    /// </summary>
    public VerifiedAuthorization Consume(AuthorizationRecordDto record, string stepId)
    {
        lock (_lock)
        {
            // 1. Verify HMAC signature
            var key = _keyStore.GetOrCreateKey();
            if (!VerifyHmac(record, key))
            {
                throw new AuthorizationSecurityException("HMAC signature mismatch: authorization record was forged or tampered.");
            }

            // 2. Validate Step ID match
            if (!string.Equals(record.StepId, stepId, StringComparison.Ordinal))
            {
                throw new AuthorizationSecurityException($"Step ID mismatch: authorized for '{record.StepId}' but consumed for '{stepId}'.");
            }

            // 3. Validate deadlines
            var now = _clock.UtcNow;
            if (DateTimeOffset.TryParse(record.ExecuteBefore, out var execDeadline) && now > execDeadline)
            {
                throw new AuthorizationSecurityException($"Execution deadline expired at {record.ExecuteBefore} (current: {now:o}).");
            }

            if (DateTimeOffset.TryParse(record.ExpiresAt, out var totalExpiry) && now > totalExpiry)
            {
                throw new AuthorizationSecurityException($"Authorization record expired at {record.ExpiresAt} (current: {now:o}).");
            }

            // 4. Validate lifecycle and single-use nonce
            if (!_recordStates.TryGetValue(record.AuthId, out var state))
            {
                throw new AuthorizationSecurityException($"Authorization record '{record.AuthId}' was never issued by this host.");
            }

            if (state == AuthorizationLifecycleState.Consumed || _consumedNonces.Contains(record.Nonce))
            {
                throw new AuthorizationSecurityException($"Authorization record '{record.AuthId}' was already consumed: single-use nonce '{record.Nonce}' cannot be reused.");
            }

            if (state != AuthorizationLifecycleState.Issued)
            {
                throw new AuthorizationSecurityException($"Authorization record is in invalid state '{state}' for execution.");
            }

            // 5. Atomically transition lifecycle state to Consumed
            _recordStates[record.AuthId] = AuthorizationLifecycleState.Consumed;
            _consumedNonces.Add(record.Nonce);

            _auditStore?.RecordAuthorizationLifecycleAsync(new AuthorizationLifecycleRecord(
                record.AuthId,
                record.TaskId,
                record.StepId,
                "Consumed",
                record.Nonce,
                now
            ));

            return new VerifiedAuthorization(record);
        }
    }

    /// <summary>
    /// Re-verifies HMAC over the actual field values of VerifiedAuthorization.
    /// Called by executors to ensure that no forged or tampered copy can execute.
    /// </summary>
    public void Consume(VerifiedAuthorization auth, string? expectedStepId = null)
    {
        lock (_lock)
        {
            // 1. Re-verify HMAC over actual fields of auth
            var key = _keyStore.GetOrCreateKey();
            if (!VerifyHmac(auth, key))
            {
                throw new AuthorizationSecurityException("HMAC signature mismatch: forged or tampered VerifiedAuthorization object.");
            }

            // 2. Validate Step ID if specified
            if (expectedStepId != null && !string.Equals(auth.StepId, expectedStepId, StringComparison.Ordinal))
            {
                throw new AuthorizationSecurityException($"Step ID mismatch: authorized for '{auth.StepId}' but executed for '{expectedStepId}'.");
            }

            // 3. Validate deadline
            var now = _clock.UtcNow;
            if (DateTimeOffset.TryParse(auth.ExecuteBefore, out var execDeadline) && now > execDeadline)
            {
                throw new AuthorizationSecurityException($"Execution deadline expired at {auth.ExecuteBefore} (current: {now:o}).");
            }

            // 4. Validate state
            if (!_recordStates.TryGetValue(auth.AuthId, out var state) || state != AuthorizationLifecycleState.Consumed)
            {
                throw new AuthorizationSecurityException($"Authorization '{auth.AuthId}' is not in Consumed state.");
            }
        }
    }

    /// <summary>
    /// Verifies the authorization record during verification stage 2.
    /// Invariant: A record that was never consumed must not verify.
    /// </summary>
    public bool Verify(AuthorizationRecordDto record)
    {
        var lifecycle = MarkVerified(record);
        _auditStore?.RecordAuthorizationLifecycleAsync(lifecycle);
        return true;
    }

    private AuthorizationLifecycleRecord MarkVerified(AuthorizationRecordDto record)
    {
        lock (_lock)
        {
            // 1. Verify HMAC signature with current key first
            var key = _keyStore.GetOrCreateKey();
            if (!VerifyHmac(record, key))
            {
                throw new AuthorizationSecurityException("HMAC signature mismatch: authorization record was tampered or signing key was regenerated.");
            }

            // 2. Invariant: A record that was never consumed must not verify
            if (!_recordStates.TryGetValue(record.AuthId, out var state))
            {
                throw new AuthorizationSecurityException($"Authorization record '{record.AuthId}' was never issued.");
            }

            if (state == AuthorizationLifecycleState.Issued)
            {
                throw new AuthorizationSecurityException($"Authorization record '{record.AuthId}' was never consumed by an executor.");
            }
            if (state == AuthorizationLifecycleState.Verified)
            {
                throw new AuthorizationSecurityException($"Authorization record '{record.AuthId}' was already verified and cannot be replayed.");
            }

            // 3. Validate deadlines
            var now = _clock.UtcNow;
            if (DateTimeOffset.TryParse(record.VerifyBefore, out var verifyDeadline) && now > verifyDeadline)
            {
                throw new AuthorizationSecurityException($"Verification deadline expired at {record.VerifyBefore} (current: {now:o}).");
            }

            if (DateTimeOffset.TryParse(record.ExpiresAt, out var totalExpiry) && now > totalExpiry)
            {
                throw new AuthorizationSecurityException($"Authorization record expired at {record.ExpiresAt} (current: {now:o}).");
            }

            // 4. Transition to Verified
            _recordStates[record.AuthId] = AuthorizationLifecycleState.Verified;

            return new AuthorizationLifecycleRecord(
                record.AuthId,
                record.TaskId,
                record.StepId,
                "Verified",
                record.Nonce,
                now
            );
        }
    }

    public async Task VerifyAndMarkVerifiedAsync(
        AuthorizationRecordDto authorization,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        cancellationToken.ThrowIfCancellationRequested();
        var lifecycle = MarkVerified(authorization);
        if (_auditStore is not null)
        {
            await _auditStore.RecordAuthorizationLifecycleAsync(lifecycle, cancellationToken).ConfigureAwait(false);
        }
    }

    public static string ComputeCanonicalRepresentation(AuthorizationRecordDto record)
    {
        var canonicalArgsJson = CanonicalizeDictionary(record.CanonicalArgs);
        var expectedJson = CanonicalizeDictionary(record.Expected);
        var allowedEffectsStr = string.Join(",", (record.AllowedEffects ?? new List<string>()).OrderBy(x => x, StringComparer.Ordinal));
        var watchScopesStr = string.Join(",", (record.WatchScopes ?? new List<string>()).OrderBy(x => x, StringComparer.Ordinal));

        return string.Join("|",
            record.AuthId,
            record.TaskId,
            record.StepId,
            record.ToolId,
            canonicalArgsJson,
            expectedJson,
            allowedEffectsStr,
            watchScopesStr,
            record.Risk,
            record.ApprovedBy,
            record.IssuedAt,
            record.ExpiresAt,
            record.ExecuteBefore,
            record.VerifyBefore,
            record.Nonce
        );
    }

    public static string ComputeCanonicalRepresentation(VerifiedAuthorization auth)
    {
        var canonicalArgsJson = CanonicalizeDictionary(auth.CanonicalArgs);
        var expectedJson = CanonicalizeDictionary(auth.Expected);
        var allowedEffectsStr = string.Join(",", (auth.AllowedEffects ?? new List<string>()).OrderBy(x => x, StringComparer.Ordinal));
        var watchScopesStr = string.Join(",", (auth.WatchScopes ?? new List<string>()).OrderBy(x => x, StringComparer.Ordinal));

        return string.Join("|",
            auth.AuthId,
            auth.TaskId,
            auth.StepId,
            auth.ToolId,
            canonicalArgsJson,
            expectedJson,
            allowedEffectsStr,
            watchScopesStr,
            auth.Risk,
            auth.ApprovedBy,
            auth.IssuedAt,
            auth.ExpiresAt,
            auth.ExecuteBefore,
            auth.VerifyBefore,
            auth.Nonce
        );
    }

    private static string CanonicalizeDictionary(IReadOnlyDictionary<string, object?>? dict)
    {
        if (dict == null || dict.Count == 0) return "{}";
        var sorted = dict.OrderBy(k => k.Key, StringComparer.Ordinal);
        var entries = sorted.Select(kv => $"\"{kv.Key}\":{FormatValue(kv.Value)}");
        return "{" + string.Join(",", entries) + "}";
    }

    private static string FormatValue(object? val)
    {
        if (val == null) return "null";
        if (val is string s) return $"\"{s}\"";
        if (val is bool b) return b ? "true" : "false";
        if (val is JsonElement je) return je.GetRawText();
        if (val is JsonNode jn) return jn.ToJsonString();
        return val.ToString() ?? "";
    }

    private static string ComputeHmac(string payload, byte[] key)
    {
        using var hmac = new HMACSHA256(key);
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static bool VerifyHmac(AuthorizationRecordDto record, byte[] key)
    {
        var expectedHmac = ComputeHmac(ComputeCanonicalRepresentation(record), key);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedHmac);
        var actualBytes = Encoding.UTF8.GetBytes(record.Hmac.ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    private static bool VerifyHmac(VerifiedAuthorization auth, byte[] key)
    {
        var expectedHmac = ComputeHmac(ComputeCanonicalRepresentation(auth), key);
        var expectedBytes = Encoding.UTF8.GetBytes(expectedHmac);
        var actualBytes = Encoding.UTF8.GetBytes(auth.Hmac.ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    private Dictionary<string, object?> DeriveExpected(
        string toolId,
        Dictionary<string, object?> args,
        string authId)
    {
        var expected = new Dictionary<string, object?>(StringComparer.Ordinal);
        switch (toolId.ToLowerInvariant())
        {
            case "fs.create_file":
                expected["exists"] = true;
                if (args.TryGetValue("path", out var p)) expected["path"] = p;
                if (args.TryGetValue("content", out var content) && content is string text)
                {
                    var contentBytes = Utf8NoBom.GetBytes(text);
                    expected["size"] = contentBytes.LongLength;
                    expected["sha256"] = Convert.ToHexString(SHA256.HashData(contentBytes));
                }
                break;
            case "fs.create_folder":
                expected["exists"] = true;
                if (args.TryGetValue("path", out var fp)) expected["path"] = fp;
                break;
            case "app.launch":
                if (args.TryGetValue("app_id", out var aid)) expected["app_id"] = aid;
                expected["running"] = true;
                break;
            case "task.schedule_reminder":
                if (args.TryGetValue("time_iso", out var time)) expected["time_iso"] = time;
                expected["scheduled"] = true;
                expected["task_path"] = $@"\Jarvis\{authId}";
                var reminderTaskId = authId["auth-".Length..];
                if (!ToolPolicy.IsValidReminderTaskId(reminderTaskId))
                    throw new InvalidOperationException("Authorization generated an invalid reminder task ID.");
                var hostPath = _hostPathProvider.GetHostPath();
                if (string.Equals(Path.GetFileName(hostPath), "dotnet.exe", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "Cannot register a reminder from dotnet.exe. Run the published Jarvis.Cli.exe host instead.");
                }
                expected["action_path"] = Path.GetFullPath(hostPath);
                expected["task_id"] = reminderTaskId;
                expected["action_arguments"] = $"--jarvis-reminder {reminderTaskId}";
                expected["reminder_data_path"] = ReminderDataPaths.GetFilePath(reminderTaskId, _reminderDataLocation.DirectoryPath);
                expected["scheduler_task_name"] = authId;
                expected["disallow_start_if_on_batteries"] = ReminderSchedulerPolicy.DisallowStartIfOnBatteries;
                expected["stop_if_going_on_batteries"] = ReminderSchedulerPolicy.StopIfGoingOnBatteries;
                expected["execution_time_limit_seconds"] = ReminderSchedulerPolicy.ExecutionTimeLimitSeconds;
                expected["start_when_available"] = ReminderSchedulerPolicy.StartWhenAvailable;
                expected["multiple_instances_policy"] = ReminderSchedulerPolicy.MultipleInstances;
                expected["logon_type"] = ReminderSchedulerPolicy.LogonType;
                expected["run_level"] = ReminderSchedulerPolicy.RunLevel;
                break;
            default:
                expected["status"] = "success";
                break;
        }
        return expected;
    }

    private static List<string> DeriveAllowedEffects(string toolId, Dictionary<string, object?> args)
    {
        var list = new List<string>();
        if (args.TryGetValue("path", out var p) && p != null)
        {
            list.Add($"create:{p}");
        }
        else if (args.TryGetValue("app_id", out var aid) && aid != null)
        {
            list.Add($"launch:{aid}");
        }
        return list;
    }

    private static List<string> DeriveWatchScopes(string toolId, Dictionary<string, object?> args)
    {
        var list = new List<string>();
        if (args.TryGetValue("path", out var p) && p != null)
        {
            var path = p.ToString()!;
            var scope = toolId.Equals("fs.create_file", StringComparison.OrdinalIgnoreCase)
                || toolId.Equals("fs.create_folder", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(Path.GetFullPath(path)) ?? path
                : path;
            list.Add(scope);
        }
        else if (args.TryGetValue("root_path", out var rp) && rp != null)
        {
            list.Add(rp.ToString()!);
        }
        return list;
    }
}
