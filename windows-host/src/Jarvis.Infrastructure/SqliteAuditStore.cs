using System.Data;
using Microsoft.Data.Sqlite;

namespace Jarvis.Infrastructure;

/// <summary>
/// SQLite audit store persisting all DESIGN.md section 10 audit tables
/// (tasks, steps, executions, verifications, evidence, api_calls, state_transitions,
/// safety_decisions, auth_lifecycles) to audit.db.
/// </summary>
public sealed class SqliteAuditStore : IAuditStore, IDisposable
{
    private readonly string _connectionString;
    private readonly object _lock = new();

    public SqliteAuditStore(string dbPathOrConnectionString)
    {
        if (string.IsNullOrWhiteSpace(dbPathOrConnectionString))
            throw new ArgumentException("Database path or connection string cannot be empty.", nameof(dbPathOrConnectionString));

        _connectionString = dbPathOrConnectionString.Contains("Data Source=", StringComparison.OrdinalIgnoreCase)
            ? dbPathOrConnectionString
            : $"Data Source={dbPathOrConnectionString}";

        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        lock (_lock)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS state_transitions (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    task_id TEXT NOT NULL,
                    from_state TEXT NOT NULL,
                    to_state TEXT NOT NULL,
                    reason TEXT,
                    timestamp TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS safety_decisions (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    task_id TEXT NOT NULL,
                    step_id TEXT NOT NULL,
                    tool_id TEXT NOT NULL,
                    advisory_risk TEXT NOT NULL,
                    policy_risk TEXT NOT NULL,
                    final_risk TEXT NOT NULL,
                    approved_by TEXT NOT NULL,
                    approved INTEGER NOT NULL,
                    reason TEXT,
                    timestamp TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS auth_lifecycles (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    auth_id TEXT NOT NULL,
                    task_id TEXT NOT NULL,
                    step_id TEXT NOT NULL,
                    state TEXT NOT NULL,
                    nonce TEXT NOT NULL,
                    timestamp TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS tasks (
                    task_id TEXT PRIMARY KEY,
                    prompt TEXT NOT NULL,
                    session_id TEXT,
                    state TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    updated_at TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS steps (
                    task_id TEXT NOT NULL,
                    step_id TEXT NOT NULL,
                    tool_id TEXT NOT NULL,
                    order_index INTEGER NOT NULL,
                    state TEXT NOT NULL,
                    risk TEXT NOT NULL,
                    created_at TEXT NOT NULL,
                    PRIMARY KEY (task_id, step_id)
                );

                CREATE TABLE IF NOT EXISTS executions (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    step_id TEXT NOT NULL,
                    auth_id TEXT NOT NULL,
                    exit_code INTEGER NOT NULL,
                    reported_success INTEGER NOT NULL,
                    stdout TEXT,
                    stderr TEXT,
                    started_at TEXT NOT NULL,
                    completed_at TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS verifications (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    step_id TEXT NOT NULL,
                    overall_status TEXT NOT NULL,
                    summary TEXT,
                    verified_at TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS evidence (
                    evidence_id TEXT PRIMARY KEY,
                    step_id TEXT NOT NULL,
                    collector TEXT NOT NULL,
                    check_type TEXT NOT NULL,
                    match INTEGER NOT NULL,
                    collected_at TEXT NOT NULL,
                    nonce TEXT NOT NULL,
                    record_sha256 TEXT NOT NULL,
                    record_json TEXT NOT NULL DEFAULT '{}'
                );

                CREATE TABLE IF NOT EXISTS api_calls (
                    call_id TEXT PRIMARY KEY,
                    endpoint TEXT NOT NULL,
                    method TEXT NOT NULL,
                    trace_id TEXT NOT NULL,
                    status_code INTEGER NOT NULL,
                    duration_ms REAL NOT NULL,
                    timestamp TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_state_transitions_task_id ON state_transitions(task_id);
                CREATE INDEX IF NOT EXISTS idx_safety_decisions_task_id ON safety_decisions(task_id);
                CREATE INDEX IF NOT EXISTS idx_auth_lifecycles_auth_id ON auth_lifecycles(auth_id);
                CREATE INDEX IF NOT EXISTS idx_steps_task_id ON steps(task_id);
                CREATE INDEX IF NOT EXISTS idx_executions_step_id ON executions(step_id);
                CREATE INDEX IF NOT EXISTS idx_verifications_step_id ON verifications(step_id);
                CREATE INDEX IF NOT EXISTS idx_evidence_step_id ON evidence(step_id);
                CREATE INDEX IF NOT EXISTS idx_api_calls_trace_id ON api_calls(trace_id);
            ";
            cmd.ExecuteNonQuery();

            using var evidenceColumns = connection.CreateCommand();
            evidenceColumns.CommandText = "PRAGMA table_info(evidence);";
            using var reader = evidenceColumns.ExecuteReader();
            var hasRecordJson = false;
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), "record_json", StringComparison.OrdinalIgnoreCase))
                    hasRecordJson = true;
            }
            reader.Close();
            if (!hasRecordJson)
            {
                using var migration = connection.CreateCommand();
                migration.CommandText = "ALTER TABLE evidence ADD COLUMN record_json TEXT NOT NULL DEFAULT '{}';";
                migration.ExecuteNonQuery();
            }
        }
    }

    public async Task RecordStateTransitionAsync(StateTransitionRecord record, CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO state_transitions (task_id, from_state, to_state, reason, timestamp)
            VALUES (@taskId, @fromState, @toState, @reason, @timestamp);
        ";
        cmd.Parameters.AddWithValue("@taskId", record.TaskId);
        cmd.Parameters.AddWithValue("@fromState", record.FromState);
        cmd.Parameters.AddWithValue("@toState", record.ToState);
        cmd.Parameters.AddWithValue("@reason", (object?)record.Reason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@timestamp", record.Timestamp.ToString("o"));

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RecordSafetyDecisionAsync(SafetyDecisionRecord record, CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO safety_decisions (task_id, step_id, tool_id, advisory_risk, policy_risk, final_risk, approved_by, approved, reason, timestamp)
            VALUES (@taskId, @stepId, @toolId, @advisoryRisk, @policyRisk, @finalRisk, @approvedBy, @approved, @reason, @timestamp);
        ";
        cmd.Parameters.AddWithValue("@taskId", record.TaskId);
        cmd.Parameters.AddWithValue("@stepId", record.StepId);
        cmd.Parameters.AddWithValue("@toolId", record.ToolId);
        cmd.Parameters.AddWithValue("@advisoryRisk", record.AdvisoryRisk);
        cmd.Parameters.AddWithValue("@policyRisk", record.PolicyRisk);
        cmd.Parameters.AddWithValue("@finalRisk", record.FinalRisk);
        cmd.Parameters.AddWithValue("@approvedBy", record.ApprovedBy);
        cmd.Parameters.AddWithValue("@approved", record.Approved ? 1 : 0);
        cmd.Parameters.AddWithValue("@reason", (object?)record.Reason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@timestamp", record.Timestamp.ToString("o"));

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RecordAuthorizationLifecycleAsync(AuthorizationLifecycleRecord record, CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO auth_lifecycles (auth_id, task_id, step_id, state, nonce, timestamp)
            VALUES (@authId, @taskId, @stepId, @state, @nonce, @timestamp);
        ";
        cmd.Parameters.AddWithValue("@authId", record.AuthId);
        cmd.Parameters.AddWithValue("@taskId", record.TaskId);
        cmd.Parameters.AddWithValue("@stepId", record.StepId);
        cmd.Parameters.AddWithValue("@state", record.State);
        cmd.Parameters.AddWithValue("@nonce", record.Nonce);
        cmd.Parameters.AddWithValue("@timestamp", record.Timestamp.ToString("o"));

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RecordTaskAsync(TaskAuditRecord record, CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO tasks (task_id, prompt, session_id, state, created_at, updated_at)
            VALUES (@taskId, @prompt, @sessionId, @state, @createdAt, @updatedAt)
            ON CONFLICT(task_id) DO UPDATE SET state = excluded.state, updated_at = excluded.updated_at;
        ";
        cmd.Parameters.AddWithValue("@taskId", record.TaskId);
        cmd.Parameters.AddWithValue("@prompt", record.Prompt);
        cmd.Parameters.AddWithValue("@sessionId", (object?)record.SessionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@state", record.State);
        cmd.Parameters.AddWithValue("@createdAt", record.CreatedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@updatedAt", record.UpdatedAt.ToString("o"));

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RecordStepAsync(StepAuditRecord record, CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO steps (task_id, step_id, tool_id, order_index, state, risk, created_at)
            VALUES (@taskId, @stepId, @toolId, @orderIndex, @state, @risk, @createdAt)
            ON CONFLICT(task_id, step_id) DO UPDATE SET state = excluded.state;
        ";
        cmd.Parameters.AddWithValue("@taskId", record.TaskId);
        cmd.Parameters.AddWithValue("@stepId", record.StepId);
        cmd.Parameters.AddWithValue("@toolId", record.ToolId);
        cmd.Parameters.AddWithValue("@orderIndex", record.OrderIndex);
        cmd.Parameters.AddWithValue("@state", record.State);
        cmd.Parameters.AddWithValue("@risk", record.Risk);
        cmd.Parameters.AddWithValue("@createdAt", record.CreatedAt.ToString("o"));

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RecordExecutionAsync(ExecutionAuditRecord record, CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO executions (step_id, auth_id, exit_code, reported_success, stdout, stderr, started_at, completed_at)
            VALUES (@stepId, @authId, @exitCode, @reportedSuccess, @stdout, @stderr, @startedAt, @completedAt);
        ";
        cmd.Parameters.AddWithValue("@stepId", record.StepId);
        cmd.Parameters.AddWithValue("@authId", record.AuthId);
        cmd.Parameters.AddWithValue("@exitCode", record.ExitCode);
        cmd.Parameters.AddWithValue("@reportedSuccess", record.ReportedSuccess ? 1 : 0);
        cmd.Parameters.AddWithValue("@stdout", (object?)record.Stdout ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@stderr", (object?)record.Stderr ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@startedAt", record.StartedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@completedAt", record.CompletedAt.ToString("o"));

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RecordVerificationAsync(VerificationAuditRecord record, CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO verifications (step_id, overall_status, summary, verified_at)
            VALUES (@stepId, @overallStatus, @summary, @verifiedAt);
        ";
        cmd.Parameters.AddWithValue("@stepId", record.StepId);
        cmd.Parameters.AddWithValue("@overallStatus", record.OverallStatus);
        cmd.Parameters.AddWithValue("@summary", (object?)record.Summary ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@verifiedAt", record.VerifiedAt.ToString("o"));

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RecordEvidenceAsync(EvidenceAuditRecord record, CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO evidence (evidence_id, step_id, collector, check_type, match, collected_at, nonce, record_sha256, record_json)
            VALUES (@evidenceId, @stepId, @collector, @checkType, @match, @collectedAt, @nonce, @recordSha256, @recordJson);
        ";
        cmd.Parameters.AddWithValue("@evidenceId", record.EvidenceId);
        cmd.Parameters.AddWithValue("@stepId", record.StepId);
        cmd.Parameters.AddWithValue("@collector", record.Collector);
        cmd.Parameters.AddWithValue("@checkType", record.CheckType);
        cmd.Parameters.AddWithValue("@match", record.Match ? 1 : 0);
        cmd.Parameters.AddWithValue("@collectedAt", record.CollectedAt.ToString("o"));
        cmd.Parameters.AddWithValue("@nonce", record.Nonce);
        cmd.Parameters.AddWithValue("@recordSha256", record.RecordSha256);
        cmd.Parameters.AddWithValue("@recordJson", record.RecordJson);

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RecordApiCallAsync(ApiCallAuditRecord record, CancellationToken cancellationToken = default)
    {
        using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        using var cmd = connection.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO api_calls (call_id, endpoint, method, trace_id, status_code, duration_ms, timestamp)
            VALUES (@callId, @endpoint, @method, @traceId, @statusCode, @durationMs, @timestamp);
        ";
        cmd.Parameters.AddWithValue("@callId", record.CallId);
        cmd.Parameters.AddWithValue("@endpoint", record.Endpoint);
        cmd.Parameters.AddWithValue("@method", record.Method);
        cmd.Parameters.AddWithValue("@traceId", record.TraceId);
        cmd.Parameters.AddWithValue("@statusCode", record.StatusCode);
        cmd.Parameters.AddWithValue("@durationMs", record.DurationMs);
        cmd.Parameters.AddWithValue("@timestamp", record.Timestamp.ToString("o"));

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public IReadOnlyList<StateTransitionRecord> GetStateTransitions(string? taskId = null)
    {
        var list = new List<StateTransitionRecord>();
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = taskId != null
            ? "SELECT task_id, from_state, to_state, reason, timestamp FROM state_transitions WHERE task_id = @taskId ORDER BY id ASC"
            : "SELECT task_id, from_state, to_state, reason, timestamp FROM state_transitions ORDER BY id ASC";

        if (taskId != null) cmd.Parameters.AddWithValue("@taskId", taskId);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new StateTransitionRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                DateTimeOffset.Parse(reader.GetString(4))
            ));
        }

        return list;
    }

    public IReadOnlyList<SafetyDecisionRecord> GetSafetyDecisions(string? taskId = null)
    {
        var list = new List<SafetyDecisionRecord>();
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = taskId != null
            ? "SELECT task_id, step_id, tool_id, advisory_risk, policy_risk, final_risk, approved_by, approved, reason, timestamp FROM safety_decisions WHERE task_id = @taskId ORDER BY id ASC"
            : "SELECT task_id, step_id, tool_id, advisory_risk, policy_risk, final_risk, approved_by, approved, reason, timestamp FROM safety_decisions ORDER BY id ASC";

        if (taskId != null) cmd.Parameters.AddWithValue("@taskId", taskId);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new SafetyDecisionRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetInt32(7) == 1,
                reader.IsDBNull(8) ? null : reader.GetString(8),
                DateTimeOffset.Parse(reader.GetString(9))
            ));
        }

        return list;
    }

    public IReadOnlyList<AuthorizationLifecycleRecord> GetAuthorizationLifecycle(string? authId = null)
    {
        var list = new List<AuthorizationLifecycleRecord>();
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = authId != null
            ? "SELECT auth_id, task_id, step_id, state, nonce, timestamp FROM auth_lifecycles WHERE auth_id = @authId ORDER BY id ASC"
            : "SELECT auth_id, task_id, step_id, state, nonce, timestamp FROM auth_lifecycles ORDER BY id ASC";

        if (authId != null) cmd.Parameters.AddWithValue("@authId", authId);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new AuthorizationLifecycleRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                DateTimeOffset.Parse(reader.GetString(5))
            ));
        }

        return list;
    }

    public IReadOnlyList<TaskAuditRecord> GetTasks()
    {
        var list = new List<TaskAuditRecord>();
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT task_id, prompt, session_id, state, created_at, updated_at FROM tasks ORDER BY created_at ASC";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new TaskAuditRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetString(3),
                DateTimeOffset.Parse(reader.GetString(4)),
                DateTimeOffset.Parse(reader.GetString(5))
            ));
        }

        return list;
    }

    public IReadOnlyList<StepAuditRecord> GetSteps(string? taskId = null)
    {
        var list = new List<StepAuditRecord>();
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = taskId != null
            ? "SELECT task_id, step_id, tool_id, order_index, state, risk, created_at FROM steps WHERE task_id = @taskId ORDER BY order_index ASC"
            : "SELECT task_id, step_id, tool_id, order_index, state, risk, created_at FROM steps ORDER BY task_id, order_index ASC";

        if (taskId != null) cmd.Parameters.AddWithValue("@taskId", taskId);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new StepAuditRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetString(4),
                reader.GetString(5),
                DateTimeOffset.Parse(reader.GetString(6))
            ));
        }

        return list;
    }

    public IReadOnlyList<ExecutionAuditRecord> GetExecutions(string? stepId = null)
    {
        var list = new List<ExecutionAuditRecord>();
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = stepId != null
            ? "SELECT step_id, auth_id, exit_code, reported_success, stdout, stderr, started_at, completed_at FROM executions WHERE step_id = @stepId ORDER BY id ASC"
            : "SELECT step_id, auth_id, exit_code, reported_success, stdout, stderr, started_at, completed_at FROM executions ORDER BY id ASC";

        if (stepId != null) cmd.Parameters.AddWithValue("@stepId", stepId);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new ExecutionAuditRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetInt32(3) == 1,
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                DateTimeOffset.Parse(reader.GetString(6)),
                DateTimeOffset.Parse(reader.GetString(7))
            ));
        }

