using System.Globalization;
using Jarvis.Contracts;

namespace Jarvis.UI.ViewModels;

/// <summary>Read-only signed-call details and fail-closed approval decision state.</summary>
public sealed class ApprovalViewModel : ViewModelBase
{
    private readonly IClock _clock;
    private readonly string _userName;
    private bool _decisionFinalized;

    /// <summary>Creates the approval dialog state from the canonical request.</summary>
    public ApprovalViewModel(
        ApprovalRequest request,
        IClock? clock = null,
        string? resolvedImagePath = null,
        string? userName = null)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        _clock = clock ?? SystemClock.Instance;
        _userName = string.IsNullOrWhiteSpace(userName) ? Environment.UserName : userName;
        ToolId = request.ToolId;
        CanonicalArguments = request.CanonicalArgs;
        RiskBadge = request.Risk.ToUpperInvariant();
        ResolvedImagePath = request.ToolId == "app.launch" ? resolvedImagePath : null;
        (ReminderTimeLocal, ReminderTimeIso, _reminderTimeUtc) = ReadReminderTime(request);
        Decision = new ApprovalDecision(false, _userName, "User denied execution.");
        ApproveCommand = new RelayCommand(Approve, () => !_decisionFinalized);
        DenyCommand = new RelayCommand(Deny, () => !_decisionFinalized);
        RefreshCountdown();
    }

    private readonly DateTimeOffset? _reminderTimeUtc;

    /// <summary>The unmodified request received from policy validation.</summary>
    public ApprovalRequest Request { get; }

    /// <summary>Tool identifier whose canonical call is awaiting approval.</summary>
    public string ToolId { get; }

    /// <summary>Canonical arguments passed to authorization signing.</summary>
    public IReadOnlyDictionary<string, object?> CanonicalArguments { get; }

    /// <summary>Uppercase risk level suitable for a prominent dialog badge.</summary>
    public string RiskBadge { get; }

    /// <summary>Resolved allowlisted image path for app.launch, when supplied by composition.</summary>
    public string? ResolvedImagePath { get; }

    /// <summary>Reminder time converted to the user's local time zone.</summary>
    public DateTimeOffset? ReminderTimeLocal { get; }

    /// <summary>Original ISO time string from the canonical request.</summary>
    public string? ReminderTimeIso { get; }

    /// <summary>Time remaining until the reminder, clamped to zero after its scheduled time.</summary>
    public TimeSpan? ReminderCountdown { get; private set; }

    /// <summary>Approval defaults to denial; dialog closure and timeout remain explicit decisions.</summary>
    public ApprovalDecision Decision { get; private set; }

    /// <summary>Command that records an approval by the current interactive user.</summary>
    public System.Windows.Input.ICommand ApproveCommand { get; }

    /// <summary>Command that records a denial by the current interactive user.</summary>
    public System.Windows.Input.ICommand DenyCommand { get; }

    /// <summary>Returns the current decision after the dialog is closed without a button decision.</summary>
    public void Close() => FinalizeDecision(new ApprovalDecision(false, _userName, "User denied execution."));

    /// <summary>Records a denial whose approver is the reserved timeout identity.</summary>
    public void Timeout() => FinalizeDecision(new ApprovalDecision(false, "timeout", "Approval timed out."));

    /// <summary>Refreshes the countdown using the injected clock.</summary>
    public void RefreshCountdown()
    {
        ReminderCountdown = _reminderTimeUtc is { } reminder
            ? MaxZero(reminder - _clock.UtcNow)
            : null;
        OnPropertyChanged(nameof(ReminderCountdown));
    }

    private void Approve() => FinalizeDecision(new ApprovalDecision(true, _userName));

    private void Deny() => FinalizeDecision(new ApprovalDecision(false, _userName, "User denied execution."));

    private void FinalizeDecision(ApprovalDecision decision)
    {
        if (_decisionFinalized)
            return;

        Decision = decision;
        _decisionFinalized = true;
        OnPropertyChanged(nameof(Decision));
        ((RelayCommand)ApproveCommand).RaiseCanExecuteChanged();
        ((RelayCommand)DenyCommand).RaiseCanExecuteChanged();
    }

    private static TimeSpan MaxZero(TimeSpan value) => value < TimeSpan.Zero ? TimeSpan.Zero : value;

    private static (DateTimeOffset? Local, string? Iso, DateTimeOffset? Utc) ReadReminderTime(ApprovalRequest request)
    {
        if (request.ToolId != "task.schedule_reminder"
            || !request.CanonicalArgs.TryGetValue("time_iso", out var value)
            || value is not string iso
            || !DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time))
        {
            return (null, null, null);
        }

        return (time.ToLocalTime(), iso, time.ToUniversalTime());
    }
}
