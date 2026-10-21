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

    public ConsoleApprovalHandler(TextReader? reader = null)
    {
        _reader = reader;
    }

    public Task<bool> RequestApprovalAsync(ApprovalRequest request, CancellationToken cancellationToken = default)
    {
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
            return Task.FromResult(true);
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
        var input = reader.ReadLine();
        var approved = string.Equals(input?.Trim(), "y", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(input?.Trim(), "yes", StringComparison.OrdinalIgnoreCase);

        if (!approved)
        {
            Console.WriteLine("[Execution REJECTED by user]");
        }
        else
        {
            Console.WriteLine("[Execution APPROVED by user]");
        }

        return Task.FromResult(approved);
    }
}
