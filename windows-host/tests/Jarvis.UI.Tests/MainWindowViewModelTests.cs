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
    public async Task RunPromptAsync_Shows_Progress_While_Session_Is_Running()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource<TaskOrchestratorResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var session = new FakeJarvisSession
        {
            Run = _ =>
            {
                started.SetResult();
                return finish.Task;
            }
        };
        var viewModel = new MainWindowViewModel(
            new FakeJarvisSessionFactory(session),
            new WpfApprovalHandler(new InlineDispatcher(), new NoOpDialogs()),
            new WpfEscalationHandler(new InlineDispatcher(), new NoOpDialogs()));

        var run = viewModel.RunPromptAsync("find documents");
        await started.Task;
        viewModel.IsRunning.Should().BeTrue();
        viewModel.ProgressMessage.Should().NotBeNullOrWhiteSpace();
        finish.SetResult(session.Result);
        await run;
        viewModel.ProgressMessage.Should().BeEmpty();
        viewModel.Task!.FinalMessage.Should().Be("verified");
    }

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

    [Fact]
    public async Task ResolveBrowserTargetAsync_Opens_Explicit_Domain_Without_Starting_Ollama()
    {
        var factory = new FakeJarvisSessionFactory(new FakeJarvisSession());
        var viewModel = new MainWindowViewModel(
            factory,
            new WpfApprovalHandler(new InlineDispatcher(), new NoOpDialogs()),
            new WpfEscalationHandler(new InlineDispatcher(), new NoOpDialogs()));

        var address = await viewModel.ResolveBrowserTargetAsync("amazon.in");

        address!.AbsoluteUri.Should().Be("https://amazon.in/");
        factory.StartCount.Should().Be(0);
    }

    [Theory]
    [InlineData("https://user:password@example.com/")]
    [InlineData("javascript:alert(1)")]
    public async Task ResolveBrowserTargetAsync_Rejects_Invalid_Explicit_Addresses_Without_Model(string target)
    {
        var factory = new FakeJarvisSessionFactory(new FakeJarvisSession());
        var viewModel = new MainWindowViewModel(
            factory,
            new WpfApprovalHandler(new InlineDispatcher(), new NoOpDialogs()),
            new WpfEscalationHandler(new InlineDispatcher(), new NoOpDialogs()));

        var address = await viewModel.ResolveBrowserTargetAsync(target);

        address.Should().BeNull();
        viewModel.ErrorMessage.Should().NotBeNullOrWhiteSpace();
        factory.StartCount.Should().Be(0);
    }

    [Fact]
    public async Task ResolveBrowserTargetAsync_Uses_Local_Session_And_Validates_Url()
    {
        var session = new FakeJarvisSession
        {
            BrowserResolution = new BrowserResolveResponseDto(
                1, "open", "https://mail.google.com/", string.Empty)
        };
        var factory = new FakeJarvisSessionFactory(session);
        var viewModel = new MainWindowViewModel(
            factory,
            new WpfApprovalHandler(new InlineDispatcher(), new NoOpDialogs()),
            new WpfEscalationHandler(new InlineDispatcher(), new NoOpDialogs()));

        var address = await viewModel.ResolveBrowserTargetAsync("Gmail");

        address!.AbsoluteUri.Should().Be("https://mail.google.com/");
        session.BrowserTarget.Should().Be("Gmail");
        factory.StartCount.Should().Be(1);
        viewModel.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task ResolveBrowserTargetAsync_Rejects_NonWeb_Model_Output()
    {
        var session = new FakeJarvisSession
        {
            BrowserResolution = new BrowserResolveResponseDto(
                1, "open", "javascript:alert(1)", string.Empty)
        };
        var viewModel = new MainWindowViewModel(
            new FakeJarvisSessionFactory(session),
            new WpfApprovalHandler(new InlineDispatcher(), new NoOpDialogs()),
            new WpfEscalationHandler(new InlineDispatcher(), new NoOpDialogs()));

        var address = await viewModel.ResolveBrowserTargetAsync("Gmail");

        address.Should().BeNull();
        viewModel.ErrorMessage.Should().Contain("invalid browser destination");
    }

    [Fact]
    public async Task PrepareBrowserSearchAsync_Opens_Encoded_Google_Search_And_Reports_User_Selection()
    {
        var session = new FakeJarvisSession
        {
            BrowserSearch = new BrowserSearchQueryResponseDto(
                1, "ready", "best beginner C++ tutorial", "ready")
        };
        var viewModel = new MainWindowViewModel(
            new FakeJarvisSessionFactory(session),
            new WpfApprovalHandler(new InlineDispatcher(), new NoOpDialogs()),
            new WpfEscalationHandler(new InlineDispatcher(), new NoOpDialogs()));

        var search = await viewModel.PrepareBrowserSearchAsync("find C++ tutorial online");

        search.Address!.Host.Should().Be("www.google.com");
        search.Address.AbsolutePath.Should().Be("/search");
        search.Address.Query.Should().Contain("best%20beginner%20C%2B%2B%20tutorial");
        search.StatusMessage.Should().Contain("has not ranked");
        session.SearchPrompt.Should().Be("find C++ tutorial online");
    }

    [Fact]
    public async Task PrepareBrowserSearchAsync_Uses_Original_Query_When_Ollama_Reports_Fallback()
    {
        var session = new FakeJarvisSession
        {
            BrowserSearch = new BrowserSearchQueryResponseDto(
                1, "fallback", "original search", "Ollama is unavailable.")
        };
        var viewModel = new MainWindowViewModel(
            new FakeJarvisSessionFactory(session),
            new WpfApprovalHandler(new InlineDispatcher(), new NoOpDialogs()),
            new WpfEscalationHandler(new InlineDispatcher(), new NoOpDialogs()));

        var search = await viewModel.PrepareBrowserSearchAsync("original search");

        search.Address!.Query.Should().Contain("original%20search");
        search.StatusMessage.Should().Be("Ollama is unavailable.");
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
        public BrowserResolveResponseDto BrowserResolution { get; init; } =
            new(1, "open", "https://example.com/", string.Empty);
        public BrowserSearchQueryResponseDto BrowserSearch { get; init; } =
            new(1, "ready", "example search", string.Empty);
        public int AiServerPort => 8000;
        public string? Prompt { get; private set; }
        public string? BrowserTarget { get; private set; }
        public string? SearchPrompt { get; private set; }
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

        public Task<BrowserResolveResponseDto> ResolveBrowserTargetAsync(
            string target,
            CancellationToken cancellationToken = default)
        {
            BrowserTarget = target;
            return Task.FromResult(BrowserResolution);
        }

        public Task<BrowserPlanResponseDto> PlanBrowserActionAsync(
            string instruction,
            BrowserPageContextDto? page,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new BrowserPlanResponseDto(1, "clarify", null, "Not supported in this fake."));

        public Task<BrowserSearchQueryResponseDto> PrepareBrowserSearchQueryAsync(
            string prompt,
            CancellationToken cancellationToken = default)
        {
            SearchPrompt = prompt;
            return Task.FromResult(BrowserSearch);
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
