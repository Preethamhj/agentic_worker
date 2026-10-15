using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Jarvis.Contracts;
using Jarvis.Infrastructure;
using Jarvis.Safety;
using Jarvis.Tools;
using Xunit;
using Xunit.Abstractions;

namespace Jarvis.Verification.Tests;

public sealed class RealExecutorRoundTripTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"jarvis-roundtrip-{Guid.NewGuid():N}");
    private readonly ITestOutputHelper _output;

    public RealExecutorRoundTripTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_root);
    }

    public static IEnumerable<object[]> FileContentCases()
    {
        yield return ["Kannada", "ನಮಸ್ಕಾರ, ಜಾರ್ವಿಸ್"];
        yield return ["CRLF", "first\r\nsecond\r\nthird"];
        yield return ["Trailing newline", "last line\r\n"];
        yield return ["Empty content", string.Empty];
        yield return ["900 KB", new string('x', 900 * 1024)];
    }

    [Theory]
    [MemberData(nameof(FileContentCases))]
    public async Task Real_CreateFile_Executor_RoundTrips_And_Verifies_Exact_Utf8_Bytes(string scenario, string content)
    {
        var clock = SystemClock.Instance;
        var auditStore = new InMemoryAuditStore();
        var authorizationService = new AuthorizationService(new InMemoryDpapiKeyStore(), clock, auditStore);
        var pathPolicy = new PathPolicy([_root]);
        var executor = new CreateFileExecutor(pathPolicy, authorizationService);
        var engine = new VerificationEngine(authorizationService, auditStore, clock);
        var target = Path.Combine(_root, $"roundtrip-{Guid.NewGuid():N}.txt");
        var authorization = authorizationService.IssueAuthorization(
            $"task-{scenario}",
            $"step-{Guid.NewGuid():N}",
            "fs.create_file",
            new Dictionary<string, object?> { ["path"] = target, ["content"] = content });
        var token = await engine.BeginStepAsync(authorization);
        authorization.WatchScopes.Should().ContainSingle().Which.Should().Be(_root);
        _output.WriteLine($"{scenario}: default watch_scopes=[{string.Join(", ", authorization.WatchScopes)}]");

        var verifiedAuthorization = authorizationService.Consume(authorization, authorization.StepId);
        var startedAt = clock.UtcNow;
        var receipt = await executor.ExecuteAsync(verifiedAuthorization);
        var completedAt = clock.UtcNow;
        var result = await engine.VerifyAsync(
            receipt,
            new ExecutionObservation(startedAt, completedAt),
            token);

        result.Status.Should().Be("VERIFIED", scenario);
        var expectedBytes = Encoding.UTF8.GetBytes(content);
        var actualBytes = await File.ReadAllBytesAsync(target);
        actualBytes.Should().Equal(expectedBytes, "the executor must write UTF-8 without a BOM");
        authorization.Expected["size"].Should().Be(expectedBytes.LongLength);
        authorization.Expected["sha256"].Should().Be(Convert.ToHexString(SHA256.HashData(expectedBytes)));
    }

    [Fact]
    public async Task Real_CreateFolder_Executor_RoundTrips_And_Verifies()
    {
        var clock = SystemClock.Instance;
        var auditStore = new InMemoryAuditStore();
        var authorizationService = new AuthorizationService(new InMemoryDpapiKeyStore(), clock, auditStore);
        var pathPolicy = new PathPolicy([_root]);
        var executor = new CreateFolderExecutor(pathPolicy, authorizationService);
        var engine = new VerificationEngine(authorizationService, auditStore, clock);
        var target = Path.Combine(_root, $"folder-{Guid.NewGuid():N}");
        var authorization = authorizationService.IssueAuthorization(
            "task-create-folder",
            $"step-{Guid.NewGuid():N}",
            "fs.create_folder",
            new Dictionary<string, object?> { ["path"] = target });
        var token = await engine.BeginStepAsync(authorization);
        authorization.WatchScopes.Should().ContainSingle().Which.Should().Be(_root);
        _output.WriteLine($"fs.create_folder: default watch_scopes=[{string.Join(", ", authorization.WatchScopes)}]");

        var verifiedAuthorization = authorizationService.Consume(authorization, authorization.StepId);
        var startedAt = clock.UtcNow;
        var receipt = await executor.ExecuteAsync(verifiedAuthorization);
        var completedAt = clock.UtcNow;
        var result = await engine.VerifyAsync(
            receipt,
            new ExecutionObservation(startedAt, completedAt),
            token);

        result.Status.Should().Be("VERIFIED");
        Directory.Exists(target).Should().BeTrue();
    }

    [Fact]
    public void Default_WatchScope_Snapshot_Of_5000_Files_Reports_Time_And_Cap()
    {
        for (var i = 0; i < 5_000; i++)
            File.WriteAllText(Path.Combine(_root, $"existing-{i:D4}.txt"), "x");

        var clock = SystemClock.Instance;
        var authorizationService = new AuthorizationService(new InMemoryDpapiKeyStore(), clock);
        var authorization = authorizationService.IssueAuthorization(
            "task-large-folder",
            "step-large-folder",
            "fs.create_file",
            new Dictionary<string, object?>
            {
                ["path"] = Path.Combine(_root, "new-file.txt"),
                ["content"] = "x"
            });
        var timer = Stopwatch.StartNew();
        var snapshot = new SnapshotService().Capture(authorization.WatchScopes);
        timer.Stop();

        authorization.WatchScopes.Should().ContainSingle().Which.Should().Be(_root);
        snapshot.EntryLimitExceeded.Should().BeFalse();
        snapshot.Entries.Should().HaveCount(5_000);
        snapshot.Entries.All(entry => entry.Sha256 is null).Should().BeTrue("unchanged entries are metadata-only");
        timer.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(500));
        _output.WriteLine($"5,000-file default watch scope=[{string.Join(", ", authorization.WatchScopes)}]");
        _output.WriteLine($"snapshot entries={snapshot.Entries.Count}; elapsed={timer.Elapsed.TotalMilliseconds:F2}ms; default cap=10000; cap_triggered={snapshot.EntryLimitExceeded}");
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
