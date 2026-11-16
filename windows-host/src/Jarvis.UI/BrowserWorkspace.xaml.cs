using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Jarvis.Browser.Core;
using Jarvis.UI.Browser;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Jarvis.UI;

/// <summary>Hosts user-operated WebView2 tabs in a profile dedicated to Jarvis.</summary>
public partial class BrowserWorkspace : UserControl
{
    private readonly Dictionary<TabItem, IBrowserTab> _browserTabs = [];
    private readonly Dictionary<TabItem, TextBlock> _tabTitles = [];
    private readonly Dictionary<WebView2, (TabItem Tab, Button CloseButton)> _initializingViews = [];
    private readonly SemaphoreSlim _tabCreationGate = new(1, 1);
    private WebView2BrowserRuntime? _runtime;
    private Window? _ownerWindow;
    private CancellationTokenSource? _observationCancellation;
    private long _observationVersion;
    private bool _initialTabCreated;
    private bool _initializing;
    private bool _agentTabAttachmentExpected;
    private TabItem? _expectedAgentSelection;
    private WebView2BrowserTab? _agentDestination;

    /// <summary>Creates the browser workspace.</summary>
    public BrowserWorkspace()
    {
        InitializeComponent();
    }

    /// <summary>Opens a validated web address in a new browser tab.</summary>
    public async Task<bool> OpenInNewTabAsync(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (!BrowserAddressPolicy.TryNormalize(address.AbsoluteUri, out var normalized, out var error)
            || normalized is null)
        {
            ShowStatus($"Browser navigation rejected: {error}");
            return false;
        }

        return await CreateTabAsync(normalized);
    }

    /// <summary>Displays a status message in the browser workspace.</summary>
    public void ShowStatus(string message) =>
        StatusText.Text = string.IsNullOrWhiteSpace(message) ? "Ready." : message;

    private async void BrowserWorkspace_Loaded(object sender, RoutedEventArgs e)
    {
        _ownerWindow ??= Window.GetWindow(this);
        if (_ownerWindow is not null)
        {
            _ownerWindow.Closed -= OwnerWindow_Closed;
            _ownerWindow.Closed += OwnerWindow_Closed;
        }

        if (_initialTabCreated || _initializing)
            return;

        _initializing = true;
        try
        {
            _initialTabCreated = await CreateTabAsync(null, onlyIfEmpty: true);
        }
        finally
        {
            _initializing = false;
        }
    }

    private void BrowserWorkspace_Unloaded(object sender, RoutedEventArgs e)
    {
        _chatCancellation?.Cancel();
        foreach (var tab in _browserTabs.Values.OfType<WebView2BrowserTab>())
            tab.InvalidateSearchObservation();
        ClearSnapshot("Snapshot cleared. Select Inspect page to capture the selected tab.");
    }

    private async void NewTab_Click(object sender, RoutedEventArgs e) =>
        await CreateTabAsync(null);

    private async void Navigate_Click(object sender, RoutedEventArgs e) =>
        await NavigateCurrentAsync();

