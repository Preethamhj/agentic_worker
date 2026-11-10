using System.ComponentModel;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using Google.Apis.Auth.OAuth2.Responses;
using Jarvis.Infrastructure.Google;

namespace Jarvis.UI;

/// <summary>Manages Google account status and the persisted service switches.</summary>
public sealed class GoogleAccountViewModel : INotifyPropertyChanged
{
    private readonly IGoogleOAuthService _oauthService;
    private readonly GoogleEndpointPolicy _policy;
    private readonly GoogleOAuthOptions _options;
    private readonly Action<string, bool> _setServiceEnabled;
    private GoogleAccountInfo? _account;
    private string _statusMessage = "Google account status has not been loaded.";
    private bool _isBusy;
    private bool _hasCredential;
    private bool _calendarEnabled;
    private bool _gmailEnabled;
    private bool _driveEnabled;
    private bool _meetEnabled;

    /// <summary>Creates the account view model from the controlled Google services.</summary>
    public GoogleAccountViewModel(
        IGoogleOAuthService oauthService,
        GoogleEndpointPolicy policy,
        GoogleOAuthOptions options,
        Action<string, bool> setServiceEnabled)
    {
        _oauthService = oauthService ?? throw new ArgumentNullException(nameof(oauthService));
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _setServiceEnabled = setServiceEnabled ??
            throw new ArgumentNullException(nameof(setServiceEnabled));
        _calendarEnabled = policy.CalendarEnabled;
        _gmailEnabled = policy.GmailEnabled;
        _driveEnabled = policy.DriveEnabled;
        _meetEnabled = policy.MeetEnabled;
    }

    /// <summary>Raised when a view-bound property changes.</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gets the connected Google account, if present.</summary>
    public GoogleAccountInfo? Account
    {
        get => _account;
        private set => SetProperty(ref _account, value);
    }

    /// <summary>Gets or sets the status displayed without exposing OAuth tokens.</summary>
    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>Gets whether an account operation is running.</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    /// <summary>Gets whether policy permits the Google sign-in action.</summary>
    public bool CanConnect => _policy.Enabled && !IsBusy && !HasCredential;

    /// <summary>Gets whether an account can be disconnected.</summary>
    public bool CanDisconnect => !IsBusy && (Account is not null || HasCredential);

    /// <summary>Gets whether a protected OAuth token is stored locally.</summary>
    public bool HasCredential
    {
        get => _hasCredential;
        private set => SetProperty(ref _hasCredential, value);
    }

    /// <summary>Gets whether the exact-host Google network policy is enabled.</summary>
    public bool NetworkAccessEnabled => _policy.Enabled;

    /// <summary>Gets or sets the Calendar service switch.</summary>
    public bool CalendarEnabled
    {
        get => _calendarEnabled;
        set => SetServiceEnabled(ref _calendarEnabled, value, "calendar", nameof(CalendarEnabled));
    }

    /// <summary>Gets or sets the Gmail service switch.</summary>
    public bool GmailEnabled
    {
        get => _gmailEnabled;
        set => SetServiceEnabled(ref _gmailEnabled, value, "gmail", nameof(GmailEnabled));
    }

    /// <summary>Gets or sets the Drive service switch.</summary>
    public bool DriveEnabled
    {
        get => _driveEnabled;
        set => SetServiceEnabled(ref _driveEnabled, value, "drive", nameof(DriveEnabled));
    }

    /// <summary>Gets or sets the Meet service switch.</summary>
    public bool MeetEnabled
    {
        get => _meetEnabled;
        set => SetServiceEnabled(ref _meetEnabled, value, "meet", nameof(MeetEnabled));
    }

