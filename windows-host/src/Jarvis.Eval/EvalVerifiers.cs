using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Jarvis.Contracts;

namespace Jarvis.Eval;

public sealed class ReceiptEvalVerifier : IEvalVerifier
{
    public string VerifierId => "A";

    public Task<EvalVerdict> VerifyAsync(
        EvalCase evalCase,
        EvalExecutionContext execution,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var succeeded = execution.Receipt.ReportedSuccess && execution.Receipt.ExitCode == 0;
        return Task.FromResult(new EvalVerdict(
            VerifierId,
            succeeded ? EvalStatus.Verified : EvalStatus.Failed,
            succeeded ? [] : ["Receipt did not report successful execution."],
            TimeSpan.Zero));
    }
}

public sealed class SelfCheckEvalVerifier : IEvalVerifier
{
    public string VerifierId => "B";

    public async Task<EvalVerdict> VerifyAsync(
        EvalCase evalCase,
        EvalExecutionContext execution,
        CancellationToken cancellationToken = default)
    {
        var succeeded = await execution.ExecutorPostcondition(cancellationToken).ConfigureAwait(false);
        return new EvalVerdict(
            VerifierId,
            succeeded ? EvalStatus.Verified : EvalStatus.Failed,
            succeeded ? [] : ["Executor postcondition returned false."],
            TimeSpan.Zero);
    }
}

public interface IEvalReminderPredicateReader
{
    Task<IReadOnlyList<string>> FindMismatchesAsync(
        AuthorizationRecordDto authorization,
        CancellationToken cancellationToken = default);
}

public interface IEvalPredicateChecker
{
    Task<IReadOnlyList<string>> FindMismatchesAsync(
        EvalCase evalCase,
        EvalExecutionContext execution,
        CancellationToken cancellationToken = default);
}

public sealed class FileSystemEvalPredicateChecker : IEvalPredicateChecker
{
    public async Task<IReadOnlyList<string>> FindMismatchesAsync(
        EvalCase evalCase,
        EvalExecutionContext execution,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var authorization = execution.Authorization;
        switch (authorization.ToolId)
        {
            case "fs.create_file":
            case "fs.create_folder":
                return CheckFileOrFolder(authorization);
            case "fs.search_files":
                return CheckSearchResults(authorization, execution.ReportedPaths);
            case "app.launch":
                return CheckAuthorizedProcess(authorization);
            case "task.schedule_reminder":
                if (execution.ReminderPredicateReader is null)
                    return ["No independent reminder-task predicate reader was supplied."];
                return await execution.ReminderPredicateReader
                    .FindMismatchesAsync(authorization, cancellationToken)
                    .ConfigureAwait(false);
            default:
                return [$"Unsupported predicate tool '{authorization.ToolId}'."];
        }
    }

    private static IReadOnlyList<string> CheckFileOrFolder(AuthorizationRecordDto authorization)
    {
        if (!authorization.CanonicalArgs.TryGetValue("path", out var rawPath) || rawPath is not string path)
            return ["Signed plan path is missing."];

        var expectsDirectory = authorization.ToolId == "fs.create_folder";
        var exists = expectsDirectory ? Directory.Exists(path) : File.Exists(path);
        if (!exists)
            return ["Planned target does not exist."];

        if (!expectsDirectory
            && authorization.CanonicalArgs.TryGetValue("content", out var expectedContent)
            && expectedContent is string content)
        {
            var actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            var expectedHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                return ["Planned file content hash does not match the observed file."];
        }

        return [];
    }

