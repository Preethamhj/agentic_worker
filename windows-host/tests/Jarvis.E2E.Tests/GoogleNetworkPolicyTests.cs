using System.Net;
using System.Net.Http;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3;
using Google.Apis.Http;
using Jarvis.Infrastructure.Google;
using Xunit;

namespace Jarvis.E2E.Tests;

public sealed class GoogleNetworkPolicyTests
{
    private static readonly GoogleEndpointPolicy EnabledPolicy = new(true);

    [Theory]
    [InlineData("https://accounts.google.com/o/oauth2/v2/auth?client_id=test")]
    [InlineData("https://accounts.google.com/o/oauth2/auth")]
    [InlineData("https://accounts.google.com/o/oauth2/token")]
    [InlineData("https://oauth2.googleapis.com/token")]
    [InlineData("https://www.googleapis.com/oauth2/v3/certs")]
    [InlineData("https://www.googleapis.com/calendar/v3/calendars/primary/events")]
    [InlineData("https://gmail.googleapis.com/gmail/v1/users/me/messages")]
    [InlineData("https://www.googleapis.com/drive/v3/files")]
    [InlineData("https://meet.googleapis.com/v2/spaces")]
    public void Allows_only_approved_google_endpoints(string address)
    {
        Assert.True(EnabledPolicy.IsAllowedUrl(address));
    }

    [Theory]
    [InlineData("https://google.com/")]
    [InlineData("https://notgoogleapis.com/calendar/v3/events")]
    [InlineData("https://www.googleapis.com/other/v1/")]
    [InlineData("https://www.googleapis.com/calendar/v3evil/events")]
    [InlineData("https://gmail.googleapis.com/drive/v3/files")]
    [InlineData("https://accounts.google.com/")]
    [InlineData("https://accounts.google.com/o/oauth2/approval")]
    public void Rejects_unapproved_hosts_and_paths(string address)
    {
        Assert.False(EnabledPolicy.IsAllowedUrl(address));
    }

    [Theory]
    [InlineData("http://www.googleapis.com/calendar/v3/events")]
    [InlineData("https://www.googleapis.com:444/calendar/v3/events")]
    [InlineData("https://www.googleapis.com.evil.example/calendar/v3/events")]
    [InlineData("https://accounts.google.com.attacker.example/o/oauth2/v2/auth")]
    [InlineData("https://user@www.googleapis.com/calendar/v3/events")]
    [InlineData("https://www.googleapis.com./calendar/v3/events")]
    public void Rejects_non_https_nonstandard_ports_and_lookalikes(string address)
    {
        Assert.False(EnabledPolicy.IsAllowedUrl(address));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a URL")]
    [InlineData("/calendar/v3/events")]
    [InlineData("https://[")]
    public void Rejects_invalid_or_relative_urls(string address)
    {
        Assert.False(EnabledPolicy.IsAllowedUrl(address));
    }

    [Fact]
    public void Defaults_to_deny()
    {
        Assert.False(new GoogleEndpointPolicy().IsAllowedUrl(
            "https://www.googleapis.com/calendar/v3/events"));
    }

    [Fact]
    public void Client_factory_rejects_disabled_policy()
    {
        var factory = new GoogleApiClientFactory(
            new GoogleEndpointPolicy());

        Assert.Throws<InvalidOperationException>(
            () => factory.CreateHttpClient(new CreateHttpClientArgs()));
    }

    [Fact]
    public void Reads_optional_network_policy_from_shared_settings()
    {
        var settingsPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(
                settingsPath,
                """{"integrations":{"google":{"enabled":true}}}""");
            Assert.False(
                GoogleEndpointPolicy.FromSettingsFile(settingsPath).Enabled);

            File.WriteAllText(
                settingsPath,
                """{"integrations":{"google":{"enabled":true,"network_policy":{"enabled":true,"request_timeout_seconds":45}}}}""");
            var policy = GoogleEndpointPolicy.FromSettingsFile(settingsPath);
            Assert.True(policy.Enabled);
            Assert.Equal(TimeSpan.FromSeconds(45), policy.RequestTimeout);

            File.WriteAllText(
                settingsPath,
                """{"integrations":{"google":{"enabled":true,"network_policy":{"enabled":"yes"}}}}""");
            Assert.Throws<InvalidDataException>(
                () => GoogleEndpointPolicy.FromSettingsFile(settingsPath));
        }
        finally
        {
            File.Delete(settingsPath);
        }
    }

