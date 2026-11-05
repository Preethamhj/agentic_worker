using Jarvis.UI.ViewModels;

namespace Jarvis.UI;

/// <summary>Presents user decision dialogs without blocking the calling thread.</summary>
public interface IDecisionDialogPresenter
{
    /// <summary>Shows an approval dialog until a decision, closure, timeout, or cancellation.</summary>
    Task ShowApprovalAsync(ApprovalViewModel viewModel, TimeSpan timeout, CancellationToken cancellationToken);

    /// <summary>Shows an escalation dialog until retry, abort, timeout, or cancellation.</summary>
    Task ShowEscalationAsync(EscalationViewModel viewModel, TimeSpan timeout, CancellationToken cancellationToken);
}