    private static IReadOnlyList<string> CheckSearchResults(
        AuthorizationRecordDto authorization,
        IReadOnlyList<string> reportedPaths)
    {
        if (!authorization.CanonicalArgs.TryGetValue("root_path", out var rawRoot)
            || rawRoot is not string root
            || !authorization.CanonicalArgs.TryGetValue("pattern", out var rawPattern)
            || rawPattern is not string pattern)
        {
            return ["Signed search root or pattern is missing."];
        }

        var reported = new HashSet<string>(reportedPaths.Select(Path.GetFullPath), StringComparer.OrdinalIgnoreCase);
        if (reported.Any(path => !File.Exists(path)))
            return ["Reported search results include a path that does not exist."];

        var observedMatches = Directory.Exists(root)
            ? Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
                .Select(Path.GetFullPath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];
        if (!reported.SetEquals(observedMatches))
            return ["Reported search results differ from independently enumerated matches."];
        return [];
    }

    private static IReadOnlyList<string> CheckAuthorizedProcess(AuthorizationRecordDto authorization)
    {
        if (!authorization.Expected.TryGetValue("image_path", out var rawImagePath)
            || rawImagePath is not string imagePath)
        {
            return ["Signed authorized image path is missing."];
        }

        var inspectionFailures = new List<string>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (string.Equals(
                        Path.GetFullPath(process.MainModule?.FileName ?? ""),
                        Path.GetFullPath(imagePath),
                        StringComparison.OrdinalIgnoreCase))
                    {
                        return [];
                    }
                }
                catch (Exception exception) when (
                    exception is System.ComponentModel.Win32Exception
                    or InvalidOperationException
                    or NotSupportedException
                    or IOException
                    or ArgumentException)
                {
                    inspectionFailures.Add(exception.Message);
                }
            }
        }

        var detail = "No running process has the authorized image path.";
        if (inspectionFailures.Count > 0)
            detail += $" Could not inspect {inspectionFailures.Count} process(es); first error: {inspectionFailures[0]}";
        return [detail];
    }
}

public sealed class PredicateOnlyEvalVerifier(IEvalPredicateChecker predicateChecker) : IEvalVerifier
{
    public string VerifierId => "C";

    public async Task<EvalVerdict> VerifyAsync(
        EvalCase evalCase,
        EvalExecutionContext execution,
        CancellationToken cancellationToken = default)
    {
        var mismatches = await predicateChecker
            .FindMismatchesAsync(evalCase, execution, cancellationToken)
            .ConfigureAwait(false);
        return new EvalVerdict(
            VerifierId,
            mismatches.Count == 0 ? EvalStatus.Verified : EvalStatus.Failed,
            mismatches,
            TimeSpan.Zero);
    }
}

public sealed class VerificationEngineEvalVerifier(IVerifier verifier, bool includeStageDetails = false) : IEvalVerifier
{
    public string VerifierId => "D";

    public async Task<EvalVerdict> VerifyAsync(
        EvalCase evalCase,
        EvalExecutionContext execution,
        CancellationToken cancellationToken = default)
    {
        if (!ReferenceEquals(execution.VerificationToken.Authorization, execution.Authorization))
            throw new InvalidOperationException("StepToken authorization is not the evaluation's signed authorization instance.");

        var result = await verifier.VerifyAsync(
            execution.Receipt,
            execution.Observation,
            execution.VerificationToken,
            cancellationToken).ConfigureAwait(false);
        if (evalCase.FaultId == "F18")
        {
            result = await verifier.VerifyAsync(
                execution.Receipt,
                execution.Observation,
                execution.VerificationToken,
                cancellationToken).ConfigureAwait(false);
        }
        var status = result.Status switch
        {
            "VERIFIED" => EvalStatus.Verified,
            "FAILED" => EvalStatus.Failed,
            "ESCALATE" => EvalStatus.Escalate,
            _ => EvalStatus.Error
        };
        IReadOnlyList<string> reasons = includeStageDetails
            ? result.Reasons.Concat(result.Stages.Select(stage => $"{stage.Name}: {stage.Outcome} - {stage.Detail}")).ToArray()
            : result.Reasons;
        return new EvalVerdict(
            VerifierId,
            status,
            reasons,
            TimeSpan.FromMilliseconds(result.DurationMs),
            status == EvalStatus.Error ? $"Unknown VerificationEngine status '{result.Status}'." : null);
    }
}