    [Fact]
    public void Revocation_is_allowed_only_as_a_token_post()
    {
        Assert.True(EnabledPolicy.IsAllowedRequest(
            new Uri("https://oauth2.googleapis.com/revoke"),
            HttpMethod.Post));
        Assert.False(EnabledPolicy.IsAllowedRequest(
            new Uri("https://oauth2.googleapis.com/revoke"),
            HttpMethod.Get));
        Assert.False(EnabledPolicy.IsAllowedRequest(
            new Uri("https://oauth2.googleapis.com/revoke/extra"),
            HttpMethod.Post));
    }

    [Fact]
    public void Disabled_service_paths_are_denied_by_the_policy()
    {
        var policy = new GoogleEndpointPolicy(
            enabled: true,
            calendarEnabled: false,
            gmailEnabled: false,
            driveEnabled: false,
            meetEnabled: false);

        Assert.False(policy.IsAllowedUrl(
            "https://www.googleapis.com/calendar/v3/events"));
        Assert.False(policy.IsAllowedUrl(
            "https://gmail.googleapis.com/gmail/v1/users/me/messages"));
        Assert.False(policy.IsAllowedUrl(
            "https://www.googleapis.com/drive/v3/files"));
        Assert.False(policy.IsAllowedUrl(
            "https://meet.googleapis.com/v2/spaces"));
        Assert.True(policy.IsAllowedUrl(
            "https://www.googleapis.com/oauth2/v3/certs"));
    }