    private async void AddressInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        await NavigateCurrentAsync();
    }

    private async void Back_Click(object sender, RoutedEventArgs e) =>
        await RunOnSelectedTabAsync(tab => tab.GoBackAsync());

    private async void Forward_Click(object sender, RoutedEventArgs e) =>
        await RunOnSelectedTabAsync(tab => tab.GoForwardAsync());

    private async void Refresh_Click(object sender, RoutedEventArgs e) =>
        await RunOnSelectedTabAsync(tab => tab.RefreshAsync());

    private async void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (BrowserTabs.SelectedItem is not TabItem tab)
            return;

        await CloseTabAsync(tab);
        if (BrowserTabs.Items.Count == 0)
            await CreateTabAsync(null);
    }

    private async void BrowserTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, BrowserTabs))
            return;

        if (e.RemovedItems.Count > 0
            && !(e.AddedItems.Count == 1 && ReferenceEquals(e.AddedItems[0], _expectedAgentSelection)))
            _chatCancellation?.Cancel();
        foreach (var oldTab in e.RemovedItems.OfType<TabItem>())
        {
            if (_browserTabs.TryGetValue(oldTab, out var previousTab) && previousTab is WebView2BrowserTab nativeTab)
                nativeTab.InvalidateSearchObservation();
        }
        ClearSnapshot("Select Inspect page to capture the selected tab.");
        var tab = GetSelectedBrowserTab();
        if (tab is not null && _runtime is not null)
        {
            try
            {
                await _runtime.ActivateTabAsync(tab.Id);
            }
            catch (Exception exception)
            {
                ShowStatus($"Could not activate browser tab: {exception.Message}");
            }
        }

        AddressInput.Text = tab?.State.Address?.AbsoluteUri ?? string.Empty;
        UpdateNavigationButtons(tab);
    }

    /// <summary>Planner supplied by the host; receives only a user-approved safe page projection.</summary>
    public Func<string, Jarvis.Contracts.BrowserPageContextDto, CancellationToken, Task<Jarvis.Contracts.BrowserPlanResponseDto>>? ChatPlanner { get; set; }

    /// <summary>Approval and signing path supplied by the host for planned searches.</summary>
    public Func<string, CancellationToken, Task<Jarvis.Composition.BrowserActionAuthorizationResult>>? ChatAuthorizer { get; set; }

    /// <summary>Approval and signing path supplied by the host for a selected page link.</summary>
    public Func<BrowserPageDisclosure, BrowserDisclosureLink, BrowserTabId, CancellationToken,
        Task<Jarvis.Composition.BrowserActionAuthorizationResult>>? ChatLinkAuthorizer { get; set; }

    /// <summary>Supplies the active authority used to consume the signed record and verify the result.</summary>
    public Func<Jarvis.Composition.BrowserActionAuthority?>? ChatAuthority { get; set; }

    private async Task<string> RunApprovedSearchAsync(
        Jarvis.Contracts.AuthorizationRecordDto record,
        BrowserPageDisclosure disclosure,
        CancellationToken cancellationToken)
    {
        var authority = ChatAuthority?.Invoke();
        if (authority is null || GetSelectedBrowserTab() is not WebView2BrowserTab tab)
            return "No browser tab or authority is available, so nothing was done.";
        if (tab.State.Address is null || tab.State.IsLoading)
            return "Open a page and wait for it to finish loading first; nothing was done.";

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            var current = await tab.ObserveAsync(isActive: true, linkedCancellation.Token);
            if (!disclosure.IsPageCurrent(current))
                return "The page changed after disclosure approval. Inspect it again; nothing was done.";

            var baseline = await authority.BeginSearchVerificationAsync(tab.EvidenceSource, linkedCancellation.Token);
            var consumed = authority.ConsumeSearch(record);
            await tab.SubmitSearchAsync(consumed.Query, linkedCancellation.Token);
            var result = await authority.VerifySearchAsync(record, tab.EvidenceSource, baseline, linkedCancellation.Token);
            return result.Status switch
            {
                "VERIFIED" => "Verified from the browser's own address: the search page loaded with your query.",
                "FAILED" => $"Not verified (FAILED): {string.Join(' ', result.Reasons)}",
                _ => $"Could not confirm (ESCALATE): {string.Join(' ', result.Reasons)} Please check the page yourself."
            };
        }
        catch (OperationCanceledException)
        {
            return "The search timed out or was cancelled; it was not verified.";
        }
        catch (Exception exception)
        {
            return $"The search did not run ({exception.GetType().Name}: {exception.Message}). It was not verified.";
        }
    }

    private async Task<string> RunApprovedLinkAsync(
        Jarvis.Contracts.AuthorizationRecordDto record,
        BrowserPageDisclosure disclosure,
        BrowserDisclosureLink link,
        CdpLinkObserver observer,
        CdpBoundObservation captured,
        WebView2BrowserTab tab,
        CancellationToken cancellationToken)
    {
        var authority = ChatAuthority?.Invoke();
        if (authority is null || _runtime is null || !ReferenceEquals(GetSelectedBrowserTab(), tab))
            return "No browser tab or authority is available, so nothing was done.";
        if (tab.State.Address is null || tab.State.IsLoading)
            return "Open a page and wait for it to finish loading first; nothing was done.";

        try
        {
            WebView2BrowserTab? destination = null;
            var evidence = _runtime.CreateLinkEvidenceSource(tab, () => destination);
            var baseline = await authority.BeginOpenLinkVerificationAsync(evidence, cancellationToken);
            if (!await CreateTabAsync(null, cancellationToken: cancellationToken, agentInitiated: true,
                    onCreated: created => destination = created))
            {
                cancellationToken.ThrowIfCancellationRequested();
                return "The destination tab could not be created; nothing was opened.";
            }
            if (destination is null || !ReferenceEquals(GetSelectedBrowserTab(), destination))
                throw new InvalidOperationException("The selected browser tab changed before the link could open.");
            _agentDestination = destination;
            var destinationGeneration = destination.NavigationGeneration;
            await tab.OpenAuthorizedLinkAsync(disclosure, link, () => authority.ConsumeOpenLink(record),
                observer, captured, destination,
                () => ReferenceEquals(GetSelectedBrowserTab(), destination)
                    && destination.NavigationGeneration == destinationGeneration, cancellationToken);
            var result = await authority.VerifyOpenLinkAsync(record, evidence, baseline, cancellationToken);
            return result.Status switch
            {
                "VERIFIED" => $"Verified from the browser's own address: the approved link on {link.Target.Host} opened.",
                "FAILED" => $"Not verified (FAILED): {string.Join(' ', result.Reasons)}",
                _ => $"Could not confirm (ESCALATE): {string.Join(' ', result.Reasons)} Please check the page yourself."
            };
        }
        catch (OperationCanceledException)
        {
            return "Opening the link was cancelled or timed out; the result was not verified.";
        }
        catch (Exception exception)
        {
            return $"The link action encountered an error ({exception.GetType().Name}: {exception.Message}). "
                + "Its result was not verified; check the browser before retrying.";
        }
        finally
        {
            _agentDestination = null;
        }
    }

    private CancellationTokenSource? _chatCancellation;

    private void ChatInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        e.Handled = true;
        SendChat();
    }

    private void ChatSend_Click(object sender, RoutedEventArgs e) => SendChat();

    private void ChatCancel_Click(object sender, RoutedEventArgs e) => _chatCancellation?.Cancel();

    private async void SendChat()
    {
        var text = ChatInput.Text.Trim();
        if (text.Length == 0 || _chatCancellation is not null)
            return;
        ChatInput.Clear();
        ChatTranscript.AppendText($"You: {text}\n");
        if (ChatPlanner is null)
        {
            ChatTranscript.AppendText("Jarvis: The planner is not available.\n\n");
            return;
        }

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(150));
        _chatCancellation = cancellation;
        ChatSendButton.IsEnabled = false;
        ChatCancelButton.IsEnabled = true;
        try
        {
            if (GetSelectedBrowserTab() is not WebView2BrowserTab tab
                || tab.State.Address is null
                || tab.State.IsLoading)
            {
                ChatTranscript.AppendText("Jarvis: Open a website and wait for it to finish loading. Nothing was sent.\n\n");
                return;
            }

            ChatTranscript.AppendText("Jarvis: Capturing a bounded snapshot locally...\n");
            ChatTranscript.ScrollToEnd();
            var observer = tab.CreateLinkObserver(() => ReferenceEquals(GetSelectedBrowserTab(), tab)
                || (_agentDestination is not null && ReferenceEquals(GetSelectedBrowserTab(), _agentDestination)));
            CdpBoundObservation? captured = null;
            ObservationResult observation;
            try
            {
                captured = await observer.CaptureWithRetryAsync(cancellation.Token);
                observation = captured.Observation;
            }
            catch (CdpLinkCaptureException exception)
            {
                ChatTranscript.AppendText(
                    $"Jarvis: Link actions are unavailable for this snapshot ({exception.Message}) "
                    + "Search may still be requested.\n");
                observation = await tab.ObserveAsync(ReferenceEquals(GetSelectedBrowserTab(), tab), cancellation.Token);
            }
            var disclosure = BrowserPageDisclosure.Create(observation);
            var page = new Jarvis.Contracts.BrowserPageContextDto(
                disclosure.Title,
                disclosure.VisibleText,
                disclosure.Links.Select(link => new Jarvis.Contracts.BrowserPageLinkDto(link.Id, link.Label)).ToArray());
            ChatTranscript.AppendText(
                "Jarvis: Exact page projection proposed for local Ollama (no URL or form values):\n"
                + disclosure.FormatPreview()
                + "\n");
            ChatTranscript.ScrollToEnd();
            var confirmMessage = "Send the exact page projection shown above, with your instruction, to local Ollama?";
            var owner = Window.GetWindow(this);
            var disclosureDecision = owner is null
                ? MessageBox.Show(confirmMessage, "Confirm page disclosure", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                : MessageBox.Show(owner, confirmMessage, "Confirm page disclosure", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (disclosureDecision != MessageBoxResult.Yes)
            {
                ChatTranscript.AppendText("Jarvis: Page disclosure declined. Nothing was sent.\n\n");
                return;
            }

            cancellation.Token.ThrowIfCancellationRequested();
            ChatTranscript.AppendText("Jarvis: Disclosure approved. Planning the typed browser action...\n");
            ChatTranscript.ScrollToEnd();
            var plan = await ChatPlanner(text, page, cancellation.Token);
            if (plan.Status == "search" && plan.Query is not null)
            {
                if (ChatAuthorizer is null)
                {
                    ChatTranscript.AppendText("Jarvis: Authorization is not available, so nothing was done.\n\n");
                }
                else
                {
                    ChatTranscript.AppendText($"Jarvis: Planned search \"{plan.Query}\". Waiting for your approval...\n");
                    var authorization = await ChatAuthorizer(plan.Query, cancellation.Token);
                    if (authorization.Approved && authorization.Record is not null)
                    {
                        ChatTranscript.AppendText("Jarvis: Approved. Running the search on the page...\n");
                        ChatTranscript.AppendText(
                            $"Jarvis: {await RunApprovedSearchAsync(authorization.Record, disclosure, cancellation.Token)}\n\n");
                    }
                    else
                    {
                        ChatTranscript.AppendText($"Jarvis: Not approved: {authorization.Message}\n\n");
                    }
                }
            }
            else if (plan.Status == "open_link" && plan.LinkId is not null)
            {
                var link = disclosure.FindLink(plan.LinkId);
                if (link is null)
                {
                    ChatTranscript.AppendText("Jarvis: The proposed link is no longer in this page snapshot. Inspect the page again.\n\n");
                }
                else if (disclosure.IsAmbiguous(link))
                {
                    ChatTranscript.AppendText(
                        $"Jarvis: More than one different destination is labelled \"{link.Label}\". Please clarify which link you mean.\n\n");
                }
                else if (captured is null)
                {
                    ChatTranscript.AppendText("Jarvis: This link could not be safely bound. Inspect the page again before opening it.\n\n");
                }
                else if (ChatLinkAuthorizer is null)
                {
                    ChatTranscript.AppendText("Jarvis: Link authorization is not available, so nothing was done.\n\n");
                }
                else
                {
                    ChatTranscript.AppendText(
                        $"Jarvis: Proposed action: open [{link.Id}] \"{link.Label}\" on {link.Target.Host} in a new tab. Awaiting separate approval...\n");
                    var authorization = await ChatLinkAuthorizer(disclosure, link, tab.Id, cancellation.Token);
                    if (authorization.Approved && authorization.Record is not null)
                    {
                        ChatTranscript.AppendText("Jarvis: Approved and signed. Revalidating the link before opening...\n");
                        ChatTranscript.AppendText(
                            $"Jarvis: {await RunApprovedLinkAsync(authorization.Record, disclosure, link, observer, captured, tab, cancellation.Token)}\n\n");
                    }
                    else
                    {
                        ChatTranscript.AppendText($"Jarvis: Not approved: {authorization.Message}\n\n");
                    }
                }
            }
            else
            {
                ChatTranscript.AppendText($"Jarvis: {plan.Message}\n\n");
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            ChatTranscript.AppendText("Jarvis: Cancelled. Any action already started may still complete; no success was reported without verification.\n\n");
        }
        catch (InvalidOperationException exception)
        {
            ChatTranscript.AppendText($"Jarvis: Page disclosure blocked ({exception.Message}). Nothing was sent.\n\n");
        }
        catch (Exception exception)
        {
            ChatTranscript.AppendText($"Jarvis: Planning failed ({exception.GetType().Name}).\n\n");
        }
        finally
        {
            _chatCancellation = null;
            ChatSendButton.IsEnabled = true;
            ChatCancelButton.IsEnabled = false;
            ChatTranscript.ScrollToEnd();
        }
    }

    private void ClearSnapshot(string message)
    {
        _observationVersion++;
        _observationCancellation?.Cancel();
        _observationCancellation = null;
    }
    private async Task<bool> CreateTabAsync(
        Uri? initialAddress,
        bool onlyIfEmpty = false,
        CancellationToken cancellationToken = default,
        bool agentInitiated = false,
        Action<WebView2BrowserTab>? onCreated = null)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var acquired = false;
        try
        {
            await _tabCreationGate.WaitAsync(timeout.Token);
            acquired = true;
            if (onlyIfEmpty && _browserTabs.Count > 0)
                return true;
            var runtime = GetOrCreateRuntime();
            _agentTabAttachmentExpected = agentInitiated;
            IBrowserTab browserTab;
            try
            {
                browserTab = await runtime.CreateTabAsync(timeout.Token);
            }
            finally
            {
                _agentTabAttachmentExpected = false;
            }
            if (browserTab is not WebView2BrowserTab nativeTab)
            {
                await runtime.CloseTabAsync(browserTab.Id);
                throw new InvalidOperationException("The configured browser runtime has no WPF tab presenter.");
            }

            var (tab, closeButton) = _initializingViews[nativeTab.Control];
            _initializingViews.Remove(nativeTab.Control);
            _browserTabs.Add(tab, browserTab);
            browserTab.StateChanged += BrowserTab_StateChanged;
            closeButton.IsEnabled = true;
            if (ReferenceEquals(BrowserTabs.SelectedItem, tab))
                await runtime.ActivateTabAsync(browserTab.Id);

            ConfigureWebView(tab, nativeTab.Control);
            onCreated?.Invoke(nativeTab);
            if (initialAddress is not null)
                await browserTab.NavigateAsync(initialAddress);
            else
                ShowStatus("Ready. Websites run in the dedicated Jarvis WebView2 profile.");
            _initialTabCreated = true;
            return true;
        }
        catch (OperationCanceledException)
        {
            ShowStatus("Browser tab initialization timed out. Check that the WebView2 Runtime is installed and try again.");
            return false;
        }
        catch (Exception exception)
        {
            ShowStatus($"Browser initialization or tab creation failed: {exception.Message}");
            return false;
        }
        finally
        {
            if (acquired)
                _tabCreationGate.Release();
        }
    }

    private Action AttachBrowserView(WebView2 view)
    {
        var tab = new TabItem { Content = view };
        var title = new TextBlock { Text = "New tab", VerticalAlignment = VerticalAlignment.Center };
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(title);
        var closeButton = new Button
        {
            Content = "x",
            Padding = new Thickness(5, 0, 5, 0),
            Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Close tab",
            IsEnabled = false
        };
        closeButton.Click += async (_, _) =>
        {
            await CloseTabAsync(tab);
            if (BrowserTabs.Items.Count == 0)
                await CreateTabAsync(null);
        };
        header.Children.Add(closeButton);
        tab.Header = header;
        _initializingViews.Add(view, (tab, closeButton));
        _tabTitles.Add(tab, title);
        BrowserTabs.Items.Add(tab);
        if (_agentTabAttachmentExpected)
            _expectedAgentSelection = tab;
        try
        {
            BrowserTabs.SelectedItem = tab;
        }
        finally
        {
            _expectedAgentSelection = null;
        }

        return () =>
        {
            _initializingViews.Remove(view);
            _tabTitles.Remove(tab);
            BrowserTabs.Items.Remove(tab);
        };
    }

    private void ConfigureWebView(TabItem tab, WebView2 view)
    {
        var core = view.CoreWebView2
            ?? throw new InvalidOperationException("WebView2 did not initialize.");

        core.NavigationStarting += (_, args) =>
        {
            if (!BrowserAddressPolicy.IsWebAddress(args.Uri))
            {
                args.Cancel = true;
                ShowStatus("Blocked navigation: only HTTP and HTTPS websites are allowed.");
                return;
            }

            if (BrowserTabs.SelectedItem == tab)
            {
                AddressInput.Text = args.Uri;
                ShowStatus($"Loading {args.Uri}");
            }
        };
        core.NavigationCompleted += (_, args) =>
        {
            if (BrowserTabs.SelectedItem != tab)
                return;

            AddressInput.Text = view.Source?.AbsoluteUri ?? string.Empty;
            ShowStatus(args.IsSuccess
                ? "Page loaded."
                : $"Navigation failed: {args.WebErrorStatus}.");
            UpdateNavigationButtons(GetSelectedBrowserTab());
        };
        core.NewWindowRequested += OnNewWindowRequested;
        core.DownloadStarting += (_, args) =>
        {
            args.Cancel = true;
            if (BrowserTabs.SelectedItem == tab)
                ShowStatus("Downloads are disabled until a user-controlled download manager is implemented.");
        };
        core.PermissionRequested += (_, args) =>
        {
            args.State = CoreWebView2PermissionState.Deny;
            args.Handled = true;
            if (BrowserTabs.SelectedItem == tab)
                ShowStatus($"Site permission denied: {args.PermissionKind}.");
        };
    }

    private async void OnNewWindowRequested(
        object? sender,
        CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        if (string.Equals(args.Uri, "about:blank", StringComparison.OrdinalIgnoreCase))
        {
            await CreateTabAsync(null);
            return;
        }

        if (!BrowserAddressPolicy.TryNormalize(args.Uri, out var address, out var error))
        {
            ShowStatus($"Blocked popup: {error}");
            return;
        }

        await CreateTabAsync(address);
    }

    private async Task NavigateCurrentAsync()
    {
        if (!BrowserAddressPolicy.TryNormalize(AddressInput.Text, out var address, out var error))
        {
            ShowStatus(error);
            return;
        }

        var tab = GetSelectedBrowserTab();
        if (tab is null)
        {
            await CreateTabAsync(address);
            return;
        }

        await RunOnSelectedTabAsync(browserTab => browserTab.NavigateAsync(address!));
    }

    private IBrowserTab? GetSelectedBrowserTab() =>
        BrowserTabs.SelectedItem is TabItem selected
        && _browserTabs.TryGetValue(selected, out var tab)
            ? tab
            : null;

    private void UpdateNavigationButtons(IBrowserTab? tab)
    {
        BackButton.IsEnabled = tab?.State.CanGoBack == true;
        ForwardButton.IsEnabled = tab?.State.CanGoForward == true;
    }

    private async Task CloseTabAsync(TabItem tab)
    {
        if (_initializingViews.Values.Any(entry => ReferenceEquals(entry.Tab, tab)))
        {
            ShowStatus("This tab is still initializing. Please wait.");
            return;
        }

        if (_browserTabs.Remove(tab, out var browserTab))
        {
            browserTab.StateChanged -= BrowserTab_StateChanged;
            if (_runtime is not null)
                await _runtime.CloseTabAsync(browserTab.Id);
        }

        _tabTitles.Remove(tab);
        BrowserTabs.Items.Remove(tab);
        UpdateNavigationButtons(GetSelectedBrowserTab());
    }

    private WebView2BrowserRuntime GetOrCreateRuntime()
    {
        if (_runtime is not null)
            return _runtime;

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
            throw new InvalidOperationException("The Windows local application-data folder is unavailable.");

        var profilePath = Path.Combine(localAppData, "Jarvis", "Browser", "Profile");
        return _runtime = new WebView2BrowserRuntime(profilePath, AttachBrowserView);
    }

    private async Task RunOnSelectedTabAsync(Func<IBrowserTab, Task> operation)
    {
        var tab = GetSelectedBrowserTab();
        if (tab is null)
            return;

        try
        {
            await operation(tab);
        }
        catch (Exception exception)
        {
            ShowStatus($"Browser operation failed: {exception.Message}");
        }
    }

    private void BrowserTab_StateChanged(object? sender, BrowserTabStateChangedEventArgs args)
    {
        if (sender is not IBrowserTab tab)
            return;

        var tabItem = _browserTabs.FirstOrDefault(entry => ReferenceEquals(entry.Value, tab)).Key;
        if (tabItem is null)
            return;

        var title = args.Current.Title;
        _tabTitles[tabItem].Text = title.Length > 40 ? $"{title[..37]}..." : title;
        if (ReferenceEquals(GetSelectedBrowserTab(), tab))
        {
            if ((!args.Previous.IsLoading && args.Current.IsLoading)
                || args.Previous.Address != args.Current.Address)
                ClearSnapshot("Page navigation changed. Select Inspect page to capture again.");
            AddressInput.Text = args.Current.Address?.AbsoluteUri ?? string.Empty;
            UpdateNavigationButtons(tab);
        }
    }

    private async void OwnerWindow_Closed(object? sender, EventArgs e)
    {
        ClearSnapshot("Browser closed.");
        if (_ownerWindow is not null)
            _ownerWindow.Closed -= OwnerWindow_Closed;
        if (_runtime is not null)
        {
            await _runtime.DisposeAsync();
            _runtime = null;
        }
    }
}
