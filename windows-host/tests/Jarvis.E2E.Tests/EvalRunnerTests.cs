using FluentAssertions;
using Jarvis.Contracts;
using Jarvis.Eval;
using Xunit;

namespace Jarvis.E2E.Tests;

public sealed class EvalRunnerTests
{
    [Fact]
    public async Task RunCase_Executes_Once_And_Gives_All_Verifiers_The_Same_Context()
    {
        var context = CreateContext();
        var executionCount = 0;
        var verifiers = Enumerable.Range(0, 4)
            .Select(index => new RecordingVerifier($"verifier-{index}"))
            .ToArray();
        var runner = new EvalRunner(20261025);

        var result = await runner.RunCaseAsync(
            CreateCase(),
            _ =>
            {
                executionCount++;
                return Task.FromResult(context);
            },
            verifiers);

        executionCount.Should().Be(1);
        result.Verdicts.Should().HaveCount(4);
        verifiers.Should().OnlyContain(verifier => ReferenceEquals(verifier.Context, context));
        result.Verdicts.Select(verdict => verdict.VerifierId)
            .Should().BeEquivalentTo(verifiers.Select(verifier => verifier.VerifierId));
    }

    [Fact]
    public async Task RunCase_Shuffles_Verifier_Order_Deterministically_From_Seed()
    {
        var verifiers = Enumerable.Range(0, 8)
            .Select(index => new RecordingVerifier($"verifier-{index}"))
            .ToArray();
        var evalCase = CreateCase();
        var first = await new EvalRunner(20261025)
            .RunCaseAsync(evalCase, _ => Task.FromResult(CreateContext()), verifiers);
        var second = await new EvalRunner(20261025)
            .RunCaseAsync(evalCase, _ => Task.FromResult(CreateContext()), verifiers);

        first.Verdicts.Select(verdict => verdict.VerifierId)
            .Should().Equal(second.Verdicts.Select(verdict => verdict.VerifierId));
    }

    [Fact]
    public async Task RunCase_Records_Verifier_Exception_As_Error_Not_Verified()
    {
        var verifier = new ThrowingVerifier();
        var result = await new EvalRunner(20261025)
            .RunCaseAsync(CreateCase(), _ => Task.FromResult(CreateContext()), [verifier]);

        result.Verdicts.Should().ContainSingle();
        result.Verdicts[0].Status.Should().Be(EvalStatus.Error);
        result.Verdicts[0].Status.Should().NotBe(EvalStatus.Verified);
        result.Verdicts[0].Error.Should().Contain("verifier failed");
    }

    private static EvalCase CreateCase() =>
        new("case-1", "fs.create_file", "task-1", "F0", EvalGroundTruth.Good, 1, 20261025);

    private static EvalExecutionContext CreateContext()
    {
        var authorization = new AuthorizationRecordDto(
            1, "auth-1", "task-1", "step-1", "fs.create_file", [], [], [], [], "low", "auto-low",
            "2026-10-25T00:00:00Z", "2026-10-26T00:00:00Z", "2026-10-25T00:00:00Z",
            "2026-10-25T23:59:59Z", "nonce", "signature");
        var receipt = new ExecutionReceiptDto(1, "step-1", "auth-1", "start", "end", 0, true, "", "");
        var token = new StepToken(authorization, "nonce", []);
        return new EvalExecutionContext(
            authorization,
            receipt,
            new ExecutionObservation(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow),
            token,
            new object(),
            _ => ValueTask.FromResult(true),
            []);
    }

    private sealed class RecordingVerifier(string verifierId) : IEvalVerifier
    {
        public string VerifierId { get; } = verifierId;
        public EvalExecutionContext? Context { get; private set; }

        public Task<EvalVerdict> VerifyAsync(
            EvalCase evalCase,
            EvalExecutionContext execution,
            CancellationToken cancellationToken = default)
        {
            Context = execution;
            return Task.FromResult(new EvalVerdict(
                VerifierId, EvalStatus.Verified, [], TimeSpan.Zero));
        }
    }

    private sealed class ThrowingVerifier : IEvalVerifier
    {
        public string VerifierId => "throwing";

        public Task<EvalVerdict> VerifyAsync(
            EvalCase evalCase,
            EvalExecutionContext execution,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("verifier failed");
    }
}
