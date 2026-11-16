using System.Text.Json;
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
    [Theory]
    [InlineData("fs.create_file", "path", true)]
    [InlineData("fs.create_file", "path", false)]
    [InlineData("fs.create_folder", "path", true)]
    [InlineData("fs.search_files", "root_path", true)]
    public async Task EvaluateStep_Rejects_Outside_Root_Regardless_Of_Argument_Representation(
        string toolId, string pathKey, bool useJsonValues)
    {
        var root = AppContext.BaseDirectory;
        var outside = Path.Combine(Path.GetPathRoot(root)!, "JarvisOutsideTest", "blocked");
        var args = new Dictionary<string, object?> { [pathKey] = outside };
        if (toolId == "fs.create_file")
            args["content"] = "test";
        if (toolId == "fs.search_files")
            args["pattern"] = "*.txt";
        if (useJsonValues)
            args = JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(args))!;
        var gate = new SafetyGate(_toolPolicy, _authService, auditStore: _auditStore, pathPolicy: new PathPolicy([root]));

        var result = await gate.EvaluateStepAsync("task-path", "step-path", toolId, args);

        result.IsAllowed.Should().BeFalse();
        result.RejectionReason.Should().Contain("outside allowed roots");
        _auditStore.GetPolicyValidations("task-path").Should().ContainSingle(entry => !entry.IsValid);
        _auditStore.GetApprovalDecisions("task-path").Should().BeEmpty();
    }

    [Fact]
    public async Task EvaluateStep_Allows_Validated_Json_String_Inside_Root()
    {
        var root = AppContext.BaseDirectory;
        var path = Path.Combine(root, "safe.txt");
        var args = JsonSerializer.Deserialize<Dictionary<string, object?>>(
            JsonSerializer.Serialize(new { path, content = "test" }))!;
        var gate = new SafetyGate(_toolPolicy, _authService, auditStore: _auditStore, pathPolicy: new PathPolicy([root]));

        var result = await gate.EvaluateStepAsync("task-inside", "step-inside", "fs.create_file", args);

        result.IsAllowed.Should().BeTrue();
        result.CanonicalArgs!["path"].Should().Be(path);
    }

    [Fact]
    public async Task EvaluateStep_Rejects_NonString_Json_Path()
    {
        var args = JsonSerializer.Deserialize<Dictionary<string, object?>>("""{"path":123,"content":"test"}""")!;
        var gate = new SafetyGate(_toolPolicy, _authService, auditStore: _auditStore, pathPolicy: new PathPolicy([AppContext.BaseDirectory]));

        var result = await gate.EvaluateStepAsync("task-invalid", "step-invalid", "fs.create_file", args);

        result.IsAllowed.Should().BeFalse();
        _auditStore.GetPolicyValidations("task-invalid").Should().ContainSingle(entry => !entry.IsValid);
    }

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
    public async Task Browser_Open_Link_Requires_Approval_And_Signs_Only_Target_Fingerprint()
    {
        const string targetHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var args = new JsonObject
        {
            ["link_id"] = "l123456789abc",
            ["observation_id"] = "11111111-1111-1111-1111-111111111111",
            ["tab_id"] = "22222222222222222222222222222222",
            ["target_hash"] = targetHash,
            ["target_host"] = "example.com",
            ["mode"] = "new_tab"
        };

        var result = await _safetyGate.EvaluateAndAuthorizeAsync(
            "task-browser-link", "step-browser-link", "browser.open_link", args, "medium");

        result.Approved.Should().BeTrue();
        _approvalProvider.CallCount.Should().Be(1);
        _approvalProvider.LastRequest!.ToolId.Should().Be("browser.open_link");
        _approvalProvider.LastRequest.CanonicalArgs["target_hash"].Should().Be(targetHash);
        _approvalProvider.LastRequest.CanonicalArgs["mode"].Should().Be("new_tab");
        result.Record!.CanonicalArgs["target_hash"].Should().Be(targetHash);
        result.Record.CanonicalArgs["mode"].Should().Be("new_tab");
        result.Record.Expected["mode"].Should().Be("new_tab");
        result.Record.CanonicalArgs.Should().NotContainKey("url");
        result.Record.AllowedEffects.Should().Contain("browser:open-link:l123456789abc");
        var tamperedArgs = new Dictionary<string, object?>(result.Record.CanonicalArgs) { ["mode"] = "same_tab" };
        Action consume = () => _authService.Consume(result.Record with { CanonicalArgs = tamperedArgs }, result.Record.StepId);
        consume.Should().Throw<AuthorizationSecurityException>().WithMessage("*HMAC*");
        _authService.Consume(result.Record, result.Record.StepId).Record.CanonicalArgs["mode"].Should().Be("new_tab");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("same_tab")]
    [InlineData("NEW_TAB")]
    public async Task Browser_Open_Link_Rejects_Missing_Or_Wrong_Mode_Before_Approval(string? mode)
    {
        var args = new JsonObject
        {
            ["link_id"] = "l123456789abc",
            ["observation_id"] = "11111111-1111-1111-1111-111111111111",
            ["tab_id"] = "22222222222222222222222222222222",
            ["target_hash"] = new string('a', 64),
            ["target_host"] = "example.com"
        };
        if (mode is not null)
            args["mode"] = mode;

        var result = await _safetyGate.EvaluateAndAuthorizeAsync(
            "task-browser-mode", "step-browser-mode", "browser.open_link", args, "medium");

        result.Approved.Should().BeFalse();
        _approvalProvider.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task Browser_Open_Link_Rejects_Missing_Signed_Target_Before_Approval()
    {
        var result = await _safetyGate.EvaluateAndAuthorizeAsync(
            "task-browser-link-invalid",
            "step-browser-link-invalid",
            "browser.open_link",
            new JsonObject { ["link_id"] = "l123456789abc" },
            "medium");

        result.Approved.Should().BeFalse();
        result.Record.Should().BeNull();
        _approvalProvider.CallCount.Should().Be(0);
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
