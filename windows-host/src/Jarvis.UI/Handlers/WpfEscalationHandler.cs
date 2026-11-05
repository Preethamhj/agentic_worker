using Jarvis.Contracts;
using Jarvis.UI.ViewModels;

namespace Jarvis.UI.Handlers;

/// <summary>Bridges verification escalations to non-blocking WPF decision dialogs.</summary>
public sealed class WpfEscalationHandler : IEscalationHandler
{
    private readonly IUiDispatcher _dispatcher;
    private readonly IDecisionDialogPresenter _dialogs;
    private readonly TimeSpan _timeout;

    /// <summary>Creates a dispatcher-safe escalation handler with an aborting timeout.</summary>
    public WpfEscalationHandler(
        IUiDispatcher dispatcher,
        IDecisionDialogPresenter dialogs,
        TimeSpan? timeout = null)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _timeout = timeout ?? TimeSpan.FromMinutes(2);
        if (_timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    /// <inheritdoc />
    public async Task<EscalationDecision> RequestDecisionAsync(
        VerificationResultDto verification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(verification);
        cancellationToken.ThrowIfCancellationRequested();
        var viewModel = new EscalationViewModel(verification);

        return await _dispatcher.InvokeAsync(async () =>
        {
            await _dialogs.ShowEscalationAsync(viewModel, _timeout, cancellationToken);
            return viewModel.Decision;
        }, cancellationToken).ConfigureAwait(false);
    }
}
