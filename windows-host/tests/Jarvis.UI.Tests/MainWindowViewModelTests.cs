using FluentAssertions;
using Jarvis.Contracts;
using Jarvis.Composition;
using Jarvis.Core;
using Jarvis.Infrastructure;
using Jarvis.UI;
using Jarvis.UI.Handlers;
using Jarvis.UI.ViewModels;
using Xunit;

namespace Jarvis.UI.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public async Task CancelCurrentTask_Cancels_Active_Session_And_Shows_Status()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new FakeJarvisSession
        {
            Run = async ct =>
            {
                started.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                throw new InvalidOperationException("Unreachable");
            }
        };
        var viewModel = new MainWindowViewModel(
            new FakeJarvisSessionFactory(session),
            new WpfApprovalHandler(new InlineDispatcher(), new NoOpDialogs()),
            new WpfEscalationHandler(new InlineDispatcher(), new NoOpDialogs()));

        var run = viewModel.RunPromptAsync("find documents");
        await started.Task;
        viewModel.IsRunning.Should().BeTrue();
        viewModel.CancelCurrentTask();
        await run;

        viewModel.IsRunning.Should().BeFalse();
        viewModel.ErrorMessage.Should().Contain("cancel").And.Contain("may have");
        viewModel.Task.Should().BeNull();
    }

    [Fact]
    public async Task RunPromptAsync_Uses_Shared_Orchestration_Session_And_Refreshes_Task_History()
    {
        var session = new FakeJarvisSession();
        var factory = new FakeJarvisSessionFactory(session);
        var viewModel = new MainWindowViewModel(
            factory,
            new WpfApprovalHandler(new InlineDispatcher(), new NoOpDialogs()),
            new WpfEscalationHandler(new InlineDispatcher(), new NoOpDialogs()));

        await viewModel.RunPromptAsync("create a file");

        factory.StartCount.Should().Be(1);
        session.Prompt.Should().Be("create a file");
        session.ReportedResult.Should().BeSameAs(session.Result);
        viewModel.Task.Should().NotBeNull();
        viewModel.Task!.FinalMessage.Should().Be("verified");
        viewModel.History.Events.Should().ContainSingle(entry => entry.Kind == "Approval");
        viewModel.Settings.ModelName.Should().Be("configured-model");
        viewModel.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task RunPromptAsync_Rejects_Empty_Prompt_Without_Starting_Host()
    {
        var factory = new FakeJarvisSessionFactory(new FakeJarvisSession());
        var viewModel = new MainWindowViewModel(
            factory,
            new WpfApprovalHandler(new InlineDispatcher(), new NoOpDialogs()),
            new WpfEscalationHandler(new InlineDispatcher(), new NoOpDialogs()));

        await viewModel.RunPromptAsync("  ");

        factory.StartCount.Should().Be(0);
        viewModel.ErrorMessage.Should().NotBeNullOrWhiteSpace();
        viewModel.Task.Should().BeNull();
    }

    private sealed class FakeJarvisSessionFactory(FakeJarvisSession session) : IJarvisSessionFactory
    {
        public int StartCount { get; private set; }

        public JarvisSettingsSnapshot ReadSettings() =>
            new(["C:\\Jarvis\\workspace"], "configured-model", ["Doctor: PASS"]);

        public Task<IJarvisSession> StartAsync(
            IApprovalHandler approvalHandler,
            IEscalationHandler escalationHandler,
            CancellationToken cancellationToken = default)
        {
            StartCount++;
            return Task.FromResult<IJarvisSession>(session);
        }
    }

    private sealed class FakeJarvisSession : IJarvisSession
    {
        public Func<CancellationToken, Task<TaskOrchestratorResult>>? Run { get; init; }
        public int AiServerPort => 8000;
        public string? Prompt { get; private set; }
        public TaskOrchestratorResult? ReportedResult { get; private set; }
        public TaskOrchestratorResult Result { get; } = new(
            "task-1",
            TaskState.Completed,
            [],
            [new VerificationResultDto(1, "step-1", "VERIFIED", [], [], [], [], 1)],
            "verified");

        public Task<TaskOrchestratorResult> RunTaskAsync(string prompt, CancellationToken cancellationToken = default)
        {
            Prompt = prompt;
            ReportedResult = Result;
            if (Run is not null)
                return Run(cancellationToken);
            return Task.FromResult(Result);
        }

        public JarvisHistorySnapshot ReadHistory(string? taskId = null) =>
            new(
                [],
                [],
                [new ApprovalDecisionRecord("task-1", "step-1", true, "alice", null, DateTimeOffset.UtcNow)],
                [],
                []);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class InlineDispatcher : IUiDispatcher
    {
        public Task<T> InvokeAsync<T>(Func<Task<T>> callback, CancellationToken cancellationToken = default) =>
            callback();
    }

    private sealed class NoOpDialogs : IDecisionDialogPresenter
    {
        public Task ShowApprovalAsync(ViewModels.ApprovalViewModel viewModel, TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task ShowEscalationAsync(ViewModels.EscalationViewModel viewModel, TimeSpan timeout, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
