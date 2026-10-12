using System.IO;
using Jarvis.Contracts;
using Jarvis.Safety;

namespace Jarvis.Tools;

/// <summary>
/// Executor for fs.create_folder.
/// Invariants:
/// - Calls AuthorizationService.Consume first and works only from canonical_args.
/// - Re-validates path at use time with PathPolicy and Win32PathResolver.
/// - Creates folder and re-validates the created directory.
/// - Returns an ExecutionReceiptDto.
/// </summary>
public sealed class CreateFolderExecutor : IToolExecutor
{
    public string ToolId => "fs.create_folder";

    private readonly PathPolicy _pathPolicy;
    private readonly AuthorizationService _authService;

    public CreateFolderExecutor(PathPolicy pathPolicy, AuthorizationService authService)
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
        if (!auth.CanonicalArgs.TryGetValue("path", out var pathObj) || pathObj is null)
        {
            return Task.FromResult(FailureReceipt(auth, startedAt, "Missing required argument 'path'."));
        }

        var path = pathObj.ToString()!;

        // 3. Re-validate path at use time
        var (isValid, errorMessage) = Win32PathResolver.ValidateUseTimePath(path, _pathPolicy);
        if (!isValid)
        {
            return Task.FromResult(FailureReceipt(auth, startedAt, errorMessage ?? "Path validation failed."));
        }

        try
        {
            Directory.CreateDirectory(path);

            // Re-validate after creation to verify no junction was created
            var (isCreatedValid, createdErrorMessage) = Win32PathResolver.ValidateUseTimePath(path, _pathPolicy);
            if (!isCreatedValid)
            {
                return Task.FromResult(FailureReceipt(auth, startedAt, createdErrorMessage ?? "Created folder validation failed."));
            }

            var completedAt = DateTimeOffset.UtcNow.ToString("o");
            return Task.FromResult(new ExecutionReceiptDto(
                SchemaVersion: 1,
                StepId: auth.StepId,
                AuthId: auth.AuthId,
                StartedAt: startedAt,
                CompletedAt: completedAt,
                ExitCode: 0,
                ReportedSuccess: true,
                Stdout: $"Folder created successfully: {path}",
                Stderr: string.Empty
            ));
        }
        catch (Exception ex)
        {
            return Task.FromResult(FailureReceipt(auth, startedAt, $"Failed to create folder: {ex.Message}"));
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