        return list;
    }

    public IReadOnlyList<VerificationAuditRecord> GetVerifications(string? stepId = null)
    {
        var list = new List<VerificationAuditRecord>();
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = stepId != null
            ? "SELECT step_id, overall_status, summary, verified_at FROM verifications WHERE step_id = @stepId ORDER BY id ASC"
            : "SELECT step_id, overall_status, summary, verified_at FROM verifications ORDER BY id ASC";

        if (stepId != null) cmd.Parameters.AddWithValue("@stepId", stepId);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new VerificationAuditRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                DateTimeOffset.Parse(reader.GetString(3))
            ));
        }

        return list;
    }

    public IReadOnlyList<EvidenceAuditRecord> GetEvidence(string? stepId = null)
    {
        var list = new List<EvidenceAuditRecord>();
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = stepId != null
            ? "SELECT evidence_id, step_id, collector, check_type, match, collected_at, nonce, record_sha256, record_json FROM evidence WHERE step_id = @stepId ORDER BY collected_at ASC"
            : "SELECT evidence_id, step_id, collector, check_type, match, collected_at, nonce, record_sha256, record_json FROM evidence ORDER BY collected_at ASC";

        if (stepId != null) cmd.Parameters.AddWithValue("@stepId", stepId);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new EvidenceAuditRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4) == 1,
                DateTimeOffset.Parse(reader.GetString(5)),
                reader.GetString(6),
                reader.GetString(7),
                reader.GetString(8)
            ));
        }

        return list;
    }

    public IReadOnlyList<ApiCallAuditRecord> GetApiCalls(string? traceId = null)
    {
        var list = new List<ApiCallAuditRecord>();
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = traceId != null
            ? "SELECT call_id, endpoint, method, trace_id, status_code, duration_ms, timestamp FROM api_calls WHERE trace_id = @traceId ORDER BY timestamp ASC"
            : "SELECT call_id, endpoint, method, trace_id, status_code, duration_ms, timestamp FROM api_calls ORDER BY timestamp ASC";

        if (traceId != null) cmd.Parameters.AddWithValue("@traceId", traceId);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new ApiCallAuditRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetDouble(5),
                DateTimeOffset.Parse(reader.GetString(6))
            ));
        }

        return list;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
    }
}
