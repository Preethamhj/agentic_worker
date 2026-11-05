using System.Windows.Threading;

namespace Jarvis.UI.Handlers;

/// <summary>WPF dispatcher adapter that awaits callbacks without blocking the UI thread.</summary>
public sealed class WpfUiDispatcher(Dispatcher dispatcher) : IUiDispatcher
{
    private readonly Dispatcher _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

    /// <inheritdoc />
    public Task<T> InvokeAsync<T>(Func<Task<T>> callback, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(callback);
        cancellationToken.ThrowIfCancellationRequested();
        if (_dispatcher.CheckAccess())
            return callback();

        return _dispatcher
            .InvokeAsync(callback, DispatcherPriority.Normal, cancellationToken)
            .Task
            .Unwrap();
    }
}
