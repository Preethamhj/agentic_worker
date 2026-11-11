using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using Jarvis.Browser.Core;
using Jarvis.Composition;
using Jarvis.Contracts;
using Jarvis.Infrastructure;
using Jarvis.Infrastructure.Google;
using Jarvis.UI.ViewModels;

namespace Jarvis.UI;

/// <summary>Coordinates prompt submission and read-only task, history, and settings views.</summary>
public sealed class MainWindowViewModel : INotifyPropertyChanged
{
    private readonly IJarvisSessionFactory _sessionFactory;
    private readonly IApprovalHandler _approvalHandler;
    private readonly IEscalationHandler _escalationHandler;
    private TaskViewModel? _task;
    private string? _errorMessage;
    private bool _isRunning;
    private string _progressMessage = string.Empty;
    private string _prompt = string.Empty;
    private CancellationTokenSource? _currentTaskCancellation;
    private GoogleGmailMessage? _selectedMail;
    private MailResultViewModel? _mailDetail;

    /// <summary>Creates the WPF host view model around the shared Jarvis session factory.</summary>
    public MainWindowViewModel(
        IJarvisSessionFactory sessionFactory,
        IApprovalHandler approvalHandler,
        IEscalationHandler escalationHandler,
        GoogleAccountServices? googleAccountServices = null)
    {
        _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
        _approvalHandler = approvalHandler ?? throw new ArgumentNullException(nameof(approvalHandler));
        _escalationHandler = escalationHandler ?? throw new ArgumentNullException(nameof(escalationHandler));
        var settings = _sessionFactory.ReadSettings();
        Settings = new SettingsViewModel(settings.AllowedRoots, settings.ModelName, settings.DoctorResults);
        GoogleAccount = googleAccountServices is null
            ? null
            : new GoogleAccountViewModel(
                googleAccountServices.OAuthService,
                googleAccountServices.Policy,
                googleAccountServices.Options,
                googleAccountServices.SetServiceEnabled);
        GoogleWorkspace = googleAccountServices is null || GoogleAccount is null
            ? null
            : new GoogleWorkspaceViewModel(
                googleAccountServices.WorkspaceReader,
                googleAccountServices.Policy,
                GoogleAccount);
        History = new HistoryViewModel();
        if (googleAccountServices is not null
            && googleAccountServices.Policy.Enabled
            && googleAccountServices.Policy.GmailEnabled)
        {
            _mailSearcher = googleAccountServices.MailSearcher;
            _mailSummarizer = googleAccountServices.MailSummarizer;
        }
    }

    private readonly IGoogleMailSearcher? _mailSearcher;
    private readonly IMailSummarizer? _mailSummarizer;

    /// <summary>Raised when a view-bound property changes.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Prompt entered by the user.</summary>
    public string Prompt
    {
        get => _prompt;
        set => SetProperty(ref _prompt, value ?? string.Empty);
    }

    /// <summary>Current task presentation, if one has been submitted.</summary>
    public TaskViewModel? Task
    {
        get => _task;
        private set => SetProperty(ref _task, value);
    }

    /// <summary>Most recent host error, if startup or task submission failed.</summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    /// <summary>Whether a prompt is currently being handled.</summary>
    public bool IsRunning
    {
        get => _isRunning;
        private set => SetProperty(ref _isRunning, value);
    }

    /// <summary>Describes the currently active host operation.</summary>
    public string ProgressMessage
    {
        get => _progressMessage;
        private set => SetProperty(ref _progressMessage, value);
    }

    /// <summary>Read-only history projection for the history tab.</summary>
    public HistoryViewModel History { get; private set; }

    /// <summary>Read-only settings and diagnostics projection.</summary>
    public SettingsViewModel Settings { get; }

    /// <summary>Google account management, when composed by the Windows application.</summary>
    public GoogleAccountViewModel? GoogleAccount { get; }

    /// <summary>Explicit, read-only Calendar and Gmail data presentation.</summary>
    public GoogleWorkspaceViewModel? GoogleWorkspace { get; }

