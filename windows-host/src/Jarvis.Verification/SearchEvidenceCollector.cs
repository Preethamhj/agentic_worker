using System.ComponentModel;
using System.IO.Enumeration;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Jarvis.Contracts;

namespace Jarvis.Verification;

internal sealed class SearchEvidenceCollector
{
    private const int DefaultMaxDepth = 5;
    private const int AbsoluteMaxDepth = 10;
    private const int DefaultMaxResults = 100;
    private const int AbsoluteMaxResults = 500;
    private const int ErrorFileNotFound = 2;
    private const int ErrorNoMoreFiles = 18;
    private const int FindExInfoBasic = 1;
    private const int FindExSearchNameMatch = 0;
    private const int FindFirstExLargeFetch = 2;

    public IndependentEvidenceObservation Collect(
        AuthorizationRecordDto authorization,
        CancellationToken cancellationToken)
    {
        if (!authorization.CanonicalArgs.TryGetValue("root_path", out var rootValue)
            || rootValue is not string rootPath
            || !authorization.CanonicalArgs.TryGetValue("pattern", out var patternValue)
            || patternValue is not string pattern
            || authorization.WatchScopes.Count == 0)
        {
            throw new ArgumentException("Search authorization is missing a signed root, pattern, or allowed root.");
        }

        var maxDepth = ReadBoundedInt(authorization, "max_depth", DefaultMaxDepth, 1, AbsoluteMaxDepth);
        var maxResults = ReadBoundedInt(authorization, "max_results", DefaultMaxResults, 1, AbsoluteMaxResults);
        var root = Path.GetFullPath(rootPath);
        var enumeration = EnumerateMatches(root, pattern, maxDepth, maxResults, cancellationToken);

        var observed = new Dictionary<string, object?>
        {
            ["root_path"] = root,
            ["pattern"] = pattern,
            ["max_depth"] = maxDepth,
            ["max_results"] = maxResults,
            ["independently_enumerated_count"] = enumeration.Paths.Count,
            ["enumeration_hit_result_cap"] = enumeration.CapReached,
            ["matching_reparse_point_encountered"] = enumeration.MatchingReparsePointEncountered,
            ["enumerated_paths"] = enumeration.Paths
        };

        if (enumeration.MatchingReparsePointEncountered)
        {
            return new IndependentEvidenceObservation(
                "paths_match",
                nameof(SearchEvidenceCollector),
                observed,
                false,
                "A matching file is a reparse point; its target cannot be independently established as safe.",
                "ESCALATE");
        }

        if (enumeration.CapReached)
        {
            return new IndependentEvidenceObservation(
                "paths_match",
                nameof(SearchEvidenceCollector),
                observed,
                false,
                $"Independent search reached the signed result cap ({maxResults}); completeness cannot be established.",
                "ESCALATE");
        }

        return new IndependentEvidenceObservation(
            "paths_match",
            nameof(SearchEvidenceCollector),
            observed,
            true,
            "Search results were independently enumerated from the signed root and pattern.");
    }

    private static EnumerationResult EnumerateMatches(
        string root,
        string pattern,
        int maxDepth,
        int maxResults,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Independent FindFirstFileEx search verification is Windows-only.");

        var paths = new List<string>(maxResults);
        var matchingReparsePointEncountered = false;
        var queue = new Queue<(string Directory, int Depth)>();
        queue.Enqueue((root, 0));

        while (queue.TryDequeue(out var item))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var handle = FindFirst(item.Directory, out var entry);
            if (handle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                if (error == ErrorFileNotFound)
                    continue;
                throw new IOException(
                    $"Independent search enumeration failed for '{item.Directory}'.",
                    new Win32Exception(error));
            }

            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.FileName is not "." and not "..")
                {
                    var path = Path.Combine(item.Directory, entry.FileName);
                    var isDirectory = (entry.Attributes & FileAttributes.Directory) != 0;
                    var isReparsePoint = (entry.Attributes & FileAttributes.ReparsePoint) != 0;
                    if (isDirectory)
                    {
                        if (item.Depth < maxDepth && !isReparsePoint)
                            queue.Enqueue((path, item.Depth + 1));
                    }
                    else if (FileSystemName.MatchesSimpleExpression(pattern, entry.FileName, ignoreCase: true))
                    {
                        if (isReparsePoint)
                        {
                            matchingReparsePointEncountered = true;
                            continue;
                        }
                        paths.Add(Path.GetFullPath(path));
                        if (paths.Count >= maxResults)
                            return new EnumerationResult(paths, true, matchingReparsePointEncountered);
                    }
                }
            }
            while (FindNext(handle, out entry));

            var nextError = Marshal.GetLastWin32Error();
            if (nextError != ErrorNoMoreFiles)
                throw new IOException(
                    $"Independent search enumeration ended unexpectedly for '{item.Directory}'.",
                    new Win32Exception(nextError));
        }

        return new EnumerationResult(paths, false, matchingReparsePointEncountered);
    }

    private static SafeFindHandle FindFirst(string directory, out FindData entry) =>
        FindFirstFileExW(
            Path.Combine(directory, "*"),
            FindExInfoBasic,
            out entry,
            FindExSearchNameMatch,
            IntPtr.Zero,
            FindFirstExLargeFetch);

    private static int ReadBoundedInt(
        AuthorizationRecordDto authorization,
        string key,
        int defaultValue,
        int minimum,
        int maximum)
    {
        if (authorization.CanonicalArgs.TryGetValue(key, out var value)
            && int.TryParse(value?.ToString(), out var parsed))
            return Math.Clamp(parsed, minimum, maximum);
        return defaultValue;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FindData
    {
        public FileAttributes Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint Reserved0;
        public uint Reserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string FileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
        public string AlternateFileName;
    }

    private sealed class SafeFindHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private SafeFindHandle() : base(true) { }

        protected override bool ReleaseHandle() => FindClose(handle);
    }

    private sealed record EnumerationResult(
        List<string> Paths,
        bool CapReached,
        bool MatchingReparsePointEncountered);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "FindFirstFileExW")]
    private static extern SafeFindHandle FindFirstFileExW(
        string fileName,
        int infoLevel,
        out FindData findFileData,
        int searchOperation,
        IntPtr searchFilter,
        int additionalFlags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "FindNextFileW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindNext(SafeFindHandle findFile, out FindData findFileData);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FindClose(IntPtr findFile);
}
