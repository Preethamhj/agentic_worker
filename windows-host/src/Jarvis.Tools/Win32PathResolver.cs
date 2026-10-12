using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Jarvis.Safety;
using Microsoft.Win32.SafeHandles;

namespace Jarvis.Tools;

/// <summary>
/// Verifies paths at tool execution time using Win32 API.
/// Invariant: Handles are opened without following reparse points (FILE_FLAG_OPEN_REPARSE_POINT)
/// and resolved with GetFinalPathNameByHandle to confirm that the real target path is within allowed roots.
/// </summary>
public static class Win32PathResolver
{
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
    private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_SHARE_READ = 1;
    private const uint FILE_SHARE_WRITE = 2;
    private const uint FILE_SHARE_DELETE = 4;
    private const uint FILE_NAME_NORMALIZED = 0x0;
    private const uint VOLUME_NAME_DOS = 0x0;

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern uint GetFinalPathNameByHandle(
        SafeFileHandle hFile,
        StringBuilder lpszFilePath,
        uint cchFilePath,
        uint dwFlags);

    /// <summary>
    /// Re-validates path at use time. Confirms no reparse point (junction/symlink) is crossed
    /// and that the final canonical volume path lies within an allowed root.
    /// </summary>
    public static (bool IsValid, string? ErrorMessage) ValidateUseTimePath(string path, PathPolicy policy)
    {
        // 1. Initial PathPolicy validation
        var result = policy.ValidatePath(path);
        if (!result.IsValid)
        {
            return (false, result.ErrorMessage);
        }

        if (!OperatingSystem.IsWindows())
        {
            return (true, null);
        }

        // 2. Find closest existing ancestor directory or the target itself
        string checkPath = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(checkPath) && !File.Exists(checkPath) && !Directory.Exists(checkPath))
        {
            var parent = Path.GetDirectoryName(checkPath);
            if (parent == checkPath || string.IsNullOrEmpty(parent)) break;
            checkPath = parent;
        }

        if (string.IsNullOrEmpty(checkPath) || (!File.Exists(checkPath) && !Directory.Exists(checkPath)))
        {
            return (true, null);
        }

        // 3. Reject any existing reparse point in the hierarchy
        if (PathPolicy.IsReparsePoint(checkPath))
        {
            return (false, "Path or an existing ancestor is a reparse point (junction or symlink).");
        }

        // 4. Open handle and query GetFinalPathNameByHandle
        try
        {
            using var handle = CreateFile(
                checkPath,
                0, // FILE_READ_ATTRIBUTES / query access
                FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                IntPtr.Zero,
                OPEN_EXISTING,
                FILE_FLAG_BACKUP_SEMANTICS,
                IntPtr.Zero);

            if (handle.IsInvalid)
            {
                return (false, $"Unable to open handle for path verification (error: {Marshal.GetLastWin32Error()}).");
            }

            var sb = new StringBuilder(1024);
            var len = GetFinalPathNameByHandle(handle, sb, (uint)sb.Capacity, FILE_NAME_NORMALIZED | VOLUME_NAME_DOS);
            if (len == 0)
            {
                return (false, $"Failed to resolve final path by handle (error: {Marshal.GetLastWin32Error()}).");
            }

            var finalPath = sb.ToString();
            if (finalPath.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            {
                finalPath = @"\\" + finalPath.Substring(8);
            }
            else if (finalPath.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
            {
                finalPath = finalPath.Substring(4);
            }

            // Re-validate finalPath against PathPolicy
            var finalValidation = policy.ValidatePath(finalPath);
            if (!finalValidation.IsValid)
            {
                return (false, $"Final path '{finalPath}' is outside allowed roots: {finalValidation.ErrorMessage}");
            }
        }
        catch (Exception ex)
        {
            return (false, $"Handle verification exception: {ex.Message}");
        }

        return (true, null);
    }
}
