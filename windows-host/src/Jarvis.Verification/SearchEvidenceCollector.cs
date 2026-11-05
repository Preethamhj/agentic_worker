using System.ComponentModel;
using System.IO.Enumeration;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using Jarvis.Contracts;

namespace Jarvis.Verification;

internal sealed class SearchEvidenceCollector(FileEvidenceCollector fileCollector)
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
        string reportedPaths,
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
        var claimsValid = TryReadClaims(reportedPaths, out var claims);
        var root = Path.GetFullPath(rootPath);
        var enumeration = EnumerateMatches(root, pattern, maxDepth, maxResults, cancellationToken);
        var expectedSet = enumeration.Paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var claimSet = claims
            .Where(Path.IsPathRooted)
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var exactSetMatch = claimsValid
            && claims.Count == claimSet.Count
            && claimSet.SetEquals(expectedSet);
        var results = new List<Dictionary<string, object?>>(claims.Count);
        var allClaimsMatch = claimsValid;

        foreach (var claim in claims)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Path.IsPathRooted(claim))
            {
                results.Add(new Dictionary<string, object?>
                {
                    ["reported_path"] = claim,
                    ["exists"] = false,
                    ["inside_allowed_root"] = false,
                    ["matches_pattern"] = false
                });
                allClaimsMatch = false;
                continue;
            }

            FileSystemObservation file;
            try
            {
                file = fileCollector.Collect(
                    claim,
                    authorization.WatchScopes,
                    cancellationToken,
                    hashFile: false);
            }
            catch (ArgumentException)
            {
                results.Add(new Dictionary<string, object?>
                {
                    ["reported_path"] = claim,
                    ["exists"] = false,
                    ["inside_allowed_root"] = false,
                    ["matches_pattern"] = false
                });
                allClaimsMatch = false;
                continue;
            }
            catch (NotSupportedException)
            {
                results.Add(new Dictionary<string, object?>
                {
                    ["reported_path"] = claim,
                    ["exists"] = false,
                    ["inside_allowed_root"] = false,
                    ["matches_pattern"] = false
                });
                allClaimsMatch = false;
                continue;
            }

            var pathMatchesPattern = FileSystemName.MatchesSimpleExpression(
                pattern,
                Path.GetFileName(claim),
                ignoreCase: true);
            var insideRoot = IsWithinRoot(Path.GetFullPath(claim), root);
            var match = file.Exists
                && !file.IsDirectory
                && !file.IsReparsePoint
                && file.IsInsideAllowedRoot
                && pathMatchesPattern
                && insideRoot;
            results.Add(new Dictionary<string, object?>
            {
                ["reported_path"] = claim,
                ["exists"] = file.Exists,
                ["is_directory"] = file.IsDirectory,
                ["is_reparse_point"] = file.IsReparsePoint,
                ["inside_allowed_root"] = file.IsInsideAllowedRoot,
                ["final_path"] = file.FinalPath,
                ["size"] = file.Size,
                ["creation_time_utc"] = file.CreationTimeUtc,
                ["last_write_time_utc"] = file.LastWriteTimeUtc,
                ["matches_pattern"] = pathMatchesPattern,
                ["inside_signed_search_root"] = insideRoot
            });
            allClaimsMatch &= match;
        }

        var observed = new Dictionary<string, object?>
        {
            ["root_path"] = root,
            ["pattern"] = pattern,
            ["max_depth"] = maxDepth,
            ["max_results"] = maxResults,
            ["claims_valid_json_array"] = claimsValid,
            ["reported_path_count"] = claims.Count,
            ["independently_enumerated_count"] = enumeration.Paths.Count,
            ["enumeration_hit_result_cap"] = enumeration.CapReached,
            ["enumerated_paths"] = enumeration.Paths,
            ["reported_paths"] = results
        };

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

        var matches = exactSetMatch && allClaimsMatch;
        return new IndependentEvidenceObservation(
            "paths_match",
            nameof(SearchEvidenceCollector),
            observed,
            matches,
            matches
                ? "Independently enumerated OS paths exactly match the executor's verified result set."
                : "Reported search paths under-report, over-report, duplicate, or contain paths that do not match independently enumerated OS results.");
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
                        paths.Add(Path.GetFullPath(path));
                        if (paths.Count >= maxResults)
                            return new EnumerationResult(paths, true);
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

        return new EnumerationResult(paths, false);
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

    private static bool TryReadClaims(string value, out List<string> claims)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<string?[]>(value);
            if (parsed is null || parsed.Any(path => path is null))
            {
                claims = parsed?.Where(path => path is not null).Select(path => path!).ToList() ?? [];
                return false;
            }
            claims = parsed.Select(path => path!).ToList();
            return true;
        }
        catch (JsonException)
        {
            claims = [];
            return false;
        }
    }

    private static bool IsWithinRoot(string target, string root)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(root);
        var normalizedTarget = Path.TrimEndingDirectorySeparator(target);
        return string.Equals(normalizedTarget, normalizedRoot, StringComparison.OrdinalIgnoreCase)
            || normalizedTarget.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
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

    private sealed record EnumerationResult(List<string> Paths, bool CapReached);

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
