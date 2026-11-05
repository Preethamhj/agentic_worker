using System.ComponentModel;
using System.Runtime.CompilerServices;
using Jarvis.Composition;
using Jarvis.Contracts;
using Jarvis.Infrastructure;
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
    private string _prompt = string.Empty;

    /// <summary>Creates the WPF host view model around the shared Jarvis session factory.</summary>
    public MainWindowViewModel(
        IJarvisSessionFactory sessionFactory,
        IApprovalHandler approvalHandler,
        IEscalationHandler escalationHandler)
    {
        _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
        _approvalHandler = approvalHandler ?? throw new ArgumentNullException(nameof(approvalHandler));
        _escalationHandler = escalationHandler ?? throw new ArgumentNullException(nameof(escalationHandler));
        var settings = _sessionFactory.ReadSettings();
        Settings = new SettingsViewModel(settings.AllowedRoots, settings.ModelName, settings.DoctorResults);
        History = new HistoryViewModel();
    }

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

    /// <summary>Read-only history projection for the history tab.</summary>
    public HistoryViewModel History { get; private set; }

    /// <summary>Read-only settings and diagnostics projection.</summary>
    public SettingsViewModel Settings { get; }

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

        IsRunning = true;
        ErrorMessage = null;
        try
        {
            await using var session = await _sessionFactory.StartAsync(
                _approvalHandler,
                _escalationHandler,
                cancellationToken);
            var result = await session.RunTaskAsync(prompt, cancellationToken);
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception.Message;
        }
        finally
        {
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
