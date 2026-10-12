using FluentAssertions;
using Jarvis.Infrastructure;
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
