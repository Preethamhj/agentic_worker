using System.Threading.Channels;
using Jarvis.Contracts;

namespace Jarvis.Core;

public sealed class ExecutionWorkItem
{
    public required string TaskId { get; init; }
    public required string StepId { get; init; }
    public required string Risk { get; init; }
    public required Func<CancellationToken, Task<ExecutionReceiptDto>> Work { get; init; }
    public required TaskCompletionSource<ExecutionReceiptDto> CompletionSource { get; init; }
    public TimeSpan? StepTimeout { get; init; }
    public CancellationToken CancellationToken { get; init; }
}

/// <summary>
/// Channel-based sequential execution queue defined in DESIGN.md section 8.
/// Invariants:
/// 1. Sequential execution: items execute in strict FIFO order.
/// 2. Concurrency limit: at most one high-risk task active at any time.
/// 3. Per-step timeout: enforced via linked CancellationTokenSource.
/// 4. Cancellation: cancels work item immediately.
/// </summary>
public sealed class ExecutionQueue : IAsyncDisposable
{
    private readonly Channel<ExecutionWorkItem> _channel;
    private readonly SemaphoreSlim _highRiskLock = new(1, 1);
    private readonly TimeSpan _defaultStepTimeout;
    private readonly Task _workerTask;
    private readonly CancellationTokenSource _shutdownCts = new();

    public ExecutionQueue(TimeSpan? defaultStepTimeout = null)
    {
        _defaultStepTimeout = defaultStepTimeout ?? TimeSpan.FromSeconds(60);
        _channel = Channel.CreateUnbounded<ExecutionWorkItem>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        _workerTask = Task.Run(ProcessQueueAsync);
    }

    public Task<ExecutionReceiptDto> EnqueueAsync(
        string taskId,
        string stepId,
        string risk,
        Func<CancellationToken, Task<ExecutionReceiptDto>> work,
        TimeSpan? stepTimeout = null,
        CancellationToken cancellationToken = default)
    {
        var tcs = new TaskCompletionSource<ExecutionReceiptDto>(TaskCreationOptions.RunContinuationsAsynchronously);

        var item = new ExecutionWorkItem
        {
            TaskId = taskId,
            StepId = stepId,
            Risk = risk,
            Work = work,
            CompletionSource = tcs,
            StepTimeout = stepTimeout,
            CancellationToken = cancellationToken
        };

        if (!_channel.Writer.TryWrite(item))
        {
            tcs.SetException(new InvalidOperationException("ExecutionQueue channel is closed."));
        }

        return tcs.Task;
    }

    private async Task ProcessQueueAsync()
    {
        var reader = _channel.Reader;

        while (await reader.WaitToReadAsync(_shutdownCts.Token).ConfigureAwait(false))
        {
            while (reader.TryRead(out var item))
            {
                if (item.CancellationToken.IsCancellationRequested)
                {
                    item.CompletionSource.TrySetCanceled(item.CancellationToken);
                    continue;
                }

                var isHighRisk = string.Equals(item.Risk, "high", StringComparison.OrdinalIgnoreCase);
                bool lockAcquired = false;

                try
                {
                    if (isHighRisk)
                    {
                        await _highRiskLock.WaitAsync(item.CancellationToken).ConfigureAwait(false);
                        lockAcquired = true;
                    }

                    var timeout = item.StepTimeout ?? _defaultStepTimeout;
                    using var timeoutCts = new CancellationTokenSource(timeout);
                    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(item.CancellationToken, timeoutCts.Token, _shutdownCts.Token);

                    try
                    {
                        var receipt = await item.Work(linkedCts.Token).ConfigureAwait(false);
                        item.CompletionSource.TrySetResult(receipt);
                    }
                    catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
                    {
                        item.CompletionSource.TrySetException(new TimeoutException($"Execution of step '{item.StepId}' timed out after {timeout.TotalSeconds:F1}s."));
                    }
                    catch (OperationCanceledException oce)
                    {
                        item.CompletionSource.TrySetCanceled(oce.CancellationToken);
                    }
                    catch (Exception ex)
                    {
                        item.CompletionSource.TrySetException(ex);
                    }
                }
                catch (OperationCanceledException oce)
                {
                    item.CompletionSource.TrySetCanceled(oce.CancellationToken);
                }
                catch (Exception ex)
                {
                    item.CompletionSource.TrySetException(ex);
                }
                finally
                {
                    if (lockAcquired)
                    {
                        _highRiskLock.Release();
                    }
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        _shutdownCts.Cancel();

        try
        {
            await _workerTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        finally
        {
            _shutdownCts.Dispose();
            _highRiskLock.Dispose();
        }
    }
}
