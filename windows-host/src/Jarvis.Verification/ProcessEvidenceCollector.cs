using System.ComponentModel;
using System.Runtime.InteropServices;
using Jarvis.Contracts;
using Microsoft.Win32.SafeHandles;

namespace Jarvis.Verification;

internal sealed class ProcessEvidenceCollector
{
    private const uint SnapshotProcesses = 0x00000002;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint TokenQuery = 0x0008;
    private const uint StillActive = 259;
    private const int TokenElevation = 20;

    public IndependentEvidenceObservation Collect(
        AuthorizationRecordDto authorization,
        ExecutionObservation observation,
        CancellationToken cancellationToken)
    {
        if (!authorization.Expected.TryGetValue("image_path", out var expectedPathValue)
            || expectedPathValue is not string expectedImagePath)
        {
            throw new ArgumentException("App-launch authorization is missing its signed image_path.");
        }

        var expectedImage = Path.GetFullPath(expectedImagePath);
        using var snapshot = CreateToolhelp32Snapshot(SnapshotProcesses, 0);
        if (snapshot.IsInvalid)
            throw new IOException("Could not enumerate OS processes.", new Win32Exception(Marshal.GetLastWin32Error()));

        var matches = new List<Dictionary<string, object?>>();
        var entry = new ProcessEntry32 { Size = (uint)Marshal.SizeOf<ProcessEntry32>() };
        if (!Process32FirstW(snapshot, ref entry))
            throw new IOException("Could not read the initial OS process entry.", new Win32Exception(Marshal.GetLastWin32Error()));

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.ParentProcessId == (uint)Environment.ProcessId
                && string.Equals(entry.ExecutableFile, Path.GetFileName(expectedImage), StringComparison.OrdinalIgnoreCase))
            {
                using var process = OpenProcess(ProcessQueryLimitedInformation, false, entry.ProcessId);
                if (process.IsInvalid)
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error != 87)
                        throw new IOException(
                            $"Could not inspect child process {entry.ProcessId}.",
                            new Win32Exception(error));
                }
                else
                {
                    if (!GetProcessTimes(process, out var creationTime, out _, out _, out _))
                        throw new IOException(
                            $"Could not read child process {entry.ProcessId} creation time.",
                            new Win32Exception(Marshal.GetLastWin32Error()));
                    var processStartedAt = DateTimeOffset.FromFileTime(ToInt64(creationTime));
                    if (processStartedAt >= observation.StartedAtUtc)
                    {
                        var imagePath = ReadImagePath(process);
                        var elevated = IsElevated(process);
                        var isLive = GetExitCodeProcess(process, out var exitCode)
                            ? exitCode == StillActive
                            : throw new IOException(
                                $"Could not determine whether child process {entry.ProcessId} is live.",
                                new Win32Exception(Marshal.GetLastWin32Error()));
                        var pathMatches = string.Equals(
                            Path.GetFullPath(imagePath),
                            expectedImage,
                            StringComparison.OrdinalIgnoreCase);
                        matches.Add(new Dictionary<string, object?>
                        {
                            ["process_id"] = entry.ProcessId,
                            ["parent_process_id"] = entry.ParentProcessId,
                            ["image_path"] = imagePath,
                            ["started_at_utc"] = processStartedAt,
                            ["is_elevated"] = elevated,
                            ["is_live"] = isLive,
                            ["image_matches_signed_allowlist"] = pathMatches
                        });
                    }
                }
            }

            if (!Process32NextW(snapshot, ref entry))
            {
                var error = Marshal.GetLastWin32Error();
                if (error != 18)
                    throw new IOException("OS process enumeration ended unexpectedly.", new Win32Exception(error));
                break;
            }
        }

        var matchedProcesses = matches.Where(process =>
                Equals(process["image_matches_signed_allowlist"], true)
                && Equals(process["is_live"], true)
                && Equals(process["is_elevated"], false))
            .ToArray();
        var observed = new Dictionary<string, object?>
        {
            ["expected_image_path"] = expectedImage,
            ["host_process_id"] = Environment.ProcessId,
            ["candidate_count"] = matches.Count,
            ["candidate_processes"] = matches
        };
        var matched = matchedProcesses.Length == 1;
        return new IndependentEvidenceObservation(
            "process_running",
            nameof(ProcessEvidenceCollector),
            observed,
            matched,
            matched
                ? "Exactly one live, non-elevated child process matched the signed image path and host parent."
                : $"Expected one live, non-elevated child process with the signed image path; found {matchedProcesses.Length}.");
    }

    private static string ReadImagePath(SafeProcessHandle process)
    {
        var capacity = 1024;
        while (capacity <= 32768)
        {
            var buffer = new char[capacity];
            if (QueryFullProcessImageNameW(process, 0, buffer, ref capacity))
                return new string(buffer, 0, capacity);
            var error = Marshal.GetLastWin32Error();
            if (error != 122)
                throw new IOException("Could not read child process image path.", new Win32Exception(error));
            capacity = Math.Min(capacity * 2, 32768);
        }
        throw new IOException("Child process image path exceeds the supported Win32 path length.");
    }

    private static bool IsElevated(SafeProcessHandle process)
    {
        if (!OpenProcessToken(process, TokenQuery, out var token))
            throw new IOException("Could not open child process token.", new Win32Exception(Marshal.GetLastWin32Error()));
        using (token)
        {
            if (!GetTokenInformation(token, TokenElevation, out var elevation, (uint)Marshal.SizeOf<TokenElevationInfo>(), out _))
                throw new IOException("Could not read child process elevation state.", new Win32Exception(Marshal.GetLastWin32Error()));
            return elevation.TokenIsElevated != 0;
        }
    }

    private static long ToInt64(FileTime value) => ((long)(uint)value.High << 32) | (uint)value.Low;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public UIntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string? ExecutableFile;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public int Low;
        public int High;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenElevationInfo
    {
        public uint TokenIsElevated;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32FirstW(SafeFileHandle snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32NextW(SafeFileHandle snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(
        SafeProcessHandle process,
        uint flags,
        [Out] char[] imageName,
        ref int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(
        SafeProcessHandle process,
        out FileTime creationTime,
        out FileTime exitTime,
        out FileTime kernelTime,
        out FileTime userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint exitCode);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(SafeProcessHandle process, uint desiredAccess, out SafeAccessTokenHandle token);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(
        SafeAccessTokenHandle token,
        int tokenInformationClass,
        out TokenElevationInfo tokenInformation,
        uint tokenInformationLength,
        out uint returnLength);
}