    /// <summary>Loads local account metadata and credential presence without a network request.</summary>
    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Account = await _oauthService.GetConnectedAccountAsync(cancellationToken);
            HasCredential = await _oauthService.HasStoredCredentialAsync(
                cancellationToken);
            if (Account is null && HasCredential)
            {
                StatusMessage = "A local Google credential exists without account metadata. Reconnect or disconnect.";
            }
            else if (Account is null)
            {
                StatusMessage = "No Google account is connected.";
            }
            else if (!HasCredential)
            {
                StatusMessage = "Account metadata exists, but its local credential is missing. Reconnect.";
            }
            else
            {
                StatusMessage = "Google account connected; credentials remain in the Windows DPAPI store.";
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusMessage = "Loading Google account status was cancelled.";
        }
        catch (CryptographicException)
        {
            Account = null;
            HasCredential = false;
            StatusMessage = "The local Google credential could not be decrypted for this Windows user.";
        }
        catch (IOException)
        {
            Account = null;
            HasCredential = false;
            StatusMessage = "The local Google account data could not be read.";
        }
        catch (UnauthorizedAccessException)
        {
            Account = null;
            HasCredential = false;
            StatusMessage = "The local Google account data could not be accessed.";
        }
        catch (JsonException)
        {
            Account = null;
            HasCredential = false;
            StatusMessage = "The local Google account data is invalid.";
        }

        NotifyAvailabilityChanged();
    }

    /// <summary>Starts the OAuth authorization flow using the controlled transport.</summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (!_policy.Enabled)
        {
            StatusMessage = "Google network access is disabled by policy.";
            return;
        }

        IsBusy = true;
        NotifyAvailabilityChanged();
        try
        {
            Account = await _oauthService.ConnectAsync(cancellationToken);
            HasCredential = true;
            StatusMessage = "Google account connected.";
        }
        catch (FileNotFoundException)
        {
            StatusMessage =
                $"OAuth client JSON is missing. Place the Desktop client file at: {_options.OAuthClientFilePath}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusMessage = "Google sign-in was cancelled.";
        }
        catch (Exception exception) when (
            exception is OperationCanceledException or TokenResponseException or
                HttpRequestException or IOException or UnauthorizedAccessException or
                InvalidOperationException or InvalidDataException or
                CryptographicException or JsonException)
        {
            StatusMessage = "Google sign-in failed. Check OAuth setup and retry; sensitive details were not displayed.";
        }
        finally
        {
            IsBusy = false;
            NotifyAvailabilityChanged();
        }
    }

    /// <summary>Revokes the stored token when possible and reports local deletion separately.</summary>
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (Account is null)
            return;

        IsBusy = true;
        NotifyAvailabilityChanged();
        try
        {
            var result = await _oauthService.DisconnectAsync(cancellationToken);
            if (result.LocalCredentialsDeleted)
            {
                Account = null;
                HasCredential = false;
            }

            var outcome = result.ProviderRevocationConfirmed
                ? result.LocalCredentialsDeleted
                    ? "Google confirmed token revocation and local credentials were deleted."
                    : "Google confirmed token revocation, but local credential deletion failed."
                : result.LocalCredentialsDeleted
                    ? "Local credentials were deleted, but Google did not confirm provider revocation."
                    : "Neither provider revocation nor local credential deletion was confirmed.";
            StatusMessage = string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? outcome
                : $"{outcome} {result.ErrorMessage}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            StatusMessage = "Disconnect was cancelled before it could complete.";
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or
                InvalidOperationException or CryptographicException or
                JsonException or UnauthorizedAccessException)
        {
            StatusMessage = "Disconnect could not complete. Local credential state may require attention.";
        }
        finally
        {
            IsBusy = false;
            NotifyAvailabilityChanged();
        }
    }

    private void SetServiceEnabled(
        ref bool field,
        bool value,
        string serviceName,
        string propertyName)
    {
        if (field == value)
            return;

        try
        {
            _setServiceEnabled(serviceName, value);
            field = value;
            OnPropertyChanged(propertyName);
            StatusMessage = "Google service setting saved. Restart Jarvis to apply the updated network policy and OAuth scopes.";
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                InvalidDataException or JsonException)
        {
            OnPropertyChanged(propertyName);
            StatusMessage = "Could not save the Google service setting.";
        }
    }

    private void NotifyAvailabilityChanged()
    {
        OnPropertyChanged(nameof(CanConnect));
        OnPropertyChanged(nameof(CanDisconnect));
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
