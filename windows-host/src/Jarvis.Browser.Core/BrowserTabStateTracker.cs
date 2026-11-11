namespace Jarvis.Browser.Core;

/// <summary>Tracks current tab state and publishes changes from a browser runtime.</summary>
public sealed class BrowserTabStateTracker
{
    /// <summary>Creates an initially blank tab state.</summary>
    public BrowserTabStateTracker(BrowserTabId tabId)
    {
        if (tabId.Value == Guid.Empty)
            throw new ArgumentException("Browser tab IDs must not be empty.", nameof(tabId));

        State = new BrowserTabState(tabId, null, "New tab", false, false, false);
    }

    /// <summary>Raised only when the observable tab state changes.</summary>
    public event EventHandler<BrowserTabStateChangedEventArgs>? StateChanged;

    /// <summary>Gets the latest immutable state snapshot.</summary>
    public BrowserTabState State { get; private set; }

    /// <summary>Records the start of top-level navigation.</summary>
    public void NavigationStarted(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        Update(State with { Address = address, IsLoading = true });
    }

    /// <summary>Records the completion of top-level navigation.</summary>
    public void NavigationCompleted(
        Uri? address,
        string? title,
        bool canGoBack,
        bool canGoForward)
    {
        Update(State with
        {
            Address = address ?? State.Address,
            Title = NormalizeTitle(title, State.Title),
            IsLoading = false,
            CanGoBack = canGoBack,
            CanGoForward = canGoForward
        });
    }

    /// <summary>Updates the tab title after a document title change.</summary>
    public void SetTitle(string? title) =>
        Update(State with { Title = NormalizeTitle(title, State.Title) });

    /// <summary>Updates current back and forward history availability.</summary>
    public void SetHistoryState(bool canGoBack, bool canGoForward) =>
        Update(State with { CanGoBack = canGoBack, CanGoForward = canGoForward });

    private static string NormalizeTitle(string? title, string fallback) =>
        string.IsNullOrWhiteSpace(title) ? fallback : title.Trim();

    private void Update(BrowserTabState state)
    {
        if (state == State)
            return;

        var previous = State;
        State = state;
        StateChanged?.Invoke(this, new BrowserTabStateChangedEventArgs(previous, state));
    }
}
