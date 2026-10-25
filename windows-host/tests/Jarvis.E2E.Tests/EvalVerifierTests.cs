using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Jarvis.Contracts;
using Jarvis.Cli;
using Jarvis.Eval;
using Jarvis.Infrastructure;
using Jarvis.Safety;
using Jarvis.Tools;
using Jarvis.Verification;
using Xunit;

namespace Jarvis.E2E.Tests;

public sealed class EvalVerifierTests
{
    [Fact]
    public async Task Receipt_Verifier_Accepts_A_NoOp_Success_Receipt()
    {
        using var fixture = CreateExecution("expected");
        var result = await new ReceiptEvalVerifier().VerifyAsync(
            CreateCase("F1"), fixture.Execution);

        result.Status.Should().Be(EvalStatus.Verified);
    }

    [Fact]
    public async Task Self_Check_Verifier_Accepts_A_Deleted_After_Success_Result()
    {
        using var fixture = CreateExecution("expected");
        var execution = fixture.Execution;
        var path = (string)execution.Authorization.CanonicalArgs["path"]!;
        File.WriteAllText(path, "expected");
        var executorPostconditionPassed = true;
        File.Delete(path);
        execution = execution with
        {
            ExecutorPostcondition = _ => ValueTask.FromResult(executorPostconditionPassed)
        };

        var result = await new SelfCheckEvalVerifier().VerifyAsync(CreateCase("F6"), execution);

        result.Status.Should().Be(EvalStatus.Verified);
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public async Task Predicate_Verifier_Catches_Wrong_Content_But_Does_Not_Check_Extra_Files()
    {
        using var fixture = CreateExecution("expected");
        var execution = fixture.Execution;
        var target = (string)execution.Authorization.CanonicalArgs["path"]!;
        var root = execution.Authorization.WatchScopes[0];
        File.WriteAllText(target, "different");

        var verifier = new PredicateOnlyEvalVerifier(new FileSystemEvalPredicateChecker());
        var wrongContent = await verifier.VerifyAsync(CreateCase("F2"), execution);

        File.WriteAllText(target, "expected");
        File.WriteAllText(Path.Combine(root, "unauthorized-extra.txt"), "extra");
        var extraSideEffect = await verifier.VerifyAsync(CreateCase("F5"), execution);

        wrongContent.Status.Should().Be(EvalStatus.Failed);
        wrongContent.Reasons.Should().NotBeEmpty();
        extraSideEffect.Status.Should().Be(EvalStatus.Verified);
    }

    [Fact]
    public async Task Control_Gives_Verified_For_All_Four_Verifiers()
    {
        using var scenario = await CreateEngineScenarioAsync("expected", deleteAfterCreate: false);
        var verifiers = new IEvalVerifier[]
        {
            new ReceiptEvalVerifier(),
            new SelfCheckEvalVerifier(),
            new PredicateOnlyEvalVerifier(new FileSystemEvalPredicateChecker()),
            new VerificationEngineEvalVerifier(scenario.Engine)
        };
        var result = await new EvalRunner(20261025)
            .RunCaseAsync(
                CreateCase("F0"),
                _ => Task.FromResult(scenario.Execution),
                verifiers);

        result.Verdicts.Should().HaveCount(4);
        result.Verdicts.Should().OnlyContain(verdict => verdict.Status == EvalStatus.Verified);
    }

    [Theory]
    [InlineData("F0", "expected", false, "VERIFIED")]
    [InlineData("F1", null, false, "FAILED")]
    [InlineData("F2", "expected", false, "FAILED")]
    [InlineData("F6", "expected", false, "FAILED")]
    public async Task Engine_Verifier_Uses_Real_VerificationEngine_For_Control_And_File_Faults(
        string faultId,
        string? content,
        bool deleteAfterCreate,
        string expectedStatus)
    {
        using var scenario = await CreateEngineScenarioAsync(content, deleteAfterCreate, faultId);
        if (faultId != "F0")
            scenario.FaultGroundTruth.Should().Be(EvalGroundTruth.Bad);
        var targetPath = (string)scenario.Execution.Authorization.CanonicalArgs["path"]!;
        if (faultId is "F1" or "F6")
            File.Exists(targetPath).Should().BeFalse();
        if (faultId == "F2")
            File.ReadAllBytes(targetPath).Should().NotEqual(Encoding.UTF8.GetBytes("expected"));
        var verifier = new VerificationEngineEvalVerifier(scenario.Engine);

        var result = await verifier.VerifyAsync(CreateCase(faultId), scenario.Execution);

        result.Status.Should().Be(
            expectedStatus == "VERIFIED" ? EvalStatus.Verified : EvalStatus.Failed,
            "observed {0}; reasons: {1}; stages: {2}",
            result.Status,
            string.Join(" | ", result.Reasons),
            string.Join(" | ", scenario.Execution.VerificationToken.BeforeSnapshots.Select(snapshot => snapshot.Path)));
        if (expectedStatus == "VERIFIED")
        {
            result.Reasons.Should().BeEmpty();
        }
        else
        {
            result.Reasons.Should().Contain(reason =>
                scenario.ExpectedReasons.All(expected => reason.Contains(expected, StringComparison.Ordinal)));
        }
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("expected")]
    public async Task Engine_Verifier_Rejects_Tampered_Signed_Authorization(string tamper)
    {
        using var scenario = await CreateEngineScenarioAsync("expected", deleteAfterCreate: false);
        var original = scenario.Execution.Authorization;
        var tampered = tamper == "signature"
            ? original with { Hmac = "tampered-signature" }
            : original with { Expected = new Dictionary<string, object?>(original.Expected) { ["size"] = 999 } };
        var token = await scenario.Engine.BeginStepAsync(tampered);
        var execution = scenario.Execution with
        {
            Authorization = tampered,
            VerificationToken = token
        };

        var result = await new VerificationEngineEvalVerifier(scenario.Engine)
            .VerifyAsync(CreateCase("F0"), execution);

        result.Status.Should().NotBe(EvalStatus.Verified);
        result.Reasons.Should().Contain(reason =>
            reason.Contains("HMAC", StringComparison.OrdinalIgnoreCase)
            || reason.Contains("signature", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("F3")]
    [InlineData("F4")]
    [InlineData("F5")]
    public async Task Real_File_Fault_Injector_Changes_The_Observed_Filesystem(string faultId)
    {
        using var scenario = await CreateEngineScenarioAsync("expected", false, faultId);
        var path = (string)scenario.Execution.Authorization.CanonicalArgs["path"]!;
        var sibling = Path.Combine(Path.GetDirectoryName(path)!, $"wrong-location-{Path.GetFileName(path)}");
        var extra = Path.Combine(Path.GetDirectoryName(path)!, "unauthorized-extra.txt");

        scenario.FaultGroundTruth.Should().Be(EvalGroundTruth.Bad);
        switch (faultId)
        {
            case "F3":
                File.ReadAllBytes(path).Length.Should().Be(Encoding.UTF8.GetByteCount("expected") / 2);
                break;
            case "F4":
                File.Exists(path).Should().BeFalse();
                File.Exists(sibling).Should().BeTrue();
                break;
            case "F5":
                File.Exists(path).Should().BeTrue();
                File.Exists(extra).Should().BeTrue();
                break;
        }

        var result = await new VerificationEngineEvalVerifier(scenario.Engine)
            .VerifyAsync(CreateCase(faultId), scenario.Execution);
        result.Status.Should().Be(EvalStatus.Failed);
    }

    [Theory]
    [InlineData("F7")]
    [InlineData("F8")]
    public async Task Real_Search_Fault_Injector_Changes_The_Executor_Reported_Paths(string faultId)
    {
        using var scenario = await CreateSearchScenarioAsync(faultId);
        var reported = System.Text.Json.JsonSerializer.Deserialize<string[]>(scenario.Execution.Receipt.Stdout)!;

        scenario.FaultGroundTruth.Should().Be(EvalGroundTruth.Bad);
        if (faultId == "F7")
        {
            reported.Should().BeEmpty();
            File.Exists(Path.Combine((string)scenario.Execution.Authorization.CanonicalArgs["root_path"]!, "match.txt"))
                .Should().BeTrue();
        }
        else
        {
            reported.Should().Contain(path => !File.Exists(path));
        }

        var result = await new VerificationEngineEvalVerifier(scenario.Engine)
            .VerifyAsync(CreateCase(faultId) with { ToolId = "fs.search_files" }, scenario.Execution);
        result.Status.Should().Be(EvalStatus.Failed);
    }

    private static EvalCase CreateCase(string faultId) =>
        new(
            $"case-{faultId}",
            "fs.create_file",
            "task-1",
            faultId,
            faultId == "F0" ? EvalGroundTruth.Good : EvalGroundTruth.Bad,
            1,
            20261025);

    private static EvalExecutionFixture CreateExecution(string expectedContent)
    {
        var root = Path.Combine(Path.GetTempPath(), $"jarvis-eval-predicate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "target.txt");
        var authorization = CreateAuthorization(root, path, expectedContent);
        var token = new StepToken(authorization, authorization.Nonce, []);
        var execution = new EvalExecutionContext(
            authorization, CreateReceipt(authorization),
            new ExecutionObservation(DateTimeOffset.UtcNow.AddSeconds(-1), DateTimeOffset.UtcNow),
            token, root, _ => ValueTask.FromResult(true), []);
        return new EvalExecutionFixture(root, execution);
    }

    private static async Task<EngineScenario> CreateEngineScenarioAsync(
        string? content,
        bool deleteAfterCreate,
        string faultId = "F0")
    {
        var root = Path.Combine(Path.GetTempPath(), $"jarvis-eval-engine-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "target.txt");
        const string expectedContent = "expected";
        var auditPath = Path.Combine(Path.GetTempPath(), $"jarvis-eval-audit-{Guid.NewGuid():N}.db");
        var auditStore = new SqliteAuditStore(auditPath);
        var keyStore = new InMemoryDpapiKeyStore();
        var hostPath = new EvalHostPathProvider(Path.Combine(root, "Jarvis.Cli.exe"));
        var authService = new AuthorizationService(keyStore, SystemClock.Instance, auditStore, hostPath);
        var toolPolicy = LoadToolPolicy();
        var pathPolicy = new PathPolicy([root]);
        var createFileExecutor = new CreateFileExecutor(pathPolicy, authService);
        IToolExecutor executor = faultId == "F0"
            ? createFileExecutor
            : new EvalFaultInjectingExecutor(
                createFileExecutor,
                faultId,
                Path.Combine(root, "unauthorized-extra.txt"));
        var stepExecutor = new StepExecutor(
            authService,
            [executor],
            toolPolicy);
        var arguments = new Dictionary<string, object?>
        {
            ["path"] = path,
            ["content"] = expectedContent
        };
        var authorization = stepExecutor.PrepareAuthorization(
            "task-1", "step-1", "fs.create_file", arguments, "low");
        var engine = new VerificationEngine(authService, auditStore);
        var token = await engine.BeginStepAsync(authorization);
        var startedAt = DateTimeOffset.UtcNow;
        var receipt = await stepExecutor.ExecuteStepAsync(authorization);
        if (deleteAfterCreate)
            File.Delete(path);
        var completedAt = DateTimeOffset.UtcNow;
        var execution = new EvalExecutionContext(
            authorization,
            receipt,
            new ExecutionObservation(startedAt, completedAt),
            token,
            root,
            _ => ValueTask.FromResult(File.Exists(path)
                && string.Equals(File.ReadAllText(path), expectedContent, StringComparison.Ordinal)),
            []);

        var expectedReasons = faultId switch
        {
            "F1" or "F4" or "F6" => new[] { "target is absent" },
            "F2" => new[] { "SHA-256 differs from authorization.expected.sha256" },
            "F3" => new[] { "size" },
            "F5" => new[] { "Unauthorized side effect detected" },
            _ => Array.Empty<string>()
        };
        return new EngineScenario(
            root,
            auditPath,
            auditStore,
            engine,
            execution,
            expectedReasons,
            executor is EvalFaultInjectingExecutor faultInjector ? faultInjector.GroundTruth : EvalGroundTruth.Good);
    }

    private static async Task<EngineScenario> CreateSearchScenarioAsync(string faultId)
    {
        var root = Path.Combine(Path.GetTempPath(), $"jarvis-eval-search-{Guid.NewGuid():N}");
        var searchRoot = Path.Combine(root, "search");
        Directory.CreateDirectory(searchRoot);
        await File.WriteAllTextAsync(Path.Combine(searchRoot, "match.txt"), "search fixture");
        var auditPath = Path.Combine(Path.GetTempPath(), $"jarvis-eval-audit-{Guid.NewGuid():N}.db");
        var auditStore = new SqliteAuditStore(auditPath);
        var authService = new AuthorizationService(
            new InMemoryDpapiKeyStore(),
            SystemClock.Instance,
            auditStore,
            new EvalHostPathProvider(Path.Combine(root, "Jarvis.Cli.exe")));
        var policy = LoadToolPolicy();
        var executor = new SearchFilesExecutor(new PathPolicy([root]), authService);
        var stepExecutor = new StepExecutor(
            authService,
            [new EvalFaultInjectingExecutor(executor, faultId, Path.Combine(root, "extra.txt"))],
            policy);
        var arguments = new Dictionary<string, object?>
        {
            ["root_path"] = searchRoot,
            ["pattern"] = "*.txt",
            ["max_depth"] = 5,
            ["max_results"] = 100
        };
        var authorization = stepExecutor.PrepareAuthorization(
            "task-1", "step-1", "fs.search_files", arguments, "low");
        var engine = new VerificationEngine(authService, auditStore);
        var token = await engine.BeginStepAsync(authorization);
        var startedAt = DateTimeOffset.UtcNow;
        var receipt = await stepExecutor.ExecuteStepAsync(authorization);
        var completedAt = DateTimeOffset.UtcNow;
        var executionContext = new EvalExecutionContext(
            authorization,
            receipt,
            new ExecutionObservation(startedAt, completedAt),
            token,
            root,
            _ => ValueTask.FromResult(true),
            System.Text.Json.JsonSerializer.Deserialize<string[]>(receipt.Stdout) ?? []);
        return new EngineScenario(
            root,
            auditPath,
            auditStore,
            engine,
            executionContext,
            ["Search"],
            EvalGroundTruth.Bad);
    }

    private static AuthorizationRecordDto CreateAuthorization(string root, string path, string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new AuthorizationRecordDto(
            1,
            $"auth-{Guid.NewGuid():N}",
            "task-1",
            "step-1",
            "fs.create_file",
            new Dictionary<string, object?> { ["path"] = path, ["content"] = content },
            new Dictionary<string, object?>
            {
                ["exists"] = true,
                ["path"] = path,
                ["size"] = bytes.Length,
                ["sha256"] = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()
            },
            [$"create:{path}"],
            [root],
            "low",
            "auto-low",
            DateTimeOffset.UtcNow.AddMinutes(-1).ToString("o"),
            DateTimeOffset.UtcNow.AddMinutes(10).ToString("o"),
            DateTimeOffset.UtcNow.AddMinutes(10).ToString("o"),
            DateTimeOffset.UtcNow.AddMinutes(10).ToString("o"),
            $"nonce-{Guid.NewGuid():N}",
            "test-signature");
    }

    private static ExecutionReceiptDto CreateReceipt(
        AuthorizationRecordDto authorization,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? completedAt = null)
    {
        var start = startedAt ?? DateTimeOffset.UtcNow.AddSeconds(-1);
        var end = completedAt ?? DateTimeOffset.UtcNow;
        return new ExecutionReceiptDto(
            1,
            authorization.StepId,
            authorization.AuthId,
            start.ToString("o"),
            end.ToString("o"),
            0,
            true,
            "",
            "");
    }

    private sealed class EngineScenario(
        string root,
        string auditPath,
        SqliteAuditStore auditStore,
        VerificationEngine engine,
        EvalExecutionContext execution,
        IReadOnlyList<string> expectedReasons,
        EvalGroundTruth faultGroundTruth) : IDisposable
    {
        public VerificationEngine Engine { get; } = engine;
        public EvalExecutionContext Execution { get; } = execution;
        public IReadOnlyList<string> ExpectedReasons { get; } = expectedReasons;
        public EvalGroundTruth FaultGroundTruth { get; } = faultGroundTruth;

        public void Dispose()
        {
            auditStore.Dispose();
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
            if (File.Exists(auditPath))
                File.Delete(auditPath);
        }
    }

    private sealed class EvalExecutionFixture(string root, EvalExecutionContext execution) : IDisposable
    {
        public EvalExecutionContext Execution { get; } = execution;

        public void Dispose()
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static ToolPolicy LoadToolPolicy()
    {
        var repositoryRoot = new DirectoryInfo(AppContext.BaseDirectory);
        while (repositoryRoot is not null && !File.Exists(Path.Combine(repositoryRoot.FullName, "config", "tools.registry.json")))
            repositoryRoot = repositoryRoot.Parent;
        if (repositoryRoot is null)
            throw new DirectoryNotFoundException("Cannot locate the repository tool registry.");

        return ToolPolicy.FromFile(
            Path.Combine(repositoryRoot.FullName, "config", "tools.registry.json"),
            Path.Combine(repositoryRoot.FullName, "config", "apps.allowlist.json"));
    }

    private sealed class EvalHostPathProvider(string path) : IHostPathProvider
    {
        public string GetHostPath() => path;
    }

}
