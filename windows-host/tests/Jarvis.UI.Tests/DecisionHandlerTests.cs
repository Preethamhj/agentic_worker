using FluentAssertions;
using Jarvis.Contracts;
using Jarvis.UI;
using Jarvis.UI.Handlers;
using Jarvis.UI.ViewModels;
using Xunit;

namespace Jarvis.UI.Tests;

public sealed class DecisionHandlerTests
{
    [Fact]
    public async Task WpfApprovalHandler_Dispatches_Dialog_And_Returns_Human_Approval()
    {
        var dispatcher = new FakeUiDispatcher();
        var dialogs = new FakeDecisionDialogPresenter(
            approval: (viewModel, _) =>
            {
                viewModel.ApproveCommand.Execute(null);
                return Task.CompletedTask;
            });
        var handler = new WpfApprovalHandler(
            dispatcher,
            dialogs,
            TimeSpan.FromSeconds(20),
            userName: "test-user");

        var decision = await handler.RequestApprovalAsync(CreateApprovalRequest());

        dispatcher.InvocationCount.Should().Be(1);
        dialogs.LastApprovalTimeout.Should().Be(TimeSpan.FromSeconds(20));
        decision.Should().Be(new ApprovalDecision(true, "test-user"));
    }

    [Fact]
    public async Task WpfApprovalHandler_Maps_Window_Close_To_Deny()
    {
        var handler = new WpfApprovalHandler(
            new FakeUiDispatcher(),
            new FakeDecisionDialogPresenter(approval: (viewModel, _) =>
            {
                viewModel.Close();
                return Task.CompletedTask;
            }),
            userName: "test-user");

        var decision = await handler.RequestApprovalAsync(CreateApprovalRequest());

        decision.Approved.Should().BeFalse();
        decision.Approver.Should().Be("test-user");
    }

    [Fact]
    public async Task WpfApprovalHandler_Maps_Timeout_To_Timeout_Approver()
    {
        var handler = new WpfApprovalHandler(
            new FakeUiDispatcher(),
            new FakeDecisionDialogPresenter(approval: (viewModel, _) =>
            {
                viewModel.Timeout();
                return Task.CompletedTask;
            }));

        var decision = await handler.RequestApprovalAsync(CreateApprovalRequest());

        decision.Should().Be(new ApprovalDecision(false, "timeout", "Approval timed out."));
    }

    [Fact]
    public async Task WpfApprovalHandler_Propagates_Cancellation_Without_Blocking()
    {
        using var cancellation = new CancellationTokenSource();
        var dialogs = new FakeDecisionDialogPresenter(
            approval: async (_, token) => await Task.Delay(Timeout.InfiniteTimeSpan, token));
        var dispatcher = new FakeUiDispatcher();
        var handler = new WpfApprovalHandler(dispatcher, dialogs);
        var request = handler.RequestApprovalAsync(CreateApprovalRequest(), cancellation.Token);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        dispatcher.InvocationCount.Should().Be(1);
    }

    [Fact]
    public async Task WpfEscalationHandler_Dispatches_Dialog_And_Returns_Retry_Only()
    {
        var dispatcher = new FakeUiDispatcher();
        var dialogs = new FakeDecisionDialogPresenter(
            escalation: (viewModel, _) =>
            {
                viewModel.RetryCommand.Execute(null);
                return Task.CompletedTask;
            });
        var handler = new WpfEscalationHandler(dispatcher, dialogs, TimeSpan.FromSeconds(10));

        var decision = await handler.RequestDecisionAsync(CreateEscalation());

        dispatcher.InvocationCount.Should().Be(1);
        dialogs.LastEscalationTimeout.Should().Be(TimeSpan.FromSeconds(10));
        decision.Action.Should().Be(EscalationAction.RetryVerification);
    }

    [Fact]
    public async Task WpfEscalationHandler_Abort_And_Timeout_Do_Not_Request_Retry()
    {
        var abortHandler = new WpfEscalationHandler(
            new FakeUiDispatcher(),
            new FakeDecisionDialogPresenter(escalation: (viewModel, _) =>
            {
                viewModel.AbortCommand.Execute(null);
                return Task.CompletedTask;
            }));
        (await abortHandler.RequestDecisionAsync(CreateEscalation())).Action.Should().Be(EscalationAction.Abort);

        var timeoutHandler = new WpfEscalationHandler(
            new FakeUiDispatcher(),
            new FakeDecisionDialogPresenter(escalation: (viewModel, _) =>
            {
                viewModel.Timeout();
                return Task.CompletedTask;
            }));
        (await timeoutHandler.RequestDecisionAsync(CreateEscalation())).Action.Should().Be(EscalationAction.TimedOut);
    }

    private static ApprovalRequest CreateApprovalRequest() =>
        new("task-1", "step-1", "fs.create_file", new Dictionary<string, object?> { ["path"] = @"C:\Jarvis\file.txt" }, "medium");

    private static VerificationResultDto CreateEscalation() =>
        new(1, "step-1", "ESCALATE", [], [], [], ["ambiguous evidence"], 1);

    private sealed class FakeUiDispatcher : IUiDispatcher
    {
        public int InvocationCount { get; private set; }

        public Task<T> InvokeAsync<T>(Func<Task<T>> callback, CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            cancellationToken.ThrowIfCancellationRequested();
            return callback();
        }
    }

    private sealed class FakeDecisionDialogPresenter(
        Func<ApprovalViewModel, CancellationToken, Task>? approval = null,
        Func<EscalationViewModel, CancellationToken, Task>? escalation = null) : IDecisionDialogPresenter
    {
        public TimeSpan? LastApprovalTimeout { get; private set; }
        public TimeSpan? LastEscalationTimeout { get; private set; }

        public Task ShowApprovalAsync(ApprovalViewModel viewModel, TimeSpan timeout, CancellationToken cancellationToken)
        {
            LastApprovalTimeout = timeout;
            return approval?.Invoke(viewModel, cancellationToken) ?? Task.CompletedTask;
        }

        public Task ShowEscalationAsync(EscalationViewModel viewModel, TimeSpan timeout, CancellationToken cancellationToken)
        {
            LastEscalationTimeout = timeout;
            return escalation?.Invoke(viewModel, cancellationToken) ?? Task.CompletedTask;
        }
    }
}
