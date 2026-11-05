using Jarvis.Contracts;

namespace Jarvis.Cli;

/// <summary>Prompts the user to retry or abort an inconclusive verification.</summary>
public sealed class ConsoleEscalationHandler : IEscalationHandler
{
    private readonly TextReader? _reader;
    private readonly TextWriter? _writer;
    private readonly TimeSpan _timeout;

    public ConsoleEscalationHandler(TextReader? reader = null, TextWriter? writer = null, TimeSpan? timeout = null)
    {
        _reader = reader;
        _writer = writer;
        _timeout = timeout ?? TimeSpan.FromMinutes(2);
        if (_timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    public async Task<EscalationDecision> RequestDecisionAsync(
        VerificationResultDto verification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(verification);
        cancellationToken.ThrowIfCancellationRequested();

        var writer = _writer ?? Console.Out;
        var reader = _reader ?? Console.In;
        await writer.WriteLineAsync("Independent verification needs review:").ConfigureAwait(false);
        foreach (var reason in verification.Reasons.Take(3))
            await writer.WriteLineAsync($"- {reason}").ConfigureAwait(false);
        await writer.WriteLineAsync("Retry verification only? [r]etry/[a]bort (timeout fails the task)").ConfigureAwait(false);

        using var timeoutTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutTokenSource.CancelAfter(_timeout);
        string? input;
        try
        {
            input = await reader.ReadLineAsync(timeoutTokenSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new EscalationDecision(EscalationAction.TimedOut, "Verification escalation timed out.");
        }

        return string.Equals(input?.Trim(), "r", StringComparison.OrdinalIgnoreCase)
            ? new EscalationDecision(EscalationAction.RetryVerification, "User requested verification retry.")
            : new EscalationDecision(EscalationAction.Abort, "User aborted inconclusive verification.");
    }
}
