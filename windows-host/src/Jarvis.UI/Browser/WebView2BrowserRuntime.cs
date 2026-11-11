using System.IO;
using Jarvis.Contracts;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Threading;
using Jarvis.Browser.Core;
using Jarvis.Composition;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Jarvis.UI.Browser;

/// <summary>WebView2 adapter implementing the engine-independent browser runtime contract.</summary>
public sealed class WebView2BrowserRuntime : IBrowserRuntime
{
    private readonly string _userDataDirectory;
    private readonly Func<WebView2, Action> _attachView;
    private readonly Dictionary<BrowserTabId, WebView2BrowserTab> _tabs = [];
    private Task<CoreWebView2Environment>? _environmentInitialization;
    private CoreWebView2Environment? _environment;
    private BrowserTabId? _activeTabId;
    private bool _disposed;

    /// <summary>Creates a runtime with a dedicated profile and a WPF view attachment callback.</summary>
    /// <param name="userDataDirectory">Directory containing the dedicated WebView2 profile.</param>
    /// <param name="attachView">Attaches a view to a window and returns a callback to remove it on failure.</param>
    public WebView2BrowserRuntime(string userDataDirectory, Func<WebView2, Action> attachView)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userDataDirectory);
        ArgumentNullException.ThrowIfNull(attachView);
        _userDataDirectory = Path.GetFullPath(userDataDirectory);
        _attachView = attachView;
    }

    /// <inheritdoc />
    public IReadOnlyList<IBrowserTab> Tabs => _tabs.Values.Cast<IBrowserTab>().ToArray();

    /// <inheritdoc />
    public IBrowserTab? ActiveTab =>
        _activeTabId is { } tabId && _tabs.TryGetValue(tabId, out var tab)
            ? tab
            : null;

    internal IBrowserOpenLinkEvidenceSource CreateLinkEvidenceSource(
        WebView2BrowserTab source,
        Func<WebView2BrowserTab?> destination) =>
        new BrowserLinkEvidenceSource(this, source.Control.Dispatcher, destination);

    private sealed class BrowserLinkEvidenceSource(
        WebView2BrowserRuntime runtime,
        Dispatcher dispatcher,
        Func<WebView2BrowserTab?> destination) : IBrowserOpenLinkEvidenceSource
    {
        public async Task<BrowserTabTopologyEvidence> CaptureTopologyAsync(CancellationToken cancellationToken = default) =>
            await dispatcher.InvokeAsync(
                () => new BrowserTabTopologyEvidence(runtime._tabs.Keys.Select(id => id.ToString()).ToArray()),
                DispatcherPriority.Normal, cancellationToken).Task;

        public async Task<BrowserDestinationEvidence> CaptureDestinationAsync(CancellationToken cancellationToken = default) =>
            await dispatcher.InvokeAsync(() =>
            {
                var tab = destination();
                if (tab is null || !runtime._tabs.TryGetValue(tab.Id, out var actual)
                    || !ReferenceEquals(tab, actual))
                    return new BrowserDestinationEvidence(null, null, false);
                return tab.ReadDestinationEvidence();
            }, DispatcherPriority.Normal, cancellationToken).Task;
    }

    /// <inheritdoc />
    public async Task<IBrowserTab> CreateTabAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        await EnsureEnvironmentAsync(cancellationToken);
        ObjectDisposedException.ThrowIf(_disposed, this);

        var view = new WebView2();
        Action? detachView = null;
        try
        {
            detachView = _attachView(view);
            await view.EnsureCoreWebView2Async(_environment).WaitAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
            var tab = new WebView2BrowserTab(BrowserTabId.New(), view);
            _tabs.Add(tab.Id, tab);
            _activeTabId = tab.Id;
            return tab;
        }
        catch
        {
            detachView?.Invoke();
            view.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public Task ActivateTabAsync(BrowserTabId tabId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_tabs.ContainsKey(tabId))
            throw new KeyNotFoundException($"Browser tab '{tabId}' does not exist.");

        _activeTabId = tabId;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task CloseTabAsync(BrowserTabId tabId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_tabs.Remove(tabId, out var tab))
            return;

        if (_activeTabId == tabId)
            _activeTabId = _tabs.Keys.Cast<BrowserTabId?>().FirstOrDefault();

        await tab.DisposeAsync();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        var tabs = _tabs.Values.ToArray();
        _tabs.Clear();
        _activeTabId = null;
        foreach (var tab in tabs)
            await tab.DisposeAsync();
    }

    private async Task EnsureEnvironmentAsync(CancellationToken cancellationToken)
    {
        if (_environment is not null)
            return;

        Directory.CreateDirectory(_userDataDirectory);
        var initialization = _environmentInitialization ??=
            CoreWebView2Environment.CreateAsync(userDataFolder: _userDataDirectory);
        try
        {
            _environment = await initialization.WaitAsync(cancellationToken);
        }
        catch when (initialization.IsFaulted || initialization.IsCanceled)
        {
            _environmentInitialization = null;
            throw;
        }
    }
}

