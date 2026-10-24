using FluentAssertions;
using Jarvis.Infrastructure;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Jarvis.E2E.Tests;

public class SqliteAuditStoreTests : IDisposable
{
    private readonly string _dbPath;

    public SqliteAuditStoreTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"jarvis_audit_test_{Guid.NewGuid():N}.db");
    }

    [Fact]
    public async Task SqliteAuditStore_Persists_And_Retrieves_StateTransitions()
    {
        using (var store = new SqliteAuditStore(_dbPath))
        {
            var now = DateTimeOffset.UtcNow;
            await store.RecordStateTransitionAsync(new StateTransitionRecord("task-101", "created", "context_resolved", "resolved context", now));
            await store.RecordStateTransitionAsync(new StateTransitionRecord("task-101", "context_resolved", "parsed", "prompt parsed", now.AddSeconds(1)));
            await store.RecordStateTransitionAsync(new StateTransitionRecord("task-102", "created", "failed", "safety error", now.AddSeconds(2)));
        }

        // Re-open from disk to prove persistence
        using (var store2 = new SqliteAuditStore(_dbPath))
        {
            var task101Transitions = store2.GetStateTransitions("task-101");
            task101Transitions.Should().HaveCount(2);
            task101Transitions[0].FromState.Should().Be("created");
            task101Transitions[0].ToState.Should().Be("context_resolved");
            task101Transitions[1].FromState.Should().Be("context_resolved");
            task101Transitions[1].ToState.Should().Be("parsed");

            var allTransitions = store2.GetStateTransitions();
            allTransitions.Should().HaveCount(3);
        }
    }

    [Fact]
    public async Task SqliteAuditStore_Persists_And_Retrieves_SafetyDecisions()
    {
        using var store = new SqliteAuditStore(_dbPath);
        var now = DateTimeOffset.UtcNow;

        await store.RecordSafetyDecisionAsync(new SafetyDecisionRecord(
            "task-201", "step-1", "fs.create_file", "medium", "medium", "medium", "user", true, null, now));

        await store.RecordSafetyDecisionAsync(new SafetyDecisionRecord(
            "task-201", "step-2", "fs.create_folder", "medium", "high", "high", "user", false, "Path blocked", now.AddSeconds(1)));

        var decisions = store.GetSafetyDecisions("task-201");
        decisions.Should().HaveCount(2);

        decisions[0].StepId.Should().Be("step-1");
        decisions[0].Approved.Should().BeTrue();

        decisions[1].StepId.Should().Be("step-2");
        decisions[1].Approved.Should().BeFalse();
        decisions[1].Reason.Should().Be("Path blocked");
    }

    [Fact]
    public async Task SqliteAuditStore_Persists_Policy_And_Approval_Events_Separately()
    {
        using var store = new SqliteAuditStore(_dbPath);
        var timestamp = DateTimeOffset.UtcNow;
        await store.RecordPolicyValidationAsync(new PolicyValidationRecord(
            "task-audit", "step-audit", "fs.create_folder", "low", "medium", "medium", true, null, timestamp));
        await store.RecordApprovalDecisionAsync(new ApprovalDecisionRecord(
            "task-audit", "step-audit", true, "ASUS", null, timestamp.AddSeconds(1)));

        store.GetPolicyValidations("task-audit").Should().ContainSingle()
            .Which.IsValid.Should().BeTrue();
        store.GetApprovalDecisions("task-audit").Should().ContainSingle()
            .Which.Approver.Should().Be("ASUS");
    }

    [Fact]
    public void SqliteAuditStore_Migrates_Old_Database_Without_Losing_Legacy_Rows()
    {
        using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE safety_decisions (
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
                INSERT INTO safety_decisions (
                    task_id, step_id, tool_id, advisory_risk, policy_risk, final_risk,
                    approved_by, approved, reason, timestamp
                ) VALUES (
                    'legacy-task', 'legacy-step', 'fs.create_folder', 'low', 'medium', 'medium',
                    'system', 1, NULL, '2026-01-01T00:00:00.0000000+00:00'
                );";
            command.ExecuteNonQuery();
        }

        using var store = new SqliteAuditStore(_dbPath);

        store.GetSafetyDecisions("legacy-task").Should().ContainSingle()
            .Which.ApprovedBy.Should().Be("system");
        store.GetPolicyValidations("legacy-task").Should().BeEmpty();
        store.GetApprovalDecisions("legacy-task").Should().BeEmpty();
    }

    [Fact]
    public async Task SqliteAuditStore_Persists_And_Retrieves_AuthorizationLifecycles()
    {
        using var store = new SqliteAuditStore(_dbPath);
        var now = DateTimeOffset.UtcNow;

        await store.RecordAuthorizationLifecycleAsync(new AuthorizationLifecycleRecord(
            "auth-301", "task-301", "step-1", "Issued", "nonce-1", now));
        await store.RecordAuthorizationLifecycleAsync(new AuthorizationLifecycleRecord(
            "auth-301", "task-301", "step-1", "Consumed", "nonce-1", now.AddSeconds(1)));

        var lifecycles = store.GetAuthorizationLifecycle("auth-301");
        lifecycles.Should().HaveCount(2);
        lifecycles[0].State.Should().Be("Issued");
        lifecycles[1].State.Should().Be("Consumed");
    }

    [Fact]
    public async Task SqliteAuditStore_Persists_And_Retrieves_Section10_Remaining_Tables()
    {
        var now = DateTimeOffset.UtcNow;
        using (var store = new SqliteAuditStore(_dbPath))
        {
            // 1. tasks: TaskId, Prompt, SessionId, State, CreatedAt, UpdatedAt
            await store.RecordTaskAsync(new TaskAuditRecord("task-501", "create folder", "sess-1", "completed", now, now.AddSeconds(10)));

            // 2. steps: TaskId, StepId, ToolId, OrderIndex, State, Risk, CreatedAt
            await store.RecordStepAsync(new StepAuditRecord("task-501", "step-501-1", "fs.create_folder", 1, "verified", "low", now));

            // 3. executions: StepId, AuthId, ExitCode, ReportedSuccess, Stdout, Stderr, StartedAt, CompletedAt
            await store.RecordExecutionAsync(new ExecutionAuditRecord("step-501-1", "auth-1", 0, true, "out", "", now, now.AddSeconds(1)));

            // 4. verifications: StepId, OverallStatus, Summary, VerifiedAt
            await store.RecordVerificationAsync(new VerificationAuditRecord("step-501-1", "VERIFIED", "All checks matched", now));

            // 5. evidence: EvidenceId, StepId, Collector, CheckType, Match, CollectedAt, Nonce, RecordSha256
            await store.RecordEvidenceAsync(new EvidenceAuditRecord("ev-1", "step-501-1", "file_collector", "dir_exists", true, now, "nonce-ev", "sha256-hex"));

            // 6. api_calls: CallId, Endpoint, Method, TraceId, StatusCode, DurationMs, Timestamp
            await store.RecordApiCallAsync(new ApiCallAuditRecord("call-1", "/v1/plan", "POST", "trace-xyz", 200, 45.0, now));
        }

        // Reopen database to verify persistence
        using (var store2 = new SqliteAuditStore(_dbPath))
        {
            var tasks = store2.GetTasks();
            tasks.Should().ContainSingle(t => t.TaskId == "task-501" && t.State == "completed");

            var steps = store2.GetSteps("task-501");
            steps.Should().ContainSingle(s => s.StepId == "step-501-1" && s.ToolId == "fs.create_folder");

            var executions = store2.GetExecutions("step-501-1");
            executions.Should().ContainSingle(e => e.StepId == "step-501-1" && e.ReportedSuccess == true && e.AuthId == "auth-1");

            var verifications = store2.GetVerifications("step-501-1");
            verifications.Should().ContainSingle(v => v.StepId == "step-501-1" && v.OverallStatus == "VERIFIED");

            var evidence = store2.GetEvidence("step-501-1");
            evidence.Should().ContainSingle(ev => ev.EvidenceId == "ev-1" && ev.CheckType == "dir_exists" && ev.Match == true);

            var apiCalls = store2.GetApiCalls("trace-xyz");
            apiCalls.Should().ContainSingle(a => a.CallId == "call-1" && a.Endpoint == "/v1/plan" && a.StatusCode == 200);
        }
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_dbPath))
            {
                File.Delete(_dbPath);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }
}
