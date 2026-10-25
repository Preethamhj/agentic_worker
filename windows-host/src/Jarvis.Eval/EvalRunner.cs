using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Jarvis.Eval;

public sealed class EvalRunner(int seed)
{
    public async Task<EvalRunResult> RunCaseAsync(
        EvalCase evalCase,
        Func<CancellationToken, Task<EvalExecutionContext>> executeOnce,
        IReadOnlyCollection<IEvalVerifier> verifiers,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evalCase);
        ArgumentNullException.ThrowIfNull(executeOnce);
        ArgumentNullException.ThrowIfNull(verifiers);
        if (verifiers.Select(verifier => verifier.VerifierId)
                .Distinct(StringComparer.Ordinal)
                .Count() != verifiers.Count)
        {
            throw new ArgumentException("Verifier IDs must be unique within a case.", nameof(verifiers));
        }

        var execution = await executeOnce(cancellationToken).ConfigureAwait(false);
        var verdicts = new List<EvalVerdict>(verifiers.Count);
        foreach (var verifier in Shuffle(verifiers, evalCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var verdict = await verifier.VerifyAsync(evalCase, execution, cancellationToken)
                    .ConfigureAwait(false);
                verdicts.Add(verdict with { VerifierId = verifier.VerifierId, Latency = stopwatch.Elapsed });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                verdicts.Add(new EvalVerdict(
                    verifier.VerifierId,
                    EvalStatus.Error,
                    ["Verifier threw an exception."],
                    stopwatch.Elapsed,
                    exception.ToString()));
            }
        }

        return new EvalRunResult(evalCase, verdicts);
    }

    private IReadOnlyList<IEvalVerifier> Shuffle(
        IReadOnlyCollection<IEvalVerifier> verifiers,
        EvalCase evalCase)
    {
        var seedBytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{seed}:{evalCase.Id}"));
        var random = new Random(BitConverter.ToInt32(seedBytes, 0));
        var shuffled = verifiers.ToArray();
        for (var index = shuffled.Length - 1; index > 0; index--)
        {
            var swapIndex = random.Next(index + 1);
            (shuffled[index], shuffled[swapIndex]) = (shuffled[swapIndex], shuffled[index]);
        }

        return shuffled;
    }
}