/// <summary>A WebView2-backed tab exposing its native view only to the WPF presentation layer.</summary>
public sealed class WebView2BrowserTab : IBrowserTab
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly BrowserTabStateTracker _stateTracker;
    private readonly CoreWebView2 _core;
    private readonly CdpSearchObserver _searchObserver;
    private readonly Dictionary<CoreWebView2Frame, string> _frames = [];
    private long _navigationGeneration;
    private bool _disposed;

    internal WebView2BrowserTab(BrowserTabId id, WebView2 control)
    {
        Id = id;
        Control = control ?? throw new ArgumentNullException(nameof(control));
        _core = Control.CoreWebView2
            ?? throw new InvalidOperationException("WebView2 did not initialize.");
        _stateTracker = new BrowserTabStateTracker(id);
        _searchObserver = new CdpSearchObserver(
            (method, parameters) => _core.CallDevToolsProtocolMethodAsync(method, parameters),
            () => _navigationGeneration);
        _stateTracker.StateChanged += OnStateChanged;
        _core.NavigationStarting += OnNavigationStarting;
        _core.NavigationCompleted += OnNavigationCompleted;
        _core.DocumentTitleChanged += OnDocumentTitleChanged;
        _core.FrameCreated += OnFrameCreated;
    }

    /// <summary>Gets the underlying WPF control for display in Jarvis.UI.</summary>
    public WebView2 Control { get; }

    /// <inheritdoc />
    public BrowserTabId Id { get; }

    /// <inheritdoc />
    public BrowserTabState State => _stateTracker.State;

    /// <inheritdoc />
    public event EventHandler<BrowserTabStateChangedEventArgs>? StateChanged;

    /// <inheritdoc />
    public Task NavigateAsync(Uri destination, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        cancellationToken.ThrowIfCancellationRequested();
        if (!BrowserAddressPolicy.IsWebAddress(destination.AbsoluteUri))
            throw new ArgumentException("Only HTTP and HTTPS destinations are allowed.", nameof(destination));

        ThrowIfDisposed();
        _core.Navigate(destination.AbsoluteUri);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task GoBackAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (_core.CanGoBack)
            _core.GoBack();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task GoForwardAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        if (_core.CanGoForward)
            _core.GoForward();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfDisposed();
        _core.Reload();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<ObservationResult> ObserveAsync(
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        cancellationToken = timeout.Token;
        var navigationGeneration = _navigationGeneration;
        var observations = new List<BrowserFrameObservation>
        {
            await CaptureFrameAsync(
                "main",
                string.Empty,
                isMainFrame: true,
                _core.ExecuteScriptAsync(BrowserObservationScripts.Capture),
                cancellationToken)
        };
        var issues = new List<ObservationIssue>();
        var availableFrames = _frames
            .Where(frame => frame.Key.IsDestroyed() == 0)
            .ToArray();
        var childFrames = availableFrames.Take(19).ToArray();

        if (availableFrames.Length > childFrames.Length)
            issues.Add(new ObservationIssue("frames", "FrameLimitReached"));

        foreach (var (frame, frameId) in childFrames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                observations.Add(await CaptureFrameAsync(
                    frameId,
                    frame.Name,
                    isMainFrame: false,
                    frame.ExecuteScriptAsync(BrowserObservationScripts.Capture),
                    cancellationToken));
            }
            catch (Exception exception) when (
                exception is COMException
                or InvalidOperationException
                or JsonException
                or TimeoutException)
            {
                issues.Add(new ObservationIssue(frameId, "FrameUnavailable"));
            }
        }

        ThrowIfDisposed();
        if (navigationGeneration != _navigationGeneration)
            throw new InvalidOperationException("The tab navigated while the page was being observed.");

        var snapshot = BrowserObservationMapper.Create(_stateTracker.State, isActive, observations);
        return new ObservationResult(snapshot, Array.AsReadOnly(issues.ToArray()));
    }

    internal async Task<CdpSearchObservation> ObserveSearchFieldsAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (State.IsLoading || State.Address is null)
            throw new InvalidOperationException("Open a page and wait for it to load before inspecting search fields.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var observation = await _searchObserver.CaptureAsync(timeout.Token);
        ThrowIfDisposed();
        return observation;
    }

    internal async Task<bool> IsSearchFieldCurrentAsync(
        Guid observationId,
        int candidate,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (State.IsLoading || State.Address is null)
            return false;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var isCurrent = await _searchObserver.IsCurrentAsync(observationId, candidate, timeout.Token);
        ThrowIfDisposed();
        return isCurrent && !State.IsLoading && State.Address is not null;
    }

    internal void InvalidateSearchObservation() => _searchObserver.Invalidate();

    internal CdpLinkObserver CreateLinkObserver(Func<bool> isActive) => new(
        (method, parameters) => _core.CallDevToolsProtocolMethodAsync(method, parameters),
        token => ObserveAsync(isActive(), token),
        () => _navigationGeneration);

    internal long NavigationGeneration => _navigationGeneration;

    internal BrowserDestinationEvidence ReadDestinationEvidence()
    {
        ThrowIfDisposed();
        return new BrowserDestinationEvidence(Id.ToString(), _core.Source, State.IsLoading);
    }

    /// <summary>Reads the address straight from the browser engine for independent verification.</summary>
    internal IBrowserEvidenceSource EvidenceSource => new BrowserEngineEvidenceSource(this);

    /// <summary>
    /// Enters a signed query into the single visible search field and submits it. Aborts when the
    /// field is missing, ambiguous, stale or the page navigates; success is judged only by the verifier.
    /// </summary>
    internal async Task SubmitSearchAsync(string query, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        var observation = await ObserveSearchFieldsAsync(cancellationToken);
        if (observation.Candidates.Count == 0)
            throw new InvalidOperationException("No visible search field was found on the selected page.");
        if (observation.Candidates.Count > 1)
            throw new InvalidOperationException("More than one search field was found; Jarvis will not guess which to use.");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        await _searchObserver.SubmitQueryAsync(observation.ObservationId, observation.Candidates[0], query, timeout.Token);
        ThrowIfDisposed();
    }

    /// <summary>
    /// Opens a signed, user-approved temporary link ID through CDP after checking that its
    /// original document, node identity, label, and target are still current.
    /// </summary>
    internal async Task OpenAuthorizedLinkAsync(
        BrowserPageDisclosure disclosure,
        BrowserDisclosureLink link,
        Func<ConsumedBrowserLink> consumeAuthorization,
        CdpLinkObserver observer,
        CdpBoundObservation captured,
        WebView2BrowserTab destination,
        Func<bool> isDestinationActive,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(disclosure);
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(consumeAuthorization);
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        if (ReferenceEquals(this, destination)
            || !isDestinationActive()
            || !await observer.IsCurrentAsync(captured, disclosure, link, timeout.Token)
            || !isDestinationActive())
            throw new InvalidOperationException("The selected link or browser tab changed. Inspect and approve it again.");
        timeout.Token.ThrowIfCancellationRequested();
        var authorization = consumeAuthorization();
        if (State.IsLoading
            || disclosure.TabId != Id
            || !string.Equals(authorization.TabId, Id.ToString(), StringComparison.Ordinal)
            || !string.Equals(authorization.ObservationId, disclosure.ObservationId.ToString(), StringComparison.Ordinal)
            || !string.Equals(authorization.LinkId, link.Id, StringComparison.Ordinal)
            || !string.Equals(authorization.TargetHash, BrowserPageDisclosure.Fingerprint(link.Target), StringComparison.Ordinal)
            || !string.Equals(authorization.TargetHost, link.Target.Host, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(authorization.Mode, "new_tab", StringComparison.Ordinal)
            || !BrowserAddressPolicy.IsWebAddress(link.Target.AbsoluteUri))
        {
            throw new InvalidOperationException("The signed link action does not match the selected page observation.");
        }

        timeout.Token.ThrowIfCancellationRequested();
        await destination.NavigateAsync(link.Target, timeout.Token);
    }

    private sealed class BrowserEngineEvidenceSource(WebView2BrowserTab tab) : IBrowserEvidenceSource
    {
        public Task<BrowserPageEvidence> CaptureAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            tab.ThrowIfDisposed();
            return Task.FromResult(new BrowserPageEvidence(tab._core.Source, tab.State.IsLoading));
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;

        _disposed = true;
        _navigationGeneration++;
        _searchObserver.Invalidate();
        _core.NavigationStarting -= OnNavigationStarting;
        _core.NavigationCompleted -= OnNavigationCompleted;
        _core.DocumentTitleChanged -= OnDocumentTitleChanged;
        _core.FrameCreated -= OnFrameCreated;
        _stateTracker.StateChanged -= OnStateChanged;
        _frames.Clear();
        Control.Dispose();
        return ValueTask.CompletedTask;
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs args)
    {
        _navigationGeneration++;
        _searchObserver.Invalidate();
        _frames.Clear();
        if (!BrowserAddressPolicy.IsWebAddress(args.Uri))
        {
            args.Cancel = true;
            return;
        }

        if (Uri.TryCreate(args.Uri, UriKind.Absolute, out var address))
            _stateTracker.NavigationStarted(address);
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args) =>
        _stateTracker.NavigationCompleted(
            Control.Source,
            _core.DocumentTitle,
            _core.CanGoBack,
            _core.CanGoForward);

    private void OnDocumentTitleChanged(object? sender, object args) =>
        _stateTracker.SetTitle(_core.DocumentTitle);

    private void OnFrameCreated(object? sender, CoreWebView2FrameCreatedEventArgs args)
    {
        var frameId = $"frame-{Guid.NewGuid():N}";
        _frames[args.Frame] = frameId;
    }

    private void OnStateChanged(object? sender, BrowserTabStateChangedEventArgs args) =>
        StateChanged?.Invoke(this, args);

    private static async Task<BrowserFrameObservation> CaptureFrameAsync(
        string frameId,
        string frameName,
        bool isMainFrame,
        Task<string> scriptTask,
        CancellationToken cancellationToken)
    {
        var json = await scriptTask.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        var document = JsonSerializer.Deserialize<BrowserFrameDocument>(json, JsonOptions)
            ?? throw new JsonException("The page observer returned no document.");
        var elements = (document.Elements ?? [])
            .Select(element => new BrowserElementObservation(
                element.Role ?? "generic",
                element.Name ?? string.Empty,
                element.VisibleText,
                element.Visible,
                element.Enabled,
                element.Attributes ?? new Dictionary<string, string>(StringComparer.Ordinal)))
            .ToArray();
        return new BrowserFrameObservation(
            frameId,
            Uri.TryCreate(document.Address, UriKind.Absolute, out var address) ? address : null,
            document.Title ?? string.Empty,
            frameName.Length > 0 ? frameName : document.Name ?? string.Empty,
            isMainFrame,
            document.DocumentToken ?? throw new JsonException("The page observer omitted its document token."),
            document.DomVersion,
            document.VisibleText ?? string.Empty,
            elements,
            document.IsTruncated);
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed class BrowserFrameDocument
    {
        public string? Address { get; set; }
        public string? Title { get; set; }
        public string? Name { get; set; }
        public string? DocumentToken { get; set; }
        public long DomVersion { get; set; }
        public string? VisibleText { get; set; }
        public BrowserFrameElement[]? Elements { get; set; }
        public bool IsTruncated { get; set; }
    }

    private sealed class BrowserFrameElement
    {
        public string? Role { get; set; }
        public string? Name { get; set; }
        public string? VisibleText { get; set; }
        public bool Visible { get; set; }
        public bool Enabled { get; set; }
        public Dictionary<string, string>? Attributes { get; set; }
    }

}
