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

    public SnapshotCaptureResult Capture(IReadOnlyList<string> watchScopes, CancellationToken cancellationToken = default)
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

                    var pendingDirectories = new Stack<string>();
                    pendingDirectories.Push(fullScope);
                    while (pendingDirectories.Count > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var directory = pendingDirectories.Pop();
                        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            var name = Path.GetFileName(path);
                            if (_noiseExclusions.Contains(name, StringComparer.OrdinalIgnoreCase))
                                continue;

                            var entry = CaptureEntry(path);
                            if (!entries.TryAdd(entry.Path, entry))
                                continue;

                            if (entries.Count > _entryLimit)
                                return new SnapshotCaptureResult(entries.Values.Take(_entryLimit).ToArray(), true, null);

                            if (entry.IsDirectory && !entry.IsReparsePoint)
                                pendingDirectories.Push(path);
                        }
                    }
                }
                else if (File.Exists(fullScope))
                {
                    var name = Path.GetFileName(fullScope);
                    if (!_noiseExclusions.Contains(name, StringComparer.OrdinalIgnoreCase))
                    {
                        entries[fullScope] = CaptureEntry(fullScope);
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

    private static SnapshotEntryDto CaptureEntry(string path)
    {
        var attributes = File.GetAttributes(path);
        var isDirectory = (attributes & FileAttributes.Directory) != 0;
        var isReparsePoint = (attributes & FileAttributes.ReparsePoint) != 0;
        var info = isDirectory ? (FileSystemInfo)new DirectoryInfo(path) : new FileInfo(path);
        info.Refresh();
        var size = isDirectory ? 0L : ((FileInfo)info).Length;
        string? hash = null;
        if (!isDirectory && !isReparsePoint && size < HashLimitBytes)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            hash = Convert.ToHexString(SHA256.HashData(stream));
        }

        return new SnapshotEntryDto(
            Path.GetFullPath(path),
            isDirectory,
            isReparsePoint,
            size,
            new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
            hash);
    }
}
