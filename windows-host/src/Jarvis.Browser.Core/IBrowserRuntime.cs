namespace Jarvis.Browser.Core;

/// <summary>Provides browser tabs independently of a specific browser engine.</summary>
public interface IBrowserRuntime : IAsyncDisposable
{
    /// <summary>Gets a snapshot of tabs owned by this runtime.</summary>
    IReadOnlyList<IBrowserTab> Tabs { get; }

    /// <summary>Gets the currently active tab, if any.</summary>
    IBrowserTab? ActiveTab { get; }

    /// <summary>Creates a blank tab in the current browser session.</summary>
    Task<IBrowserTab> CreateTabAsync(CancellationToken cancellationToken = default);

    /// <summary>Activates a tab owned by this runtime.</summary>
    Task ActivateTabAsync(BrowserTabId tabId, CancellationToken cancellationToken = default);

    /// <summary>Closes and releases a tab owned by this runtime.</summary>
    Task CloseTabAsync(BrowserTabId tabId, CancellationToken cancellationToken = default);
}