    /// <summary>Mail results fetched from Gmail via the prompt box, with local-model summaries.</summary>
    public ObservableCollection<GoogleGmailMessage> MailResults { get; } = [];

    /// <summary>Selected metadata row, whose body is not loaded until the user asks for an explanation.</summary>
    public GoogleGmailMessage? SelectedMail
    {
        get => _selectedMail;
        set => SetProperty(ref _selectedMail, value);
    }

    /// <summary>Full content and summary for the one mail explicitly being explained.</summary>
    public MailResultViewModel? MailDetail
    {
        get => _mailDetail;
        private set => SetProperty(ref _mailDetail, value);
    }

    /// <summary>Runs a user prompt through the same shared orchestration session as the CLI.</summary>
    public async Task RunPromptAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            ErrorMessage = "Enter a task description.";
            return;
        }

        if (IsRunning)
            throw new InvalidOperationException("A Jarvis task is already running.");

        if (_mailSearcher is not null && _mailSummarizer is not null
            && GmailPromptParser.IsExplainPrompt(prompt))
        {
            await ExplainSelectedMailAsync(cancellationToken);
            return;
        }

        if (_mailSearcher is not null && GmailPromptParser.TryParse(prompt, out var mailRequest))
        {
            await RunMailPromptAsync(mailRequest, cancellationToken);
            return;
        }

        IsRunning = true;
        ProgressMessage = "Starting the local AI server...";
        ErrorMessage = null;
        Task = null;
        using var taskCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _currentTaskCancellation = taskCancellation;
        try
        {
            await using var session = await _sessionFactory.StartAsync(
                _approvalHandler,
                _escalationHandler,
                taskCancellation.Token);
            ProgressMessage = "Planning and processing your request. Approval may be required.";
            var result = await session.RunTaskAsync(prompt, taskCancellation.Token);
            var history = session.ReadHistory(result.TaskId);
            Task = new TaskViewModel(result, history.StateTransitions);
            History = new HistoryViewModel(
                history.StateTransitions,
                history.PolicyValidations,
                history.ApprovalDecisions,
                history.Verifications,
                history.LegacySafetyDecisions);
            OnPropertyChanged(nameof(History));
        }
        catch (OperationCanceledException) when (taskCancellation.IsCancellationRequested)
        {
            ErrorMessage = "Task cancelled. Operations already started may have taken effect; check history and your files.";
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            _currentTaskCancellation = null;
            ProgressMessage = string.Empty;
            IsRunning = false;
        }
    }

    /// <summary>Resolves an explicit website or web-app request without executing a Jarvis tool.</summary>
    public async Task<Uri?> ResolveBrowserTargetAsync(
        string target,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            ErrorMessage = "Name a website or web app to open in the browser.";
            return null;
        }

        if (IsRunning)
            throw new InvalidOperationException("A Jarvis operation is already running.");

        cancellationToken.ThrowIfCancellationRequested();
        if (BrowserAddressPolicy.LooksLikeExplicitAddress(target))
        {
            ErrorMessage = null;
            if (BrowserAddressPolicy.TryNormalize(target, out var directAddress, out var error)
                && directAddress is not null)
                return directAddress;

            ErrorMessage = $"Invalid browser destination: {error}";
            return null;
        }

        IsRunning = true;
        ProgressMessage = "Resolving the browser destination with the local Ollama model...";
        ErrorMessage = null;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _currentTaskCancellation = cancellation;
        try
        {
            await using var session = await _sessionFactory.StartAsync(
                _approvalHandler,
                _escalationHandler,
                cancellation.Token);
            var resolution = await session.ResolveBrowserTargetAsync(target, cancellation.Token);
            if (string.Equals(resolution.Status, "open", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(resolution.Url))
                {
                    ErrorMessage = "The model returned an empty browser destination.";
                    return null;
                }

                if (!BrowserAddressPolicy.TryNormalize(resolution.Url, out var address, out var error)
                    || address is null)
                {
                    ErrorMessage = $"The model returned an invalid browser destination: {error}";
                    return null;
                }

                return address;
            }

            ErrorMessage = string.Equals(resolution.Status, "error", StringComparison.OrdinalIgnoreCase)
                ? resolution.Message
                : string.IsNullOrWhiteSpace(resolution.Message)
                    ? "The model could not identify a website destination. Please clarify the site name."
                    : resolution.Message;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            ErrorMessage = "Browser destination resolution cancelled.";
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            _currentTaskCancellation = null;
            ProgressMessage = string.Empty;
            IsRunning = false;
        }

        return null;
    }

    /// <summary>Uses the local model to refine a web search, falling back to the original prompt.</summary>
    public async Task<(Uri? Address, string? StatusMessage)> PrepareBrowserSearchAsync(
        string prompt,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(prompt))
        {
            ErrorMessage = "Enter a topic to search on the internet.";
            return (null, ErrorMessage);
        }

        if (IsRunning)
            throw new InvalidOperationException("A Jarvis operation is already running.");

        IsRunning = true;
        ProgressMessage = "Preparing a search query with the local Ollama model...";
        ErrorMessage = null;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _currentTaskCancellation = cancellation;
        try
        {
            await using var session = await _sessionFactory.StartAsync(
                _approvalHandler,
                _escalationHandler,
                cancellation.Token);
            var result = await session.PrepareBrowserSearchQueryAsync(prompt, cancellation.Token);
            var statusMessage = string.Equals(result.Status, "fallback", StringComparison.OrdinalIgnoreCase)
                ? result.Message
                : "Opening Google results. Choose the most relevant result; Jarvis has not ranked result links.";
            return (BrowserSearchUrlBuilder.Build(result.Query), statusMessage);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            ErrorMessage = "Browser search cancelled.";
        }
        catch (Exception exception)
        {
            ErrorMessage = $"Could not prepare the search query: {exception.Message}";
        }
        finally
        {
            _currentTaskCancellation = null;
            ProgressMessage = string.Empty;
            IsRunning = false;
        }

        return (null, ErrorMessage);
    }

    /// <summary>Plans one browser instruction with the local model after page disclosure approval.</summary>
    public async Task<BrowserPlanResponseDto> PlanBrowserChatAsync(
        string instruction,
        BrowserPageContextDto? page,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var session = await _sessionFactory.StartAsync(
                _approvalHandler,
                _escalationHandler,
                cancellationToken);
            return await session.PlanBrowserActionAsync(instruction, page, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new BrowserPlanResponseDto(1, "error", null, "Cancelled.");
        }
        catch (Exception exception)
        {
            return new BrowserPlanResponseDto(1, "error", null, $"Could not plan the instruction: {exception.Message}");
        }
    }

    private BrowserActionAuthority? _browserAuthority;

    /// <summary>The active browser approval, execution-consume and verification path, once created.</summary>
    public BrowserActionAuthority? BrowserAuthority => _browserAuthority;

    /// <summary>Asks the user to approve a typed browser search and returns the signed record on approval.</summary>
    public async Task<BrowserActionAuthorizationResult> AuthorizeBrowserSearchAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _browserAuthority ??= _sessionFactory.CreateBrowserActionAuthority(_approvalHandler);
            if (_browserAuthority is null)
                return new BrowserActionAuthorizationResult(false, null, "Browser authorization is unavailable.");
            return await _browserAuthority.AuthorizeSearchAsync(query, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new BrowserActionAuthorizationResult(false, null, $"Authorization failed: {exception.Message}");
        }
    }

    /// <summary>Asks the user to approve a specific link from the current page disclosure.</summary>
    public async Task<BrowserActionAuthorizationResult> AuthorizeBrowserLinkAsync(
        BrowserPageDisclosure disclosure,
        BrowserDisclosureLink link,
        BrowserTabId tabId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _browserAuthority ??= _sessionFactory.CreateBrowserActionAuthority(_approvalHandler);
            if (_browserAuthority is null)
                return new BrowserActionAuthorizationResult(false, null, "Browser authorization is unavailable.");
            if (disclosure.TabId != tabId || disclosure.FindLink(link.Id) != link)
                return new BrowserActionAuthorizationResult(false, null, "The selected tab changed; inspect the page again.");
            return await _browserAuthority.AuthorizeOpenLinkAsync(
                link.Id,
                disclosure.ObservationId.ToString(),
                tabId.ToString(),
                BrowserPageDisclosure.Fingerprint(link.Target),
                link.Target.Host,
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new BrowserActionAuthorizationResult(false, null, $"Authorization failed: {exception.Message}");
        }
    }

    /// <summary>Consumes a signed search record once; used by the browser executor.</summary>
    public ConsumedBrowserSearch ConsumeBrowserSearch(AuthorizationRecordDto record) =>
        (_browserAuthority ?? throw new InvalidOperationException("No browser authority is active."))
            .ConsumeSearch(record);

    /// <summary>Requests cancellation of the active task, if any.</summary>
    public void CancelCurrentTask() => _currentTaskCancellation?.Cancel();

    /// <summary>Loads and summarizes only the selected mail.</summary>
    public async Task ExplainSelectedMailAsync(CancellationToken cancellationToken = default)
    {
        if (_mailSearcher is null || _mailSummarizer is null)
        {
            ErrorMessage = "Gmail is not enabled.";
            return;
        }

        var selectedMail = SelectedMail;
        if (selectedMail is null)
        {
            ErrorMessage = "Select a mail from the list before asking for an explanation.";
            return;
        }

        if (IsRunning)
            throw new InvalidOperationException("A Jarvis task is already running.");

        IsRunning = true;
        ErrorMessage = null;
        MailDetail = null;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _currentTaskCancellation = cancellation;
        try
        {
            ProgressMessage = "Loading the selected mail...";
            var mail = await _mailSearcher.GetMailAsync(selectedMail.Id, cancellation.Token);
            ProgressMessage = "Summarizing the selected mail with the local model (up to 180 seconds)...";
            try
            {
                var summary = await _mailSummarizer.SummarizeAsync(mail, cancellation.Token);
                MailDetail = new MailResultViewModel(
                    mail.Subject, mail.From, mail.Date, mail.Body, summary);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                MailDetail = new MailResultViewModel(
                    mail.Subject, mail.From, mail.Date, mail.Body, string.Empty);
                ErrorMessage = $"The mail was loaded, but its summary failed: {exception.Message}";
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            ErrorMessage = "Mail explanation cancelled.";
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            _currentTaskCancellation = null;
            ProgressMessage = string.Empty;
            IsRunning = false;
        }
    }

    private async Task RunMailPromptAsync(GmailPromptRequest request, CancellationToken cancellationToken)
    {
        IsRunning = true;
        ErrorMessage = null;
        MailResults.Clear();
        SelectedMail = null;
        MailDetail = null;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _currentTaskCancellation = cancellation;
        try
        {
            var query = request.Query;
            if (request.TodayOnly)
            {
                var today = DateTime.Today;
                var tomorrow = today.AddDays(1);
                var localStart = new DateTimeOffset(
                    today, TimeZoneInfo.Local.GetUtcOffset(today));
                var localEnd = new DateTimeOffset(
                    tomorrow, TimeZoneInfo.Local.GetUtcOffset(tomorrow));
                query = $"{query} after:{localStart.ToUnixTimeSeconds()} before:{localEnd.ToUnixTimeSeconds()}".Trim();
            }

            ProgressMessage = request.TodayOnly
                ? "Searching today's non-spam Gmail messages..."
                : "Searching non-spam Gmail messages...";
            var mails = await _mailSearcher!.SearchMailAsync(
                query, request.MaxResults, cancellation.Token);
            if (mails.Count == 0)
            {
                ErrorMessage = request.TodayOnly
                    ? "No non-spam Gmail messages were found for today."
                    : "No non-spam Gmail messages matched that request.";
                return;
            }

            foreach (var mail in mails)
                MailResults.Add(mail);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            ErrorMessage = "Mail request cancelled.";
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
            _currentTaskCancellation = null;
            ProgressMessage = string.Empty;
            IsRunning = false;
        }
    }

    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
