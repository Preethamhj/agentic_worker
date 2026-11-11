namespace Jarvis.Browser.Core;

/// <summary>Observable browser state for one tab, independent of its rendering engine.</summary>
public sealed record BrowserTabState(
    BrowserTabId TabId,
    Uri? Address,
    string Title,
    bool IsLoading,
    bool CanGoBack,
    bool CanGoForward);

/// <summary>Describes a change to the observable state of a browser tab.</summary>
public sealed class BrowserTabStateChangedEventArgs(
    BrowserTabState previous,
    BrowserTabState current) : EventArgs
{
    /// <summary>Gets the state before the change.</summary>
    public BrowserTabState Previous { get; } = previous;

    /// <summary>Gets the state after the change.</summary>
    public BrowserTabState Current { get; } = current;
}
