
using System.Security.Cryptography;
using System.Text.Json;
using Google.Apis.Auth;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Http;

namespace Jarvis.Infrastructure.Google;

public sealed class GoogleOAuthService : IGoogleOAuthService
{
    private readonly GoogleOAuthOptions _options;
    private readonly IGoogleCredentialStore _credentialStore;
    private readonly GoogleApiClientFactory _httpClientFactory;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    public GoogleOAuthService(
        GoogleOAuthOptions options,
        IGoogleCredentialStore credentialStore,
        GoogleApiClientFactory httpClientFactory)
    {
        _options = options ??
            throw new ArgumentNullException(nameof(options));

        _credentialStore = credentialStore ??
            throw new ArgumentNullException(nameof(credentialStore));

        _httpClientFactory = httpClientFactory ??
            throw new ArgumentNullException(nameof(httpClientFactory));

        _options.Validate();
    }

    public async Task<GoogleAccountInfo> ConnectAsync(
        CancellationToken cancellationToken = default)
    {
        await _connectionLock.WaitAsync(cancellationToken);

        try
        {
            _httpClientFactory.EnsureEnabled();

            if (!File.Exists(_options.OAuthClientFilePath))
            {
                throw new FileNotFoundException(
                    "Google OAuth client JSON was not found.",
                    _options.OAuthClientFilePath);
            }

            var clientSecrets = GoogleClientSecrets
                .FromFile(_options.OAuthClientFilePath)
                .Secrets;

            if (string.IsNullOrWhiteSpace(clientSecrets.ClientId) ||
                string.IsNullOrWhiteSpace(clientSecrets.ClientSecret))
            {
                throw new InvalidDataException(
                    "Google OAuth client JSON is invalid.");
            }

            var initializer = new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = clientSecrets,
                HttpClientFactory = _httpClientFactory,
                DataStore = _credentialStore
            };
            var credential =
                await GoogleWebAuthorizationBroker.AuthorizeAsync(
                    initializer,
                    GetGoogleScopes(),
                    _options.UserKey,
                    cancellationToken,
                    _credentialStore);

            var idToken = credential.Token.IdToken;

            if (string.IsNullOrWhiteSpace(idToken))
            {
                throw new InvalidOperationException(
                    "Google did not return an ID token.");
            }

            var payload =
                await GoogleJsonWebSignature.ValidateAsync(
                    idToken,
                    new GoogleJsonWebSignature.ValidationSettings
                    {
                        Audience = [clientSecrets.ClientId],
                        HttpClientFactory = _httpClientFactory
                    });

            if (string.IsNullOrWhiteSpace(payload.Subject) ||
                string.IsNullOrWhiteSpace(payload.Email))
            {
                throw new InvalidOperationException(
                    "Google account identity could not be verified.");
            }

            var account = new GoogleAccountInfo(
                AccountId: payload.Subject,
                Email: payload.Email,
                DisplayName: payload.Name,
                ConnectedAtUtc: DateTimeOffset.UtcNow);

            await _credentialStore.StoreAccountAsync(
                account,
                cancellationToken);

            return account;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    public async Task<GoogleAccountInfo?> GetConnectedAccountAsync(
        CancellationToken cancellationToken = default)
    {
        return await _credentialStore.GetAccountAsync(
            cancellationToken);
    }

    public async Task<UserCredential> GetCredentialAsync(
        CancellationToken cancellationToken = default)
    {
        var account =
            await _credentialStore.GetAccountAsync(cancellationToken);

        if (account is null)
        {
            throw new InvalidOperationException(
                "No Google account is connected.");
        }

        if (!await HasStoredCredentialAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                "Google credentials are unavailable. Reconnect the account.");
        }

        _httpClientFactory.EnsureEnabled();

        if (!File.Exists(_options.OAuthClientFilePath))
        {
            throw new FileNotFoundException(
                "Google OAuth client JSON was not found.",
                _options.OAuthClientFilePath);
        }

        var clientSecrets = GoogleClientSecrets
            .FromFile(_options.OAuthClientFilePath)
            .Secrets;

        var initializer = new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = clientSecrets,
            HttpClientFactory = _httpClientFactory,
            DataStore = _credentialStore
        };
        var flow = new GoogleAuthorizationCodeFlow(initializer);
        var token = await _credentialStore.GetAsync<TokenResponse>(
            _options.UserKey);
        if (token is null ||
            (string.IsNullOrWhiteSpace(token.AccessToken) &&
             string.IsNullOrWhiteSpace(token.RefreshToken)))
        {
            throw new InvalidOperationException(
                "Google credentials are unavailable. Reconnect the account.");
        }

