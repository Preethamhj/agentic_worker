using System.Diagnostics;
using System.IO;
using Jarvis.Contracts;
using Jarvis.Safety;

namespace Jarvis.Tools;

/// <summary>
/// Executor for app.launch.
/// Invariants:
/// - Calls AuthorizationService.Consume first and works only from canonical_args.
/// - Maps app_id to a full path from the allowlist.
/// - Prohibits free-form arguments (only app_id is permitted).
/// - Refuses to run if the host process is elevated.
/// - Launches process with UseShellExecute = false.
/// - Returns an ExecutionReceiptDto.
/// </summary>
public sealed class AppLaunchExecutor : IToolExecutor
{
    public string ToolId => "app.launch";

    private readonly ToolPolicy _toolPolicy;
    private readonly AuthorizationService _authService;
    private readonly IElevationChecker _elevationChecker;

    public AppLaunchExecutor(ToolPolicy toolPolicy, AuthorizationService authService, IElevationChecker elevationChecker)
    {
        _toolPolicy = toolPolicy ?? throw new ArgumentNullException(nameof(toolPolicy));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _elevationChecker = elevationChecker ?? throw new ArgumentNullException(nameof(elevationChecker));
    }

    public Task<ExecutionReceiptDto> ExecuteAsync(VerifiedAuthorization auth, CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow.ToString("o");

        // 1. Consume authorization and re-verify HMAC over actual fields
        _authService.Consume(auth, auth.StepId);

        // 2. Refuse execution if the current process is elevated
        try
        {
            if (_elevationChecker.IsElevated())
            {
                return Task.FromResult(FailureReceipt(auth, startedAt, "Refusing to launch application from an elevated (Administrator) process."));
            }
        }
        catch (Exception ex)
        {
            return Task.FromResult(FailureReceipt(auth, startedAt, $"Failed to check process elevation status: {ex.Message}"));
        }

        // 3. Prohibit free-form arguments
        foreach (var key in auth.CanonicalArgs.Keys)
        {
            if (!string.Equals(key, "app_id", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(FailureReceipt(auth, startedAt, $"Free-form argument '{key}' is prohibited for app.launch."));
            }
        }

        if (!auth.CanonicalArgs.TryGetValue("app_id", out var appIdObj) || appIdObj is null)
        {
            return Task.FromResult(FailureReceipt(auth, startedAt, "Missing required argument 'app_id'."));
        }

        var appId = appIdObj.ToString()!;

        // 4. Map app_id to allowlist definition
        if (!_toolPolicy.TryGetAllowedApp(appId, out var appDef) || appDef is null)
        {
            return Task.FromResult(FailureReceipt(auth, startedAt, $"App ID '{appId}' is not in the allowed applications registry."));
        }
        var executablePath = ResolveExecutableFullPath(appDef.Executable);
        if (string.IsNullOrEmpty(executablePath) || !File.Exists(executablePath))
        {
            return Task.FromResult(FailureReceipt(auth, startedAt, $"Executable '{appDef.Executable}' for app '{appId}' cannot be resolved to a verified absolute system path. PATH resolution is prohibited."));
        }

        // 5. Launch with UseShellExecute = false
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = executablePath,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc is null)
            {
                return Task.FromResult(FailureReceipt(auth, startedAt, $"Failed to start process for '{executablePath}'."));
            }

            var completedAt = DateTimeOffset.UtcNow.ToString("o");
            return Task.FromResult(new ExecutionReceiptDto(
                SchemaVersion: 1,
                StepId: auth.StepId,
                AuthId: auth.AuthId,
                StartedAt: startedAt,
                CompletedAt: completedAt,
                ExitCode: 0,
                ReportedSuccess: true,
                Stdout: $"Launched app '{appId}' ({executablePath}) with PID {proc.Id}",
                Stderr: string.Empty
            ));
        }
        catch (Exception ex)
        {
            return Task.FromResult(FailureReceipt(auth, startedAt, $"Failed to launch app '{appId}': {ex.Message}"));
        }
    }

    private static string? ResolveExecutableFullPath(string executableName)
    {
        if (string.IsNullOrWhiteSpace(executableName)) return null;

        if (Path.IsPathRooted(executableName) && File.Exists(executableName))
            return Path.GetFullPath(executableName);

        if (OperatingSystem.IsWindows())
        {
            var systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var pathInSystem = Path.Combine(systemDir, executableName);
            if (File.Exists(pathInSystem)) return pathInSystem;

            var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var pathInWindows = Path.Combine(windowsDir, executableName);
            if (File.Exists(pathInWindows)) return pathInWindows;
        }

        return null;
    }

    private static ExecutionReceiptDto FailureReceipt(VerifiedAuthorization auth, string startedAt, string error)
    {
        return new ExecutionReceiptDto(
            SchemaVersion: 1,
            StepId: auth.StepId,
            AuthId: auth.AuthId,
            StartedAt: startedAt,
            CompletedAt: DateTimeOffset.UtcNow.ToString("o"),
            ExitCode: 1,
            ReportedSuccess: false,
            Stdout: string.Empty,
            Stderr: error
        );
    }
}
