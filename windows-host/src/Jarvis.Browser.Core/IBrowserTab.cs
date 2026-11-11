namespace Jarvis.Browser.Core;

/// <summary>Controls and observes one tab without exposing browser-engine types.</summary>
public interface IBrowserTab : IAsyncDisposable
{
    /// <summary>Gets this tab's runtime-scoped identifier.</summary>
    BrowserTabId Id { get; }

    /// <summary>Gets the latest immutable state snapshot.</summary>
    BrowserTabState State { get; }

    /// <summary>Raised when navigation, title, loading, or history state changes.</summary>
    event EventHandler<BrowserTabStateChangedEventArgs>? StateChanged;

    /// <summary>Navigates to a validated HTTP or HTTPS destination.</summary>
    Task NavigateAsync(Uri destination, CancellationToken cancellationToken = default);

    /// <summary>Navigates back when browser history permits.</summary>
    Task GoBackAsync(CancellationToken cancellationToken = default);

    /// <summary>Navigates forward when browser history permits.</summary>
    Task GoForwardAsync(CancellationToken cancellationToken = default);

    /// <summary>Reloads the current document.</summary>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Captures an experimental, bounded DOM snapshot. It is not independent
    /// verification and its element references are not live action targets.
    /// </summary>
    Task<ObservationResult> ObserveAsync(bool isActive, CancellationToken cancellationToken = default);
}
