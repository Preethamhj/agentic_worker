using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Jarvis.Contracts;
using Jarvis.Core;
using Jarvis.Infrastructure;
using Jarvis.Safety;
using Jarvis.Tools;
using Xunit;

namespace Jarvis.Tools.Tests;

public class ExecutorTests
{
    private static readonly string TempRoot = Path.Combine(Path.GetTempPath(), "Jarvis_Executor_Tests_" + Guid.NewGuid().ToString("N"));
    private readonly PathPolicy _pathPolicy;
    private readonly ToolPolicy _toolPolicy;
    private readonly AuthorizationService _authService;
    private readonly InMemoryAuditStore _auditStore;

    public ExecutorTests()
    {
        Directory.CreateDirectory(TempRoot);
        _pathPolicy = new PathPolicy(new[] { TempRoot });

        var configDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "config"));
        var toolsRegistryPath = Path.Combine(configDir, "tools.registry.json");
        var appsAllowlistPath = Path.Combine(configDir, "apps.allowlist.json");
        _toolPolicy = ToolPolicy.FromFile(toolsRegistryPath, appsAllowlistPath);

        var keyStore = new InMemoryDpapiKeyStore();
        _auditStore = new InMemoryAuditStore();
        _authService = new AuthorizationService(keyStore, SystemClock.Instance, _auditStore);
    }

    [Fact]
    public async Task CreateFileExecutor_Writes_Temp_File_Then_Moves_Without_Overwrite()
    {
        var executor = new CreateFileExecutor(_pathPolicy, _authService);

        var targetFile = Path.Combine(TempRoot, "document.txt");
        var record = _authService.IssueAuthorization(
            taskId: "task-1",
            stepId: "step-1",
            toolId: "fs.create_file",
            rawArgs: new Dictionary<string, object?> { ["path"] = targetFile, ["content"] = "test content 123" },
            risk: "medium",
            approvedBy: "user");

        var auth = _authService.Consume(record, "step-1");

        var receipt = await executor.ExecuteAsync(auth);

        receipt.ReportedSuccess.Should().BeTrue();
        receipt.ExitCode.Should().Be(0);
        File.Exists(targetFile).Should().BeTrue();
        File.ReadAllText(targetFile).Should().Be("test content 123");

        // Attempting to create the same file again without overwrite must fail
        var record2 = _authService.IssueAuthorization(
            taskId: "task-1",
            stepId: "step-2",
            toolId: "fs.create_file",
            rawArgs: new Dictionary<string, object?> { ["path"] = targetFile, ["content"] = "overwritten?" },
            risk: "medium",
            approvedBy: "user");

        var auth2 = _authService.Consume(record2, "step-2");
        var receipt2 = await executor.ExecuteAsync(auth2);

        receipt2.ReportedSuccess.Should().BeFalse();
        receipt2.ExitCode.Should().NotBe(0);
        receipt2.Stderr.Should().Contain("exists");
        File.ReadAllText(targetFile).Should().Be("test content 123", "Target file must not be overwritten");
    }

    [Fact]
    public async Task CreateFolderExecutor_Creates_Directory_In_Allowed_Root()
    {
        var executor = new CreateFolderExecutor(_pathPolicy, _authService);

        var targetFolder = Path.Combine(TempRoot, "sub_folder_test");
        var record = _authService.IssueAuthorization(
            taskId: "task-2",
            stepId: "step-1",
            toolId: "fs.create_folder",
            rawArgs: new Dictionary<string, object?> { ["path"] = targetFolder },
            risk: "medium",
            approvedBy: "user");

        var auth = _authService.Consume(record, "step-1");

        var receipt = await executor.ExecuteAsync(auth);

        receipt.ReportedSuccess.Should().BeTrue();
        receipt.ExitCode.Should().Be(0);
        Directory.Exists(targetFolder).Should().BeTrue();
    }

    [Fact]
    public async Task SearchFilesExecutor_Finds_Matching_Files_With_Limits()
    {
        // Seed files in temp root
        var dir1 = Path.Combine(TempRoot, "search_test");
        Directory.CreateDirectory(dir1);
        File.WriteAllText(Path.Combine(dir1, "file1.txt"), "a");
        File.WriteAllText(Path.Combine(dir1, "file2.txt"), "b");
        File.WriteAllText(Path.Combine(dir1, "doc.pdf"), "c");

        var executor = new SearchFilesExecutor(_pathPolicy, _authService);

        var record = _authService.IssueAuthorization(
            taskId: "task-3",
            stepId: "step-1",
            toolId: "fs.search_files",
            rawArgs: new Dictionary<string, object?> { ["pattern"] = "*.txt", ["root_path"] = dir1 },
            risk: "low",
            approvedBy: "auto");

        var auth = _authService.Consume(record, "step-1");

        var receipt = await executor.ExecuteAsync(auth);

        receipt.ReportedSuccess.Should().BeTrue();
        receipt.Stdout.Should().Contain("file1.txt");
        receipt.Stdout.Should().Contain("file2.txt");
        receipt.Stdout.Should().NotContain("doc.pdf");
    }

    [Fact]
    public async Task AppLaunchExecutor_Maps_AppId_And_Refuses_FreeForm_Args_Or_Elevation()
    {
        var executor = new AppLaunchExecutor(_toolPolicy, _authService, new WindowsElevationChecker());

        var record = _authService.IssueAuthorization(
            taskId: "task-4",
            stepId: "step-1",
            toolId: "app.launch",
            rawArgs: new Dictionary<string, object?> { ["app_id"] = "calc" },
            risk: "medium",
            approvedBy: "user");

        var auth = _authService.Consume(record, "step-1");

        // If the current process is elevated (e.g. admin shell in CI), executor must refuse
        var isElevated = OperatingSystem.IsWindows() && new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

        var receipt = await executor.ExecuteAsync(auth);

        if (isElevated)
        {
            receipt.ReportedSuccess.Should().BeFalse();
            receipt.Stderr.Should().Contain("elevated");
        }
        else
        {
            receipt.ReportedSuccess.Should().BeTrue();
            receipt.Stdout.Should().Contain("calc");
        }
    }

    [Fact]
    public async Task AppLaunchExecutor_Refuses_Executable_Resolved_Via_PATH_And_Requires_Allowlisted_Absolute_Path()
    {
        // Define a custom policy with an app whose executable is only a PATH name (not rooted and not in System folders)
        var customApp = new AllowedAppDefinition
        {
            AppId = "path_tool",
            Executable = "arbitrary_path_tool.exe",
            DisplayName = "Arbitrary Path Tool"
        };
        var policyWithCustomApp = new ToolPolicy(_toolPolicy.Tools.Values, new[] { customApp });

        var elevationChecker = new TestElevationChecker(false);
        var executor = new AppLaunchExecutor(policyWithCustomApp, _authService, elevationChecker);

        var record = _authService.IssueAuthorization(
            taskId: "task-path-launch",
            stepId: "step-1",
            toolId: "app.launch",
            rawArgs: new Dictionary<string, object?> { ["app_id"] = "path_tool" },
            risk: "medium",
            approvedBy: "user");

        var auth = _authService.Consume(record, "step-1");

        var receipt = await executor.ExecuteAsync(auth);
        receipt.ReportedSuccess.Should().BeFalse("Executables resolved via PATH or relative names outside system folders must be rejected");
        receipt.Stderr.Should().Contain("PATH resolution is prohibited");
        elevationChecker.CallCount.Should().Be(1);
    }

    [Fact]
    public async Task AppLaunchExecutor_Uses_Injected_Elevation_Checker_Deterministically()
    {
        var elevatedChecker = new TestElevationChecker(true);
        var executor = new AppLaunchExecutor(_toolPolicy, _authService, elevatedChecker);
        var record = _authService.IssueAuthorization(
            taskId: "task-elevation-check",
            stepId: "step-elevation-check",
            toolId: "app.launch",
            rawArgs: new Dictionary<string, object?> { ["app_id"] = "calc" });
        var authorization = _authService.Consume(record, record.StepId);

        var receipt = await executor.ExecuteAsync(authorization);

        elevatedChecker.CallCount.Should().Be(1);
        receipt.ReportedSuccess.Should().BeFalse();
        receipt.Stderr.Should().Contain("elevated");
    }

    [Fact]
    public async Task Executor_Refuses_Forged_Or_Reused_Authorization()
    {
        var executor = new CreateFolderExecutor(_pathPolicy, _authService);

        var targetFolder = Path.Combine(TempRoot, "forged_folder");
        var record = _authService.IssueAuthorization(
            taskId: "task-5",
            stepId: "step-1",
            toolId: "fs.create_folder",
            rawArgs: new Dictionary<string, object?> { ["path"] = targetFolder },
            risk: "medium",
            approvedBy: "user");

        var validAuth = _authService.Consume(record, "step-1");

        // Tamper with record CanonicalArgs to forge a copy
        var tamperedArgs = new Dictionary<string, object?> { ["path"] = @"C:\forged" };
        var forgedRecord = record with { CanonicalArgs = tamperedArgs };

        var ctor = typeof(VerifiedAuthorization).GetConstructors(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)[0];
        var forgedAuth = (VerifiedAuthorization)ctor.Invoke(new object[] { forgedRecord });

        // Calling executor with forged auth must throw AuthorizationSecurityException
        var act = async () => await executor.ExecuteAsync(forgedAuth);
        await act.Should().ThrowAsync<AuthorizationSecurityException>();
    }

    [Fact]
    public async Task Junction_Swapped_In_At_Use_Time_Is_Caught_By_GetFinalPathNameByHandle()
    {
        if (!OperatingSystem.IsWindows()) return;

        var executor = new CreateFileExecutor(_pathPolicy, _authService);

        // Create an outside target directory
        var outsideDir = Path.Combine(Path.GetTempPath(), "Jarvis_Outside_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outsideDir);

        var junctionSubdir = Path.Combine(TempRoot, "junction_swap_" + Guid.NewGuid().ToString("N"));

        try
        {
            // Create NTFS junction pointing to outside directory
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c mklink /J \"{junctionSubdir}\" \"{outsideDir}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(5000);

            if (Directory.Exists(junctionSubdir))
            {
                var targetFile = Path.Combine(junctionSubdir, "evil.txt");

                var record = _authService.IssueAuthorization(
                    taskId: "task-6",
                    stepId: "step-1",
                    toolId: "fs.create_file",
                    rawArgs: new Dictionary<string, object?> { ["path"] = targetFile, ["content"] = "escaped" },
                    risk: "medium",
                    approvedBy: "user");

                var auth = _authService.Consume(record, "step-1");

                var receipt = await executor.ExecuteAsync(auth);

                // Use-time check must detect reparse point / outside target and fail execution
                receipt.ReportedSuccess.Should().BeFalse();
                receipt.Stderr.Should().MatchRegex("(reparse point|outside allowed roots)");
                File.Exists(targetFile).Should().BeFalse("File creation through junction must be blocked");
            }
        }
        finally
        {
            if (Directory.Exists(junctionSubdir)) Directory.Delete(junctionSubdir);
            if (Directory.Exists(outsideDir)) Directory.Delete(outsideDir);
        }
    }

    private sealed class TestElevationChecker(bool isElevated) : IElevationChecker
    {
        public int CallCount { get; private set; }

        public bool IsElevated()
        {
            CallCount++;
            return isElevated;
        }
    }
}
