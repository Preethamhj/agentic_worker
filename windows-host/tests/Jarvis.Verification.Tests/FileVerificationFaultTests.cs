using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using FluentAssertions;
using Jarvis.Contracts;
using Jarvis.Infrastructure;
using Jarvis.Safety;
using Microsoft.Win32.SafeHandles;
using Xunit;
using Xunit.Abstractions;

namespace Jarvis.Verification.Tests;

public sealed class FileVerificationFaultTests : IDisposable
{
    private const string Content = "authorized file contents";
    private static readonly UTF8Encoding Utf8NoBom = new(false);
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
        File.WriteAllText(setup.Target, "different content", Utf8NoBom);

        var result = await VerifyAsync(setup);

        result.Status.Should().Be("FAILED");
    }

    [Fact]
    public async Task Truncated_File_Is_Failed()
    {
        var setup = await PrepareAsync("fs.create_file", "truncated.txt");
        File.WriteAllText(setup.Target, Content[..5], Utf8NoBom);

        var result = await VerifyAsync(setup);

        result.Status.Should().Be("FAILED");
    }

    [Fact]
    public async Task File_Created_At_Wrong_Location_Is_Failed()
    {
        var setup = await PrepareAsync("fs.create_file", "wrong-location.txt");
        File.WriteAllText(Path.Combine(_root, "elsewhere.txt"), Content, Utf8NoBom);

        var result = await VerifyAsync(setup);

        result.Status.Should().Be("FAILED");
    }

    [Fact]
    public async Task Extra_Unauthorized_File_Is_Failed()
    {
        var setup = await PrepareAsync("fs.create_file", "expected.txt");
        File.WriteAllText(setup.Target, Content, Utf8NoBom);
        File.WriteAllText(Path.Combine(_root, "unauthorized.txt"), "unexpected", Utf8NoBom);

        var result = await VerifyAsync(setup);

        result.Status.Should().Be("FAILED");
        result.SideEffects.Should().Contain(effect => effect.Contains(Path.Combine(_root, "unauthorized.txt"), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Modified_Unauthorized_File_Is_Failed()
    {
        var modifiedPath = Path.Combine(_root, "modified.txt");
        File.WriteAllText(modifiedPath, "before", Utf8NoBom);
        var setup = await PrepareAsync("fs.create_file", "authorized.txt");
        File.WriteAllText(setup.Target, Content, Utf8NoBom);
        File.WriteAllText(modifiedPath, "after", Utf8NoBom);

        var result = await VerifyAsync(setup);

        result.Status.Should().Be("FAILED");
        result.SideEffects.Should().Contain(effect => effect.Contains(modifiedPath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Deleted_Unauthorized_File_Is_Failed()
    {
        var deletedPath = Path.Combine(_root, "deleted.txt");
        File.WriteAllText(deletedPath, "before", Utf8NoBom);
        var setup = await PrepareAsync("fs.create_file", "authorized.txt");
        File.WriteAllText(setup.Target, Content, Utf8NoBom);
        File.Delete(deletedPath);

        var result = await VerifyAsync(setup);

        result.Status.Should().Be("FAILED");
        result.SideEffects.Should().Contain(effect => effect.Contains(deletedPath, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Folder_Replaced_By_Junction_Is_Failed()
    {
        OperatingSystem.IsWindows().Should().BeTrue("the independent file collector uses Windows handle APIs");

        var setup = await PrepareAsync("fs.create_folder", "junction-target");
        var outside = Path.Combine(Path.GetTempPath(), $"jarvis-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        try
        {
            CreateJunction(setup.Target, outside);

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
        File.WriteAllText(setup.Target, Content, Utf8NoBom);
        _clock.UtcNow = DateTimeOffset.Parse(setup.Authorization.VerifyBefore).AddSeconds(1);

        var result = await VerifyAsync(setup, advanceClock: false);

        result.Status.Should().Be("FAILED");
    }

    [Fact]
    public async Task Unconsumed_Authorization_Is_Failed()
    {
        var setup = await PrepareAsync("fs.create_file", "unconsumed.txt", consume: false);
        File.WriteAllText(setup.Target, Content, Utf8NoBom);

        var result = await VerifyAsync(setup);

        result.Status.Should().Be("FAILED");
    }

    [Fact]
    public async Task Replayed_Nonce_Is_Failed()
    {
        var setup = await PrepareAsync("fs.create_file", "replay.txt");
        File.WriteAllText(setup.Target, Content, Utf8NoBom);
        var first = await VerifyAsync(setup);
        first.Status.Should().Be("VERIFIED");

        var replay = await _engine.VerifyAsync(LyingReceipt(setup.Authorization), setup.Observation!, setup.Token);

        replay.Status.Should().Be("FAILED");
    }

    [Fact]
    public async Task Stale_Evidence_Is_Failed()
    {
        var setup = await PrepareAsync("fs.create_file", "stale.txt");
        File.WriteAllText(setup.Target, Content, Utf8NoBom);
        var observation = CompleteObservation(setup);
        _clock.UtcNow = observation.CompletedAtUtc.AddSeconds(31);

        var result = await setup.Engine.VerifyAsync(LyingReceipt(setup.Authorization), observation, setup.Token);

        result.Status.Should().Be("FAILED");
    }

    [Fact]
    public async Task Tampered_Receipt_Claims_Are_Failed()
    {
        var setup = await PrepareAsync("fs.create_file", "tampered.txt");
        File.WriteAllText(setup.Target, Content, Encoding.UTF8);
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
        File.WriteAllText(setup.Target, Content, Utf8NoBom);

        var result = await VerifyAsync(setup);

        result.Status.Should().Be("ESCALATE");
        result.Reasons.Should().Contain(reason => reason.Contains("snapshot", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Clean_File_And_Folder_Creation_Have_No_False_Failures_And_Report_Latency()
    {
        var fileSamples = new List<double>();
        var folderSamples = new List<double>();
        var nestedDirectory = Path.Combine(_root, "nested");
        Directory.CreateDirectory(nestedDirectory);
        for (var i = 0; i < 50; i++)
        {
            var file = await PrepareAsync("fs.create_file", Path.Combine("nested", $"clean-{i}.txt"));
            File.WriteAllText(file.Target, Content, Utf8NoBom);
            var fileTimer = Stopwatch.StartNew();
            var fileResult = await VerifyAsync(file);
            fileTimer.Stop();
            fileResult.Status.Should().Be("VERIFIED", $"clean file run {i} should pass");
            fileSamples.Add(fileTimer.Elapsed.TotalMilliseconds);

            var folder = await PrepareAsync("fs.create_folder", Path.Combine("nested", $"clean-{i}"));
            Directory.CreateDirectory(folder.Target);
            var folderTimer = Stopwatch.StartNew();
            var folderResult = await VerifyAsync(folder);
            folderTimer.Stop();
            folderResult.Status.Should().Be("VERIFIED", $"clean folder run {i} should pass");
            folderSamples.Add(folderTimer.Elapsed.TotalMilliseconds);
        }

        fileSamples.Sort();
        folderSamples.Sort();
        var fileP50 = Percentile(fileSamples, 0.50);
        var fileP95 = Percentile(fileSamples, 0.95);
        var folderP50 = Percentile(folderSamples, 0.50);
        var folderP95 = Percentile(folderSamples, 0.95);
        _output.WriteLine($"fs.create_file verification latency: p50={fileP50:F2}ms, p95={fileP95:F2}ms, n={fileSamples.Count}");
        _output.WriteLine($"fs.create_folder verification latency: p50={folderP50:F2}ms, p95={folderP95:F2}ms, n={folderSamples.Count}");
        fileP95.Should().BeLessThan(500);
        folderP95.Should().BeLessThan(500);
    }

    [Fact]
    public async Task Verification_Evidence_And_Result_Are_Persisted()
    {
        var setup = await PrepareAsync("fs.create_file", "persisted.txt");
        File.WriteAllText(setup.Target, Content, Utf8NoBom);

        var result = await VerifyAsync(setup);

        result.Status.Should().Be("VERIFIED");
        result.Stages.Single(stage => stage.Stage == 7).Outcome.Should().Be("NOT_RUN");
        result.Stages.Single(stage => stage.Stage == 8).Outcome.Should().Be("NOT_RUN");
        _auditStore.GetVerifications(setup.Authorization.StepId).Should().ContainSingle();
        _auditStore.GetEvidence(setup.Authorization.StepId).Should().NotBeEmpty();
    }

    [Fact]
    public async Task SqliteAuditStore_Persists_Immutable_Evidence_Record_And_Verification()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"jarvis-verification-{Guid.NewGuid():N}.db");
        string authId;
        try
        {
            var clock = new TestClock();
            using (var store = new SqliteAuditStore(databasePath))
            {
                var authorizationService = new AuthorizationService(new InMemoryDpapiKeyStore(), clock, store);
                var engine = new VerificationEngine(authorizationService, store, clock);
                var target = Path.Combine(_root, "sqlite-persisted.txt");
                var authorization = authorizationService.IssueAuthorization(
                    "task-sqlite-persist",
                    "step-sqlite-persist",
                    "fs.create_file",
                    new Dictionary<string, object?> { ["path"] = target, ["content"] = Content },
                    allowedEffects: [$"create:{target}"],
                    watchScopes: [_root]);
                authId = authorization.AuthId;
                var token = await engine.BeginStepAsync(authorization);
                authorizationService.Consume(authorization, authorization.StepId);
                var started = DateTimeOffset.UtcNow;
                File.WriteAllText(target, Content, Utf8NoBom);
                var completed = DateTimeOffset.UtcNow;
                clock.UtcNow = completed;
                var receipt = LyingReceipt(authorization);

                var result = await engine.VerifyAsync(
                    receipt,
                    new ExecutionObservation(started, completed),
                    token);

                result.Status.Should().Be("VERIFIED");
            }

            using var reopenedStore = new SqliteAuditStore(databasePath);
            reopenedStore.GetVerifications("step-sqlite-persist").Should().ContainSingle(record => record.OverallStatus == "VERIFIED");
            reopenedStore.GetAuthorizationLifecycle(authId).Should().Contain(record => record.State == "Verified");
            var persistedEvidence = reopenedStore.GetEvidence("step-sqlite-persist");
            persistedEvidence.Should().ContainSingle();
            persistedEvidence[0].RecordJson.Should().Contain("record_sha256");
            persistedEvidence[0].RecordJson.Should().Contain("noise_exclusions");
        }
        finally
        {
            foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
            {
                var file = databasePath + suffix;
                if (File.Exists(file))
                    File.Delete(file);
            }
        }
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
        var result = await setup.Engine.VerifyAsync(LyingReceipt(setup.Authorization), observation, setup.Token);
        _output.WriteLine($"{setup.Authorization.ToolId}: {result.Status}; {string.Join(" | ", result.Stages.Select(stage => $"{stage.Stage}:{stage.Outcome}:{stage.Detail}"))}; reasons={string.Join(" | ", result.Reasons)}");
        return result;
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

    private static void CreateJunction(string junctionPath, string targetPath)
    {
        Directory.CreateDirectory(junctionPath);
        using var handle = CreateFileForJunction(junctionPath);
        if (handle.IsInvalid)
            throw new IOException("Could not open the junction directory.", new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));

        var substitute = @"\??\" + Path.GetFullPath(targetPath);
        var print = Path.GetFullPath(targetPath);
        var substituteBytes = Encoding.Unicode.GetBytes(substitute);
        var printBytes = Encoding.Unicode.GetBytes(print);
        var pathBytes = substituteBytes
            .Concat(new byte[sizeof(char)])
            .Concat(printBytes)
            .Concat(new byte[sizeof(char)])
            .ToArray();
        var reparseDataLength = checked((ushort)(8 + pathBytes.Length));
        var buffer = new byte[8 + reparseDataLength];
        BitConverter.GetBytes(0xA0000003u).CopyTo(buffer, 0);
        BitConverter.GetBytes(reparseDataLength).CopyTo(buffer, 4);
        BitConverter.GetBytes((ushort)0).CopyTo(buffer, 6);
        BitConverter.GetBytes((ushort)0).CopyTo(buffer, 8);
        BitConverter.GetBytes(checked((ushort)substituteBytes.Length)).CopyTo(buffer, 10);
        BitConverter.GetBytes(checked((ushort)(substituteBytes.Length + sizeof(char)))).CopyTo(buffer, 12);
        BitConverter.GetBytes(checked((ushort)printBytes.Length)).CopyTo(buffer, 14);
        pathBytes.CopyTo(buffer, 16);

        if (!DeviceIoControl(handle, 0x000900A4, buffer, (uint)buffer.Length, IntPtr.Zero, 0, out _, IntPtr.Zero))
            throw new IOException("Could not install the junction reparse point.", new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
    }

    private static SafeFileHandle CreateFileForJunction(string path) =>
        CreateFileW(path, 0x40000000, 0, IntPtr.Zero, 3, 0x02000000 | 0x00200000, IntPtr.Zero);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle device,
        uint controlCode,
        byte[] inputBuffer,
        uint inputBufferSize,
        IntPtr outputBuffer,
        uint outputBufferSize,
        out uint bytesReturned,
        IntPtr overlapped);

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