        return new UserCredential(flow, _options.UserKey, token);
    }

    private string[] GetGoogleScopes()
    {
        var scopes = new List<string>
        {
            "openid",
            "https://www.googleapis.com/auth/userinfo.email"
        };
        if (_httpClientFactory.CalendarEnabled)
            scopes.Add("https://www.googleapis.com/auth/calendar.readonly");
        if (_httpClientFactory.GmailEnabled)
            scopes.Add("https://www.googleapis.com/auth/gmail.readonly");
        if (_httpClientFactory.DriveEnabled)
            scopes.Add("https://www.googleapis.com/auth/drive.readonly");
        if (_httpClientFactory.MeetEnabled)
            scopes.Add("https://www.googleapis.com/auth/meetings.space.readonly");
        return [.. scopes];
    }

    public async Task<bool> HasStoredCredentialAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var token = await _credentialStore.GetAsync<TokenResponse>(
            _options.UserKey);
        return token is not null &&
            (!string.IsNullOrWhiteSpace(token.RefreshToken) ||
             !string.IsNullOrWhiteSpace(token.AccessToken));
    }

    public async Task<GoogleDisconnectResult> DisconnectAsync(
        CancellationToken cancellationToken = default)
    {
        await _connectionLock.WaitAsync(cancellationToken);

        try
        {
            var revocationConfirmed = false;
            string? revocationError = null;
            try
            {
                var token = await _credentialStore.GetAsync<TokenResponse>(
                    _options.UserKey);
                var revocationToken = token?.RefreshToken;
                if (string.IsNullOrWhiteSpace(revocationToken))
                    revocationToken = token?.AccessToken;

                if (string.IsNullOrWhiteSpace(revocationToken))
                {
                    revocationError = "No stored Google token was available to revoke.";
                }
                else if (!_httpClientFactory.IsEnabled)
                {
                    revocationError = "Google network access is disabled; provider revocation was not attempted.";
                }
                else
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var client = _httpClientFactory.CreateHttpClient(
                        new CreateHttpClientArgs
                        {
                            ApplicationName = _options.ApplicationName
                        });
                    using var content = new FormUrlEncodedContent(
                    [
                        new KeyValuePair<string, string>(
                            "token",
                            revocationToken)
                    ]);
                    using var response = await client.PostAsync(
                        "https://oauth2.googleapis.com/revoke",
                        content,
                        cancellationToken);
                    revocationConfirmed = response.IsSuccessStatusCode;
                    if (!revocationConfirmed)
                    {
                        revocationError =
                            $"Google did not confirm token revocation (HTTP {(int)response.StatusCode}).";
                    }
                }
            }
            catch (OperationCanceledException)
            {
                revocationError =
                    "Google token revocation was cancelled; local credential deletion will still be attempted.";
            }
            catch (HttpRequestException)
            {
                revocationError =
                    "Google token revocation could not be confirmed because the network request failed.";
            }
            catch (CryptographicException)
            {
                revocationError =
                    "Google token revocation could not be attempted because the stored token could not be decrypted.";
            }
            catch (UnauthorizedAccessException)
            {
                revocationError =
                    "Google token revocation could not be attempted because local credentials could not be accessed.";
            }
            catch (IOException)
            {
                revocationError =
                    "Google token revocation could not be attempted because local credentials could not be read.";
            }
            catch (JsonException)
            {
                revocationError =
                    "Google token revocation could not be attempted because stored credential data is invalid.";
            }

            var localDeleted = false;
            string? deletionError = null;
            try
            {
                await _credentialStore.ClearAsync();
                await _credentialStore.ClearAccountAsync(CancellationToken.None);
                localDeleted = true;
            }
            catch (IOException)
            {
                deletionError = "Local Google credential deletion failed.";
            }
            catch (UnauthorizedAccessException)
            {
                deletionError = "Local Google credential deletion failed.";
            }
            catch (CryptographicException)
            {
                deletionError = "Local Google credential deletion failed.";
            }

            var errors = new[] { revocationError, deletionError }
                .Where(message => !string.IsNullOrWhiteSpace(message));
            return new GoogleDisconnectResult(
                revocationConfirmed,
                localDeleted,
                string.Join(" ", errors));
        }
        finally
        {
            _connectionLock.Release();
        }
    }
}
