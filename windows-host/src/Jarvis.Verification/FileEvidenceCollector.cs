using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;
using Jarvis.Contracts;

namespace Jarvis.Verification;

public sealed record FileSystemObservation(
    string Path,
    bool Exists,
    bool IsDirectory,
    bool IsReparsePoint,
    bool IsInsideAllowedRoot,
    string? FinalPath,
    long Size,
    string? Sha256,
    DateTimeOffset? CreationTimeUtc,
    DateTimeOffset? LastWriteTimeUtc);

/// <summary>
/// Reads file and directory evidence using private Win32 handle operations.
/// This deliberately duplicates the OS boundary instead of sharing code or state with Jarvis.Tools.
/// </summary>
public sealed class FileEvidenceCollector
{
    private const uint GenericRead = 0x80000000;
    private const uint FileReadAttributes = 0x00000080;
    private const uint ShareRead = 0x00000001;
    private const uint ShareWrite = 0x00000002;
    private const uint ShareDelete = 0x00000004;
    private const uint OpenExisting = 3;
    private const uint FileFlagBackupSemantics = 0x02000000;
    private const uint FileFlagOpenReparsePoint = 0x00200000;
    private const uint FileAttributeDirectory = 0x00000010;
    private const uint FileAttributeReparsePoint = 0x00000400;
    private const int ErrorFileNotFound = 2;
    private const int ErrorPathNotFound = 3;
    private const int FileNameNormalized = 0;
    private const int VolumeNameDos = 0;

    public FileSystemObservation Collect(
        AuthorizationRecordDto authorization,
        IReadOnlyList<string> allowedRoots,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.CanonicalArgs.TryGetValue("path", out var pathValue) || pathValue is not string targetPath)
            throw new ArgumentException("Authorization is missing the canonical path argument.", nameof(authorization));
        return Collect(targetPath, allowedRoots, cancellationToken);
    }

    public FileSystemObservation Collect(
        string targetPath,
        IReadOnlyList<string> allowedRoots,
        CancellationToken cancellationToken = default,
        bool hashFile = true)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (allowedRoots.Count == 0)
            throw new ArgumentException("Authorization has no allowed watch root.", nameof(allowedRoots));

        var target = Path.GetFullPath(targetPath);
        var finalRoots = allowedRoots
            .Select(root => GetFinalPath(Path.GetFullPath(root), directory: true))
            .ToArray();

        using var reparseHandle = OpenHandle(
            target,
            FileReadAttributes,
            ShareRead | ShareWrite | ShareDelete,
            FileFlagBackupSemantics | FileFlagOpenReparsePoint,
            out var targetError);
        if (reparseHandle.IsInvalid)
        {
            reparseHandle.Dispose();
            if (targetError is ErrorFileNotFound or ErrorPathNotFound)
                return Missing(target);
            throw new IOException($"Could not inspect target '{target}'.", new Win32Exception(targetError));
        }

        var linkInfo = ReadInfo(reparseHandle);
        var isReparsePoint = (linkInfo.FileAttributes & FileAttributeReparsePoint) != 0;
        var isDirectory = (linkInfo.FileAttributes & FileAttributeDirectory) != 0;

        using var targetHandle = OpenHandle(
            target,
            isDirectory || !hashFile ? FileReadAttributes : GenericRead | FileReadAttributes,
            isDirectory || !hashFile ? ShareRead | ShareWrite | ShareDelete : ShareRead,
            isDirectory ? FileFlagBackupSemantics : 0,
            out var openError);
        if (targetHandle.IsInvalid)
        {
            targetHandle.Dispose();
            if (openError is ErrorFileNotFound or ErrorPathNotFound)
                return Missing(target);
            throw new IOException($"Could not read target '{target}'.", new Win32Exception(openError));
        }

        var info = ReadInfo(targetHandle);
        var finalTarget = GetFinalPath(targetHandle);
        var insideRoot = finalRoots.Any(finalRoot => IsWithinRoot(finalTarget, finalRoot));
        var size = ((long)info.FileSizeHigh << 32) | info.FileSizeLow;
        string? hash = null;
        if (hashFile && !isDirectory && !isReparsePoint)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = new FileStream(targetHandle, FileAccess.Read, 81920, isAsync: false);
            hash = Convert.ToHexString(SHA256.HashData(stream));
        }

        return new FileSystemObservation(
            target,
            true,
            isDirectory,
            isReparsePoint,
            insideRoot,
            finalTarget,
            size,
            hash,
            ToDateTimeOffset(info.CreationTime),
            ToDateTimeOffset(info.LastWriteTime));
    }

    private static FileSystemObservation Missing(string target) =>
        new(target, false, false, false, false, null, 0, null, null, null);

    private static SafeFileHandle OpenHandle(
        string path,
        uint desiredAccess,
        uint shareMode,
        uint flags,
        out int error)
    {
        var handle = CreateFileW(path, desiredAccess, shareMode, IntPtr.Zero, OpenExisting, flags, IntPtr.Zero);
        error = handle.IsInvalid ? Marshal.GetLastWin32Error() : 0;
        return handle;
    }

    private static string GetFinalPath(string path, bool directory)
    {
        using var handle = OpenHandle(
            path,
            FileReadAttributes,
            ShareRead | ShareWrite | ShareDelete,
            directory ? FileFlagBackupSemantics : 0,
            out var error);
        if (handle.IsInvalid)
            throw new IOException($"Could not resolve final path for '{path}'.", new Win32Exception(error));
        return GetFinalPath(handle);
    }

    private static string GetFinalPath(SafeFileHandle handle)
    {
        var capacity = 512;
        while (capacity <= 32768)
        {
            var buffer = new StringBuilder(capacity);
            var length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, FileNameNormalized | VolumeNameDos);
            if (length == 0)
                throw new IOException("Could not resolve the final path of the opened filesystem object.", new Win32Exception(Marshal.GetLastWin32Error()));
            if (length < buffer.Capacity)
                return NormalizePath(buffer.ToString());
            capacity = checked((int)length + 1);
        }

        throw new IOException("Resolved filesystem path exceeds the supported Win32 path length.");
    }

    private static ByHandleFileInformation ReadInfo(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var info))
            throw new IOException("Could not retrieve filesystem metadata from its handle.", new Win32Exception(Marshal.GetLastWin32Error()));
        return info;
    }

    private static DateTimeOffset ToDateTimeOffset(FileTime time)
    {
        var fileTime = ((long)time.High << 32) | time.Low;
        return new DateTimeOffset(DateTime.FromFileTimeUtc(fileTime), TimeSpan.Zero);
    }

    private static bool IsWithinRoot(string target, string root)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(root);
        var normalizedTarget = Path.TrimEndingDirectorySeparator(target);
        return string.Equals(normalizedTarget, normalizedRoot, StringComparison.OrdinalIgnoreCase)
            || normalizedTarget.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            return @"\\" + path[8..];
        if (path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase))
            return path[4..];
        return path;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(
        SafeFileHandle file,
        StringBuilder filePath,
        uint filePathLength,
        int flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out ByHandleFileInformation fileInformation);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public FileTime CreationTime;
        public FileTime LastAccessTime;
        public FileTime LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}
