using System.Text.Json;
using Jarvis.Contracts;

namespace Jarvis.Cli;

/// <summary>
/// Console approval handler showing canonical arguments to the user before prompt.
/// Security invariants:
/// 1. High risk actions must NEVER auto-approve under any circumstances.
/// 2. Auto-approval via JARVIS_AUTO_APPROVE is compiled out of Release builds.
/// 3. In Debug builds, auto-approval is restricted strictly to risk=low.
/// </summary>
public sealed class ConsoleApprovalHandler : IApprovalHandler
{
    private readonly TextReader? _reader;
    private readonly TimeSpan _timeout;

    public ConsoleApprovalHandler(TextReader? reader = null, TimeSpan? timeout = null)
    {
        _reader = reader;
        _timeout = timeout ?? TimeSpan.FromMinutes(2);
        if (_timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    public async Task<ApprovalDecision> RequestApprovalAsync(ApprovalRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var isLowRisk = string.Equals(request.Risk, "low", StringComparison.OrdinalIgnoreCase);

        bool canAutoApprove = false;

        // Invariant: High risk must NEVER auto-approve under any circumstances!
        if (isLowRisk)
        {
#if DEBUG
            // In Debug builds, JARVIS_AUTO_APPROVE environment variable can auto-approve ONLY risk=low.
            // Medium risk cannot be auto-approved via JARVIS_AUTO_APPROVE.
            // In Release builds, this entire block is compiled out.
            if (isLowRisk)
            {
                var env = Environment.GetEnvironmentVariable("JARVIS_AUTO_APPROVE");
                if (string.Equals(env, "1", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(env, "true", StringComparison.OrdinalIgnoreCase))
                {
                    canAutoApprove = true;
                }
            }
#endif
        }

        if (canAutoApprove)
        {
            Console.WriteLine($"[Auto-Approved risk '{request.Risk}' for step '{request.StepId}']");
            return new ApprovalDecision(true, "auto-low");
        }

        Console.WriteLine();
        Console.WriteLine("============================================================");
        Console.WriteLine("               AUTHORIZATION REQUIRED");
        Console.WriteLine("============================================================");
        Console.WriteLine($"Task ID:     {request.TaskId}");
        Console.WriteLine($"Step ID:     {request.StepId}");
        Console.WriteLine($"Tool ID:     {request.ToolId}");
        Console.WriteLine($"Risk Level:  {request.Risk.ToUpperInvariant()}");
        Console.WriteLine("Canonical Arguments:");
        foreach (var kv in request.CanonicalArgs)
        {
            var valStr = kv.Value is string s ? s : JsonSerializer.Serialize(kv.Value);
            Console.WriteLine($"  {kv.Key}: {valStr}");
        }
        Console.WriteLine("============================================================");

        Console.Write("Approve execution? [y/N]: ");
        var reader = _reader ?? Console.In;
        using var timeoutTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutTokenSource.CancelAfter(_timeout);
        string? input;
        try
        {
            input = await reader.ReadLineAsync(timeoutTokenSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ApprovalDecision(false, "timeout", "Approval timed out.");
        }
        var approved = string.Equals(input?.Trim(), "y", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(input?.Trim(), "yes", StringComparison.OrdinalIgnoreCase);

        if (!approved)
        {
            Console.WriteLine("[Execution REJECTED by user]");
            return new ApprovalDecision(false, Environment.UserName, "User denied execution.");
        }
        Console.WriteLine("[Execution APPROVED by user]");
        return new ApprovalDecision(true, Environment.UserName);
    }
}
