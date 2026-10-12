using System.Text.Json;
using Jarvis.Core;

namespace Jarvis.Cli;

/// <summary>
/// Console approval handler showing canonical arguments to the user before prompt.
/// Supports automated approval via JARVIS_AUTO_APPROVE environment variable.
/// </summary>
public sealed class ConsoleApprovalHandler : IApprovalHandler
{
    private readonly bool _autoApprove;

    public ConsoleApprovalHandler(bool? autoApprove = null)
    {
        _autoApprove = autoApprove ?? string.Equals(Environment.GetEnvironmentVariable("JARVIS_AUTO_APPROVE"), "1", StringComparison.OrdinalIgnoreCase)
                                   || string.Equals(Environment.GetEnvironmentVariable("JARVIS_AUTO_APPROVE"), "true", StringComparison.OrdinalIgnoreCase);
    }

    public Task<bool> RequestApprovalAsync(
        string taskId,
        string stepId,
        string toolId,
        IReadOnlyDictionary<string, object?> canonicalArgs,
        string risk,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine();
        Console.WriteLine("============================================================");
        Console.WriteLine("               AUTHORIZATION REQUIRED");
        Console.WriteLine("============================================================");
        Console.WriteLine($"Task ID:     {taskId}");
        Console.WriteLine($"Step ID:     {stepId}");
        Console.WriteLine($"Tool ID:     {toolId}");
        Console.WriteLine($"Risk Level:  {risk.ToUpperInvariant()}");
        Console.WriteLine("Canonical Arguments:");
        foreach (var kv in canonicalArgs)
        {
            var valStr = kv.Value is string s ? s : JsonSerializer.Serialize(kv.Value);
            Console.WriteLine($"  {kv.Key}: {valStr}");
        }
        Console.WriteLine("============================================================");

        if (_autoApprove)
        {
            Console.WriteLine("[Auto-Approved via JARVIS_AUTO_APPROVE flag]");
            return Task.FromResult(true);
        }

        Console.Write("Approve execution? [y/N]: ");
        var input = Console.ReadLine();
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
