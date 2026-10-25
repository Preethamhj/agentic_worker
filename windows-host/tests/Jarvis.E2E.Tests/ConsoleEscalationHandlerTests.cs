using Jarvis.Cli;
using Jarvis.Contracts;
using Xunit;

namespace Jarvis.E2E.Tests;

public sealed class ConsoleEscalationHandlerTests
{
    [Fact]
    public async Task RequestDecisionAsync_Shows_Evidence_And_Accepts_Retry()
    {
        var writer = new StringWriter();
        var handler = new ConsoleEscalationHandler(new StringReader("r"), writer);

        var decision = await handler.RequestDecisionAsync(CreateEscalation());

        Assert.Equal(EscalationAction.RetryVerification, decision.Action);
        Assert.Contains("filesystem evidence was inconclusive", writer.ToString());
        Assert.Contains("[r]etry/[a]bort", writer.ToString());
    }

    [Fact]
    public async Task RequestDecisionAsync_Maps_Abort_Input_To_Abort()
    {
        var handler = new ConsoleEscalationHandler(new StringReader("a"), new StringWriter());

        var decision = await handler.RequestDecisionAsync(CreateEscalation());

        Assert.Equal(EscalationAction.Abort, decision.Action);
    }

    [Fact]
    public async Task RequestDecisionAsync_Maps_No_Answer_Before_Timeout_To_TimedOut()
    {
        var handler = new ConsoleEscalationHandler(
            new WaitingTextReader(),
            new StringWriter(),
            TimeSpan.FromMilliseconds(20));

        var decision = await handler.RequestDecisionAsync(CreateEscalation());

        Assert.Equal(EscalationAction.TimedOut, decision.Action);
    }

    private static VerificationResultDto CreateEscalation() => new(
        1,
        "step-1",
        "ESCALATE",
        [],
        [],
        [],
        ["filesystem evidence was inconclusive"],
        1);

    private sealed class WaitingTextReader : StringReader
    {
        public WaitingTextReader() : base(string.Empty) { }

        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null;
        }
    }
}
