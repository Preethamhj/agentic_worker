using System.Security;
using System.Security.Cryptography;
using Jarvis.Contracts;

namespace Jarvis.Verification;

public sealed record SnapshotCaptureResult(
    IReadOnlyList<SnapshotEntryDto> Entries,
    bool EntryLimitExceeded,
    string? FailureReason);

/// <summary>
/// Captures filesystem snapshots for signed authorization watch scopes.
/// Only entries under those scopes are covered; reparse points are recorded but never traversed.
/// </summary>
public sealed class SnapshotService
{
    private const long HashLimitBytes = 1024 * 1024;
    private readonly int _entryLimit;
    private readonly IReadOnlyList<string> _noiseExclusions;

    public SnapshotService(int entryLimit = 10_000, IEnumerable<string>? noiseExclusions = null)
    {
        if (entryLimit < 1)
            throw new ArgumentOutOfRangeException(nameof(entryLimit));

        _entryLimit = entryLimit;
        _noiseExclusions = (noiseExclusions ?? Array.Empty<string>()).ToArray();
    }

    public IReadOnlyList<string> NoiseExclusions => _noiseExclusions;

    public SnapshotCaptureResult Capture(
        IReadOnlyList<string> watchScopes,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, SnapshotEntryDto>? previousEntries = null,
        string? authorizedTarget = null)
    {
        var entries = new Dictionary<string, SnapshotEntryDto>(StringComparer.OrdinalIgnoreCase);
        if (watchScopes.Count == 0)
            return new SnapshotCaptureResult(Array.Empty<SnapshotEntryDto>(), false, "Authorization has no watch scopes.");

        try
        {
            foreach (var scope in watchScopes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fullScope = Path.GetFullPath(scope);
                if (Directory.Exists(fullScope))
                {
                    var attributes = File.GetAttributes(fullScope);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        return new SnapshotCaptureResult(entries.Values.ToArray(), false, $"Watch scope is a reparse point: {fullScope}");

                    foreach (var info in new DirectoryInfo(fullScope).EnumerateFileSystemInfos())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var name = info.Name;
                        if (_noiseExclusions.Contains(name, StringComparer.OrdinalIgnoreCase))
                            continue;

                        var entry = CaptureEntry(info, previousEntries, authorizedTarget);
                        if (!entries.TryAdd(entry.Path, entry))
                            continue;

                        if (entries.Count > _entryLimit)
                            return new SnapshotCaptureResult(entries.Values.Take(_entryLimit).ToArray(), true, null);
                    }
                }
                else if (File.Exists(fullScope))
                {
                    var name = Path.GetFileName(fullScope);
                    if (!_noiseExclusions.Contains(name, StringComparer.OrdinalIgnoreCase))
                    {
                        entries[fullScope] = CaptureEntry(new FileInfo(fullScope), previousEntries, authorizedTarget);
                        if (entries.Count > _entryLimit)
                            return new SnapshotCaptureResult(entries.Values.Take(_entryLimit).ToArray(), true, null);
                    }
                }
                else
                {
                    return new SnapshotCaptureResult(entries.Values.ToArray(), false, $"Watch scope does not exist: {fullScope}");
                }
            }

            return new SnapshotCaptureResult(entries.Values.ToArray(), false, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException ex)
        {
            return new SnapshotCaptureResult(entries.Values.ToArray(), false, $"Snapshot I/O failed: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return new SnapshotCaptureResult(entries.Values.ToArray(), false, $"Snapshot access failed: {ex.Message}");
        }
        catch (SecurityException ex)
        {
            return new SnapshotCaptureResult(entries.Values.ToArray(), false, $"Snapshot security check failed: {ex.Message}");
        }
        catch (ArgumentException ex)
        {
            return new SnapshotCaptureResult(entries.Values.ToArray(), false, $"Invalid snapshot scope: {ex.Message}");
        }
    }

    private static SnapshotEntryDto CaptureEntry(
        FileSystemInfo info,
        IReadOnlyDictionary<string, SnapshotEntryDto>? previousEntries,
        string? authorizedTarget)
    {
        var path = info.FullName;
        var attributes = info.Attributes;
        var isDirectory = (attributes & FileAttributes.Directory) != 0;
        var isReparsePoint = (attributes & FileAttributes.ReparsePoint) != 0;
        var size = isDirectory ? 0L : ((FileInfo)info).Length;
        var fullPath = Path.GetFullPath(path);
        var lastWriteTimeUtc = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
        var metadataChanged = previousEntries is not null
            && (!previousEntries.TryGetValue(fullPath, out var previous)
                || MetadataChanged(previous, isDirectory, isReparsePoint, size, lastWriteTimeUtc));
        var isAuthorizedTarget = authorizedTarget is not null
            && string.Equals(fullPath, Path.GetFullPath(authorizedTarget), StringComparison.OrdinalIgnoreCase);
        string? hash = null;
        if (!isDirectory && !isReparsePoint && size < HashLimitBytes && (metadataChanged || isAuthorizedTarget))
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            hash = Convert.ToHexString(SHA256.HashData(stream));
        }

        return new SnapshotEntryDto(
            fullPath,
            isDirectory,
            isReparsePoint,
            size,
            lastWriteTimeUtc,
            hash);
    }

    private static bool MetadataChanged(
        SnapshotEntryDto previous,
        bool isDirectory,
        bool isReparsePoint,
        long size,
        DateTimeOffset lastWriteTimeUtc) =>
        previous.IsDirectory != isDirectory
        || previous.IsReparsePoint != isReparsePoint
        || previous.Size != size
        || previous.LastWriteTimeUtc != lastWriteTimeUtc;
}
