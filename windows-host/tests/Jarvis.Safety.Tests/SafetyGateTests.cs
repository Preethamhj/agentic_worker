using System.Text.Json.Nodes;
using FluentAssertions;
using Jarvis.Core;
using Jarvis.Infrastructure;
using Jarvis.Safety;
using Xunit;

namespace Jarvis.Safety.Tests;

public class SafetyGateTests
{
    private class FakeApprovalProvider : IApprovalProvider
    {
        public bool NextApprovalResult { get; set; } = true;
        public int CallCount { get; private set; }
        public ApprovalRequest? LastRequest { get; private set; }

        public Task<bool> RequestApprovalAsync(ApprovalRequest request, CancellationToken cancellationToken = default)
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
        result.Record.ApprovedBy.Should().Be("auto");

        _approvalProvider.CallCount.Should().Be(0);

        var decisions = _auditStore.GetSafetyDecisions("task-100");
        decisions.Should().HaveCount(1);
        decisions[0].Approved.Should().BeTrue();
        decisions[0].ApprovedBy.Should().Be("auto");
    }

    [Fact]
    public async Task Medium_Risk_Tool_Approved_By_Provider_Returns_Authorized_Record()
    {
        _approvalProvider.NextApprovalResult = true;

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
        result.Record.ApprovedBy.Should().Be("user");

        _approvalProvider.CallCount.Should().Be(1);

        var decisions = _auditStore.GetSafetyDecisions("task-101");
        decisions.Should().HaveCount(1);
        decisions[0].Approved.Should().BeTrue();
        decisions[0].ApprovedBy.Should().Be("user");
        decisions[0].FinalRisk.Should().Be("medium");
    }

    [Fact]
    public async Task Medium_Risk_Tool_Denied_By_Provider_Rejects_Execution()
    {
        _approvalProvider.NextApprovalResult = false;

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

        var decisions = _auditStore.GetSafetyDecisions("task-102");
        decisions.Should().HaveCount(1);
        decisions[0].Approved.Should().BeFalse();
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

        var decisions = _auditStore.GetSafetyDecisions("task-103");
        decisions.Should().HaveCount(1);
        decisions[0].Approved.Should().BeFalse();
    }
}