    [Fact]
    public void Persists_google_service_switches_in_the_shared_settings_file()
    {
        var settingsPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(
                settingsPath,
                """{"integrations":{"google":{"enabled":true,"network_policy":{"enabled":true},"calendar":{"enabled":true}}}}""");

            GoogleEndpointPolicy.SetServiceEnabled(
                settingsPath,
                "calendar",
                false);

            var policy = GoogleEndpointPolicy.FromSettingsFile(settingsPath);
            Assert.False(policy.CalendarEnabled);
            Assert.False(policy.IsAllowedUrl(
                "https://www.googleapis.com/calendar/v3/events"));
        }
        finally
        {
            File.Delete(settingsPath);
        }
    }

    [Fact]
    public async Task Handler_blocks_disallowed_request_before_transport()
    {
        var transport = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK));
        using var client = new HttpClient(
            new GoogleHttpMessageHandler(EnabledPolicy, transport));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync("https://example.com/"));
        Assert.Equal(0, transport.RequestCount);
    }

    [Theory]
    [InlineData("POST", "https://www.googleapis.com/calendar/v3/events")]
    [InlineData("DELETE", "https://gmail.googleapis.com/gmail/v1/users/me/messages/id")]
    [InlineData("GET", "https://oauth2.googleapis.com/token")]
    [InlineData("POST", "https://www.googleapis.com/oauth2/v3/certs")]
    public async Task Handler_restricts_methods_to_read_only_api_and_oauth_verbs(
        string method,
        string address)
    {
        var transport = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK));
        using var client = new HttpClient(
            new GoogleHttpMessageHandler(EnabledPolicy, transport));
        using var request = new HttpRequestMessage(
            new HttpMethod(method),
            address);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.SendAsync(request));

        Assert.Equal(0, transport.RequestCount);
    }

    [Theory]
    [InlineData("POST", "https://oauth2.googleapis.com/token")]
    [InlineData("POST", "https://accounts.google.com/o/oauth2/token")]
    [InlineData("GET", "https://www.googleapis.com/oauth2/v3/certs")]
    public async Task Handler_allows_required_oauth_methods(
        string method,
        string address)
    {
        var transport = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK));
        using var client = new HttpClient(
            new GoogleHttpMessageHandler(EnabledPolicy, transport));
        using var request = new HttpRequestMessage(
            new HttpMethod(method),
            address);

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, transport.RequestCount);
    }

    [Fact]
    public async Task Handler_rejects_redirect_outside_allowlist()
    {
        var transport = new RecordingHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri("https://example.com/collect");
            return response;
        });
        using var client = new HttpClient(
            new GoogleHttpMessageHandler(EnabledPolicy, transport));

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync(
                "https://www.googleapis.com/calendar/v3/events"));

        Assert.Equal(1, transport.RequestCount);
    }

    [Fact]
    public async Task Handler_does_not_follow_even_an_approved_redirect()
    {
        var transport = new RecordingHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new Uri(
                "https://www.googleapis.com/calendar/v3/calendars/primary");
            return response;
        });
        using var client = new HttpClient(
            new GoogleHttpMessageHandler(EnabledPolicy, transport));

        using var response = await client.GetAsync(
            "https://www.googleapis.com/calendar/v3/events");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal(1, transport.RequestCount);
    }

    [Fact]
    public async Task Handler_propagates_cancellation()
    {
        var transport = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK));
        using var client = new HttpClient(
            new GoogleHttpMessageHandler(EnabledPolicy, transport));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetAsync(
                "https://www.googleapis.com/calendar/v3/events",
                cancellation.Token));
    }

    [Theory]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.0.0.1", false)]
    [InlineData("169.254.1.1", false)]
    [InlineData("192.168.1.1", false)]
    [InlineData("::1", false)]
    [InlineData("fc00::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("8.8.8.8", true)]
    [InlineData("2001:4860:4860::8888", true)]
    public void DNS_addresses_must_be_public_unicast(
        string address,
        bool expected)
    {
        Assert.Equal(
            expected,
            GoogleHttpMessageHandler.IsPublicInternetAddress(
                IPAddress.Parse(address)));
    }

    [Fact]
    public async Task Google_api_service_uses_factory_transport()
    {
        var transport = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"kind":"calendar#calendarList","items":[]}""")
            });
        var factory = new GoogleApiClientFactory(
            EnabledPolicy,
            () => transport);
        var service = factory.CreateService(
            initializer => new CalendarService(initializer),
            "Jarvis test",
            new NoOpHttpClientInitializer());

        var calendars = await service.CalendarList
            .List()
            .ExecuteAsync();

        Assert.Empty(calendars.Items);
        Assert.Equal(1, transport.RequestCount);
    }

    [Fact]
    public async Task Google_oauth_refresh_uses_factory_transport()
    {
        var clientSecretsPath = Path.GetTempFileName();
        try
        {
            File.WriteAllText(
                clientSecretsPath,
                """{"installed":{"client_id":"test.apps.googleusercontent.com","client_secret":"test-secret","redirect_uris":["http://localhost"]}}""");

            var transport = new RecordingHandler(_ =>
                new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"access_token":"fresh-token","refresh_token":"refresh-token","expires_in":3600,"token_type":"Bearer"}""")
                });
            var factory = new GoogleApiClientFactory(
                EnabledPolicy,
                () => transport);
            var credentialStore = new MemoryGoogleCredentialStore();
            var oauth = new GoogleOAuthService(
                new GoogleOAuthOptions
                {
                    OAuthClientFilePath = clientSecretsPath
                },
                credentialStore,
                factory);

            var credential = await oauth.GetCredentialAsync();
            var accessToken = await credential.GetAccessTokenForRequestAsync();

            Assert.Equal("fresh-token", accessToken);
            Assert.Equal(HttpMethod.Post, transport.LastMethod);
            Assert.True(EnabledPolicy.IsAllowed(transport.LastRequestUri));
            Assert.Equal(1, transport.RequestCount);
        }
        finally
        {
            File.Delete(clientSecretsPath);
        }
    }

    [Fact]
    public async Task Disconnect_revokes_the_token_through_the_factory_and_clears_local_state()
    {
        var transport = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK));
        var store = new MemoryGoogleCredentialStore();
        var oauth = new GoogleOAuthService(
            new GoogleOAuthOptions(),
            store,
            new GoogleApiClientFactory(EnabledPolicy, () => transport));

        var result = await oauth.DisconnectAsync();

        Assert.True(result.ProviderRevocationConfirmed);
        Assert.True(result.LocalCredentialsDeleted);
        Assert.Equal(HttpMethod.Post, transport.LastMethod);
        Assert.Equal(
            new Uri("https://oauth2.googleapis.com/revoke"),
            transport.LastRequestUri);
        Assert.True(store.CredentialsCleared);
        Assert.True(store.AccountCleared);
    }

    [Fact]
    public async Task Disconnect_does_not_report_provider_revocation_on_http_failure()
    {
        var transport = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.BadRequest));
        var store = new MemoryGoogleCredentialStore();
        var oauth = new GoogleOAuthService(
            new GoogleOAuthOptions(),
            store,
            new GoogleApiClientFactory(EnabledPolicy, () => transport));

        var result = await oauth.DisconnectAsync();

        Assert.False(result.ProviderRevocationConfirmed);
        Assert.True(result.LocalCredentialsDeleted);
        Assert.DoesNotContain("refresh-token", result.ErrorMessage);
        Assert.True(store.CredentialsCleared);
        Assert.True(store.AccountCleared);
    }

    [Fact]
    public async Task Calendar_reader_uses_the_restricted_transport_and_maps_events()
    {
        var transport = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"items":[{"summary":"Planning","start":{"dateTime":"2026-11-10T09:00:00-05:00"},"location":"Room 2"}]}""")
            });
        var factory = new GoogleApiClientFactory(EnabledPolicy, () => transport);
        var reader = new GoogleWorkspaceReader(
            CreateFakeOAuthService(factory),
            factory);

        var events = await reader.ListUpcomingEventsAsync();

        Assert.Collection(
            events,
            item =>
            {
                Assert.Equal("Planning", item.Summary);
                Assert.Equal("2026-11-10T09:00:00-05:00", item.Start);
                Assert.Equal("Room 2", item.Location);
            });
        Assert.Equal(1, transport.RequestCount);
        Assert.Equal(
            "www.googleapis.com",
            transport.LastRequestUri!.Host);
        Assert.StartsWith(
            "/calendar/v3/calendars/primary/events",
            transport.LastRequestUri.AbsolutePath);
        Assert.Equal(HttpMethod.Get, transport.LastMethod);
    }

    [Fact]
    public async Task Gmail_reader_fetches_only_inbox_metadata_through_the_restricted_transport()
    {
        var transport = new RecordingHandler(request =>
        {
            var responseBody = request.RequestUri!.AbsolutePath.EndsWith(
                "/messages",
                StringComparison.Ordinal)
                ? """{"messages":[{"id":"message-1"}]}"""
                : """{"id":"message-1","snippet":"Preview text","payload":{"headers":[{"name":"From","value":"sender@example.com"},{"name":"Subject","value":"Hello"},{"name":"Date","value":"Mon, 09 Nov 2026 10:00:00 +0000"}]}}""";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody)
            };
        });
        var factory = new GoogleApiClientFactory(EnabledPolicy, () => transport);
        var reader = new GoogleWorkspaceReader(
            CreateFakeOAuthService(factory),
            factory);

        var messages = await reader.ListInboxMessagesAsync();

        Assert.Collection(
            messages,
            item =>
            {
                Assert.Equal("message-1", item.Id);
                Assert.Equal("Hello", item.Subject);
                Assert.Equal("sender@example.com", item.From);
                Assert.Equal("Preview text", item.Snippet);
            });
        Assert.Equal(2, transport.RequestCount);
        Assert.Equal(
            "gmail.googleapis.com",
            transport.LastRequestUri!.Host);
        Assert.Contains(
            "format=metadata",
            transport.LastRequestUri.Query,
            StringComparison.OrdinalIgnoreCase);
        Assert.All(transport.RequestMethods, method => Assert.Equal(HttpMethod.Get, method));
    }

    [Fact]
    public async Task Workspace_reader_denies_a_disabled_service_before_transport()
    {
        var transport = new RecordingHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK));
        var policy = new GoogleEndpointPolicy(
            enabled: true,
            calendarEnabled: false,
            gmailEnabled: false);
        var factory = new GoogleApiClientFactory(policy, () => transport);
        var reader = new GoogleWorkspaceReader(
            CreateFakeOAuthService(factory),
            factory);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => reader.ListUpcomingEventsAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => reader.ListInboxMessagesAsync());
        Assert.Equal(0, transport.RequestCount);
    }

    private static IGoogleOAuthService CreateFakeOAuthService(
        GoogleApiClientFactory factory)
    {
        var flow = new GoogleAuthorizationCodeFlow(
            new GoogleAuthorizationCodeFlow.Initializer
            {
                ClientSecrets = new Google.Apis.Auth.OAuth2.ClientSecrets
                {
                    ClientId = "test-client",
                    ClientSecret = "test-secret"
                },
                HttpClientFactory = factory
            });
        var credential = new UserCredential(
            flow,
            "test-user",
            new TokenResponse
            {
                AccessToken = "test-access-token",
                ExpiresInSeconds = 3600,
                IssuedUtc = DateTime.UtcNow
            });
        return new FakeGoogleOAuthService(credential);
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public HttpMethod? LastMethod { get; private set; }
        public Uri? LastRequestUri { get; private set; }
        public List<HttpMethod> RequestMethods { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestCount++;
            LastMethod = request.Method;
            LastRequestUri = request.RequestUri;
            RequestMethods.Add(request.Method);
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class FakeGoogleOAuthService(UserCredential credential)
        : IGoogleOAuthService
    {
        public Task<GoogleAccountInfo> ConnectAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<GoogleAccountInfo?> GetConnectedAccountAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<GoogleAccountInfo?>(null);

        public Task<UserCredential> GetCredentialAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(credential);

        public Task<bool> HasStoredCredentialAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<GoogleDisconnectResult> DisconnectAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class MemoryGoogleCredentialStore : IGoogleCredentialStore
    {
        public bool CredentialsCleared { get; private set; }
        public bool AccountCleared { get; private set; }

        public Task StoreAsync<T>(string key, T value)
        {
            return Task.CompletedTask;
        }

        public Task<T> GetAsync<T>(string key)
        {
            if (typeof(T) == typeof(TokenResponse))
            {
                var expired = new TokenResponse
                {
                    AccessToken = "expired-token",
                    RefreshToken = "refresh-token",
                    ExpiresInSeconds = 0,
                    IssuedUtc = DateTime.UtcNow.AddHours(-1)
                };
                return Task.FromResult((T)(object)expired);
            }

            return Task.FromResult(default(T)!);
        }

        public Task DeleteAsync<T>(string key)
        {
            return Task.CompletedTask;
        }

        public Task ClearAsync()
        {
            CredentialsCleared = true;
            return Task.CompletedTask;
        }

        public Task StoreAccountAsync(
            GoogleAccountInfo account,
            CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<GoogleAccountInfo?> GetAccountAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<GoogleAccountInfo?>(
                new GoogleAccountInfo(
                    "subject",
                    "test@example.com",
                    "Test",
                    DateTimeOffset.UtcNow));
        }

        public Task ClearAccountAsync(
            CancellationToken cancellationToken = default)
        {
            AccountCleared = true;
            return Task.CompletedTask;
        }
    }

    private sealed class NoOpHttpClientInitializer
        : IConfigurableHttpClientInitializer
    {
        public void Initialize(ConfigurableHttpClient httpClient)
        {
        }
    }
}
