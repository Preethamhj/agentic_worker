using Jarvis.Contracts;
using Jarvis.UI.ViewModels;

namespace Jarvis.UI.Handlers;

/// <summary>Bridges orchestration approval requests to WPF decision dialogs.</summary>
public sealed class WpfApprovalHandler : IApprovalHandler
{
    private readonly IUiDispatcher _dispatcher;
    private readonly IDecisionDialogPresenter _dialogs;
    private readonly TimeSpan _timeout;
    private readonly Func<ApprovalRequest, string?>? _resolvedImagePath;
    private readonly string? _userName;

    /// <summary>Creates a dispatcher-safe approval handler with a fail-closed timeout.</summary>
    public WpfApprovalHandler(
        IUiDispatcher dispatcher,
        IDecisionDialogPresenter dialogs,
        TimeSpan? timeout = null,
        Func<ApprovalRequest, string?>? resolvedImagePath = null,
        string? userName = null)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _timeout = timeout ?? TimeSpan.FromMinutes(2);
        if (_timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        _resolvedImagePath = resolvedImagePath;
        _userName = userName;
    }

    /// <inheritdoc />
    public async Task<ApprovalDecision> RequestApprovalAsync(
        ApprovalRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.Equals(request.Risk, "low", StringComparison.OrdinalIgnoreCase))
        {
#if DEBUG
            var autoApprove = Environment.GetEnvironmentVariable("JARVIS_AUTO_APPROVE");
            if (string.Equals(autoApprove, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(autoApprove, "true", StringComparison.OrdinalIgnoreCase))
                return new ApprovalDecision(true, "auto-low");
#endif
        }

        var viewModel = new ApprovalViewModel(
            request,
            resolvedImagePath: _resolvedImagePath?.Invoke(request),
            userName: _userName);

        return await _dispatcher.InvokeAsync(async () =>
        {
            await _dialogs.ShowApprovalAsync(viewModel, _timeout, cancellationToken);
            return viewModel.Decision;
        }, cancellationToken).ConfigureAwait(false);
    }
}
