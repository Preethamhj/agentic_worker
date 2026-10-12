using System.IO;
using System.Text.Json;
using Jarvis.Contracts;
using Jarvis.Safety;

namespace Jarvis.Tools;

/// <summary>
/// Executor for fs.search_files.
/// Invariants:
/// - Calls AuthorizationService.Consume first and works only from canonical_args.
/// - Re-validates root_path at use time with PathPolicy and Win32PathResolver.
/// - Bounded depth, result limit, timeout and cancellation.
/// - Never follows reparse points (junctions/symlinks).
/// - Returns an ExecutionReceiptDto.
/// </summary>
public sealed class SearchFilesExecutor : IToolExecutor
{
    public string ToolId => "fs.search_files";

    private const int DefaultMaxDepth = 5;
    private const int AbsoluteMaxDepth = 10;
    private const int DefaultMaxResults = 100;
    private const int AbsoluteMaxResults = 500;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private readonly PathPolicy _pathPolicy;
    private readonly AuthorizationService _authService;

    public SearchFilesExecutor(PathPolicy pathPolicy, AuthorizationService authService)
    {
        _pathPolicy = pathPolicy ?? throw new ArgumentNullException(nameof(pathPolicy));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
    }

    public Task<ExecutionReceiptDto> ExecuteAsync(VerifiedAuthorization auth, CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow.ToString("o");

        // 1. Consume authorization and re-verify HMAC over actual fields
        _authService.Consume(auth, auth.StepId);

        // 2. Read only from CanonicalArgs
        var pattern = auth.CanonicalArgs.TryGetValue("pattern", out var patObj) && patObj != null
            ? patObj.ToString()!
            : "*";

        if (!auth.CanonicalArgs.TryGetValue("root_path", out var rootObj) || rootObj is null)
        {
            return Task.FromResult(FailureReceipt(auth, startedAt, "Missing required argument 'root_path'."));
        }

        var rootPath = rootObj.ToString()!;

        var maxDepth = DefaultMaxDepth;
        if (auth.CanonicalArgs.TryGetValue("max_depth", out var mdObj) && mdObj != null && int.TryParse(mdObj.ToString(), out var md))
        {
            maxDepth = Math.Clamp(md, 1, AbsoluteMaxDepth);
        }

        var maxResults = DefaultMaxResults;
        if (auth.CanonicalArgs.TryGetValue("max_results", out var mrObj) && mrObj != null && int.TryParse(mrObj.ToString(), out var mr))
        {
            maxResults = Math.Clamp(mr, 1, AbsoluteMaxResults);
        }

        // 3. Re-validate root_path at use time
        var (isValid, errorMessage) = Win32PathResolver.ValidateUseTimePath(rootPath, _pathPolicy);
        if (!isValid)
        {
            return Task.FromResult(FailureReceipt(auth, startedAt, errorMessage ?? "Root path validation failed."));
        }

        if (!Directory.Exists(rootPath))
        {
            return Task.FromResult(FailureReceipt(auth, startedAt, $"Root directory '{rootPath}' does not exist."));
        }

        // 4. Bounded search respecting depth, result limit, timeout and cancellation
        using var timeoutCts = new CancellationTokenSource(DefaultTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var ct = linkedCts.Token;

        var results = new List<string>();
        var queue = new Queue<(string DirPath, int Depth)>();
        queue.Enqueue((Path.GetFullPath(rootPath), 0));

        try
        {
            while (queue.Count > 0 && results.Count < maxResults)
            {
                ct.ThrowIfCancellationRequested();

                var (currentDir, depth) = queue.Dequeue();

                // Enumerate matching files in current directory
                try
                {
                    var files = Directory.EnumerateFiles(currentDir, pattern, SearchOption.TopDirectoryOnly);
                    foreach (var file in files)
                    {
                        ct.ThrowIfCancellationRequested();
                        results.Add(file);
                        if (results.Count >= maxResults)
                        {
                            break;
                        }
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    // Skip inaccessible directories
                }

                // Enumerate subdirectories if within max depth
                if (depth < maxDepth && results.Count < maxResults)
                {
                    try
                    {
                        var subdirs = Directory.EnumerateDirectories(currentDir, "*", SearchOption.TopDirectoryOnly);
                        foreach (var subdir in subdirs)
                        {
                            ct.ThrowIfCancellationRequested();

                            // Never follow reparse points (junctions or symlinks)
                            var di = new DirectoryInfo(subdir);
                            if (di.Attributes.HasFlag(FileAttributes.ReparsePoint))
                            {
                                continue;
                            }

                            queue.Enqueue((subdir, depth + 1));
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Skip inaccessible directories
                    }
                }
            }

            var completedAt = DateTimeOffset.UtcNow.ToString("o");
            var stdoutJson = JsonSerializer.Serialize(results);
            return Task.FromResult(new ExecutionReceiptDto(
                SchemaVersion: 1,
                StepId: auth.StepId,
                AuthId: auth.AuthId,
                StartedAt: startedAt,
                CompletedAt: completedAt,
                ExitCode: 0,
                ReportedSuccess: true,
                Stdout: stdoutJson,
                Stderr: string.Empty
            ));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(FailureReceipt(auth, startedAt, "Search operation was cancelled or timed out."));
        }
        catch (Exception ex)
        {
            return Task.FromResult(FailureReceipt(auth, startedAt, $"Search failed: {ex.Message}"));
        }
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
