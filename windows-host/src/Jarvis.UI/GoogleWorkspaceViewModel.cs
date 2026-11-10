using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Google.Apis.Auth.OAuth2.Responses;
using Google;
using Jarvis.Infrastructure.Google;

namespace Jarvis.UI;

/// <summary>Loads Calendar events and Gmail metadata only after an explicit user action.</summary>
public sealed class GoogleWorkspaceViewModel : INotifyPropertyChanged
{
    private readonly IGoogleWorkspaceReader _reader;
    private readonly GoogleEndpointPolicy _policy;
    private readonly GoogleAccountViewModel _account;
    private bool _isLoadingCalendar;
    private bool _isLoadingGmail;
    private string _calendarStatus = "Select Refresh to load upcoming events.";
    private string _gmailStatus = "Select Refresh to load recent inbox messages.";

    /// <summary>Creates the Google Workspace display surface.</summary>
    public GoogleWorkspaceViewModel(
        IGoogleWorkspaceReader reader,
        GoogleEndpointPolicy policy,
        GoogleAccountViewModel account)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _account = account ?? throw new ArgumentNullException(nameof(account));
        _account.PropertyChanged += AccountPropertyChanged;
    }

    /// <summary>Raised when a view-bound property changes.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Upcoming events returned by the read-only Calendar API.</summary>
    public ObservableCollection<GoogleCalendarEvent> CalendarEvents { get; } = [];

    /// <summary>Metadata-only messages returned from the Gmail inbox.</summary>
    public ObservableCollection<GoogleGmailMessage> GmailMessages { get; } = [];

    /// <summary>Gets the Calendar section status.</summary>
    public string CalendarStatus
    {
        get => _calendarStatus;
        private set => SetProperty(ref _calendarStatus, value);
    }

    /// <summary>Gets the Gmail section status.</summary>
    public string GmailStatus
    {
        get => _gmailStatus;
        private set => SetProperty(ref _gmailStatus, value);
    }

    /// <summary>Gets whether a Calendar request is active.</summary>
    public bool IsLoadingCalendar
    {
        get => _isLoadingCalendar;
        private set
        {
            if (SetProperty(ref _isLoadingCalendar, value))
                OnPropertyChanged(nameof(CanLoadCalendar));
        }
    }

    /// <summary>Gets whether a Gmail request is active.</summary>
    public bool IsLoadingGmail
    {
        get => _isLoadingGmail;
        private set
        {
            if (SetProperty(ref _isLoadingGmail, value))
                OnPropertyChanged(nameof(CanLoadGmail));
        }
    }

    /// <summary>Gets whether Calendar can be loaded under current policy and account state.</summary>
    public bool CanLoadCalendar =>
        _policy.Enabled &&
        _policy.CalendarEnabled &&
        _account.Account is not null &&
        _account.HasCredential &&
        !IsLoadingCalendar;

    /// <summary>Gets whether Gmail can be loaded under current policy and account state.</summary>
    public bool CanLoadGmail =>
        _policy.Enabled &&
        _policy.GmailEnabled &&
        _account.Account is not null &&
        _account.HasCredential &&
        !IsLoadingGmail;

    /// <summary>Loads at most ten upcoming events; no data is fetched automatically.</summary>
    public async Task LoadCalendarAsync(CancellationToken cancellationToken = default)
    {
        if (!CanLoadCalendar)
        {
            CalendarStatus = "Connect a Google account and enable Calendar to load events.";
            return;
        }

        IsLoadingCalendar = true;
        CalendarStatus = "Loading upcoming Calendar events...";
        try
        {
            var events = await _reader.ListUpcomingEventsAsync(
                cancellationToken: cancellationToken);
            CalendarEvents.Clear();
            foreach (var calendarEvent in events)
                CalendarEvents.Add(calendarEvent);
            CalendarStatus = $"Loaded {events.Count} upcoming event(s).";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            CalendarStatus = "Calendar request cancelled.";
        }
        catch (GoogleApiException exception)
        {
            CalendarStatus = $"Calendar request failed (HTTP {(int?)exception.HttpStatusCode ?? 0}). Check account consent and API setup.";
        }
        catch (TokenResponseException)
        {
            CalendarStatus = "Google authorization expired or lacks Calendar permission. Reconnect the account.";
        }
        catch (HttpRequestException)
        {
            CalendarStatus = "Calendar request failed because the restricted network transport could not connect.";
        }
        catch (InvalidOperationException)
        {
            CalendarStatus = "Calendar access is disabled by policy or the Google account must be reconnected.";
        }
        catch (IOException)
        {
            CalendarStatus = "Calendar data could not be read.";
        }
        catch (CryptographicException)
        {
            CalendarStatus = "The stored Google credential could not be decrypted for this Windows user.";
        }
        finally
        {
            IsLoadingCalendar = false;
        }
    }

    /// <summary>Loads at most ten recent inbox messages without fetching bodies or attachments.</summary>
    public async Task LoadGmailAsync(CancellationToken cancellationToken = default)
    {
        if (!CanLoadGmail)
        {
            GmailStatus = "Connect a Google account and enable Gmail to load inbox messages.";
            return;
        }

        IsLoadingGmail = true;
        GmailStatus = "Loading recent Gmail inbox messages...";
        try
        {
            var messages = await _reader.ListInboxMessagesAsync(
                cancellationToken: cancellationToken);
            GmailMessages.Clear();
            foreach (var message in messages)
                GmailMessages.Add(message);
            GmailStatus = $"Loaded {messages.Count} inbox message(s). Message bodies and attachments were not fetched.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            GmailStatus = "Gmail request cancelled.";
        }
        catch (GoogleApiException exception)
        {
            GmailStatus = $"Gmail request failed (HTTP {(int?)exception.HttpStatusCode ?? 0}). Check account consent and API setup.";
        }
        catch (TokenResponseException)
        {
            GmailStatus = "Google authorization expired or lacks Gmail permission. Reconnect the account.";
        }
        catch (HttpRequestException)
        {
            GmailStatus = "Gmail request failed because the restricted network transport could not connect.";
        }
        catch (InvalidOperationException)
        {
            GmailStatus = "Gmail access is disabled by policy or the Google account must be reconnected.";
        }
        catch (IOException)
        {
            GmailStatus = "Gmail data could not be read.";
        }
        catch (CryptographicException)
        {
            GmailStatus = "The stored Google credential could not be decrypted for this Windows user.";
        }
        finally
        {
            IsLoadingGmail = false;
        }
    }

    private void AccountPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(GoogleAccountViewModel.Account) or
            nameof(GoogleAccountViewModel.HasCredential))
        {
            OnPropertyChanged(nameof(CanLoadCalendar));
            OnPropertyChanged(nameof(CanLoadGmail));
        }
    }

    private bool SetProperty<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
