using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Jarvis.Infrastructure;

public sealed class AiServerProcess : IAsyncDisposable, IDisposable
{
    private readonly IntPtr _jobHandle;
    private bool _disposed;

    public Process Process { get; }
    public int Port { get; }
    public string BaseUrl { get; }
    public string Token { get; }

    internal AiServerProcess(Process process, int port, string token, IntPtr jobHandle)
    {
        Process = process;
        Port = port;
        BaseUrl = $"http://127.0.0.1:{port}";
        Token = token;
        _jobHandle = jobHandle;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            if (!Process.HasExited)
            {
                Process.Kill(entireProcessTree: true);
                Process.WaitForExit(3000);
            }
        }
        catch
        {
            // Best effort process cleanup
        }
        finally
        {
            Process.Dispose();
            if (_jobHandle != IntPtr.Zero && OperatingSystem.IsWindows())
            {
                AiServerLauncher.CloseHandle(_jobHandle);
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Launches the local Python AI server bound to a Windows Job Object.
/// Invariants:
/// 1. Picks a free loopback port (127.0.0.1).
/// 2. Generates a random cryptographic token and passes it strictly via JARVIS_TOKEN env var.
/// 3. Never logs the token.
/// 4. Ties process lifetime to the host with a Job Object (KILL_ON_JOB_CLOSE).
/// 5. Polls /health until server is ready before returning.
/// </summary>
public static class AiServerLauncher
{
    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(
        IntPtr hJob,
        int JobObjectInfoClass,
        IntPtr lpJobObjectInfo,
        uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryLimit;
        public UIntPtr PeakJobMemoryLimit;
    }

    public static int GetFreeLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    public static string GenerateSecureToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static IntPtr CreateKillOnCloseJob()
    {
        if (!OperatingSystem.IsWindows()) return IntPtr.Zero;

        var job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero) return IntPtr.Zero;

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        int length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
        IntPtr extendedInfoPtr = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.StructureToPtr(info, extendedInfoPtr, false);
            if (!SetInformationJobObject(job, JobObjectExtendedLimitInformation, extendedInfoPtr, (uint)length))
            {
                CloseHandle(job);
                return IntPtr.Zero;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(extendedInfoPtr);
        }

        return job;
    }

    public static async Task<AiServerProcess> LaunchAsync(
        string? pythonExecutablePath = null,
        string? workingDirectory = null,
        TimeSpan? readyTimeout = null,
        CancellationToken cancellationToken = default)
    {
        var port = GetFreeLoopbackPort();
        var token = GenerateSecureToken();
        var timeout = readyTimeout ?? TimeSpan.FromSeconds(30);

        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", ".."));
        var aiServerDir = workingDirectory ?? Path.Combine(repoRoot, "ai-server");

        var pythonExe = pythonExecutablePath;
        if (string.IsNullOrEmpty(pythonExe))
        {
            var venvPython = Path.Combine(aiServerDir, ".venv", "Scripts", "python.exe");
            pythonExe = File.Exists(venvPython) ? venvPython : "python";
        }

        var jobHandle = CreateKillOnCloseJob();

        var psi = new ProcessStartInfo
        {
            FileName = pythonExe,
            Arguments = $"-m uvicorn jarvis_ai.api.main:app --host 127.0.0.1 --port {port} --no-access-log",
            WorkingDirectory = aiServerDir,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        // Pass token strictly through environment variable
        psi.Environment["JARVIS_TOKEN"] = token;

        var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to launch AI server process with '{pythonExe}'.");

        if (jobHandle != IntPtr.Zero && OperatingSystem.IsWindows())
        {
            AssignProcessToJobObject(jobHandle, proc.Handle);
        }

        var serverProcess = new AiServerProcess(proc, port, token, jobHandle);

        try
        {
            // Poll /health until server is ready
            var stopwatch = Stopwatch.StartNew();
            using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var healthUrl = $"http://127.0.0.1:{port}/health";

            while (stopwatch.Elapsed < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (proc.HasExited)
                {
                    var stderr = await proc.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                    throw new InvalidOperationException($"AI server process exited unexpectedly with code {proc.ExitCode}: {stderr}");
                }

                try
                {
                    var response = await httpClient.GetAsync(healthUrl, cancellationToken).ConfigureAwait(false);
                    if (response.IsSuccessStatusCode)
                    {
                        return serverProcess;
                    }
                }
                catch
                {
                    // Connection refused while starting up
                }

                await Task.Delay(200, cancellationToken).ConfigureAwait(false);
            }

            throw new TimeoutException($"AI server on port {port} failed to become healthy within {timeout.TotalSeconds}s.");
        }
        catch
        {
            serverProcess.Dispose();
            throw;
        }
    }
}
