using System.Diagnostics;
using System.Text;
using FluentAssertions;
using Jarvis.Contracts;
using Jarvis.Infrastructure;
using Jarvis.Safety;
using Xunit;
using Xunit.Abstractions;

namespace Jarvis.Verification.Tests;

public sealed class FileVerificationFaultTests : IDisposable
{
    private const string Content = "authorized file contents";
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"jarvis-verify-{Guid.NewGuid():N}");
    private readonly TestClock _clock = new();
    private readonly AuthorizationService _authorizationService;
    private readonly InMemoryAuditStore _auditStore = new();
    private readonly VerificationEngine _engine;
    private readonly ITestOutputHelper _output;

    public FileVerificationFaultTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_root);
        _authorizationService = new AuthorizationService(new InMemoryDpapiKeyStore(), _clock, _auditStore);
        _engine = new VerificationEngine(_authorizationService, _auditStore, _clock);
    }

    [Fact]
    public async Task File_Absent_Is_Failed_Despite_Lying_Success_Receipt()
    {
        var setup = await PrepareAsync("fs.create_file", "absent.txt");

        var result = await VerifyAsync(setup);

        result.Status.Should().Be("FAILED");
    }

    [Fact]
    public async Task File_With_Wrong_Content_Is_Failed()
    {
        var setup = await PrepareAsync("fs.create_file", "wrong-content.txt");
        File.WriteAllText(setup.Target, "different content", new UTF8Encoding(false));

        var result = await VerifyAsync(setup);

        result.Status.Should().Be("FAILED");
    }

    [Fact]
    public async Task Truncated_File_Is_Failed()
    {
        var setup = await PrepareAsync("fs.create_file", "truncated.txt");
        File.WriteAllText(setup.Target, Content[..5], new UTF8Encoding(false));

        var result = await VerifyAsync(setup);

        result.Status.Should().Be("FAILED");
    }

    [Fact]
    public async Task File_Created_At_Wrong_Location_Is_Failed()
    {
        var setup = await PrepareAsync("fs.create_file", "wrong-location.txt");
        File.WriteAllText(Path.Combine(_root, "elsewhere.txt"), Content, new UTF8Encoding(false));

        var result = await VerifyAsync(setup);

        result.Status.Should().Be("FAILED");
    }

    [Fact]
    public async Task Extra_Unauthorized_File_Is_Failed()
    {
        var setup = await PrepareAsync("fs.create_file", "expected.txt");
        File.WriteAllText(setup.Target, Content, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(_root, "unauthorized.txt"), "unexpected", new UTF8Encoding(false));

        var result = await VerifyAsync(setup);

        result.Status.Should().Be("FAILED");
        result.SideEffects.Should().Contain(Path.Combine(_root, "unauthorized.txt"));
    }

    [Fact]
    public async Task Folder_Replaced_By_Junction_Is_Failed()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var setup = await PrepareAsync("fs.create_folder", "junction-target");
        var outside = Path.Combine(Path.GetTempPath(), $"jarvis-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        try
        {
            Directory.CreateSymbolicLink(setup.Target, outside);

            var result = await VerifyAsync(setup);

            result.Status.Should().Be("FAILED");
        }
        finally
        {
            if (Directory.Exists(setup.Target))
                Directory.Delete(setup.Target);
            Directory.Delete(outside);
        }
    }

    [Fact]
    public async Task Expired_Authorization_Is_Failed()
    {
        var setup = await PrepareAsync("fs.create_file", "expired.txt");
        File.WriteAllText(setup.Target, Content, new UTF8Encoding(false));
        _clock.UtcNow = DateTimeOffset.Parse(setup.Authorization.VerifyBefore).AddSeconds(1);

        var result = await VerifyAsync(setup, advanceClock: false);

        result.Status.Should().Be("FAILED");
    }

    [Fact]
    public async Task Unconsumed_Authorization_Is_Failed()
    {
        var setup = await PrepareAsync("fs.create_file", "unconsumed.txt", consume: false);
        File.WriteAllText(setup.Target, Content, new UTF8Encoding(false));

        var result = await VerifyAsync(setup);

        result.Status.Should().Be("FAILED");
    }

    [Fact]
    public async Task Replayed_Nonce_Is_Failed()
    {
        var setup = await PrepareAsync("fs.create_file", "replay.txt");
        File.WriteAllText(setup.Target, Content, new UTF8Encoding(false));
        var first = await VerifyAsync(setup);
        first.Status.Should().Be("VERIFIED");

        var replay = await _engine.VerifyAsync(LyingReceipt(setup.Authorization), setup.Observation!, setup.Token);

        replay.Status.Should().Be("FAILED");
    }

    [Fact]
    public async Task Stale_Evidence_Is_Failed()
    {
        var setup = await PrepareAsync("fs.create_file", "stale.txt");
        File.WriteAllText(setup.Target, Content, new UTF8Encoding(false));
        _clock.UtcNow = setup.Observation.CompletedAtUtc.AddSeconds(31);

        var result = await VerifyAsync(setup, advanceClock: false);

        result.Status.Should().Be("FAILED");
    }

    [Fact]
    public async Task Tampered_Receipt_Claims_Are_Failed()
    {
        var setup = await PrepareAsync("fs.create_file", "tampered.txt");
        File.WriteAllText(setup.Target, Content, new UTF8Encoding(false));
        var receipt = LyingReceipt(setup.Authorization) with { AuthId = "forged-auth", StepId = "forged-step" };

        var observation = CompleteObservation(setup);
        var result = await _engine.VerifyAsync(receipt, observation, setup.Token);

        result.Status.Should().Be("FAILED");
    }

    [Fact]
    public async Task Exclusively_Locked_File_Escalates()
    {
        var setup = await PrepareAsync("fs.create_file", "locked.txt");
        using var locked = new FileStream(setup.Target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        locked.Write(Encoding.UTF8.GetBytes(Content));
        locked.Flush();

        var result = await VerifyAsync(setup);

        result.Status.Should().Be("ESCALATE");
    }

    [Fact]
    public async Task Snapshot_Over_Entry_Cap_Escalates()
    {
        File.WriteAllText(Path.Combine(_root, "existing-a.txt"), "a");
        File.WriteAllText(Path.Combine(_root, "existing-b.txt"), "b");
        var setup = await PrepareAsync("fs.create_file", "capped.txt", maxSnapshotEntries: 1);
        File.WriteAllText(setup.Target, Content, new UTF8Encoding(false));

        var result = await VerifyAsync(setup);

        result.Status.Should().Be("ESCALATE");
        result.Reasons.Should().Contain(reason => reason.Contains("snapshot", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Clean_File_And_Folder_Creation_Have_No_False_Failures_And_Report_Latency()
    {
        var samples = new List<double>();
        for (var i = 0; i < 50; i++)
        {
            var file = await PrepareAsync("fs.create_file", $"clean-{i}.txt");
            File.WriteAllText(file.Target, Content, new UTF8Encoding(false));
            var fileTimer = Stopwatch.StartNew();
            var fileResult = await VerifyAsync(file);
            fileTimer.Stop();
            fileResult.Status.Should().Be("VERIFIED", $"clean file run {i} should pass");
            samples.Add(fileTimer.Elapsed.TotalMilliseconds);

            var folder = await PrepareAsync("fs.create_folder", $"clean-{i}");
            Directory.CreateDirectory(folder.Target);
            var folderTimer = Stopwatch.StartNew();
            var folderResult = await VerifyAsync(folder);
            folderTimer.Stop();
            folderResult.Status.Should().Be("VERIFIED", $"clean folder run {i} should pass");
            samples.Add(folderTimer.Elapsed.TotalMilliseconds);
        }

        samples.Sort();
        var p50 = Percentile(samples, 0.50);
        var p95 = Percentile(samples, 0.95);
        _output.WriteLine($"File/folder verification latency: p50={p50:F2}ms, p95={p95:F2}ms, n={samples.Count}");
        p95.Should().BeLessThan(500);
    }

    [Fact]
    public async Task Verification_Evidence_And_Result_Are_Persisted()
    {
        var setup = await PrepareAsync("fs.create_file", "persisted.txt");
        File.WriteAllText(setup.Target, Content, new UTF8Encoding(false));

        var result = await VerifyAsync(setup);

        result.Status.Should().Be("VERIFIED");
        _auditStore.GetVerifications(setup.Authorization.StepId).Should().ContainSingle();
        _auditStore.GetEvidence(setup.Authorization.StepId).Should().NotBeEmpty();
    }

    private async Task<Setup> PrepareAsync(
        string toolId,
        string relativePath,
        bool consume = true,
        int maxSnapshotEntries = 10_000)
    {
        var target = Path.Combine(_root, relativePath);
        var args = new Dictionary<string, object?> { ["path"] = target };
        if (toolId == "fs.create_file")
            args["content"] = Content;

        var authorization = _authorizationService.IssueAuthorization(
            $"task-{Guid.NewGuid():N}",
            $"step-{Guid.NewGuid():N}",
            toolId,
            args,
            allowedEffects: [$"create:{target}"],
            watchScopes: [_root],
            risk: "medium",
            approvedBy: "user");
        var engine = maxSnapshotEntries == 10_000
            ? _engine
            : new VerificationEngine(_authorizationService, _auditStore, _clock, maxSnapshotEntries);
        var token = await engine.BeginStepAsync(authorization);
        if (consume)
            _authorizationService.Consume(authorization, authorization.StepId);
        var started = DateTimeOffset.UtcNow;
        return new Setup(engine, authorization, token, target, started);
    }

    private async Task<VerificationResultDto> VerifyAsync(Setup setup, bool advanceClock = true)
    {
        var observation = CompleteObservation(setup, advanceClock);
        return await setup.Engine.VerifyAsync(LyingReceipt(setup.Authorization), observation, setup.Token);
    }

    private ExecutionObservation CompleteObservation(Setup setup, bool advanceClock = true)
    {
        var completed = DateTimeOffset.UtcNow;
        if (advanceClock)
            _clock.UtcNow = completed;
        var observation = new ExecutionObservation(setup.StartedAtUtc, completed);
        setup.Observation = observation;
        return observation;
    }

    private static ExecutionReceiptDto LyingReceipt(AuthorizationRecordDto authorization) => new(
        1,
        authorization.StepId,
        authorization.AuthId,
        DateTimeOffset.MaxValue.ToString("o"),
        DateTimeOffset.MaxValue.ToString("o"),
        0,
        true,
        "executor claims success",
        string.Empty);

    private static double Percentile(IReadOnlyList<double> sorted, double percentile)
    {
        var index = (int)Math.Ceiling(percentile * sorted.Count) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
    }

    private sealed class Setup(
        VerificationEngine engine,
        AuthorizationRecordDto authorization,
        StepToken token,
        string target,
        DateTimeOffset startedAtUtc)
    {
        public VerificationEngine Engine { get; } = engine;
        public AuthorizationRecordDto Authorization { get; } = authorization;
        public StepToken Token { get; } = token;
        public string Target { get; } = target;
        public DateTimeOffset StartedAtUtc { get; } = startedAtUtc;
        public ExecutionObservation? Observation { get; set; }
    }
}
