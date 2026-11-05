using Jarvis.Contracts;

namespace Jarvis.UI.ViewModels;

/// <summary>Evidence summary and fail-closed retry-or-abort state for ESCALATE.</summary>
public sealed class EscalationViewModel : ViewModelBase
{
    private bool _decisionFinalized;

    /// <summary>Creates the escalation state from an independent verification result.</summary>
    public EscalationViewModel(VerificationResultDto verification)
    {
        Verification = verification ?? throw new ArgumentNullException(nameof(verification));
        EvidenceSummary = BuildEvidenceSummary(verification);
        Decision = new EscalationDecision(EscalationAction.Abort, "Verification escalation was aborted.");
        RetryCommand = new RelayCommand(
            () => FinalizeDecision(new EscalationDecision(EscalationAction.RetryVerification, "User requested verification retry.")),
            () => !_decisionFinalized);
        AbortCommand = new RelayCommand(
            () => FinalizeDecision(new EscalationDecision(EscalationAction.Abort, "User aborted inconclusive verification.")),
            () => !_decisionFinalized);
    }

    /// <summary>Original independent verification result; execution receipts are not included.</summary>
    public VerificationResultDto Verification { get; }

    /// <summary>Human-readable status, stage, side-effect, and reason summary.</summary>
    public string EvidenceSummary { get; }

    /// <summary>Selected action; defaults to abort.</summary>
    public EscalationDecision Decision { get; private set; }

    /// <summary>Command that requests a fresh verification attempt only.</summary>
    public System.Windows.Input.ICommand RetryCommand { get; }

    /// <summary>Command that aborts the task.</summary>
    public System.Windows.Input.ICommand AbortCommand { get; }

    /// <summary>Returns a timeout outcome, which the orchestrator resolves as task failure.</summary>
    public void Timeout() =>
        FinalizeDecision(new EscalationDecision(EscalationAction.TimedOut, "Verification escalation timed out."));

    private void FinalizeDecision(EscalationDecision decision)
    {
        if (_decisionFinalized)
            return;

        Decision = decision;
        _decisionFinalized = true;
        OnPropertyChanged(nameof(Decision));
        ((RelayCommand)RetryCommand).RaiseCanExecuteChanged();
        ((RelayCommand)AbortCommand).RaiseCanExecuteChanged();
    }

    private static string BuildEvidenceSummary(VerificationResultDto verification)
    {
        var lines = new List<string> { $"Status: {verification.Status}" };
        lines.AddRange(verification.Stages.Select(stage => $"{stage.Name}: {stage.Outcome} - {stage.Detail}"));
        lines.AddRange(verification.SideEffects.Select(effect => $"Side effect: {effect}"));
        lines.AddRange(verification.Reasons.Select(reason => $"Reason: {reason}"));
        return string.Join(Environment.NewLine, lines);
    }
}
