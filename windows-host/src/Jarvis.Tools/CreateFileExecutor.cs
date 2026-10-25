using System.IO;
using System.Text;
using Jarvis.Contracts;
using Jarvis.Safety;

namespace Jarvis.Tools;

/// <summary>
/// Executor for fs.create_file.
/// Invariants:
/// - Calls AuthorizationService.Consume first and works only from canonical_args.
/// - Re-validates path at use time with PathPolicy and Win32PathResolver (GetFinalPathNameByHandle).
/// - Writes a temp file in the same folder, then moves without overwrite.
/// - Returns an ExecutionReceiptDto.
/// </summary>
public sealed class CreateFileExecutor : IToolExecutor
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public string ToolId => "fs.create_file";

    private readonly PathPolicy _pathPolicy;
    private readonly AuthorizationService _authService;

#if DEBUG
    public bool DebugReportSuccessWithoutCreatingFile { get; set; }
#endif

    public CreateFileExecutor(PathPolicy pathPolicy, AuthorizationService authService)
    {
        _pathPolicy = pathPolicy ?? throw new ArgumentNullException(nameof(pathPolicy));
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
    }

    public async Task<ExecutionReceiptDto> ExecuteAsync(VerifiedAuthorization auth, CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.UtcNow.ToString("o");

        // 1. Consume authorization and re-verify HMAC over actual fields
        _authService.Consume(auth, auth.StepId);

#if DEBUG
        if (DebugReportSuccessWithoutCreatingFile)
        {
            return new ExecutionReceiptDto(
                SchemaVersion: 1,
                StepId: auth.StepId,
                AuthId: auth.AuthId,
                StartedAt: startedAt,
                CompletedAt: DateTimeOffset.UtcNow.ToString("o"),
                ExitCode: 0,
                ReportedSuccess: true,
                Stdout: "Debug test executor claimed success without creating the authorized file.",
                Stderr: string.Empty);
        }
#endif

        // 2. Read only from CanonicalArgs
        if (!auth.CanonicalArgs.TryGetValue("path", out var pathObj) || pathObj is null)
        {
            return FailureReceipt(auth, startedAt, "Missing required argument 'path'.");
        }

        var path = pathObj.ToString()!;
        var content = auth.CanonicalArgs.TryGetValue("content", out var contentObj) && contentObj != null
            ? contentObj.ToString()!
            : string.Empty;

        // 3. Re-validate path at use time
        var (isValid, errorMessage) = Win32PathResolver.ValidateUseTimePath(path, _pathPolicy);
        if (!isValid)
        {
            return FailureReceipt(auth, startedAt, errorMessage ?? "Path validation failed.");
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (string.IsNullOrEmpty(directory))
        {
            return FailureReceipt(auth, startedAt, "Invalid target directory.");
        }

        if (!Directory.Exists(directory))
        {
            try
            {
                Directory.CreateDirectory(directory);
            }
            catch (Exception ex)
            {
                return FailureReceipt(auth, startedAt, $"Failed to create directory '{directory}': {ex.Message}");
            }
        }

        // 4. Write temp file in the same folder, then move without overwrite
        var tempFilePath = Path.Combine(directory, $".tmp_{Guid.NewGuid():N}");
        try
        {
            // If destination already exists, refuse to overwrite
            if (File.Exists(path))
            {
                return FailureReceipt(auth, startedAt, $"Target file '{path}' already exists. Overwrite prohibited.");
            }

            await File.WriteAllTextAsync(tempFilePath, content, Utf8NoBom, cancellationToken);

            // Atomic move without overwrite
            File.Move(tempFilePath, path, overwrite: false);

            var completedAt = DateTimeOffset.UtcNow.ToString("o");
            return new ExecutionReceiptDto(
                SchemaVersion: 1,
                StepId: auth.StepId,
                AuthId: auth.AuthId,
                StartedAt: startedAt,
                CompletedAt: completedAt,
                ExitCode: 0,
                ReportedSuccess: true,
                Stdout: $"File created successfully: {path}",
                Stderr: string.Empty
            );
        }
        catch (IOException ex)
        {
            return FailureReceipt(auth, startedAt, $"File operation failed (exists or lock): {ex.Message}");
        }
        catch (Exception ex)
        {
            return FailureReceipt(auth, startedAt, $"Failed to create file: {ex.Message}");
        }
        finally
        {
            // Clean up temporary file if still present
            try
            {
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
            catch
            {
                // Best-effort cleanup
            }
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
