using System.Data;
using Microsoft.Data.Sqlite;

namespace Jarvis.Infrastructure;

/// <summary>
/// SQLite audit store persisting state transitions, safety decisions,
/// and authorization lifecycle records to audit.db.
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

                CREATE INDEX IF NOT EXISTS idx_state_transitions_task_id ON state_transitions(task_id);
                CREATE INDEX IF NOT EXISTS idx_safety_decisions_task_id ON safety_decisions(task_id);
                CREATE INDEX IF NOT EXISTS idx_auth_lifecycles_auth_id ON auth_lifecycles(auth_id);
            ";
            cmd.ExecuteNonQuery();
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

    public IReadOnlyList<StateTransitionRecord> GetStateTransitions(string? taskId = null)
    {
        var list = new List<StateTransitionRecord>();
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var cmd = connection.CreateCommand();
        cmd.CommandText = taskId != null
            ? "SELECT task_id, from_state, to_state, reason, timestamp FROM state_transitions WHERE task_id = @taskId ORDER BY id ASC"
            : "SELECT task_id, from_state, to_state, reason, timestamp FROM state_transitions ORDER BY id ASC";

        if (taskId != null)
        {
            cmd.Parameters.AddWithValue("@taskId", taskId);
        }

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

        if (taskId != null)
        {
            cmd.Parameters.AddWithValue("@taskId", taskId);
        }

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

        if (authId != null)
        {
            cmd.Parameters.AddWithValue("@authId", authId);
        }

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

    public void Dispose()
    {
        // SqliteConnection pooled connections can be cleared if needed
        SqliteConnection.ClearAllPools();
    }
}
