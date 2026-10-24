using System.Text.Json.Nodes;
using FluentAssertions;
using Jarvis.Contracts;
using Jarvis.Core;
using Jarvis.Infrastructure;
using Jarvis.Safety;
using Xunit;

namespace Jarvis.Safety.Tests;

public class SafetyGateTests
{
    private class FakeApprovalProvider : IApprovalHandler
    {
        public ApprovalDecision NextApprovalResult { get; set; } = new(true, "test-user");
        public int CallCount { get; private set; }
        public ApprovalRequest? LastRequest { get; private set; }

        public Task<ApprovalDecision> RequestApprovalAsync(ApprovalRequest request, CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(NextApprovalResult);
        }
    }

    private static readonly string ConfigDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "config"));
    private readonly ToolPolicy _toolPolicy;
    private readonly AuthorizationService _authService;
    private readonly FakeApprovalProvider _approvalProvider;
    private readonly InMemoryAuditStore _auditStore;
    private readonly SafetyGate _safetyGate;

    public SafetyGateTests()
    {
        var toolsRegistryPath = Path.Combine(ConfigDir, "tools.registry.json");
        var appsAllowlistPath = Path.Combine(ConfigDir, "apps.allowlist.json");
        _toolPolicy = ToolPolicy.FromFile(toolsRegistryPath, appsAllowlistPath);

        var keyStore = new InMemoryDpapiKeyStore();
        _authService = new AuthorizationService(keyStore, SystemClock.Instance);
        _approvalProvider = new FakeApprovalProvider();
        _auditStore = new InMemoryAuditStore();

        _safetyGate = new SafetyGate(_toolPolicy, _authService, _approvalProvider, _auditStore);
    }

    [Theory]
    [InlineData("low", "low", "low")]
    [InlineData("low", "medium", "medium")]
    [InlineData("medium", "low", "medium")]
    [InlineData("medium", "medium", "medium")]
    [InlineData("low", "high", "high")]
    [InlineData("high", "low", "high")]
    [InlineData("medium", "high", "high")]
    [InlineData("high", "high", "high")]
    public void MaxRisk_Calculates_Authoritative_Maximum(string advisory, string policy, string expectedFinal)
    {
        SafetyGate.MaxRisk(advisory, policy).Should().Be(expectedFinal);
    }

    [Fact]
    public async Task Reminder_Without_Title_Is_Canonicalized_Before_Human_Approval_And_Signing()
    {
        var args = new JsonObject
        {
            ["time_iso"] = "2026-10-21T12:00:00Z",
            ["message"] = new string('a', 62) + "😀"
        };
        var result = await _safetyGate.EvaluateAndAuthorizeAsync("task-reminder", "step-reminder", "task.schedule_reminder", args, "medium");

        result.Approved.Should().BeTrue();
        _approvalProvider.LastRequest!.CanonicalArgs["task_name"].Should().Be(new string('a', 62));
        result.Record!.CanonicalArgs["task_name"].Should().Be(new string('a', 62));
        result.Record.Hmac.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Low_Risk_Tool_Is_Auto_Approved_Without_Provider_Prompt()
    {
        var args = new JsonObject
        {
            ["pattern"] = "*.txt"
        };

        var result = await _safetyGate.EvaluateAndAuthorizeAsync(
            taskId: "task-100",
            stepId: "step-100",
            toolId: "fs.search_files",
            rawArgs: args,
            advisoryRisk: "low");

        result.Approved.Should().BeTrue();
        result.Record.Should().NotBeNull();
        result.Record!.Risk.Should().Be("low");
        result.Record.ApprovedBy.Should().Be("auto-low");

        _approvalProvider.CallCount.Should().Be(0);

        var decisions = _auditStore.GetApprovalDecisions("task-100");
        decisions.Should().ContainSingle();
        decisions[0].Approved.Should().BeTrue();
        decisions[0].Approver.Should().Be("auto-low");
        _auditStore.GetPolicyValidations("task-100").Should().ContainSingle(record => record.IsValid);
    }

    [Fact]
    public async Task Medium_Risk_Tool_Approved_By_Provider_Returns_Authorized_Record()
    {
        _approvalProvider.NextApprovalResult = new ApprovalDecision(true, "test-user");

        var args = new JsonObject
        {
            ["path"] = @"C:\Users\test\Documents\Jarvis\newfolder"
        };

        var result = await _safetyGate.EvaluateAndAuthorizeAsync(
            taskId: "task-101",
            stepId: "step-101",
            toolId: "fs.create_folder",
            rawArgs: args,
            advisoryRisk: "low"); // Policy risk is medium -> final is medium

        result.Approved.Should().BeTrue();
        result.Record.Should().NotBeNull();
        result.Record!.Risk.Should().Be("medium");
        result.Record.ApprovedBy.Should().Be("test-user");

        _approvalProvider.CallCount.Should().Be(1);

        var decisions = _auditStore.GetApprovalDecisions("task-101");
        decisions.Should().ContainSingle();
        decisions[0].Approved.Should().BeTrue();
        decisions[0].Approver.Should().Be("test-user");
        _auditStore.GetPolicyValidations("task-101").Should().ContainSingle(record => record.IsValid);
    }

    [Fact]
    public async Task Medium_Risk_Tool_Denied_By_Provider_Rejects_Execution()
    {
        _approvalProvider.NextApprovalResult = new ApprovalDecision(false, "test-user", "User denied execution.");

        var args = new JsonObject
        {
            ["path"] = @"C:\Users\test\Documents\Jarvis\newfolder"
        };

        var result = await _safetyGate.EvaluateAndAuthorizeAsync(
            taskId: "task-102",
            stepId: "step-102",
            toolId: "fs.create_folder",
            rawArgs: args,
            advisoryRisk: "medium");

        result.Approved.Should().BeFalse();
        result.Record.Should().BeNull();
        result.ErrorMessage.Should().Contain("denied");

        _approvalProvider.CallCount.Should().Be(1);

        var decisions = _auditStore.GetApprovalDecisions("task-102");
        decisions.Should().ContainSingle();
        decisions[0].Approved.Should().BeFalse();
        decisions[0].Approver.Should().Be("test-user");
    }

    [Fact]
    public async Task Invalid_Tool_Fails_Immediately_Without_Approval()
    {
        var args = new JsonObject();

        var result = await _safetyGate.EvaluateAndAuthorizeAsync(
            taskId: "task-103",
            stepId: "step-103",
            toolId: "nonexistent.tool",
            rawArgs: args,
            advisoryRisk: "low");

        result.Approved.Should().BeFalse();
        _approvalProvider.CallCount.Should().Be(0);

        _auditStore.GetPolicyValidations("task-103").Should().ContainSingle(record => !record.IsValid);
        _auditStore.GetApprovalDecisions("task-103").Should().BeEmpty();
    }

    [Fact]
    public async Task Approval_Timeout_Is_Audited_As_Timeout()
    {
        _approvalProvider.NextApprovalResult = new ApprovalDecision(false, "timeout", "Approval timed out.");

        var result = await _safetyGate.EvaluateAndAuthorizeAsync(
            "task-timeout",
            "step-timeout",
            "fs.create_folder",
            new JsonObject { ["path"] = @"C:\Users\test\Documents\Jarvis\newfolder" },
            "medium");

        result.Approved.Should().BeFalse();
        _auditStore.GetApprovalDecisions("task-timeout").Should().ContainSingle()
            .Which.Approver.Should().Be("timeout");
    }
}
