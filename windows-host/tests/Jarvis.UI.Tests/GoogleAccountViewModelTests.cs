using FluentAssertions;
using Google.Apis.Auth.OAuth2;
using Jarvis.Infrastructure.Google;
using Xunit;

namespace Jarvis.UI.Tests;

public sealed class GoogleAccountViewModelTests
{
    [Fact]
    public async Task Connect_loads_and_updates_account_without_exposing_credentials()
    {
        var service = new FakeGoogleOAuthService();
        var viewModel = CreateViewModel(service);

        await viewModel.LoadAsync();
        viewModel.CanConnect.Should().BeTrue();

        await viewModel.ConnectAsync();

        viewModel.Account!.Email.Should().Be("test@example.com");
        viewModel.HasCredential.Should().BeTrue();
        viewModel.CanDisconnect.Should().BeTrue();
        viewModel.StatusMessage.Should().Contain("connected");
    }

    [Fact]
    public async Task Disconnect_reports_local_deletion_separately_from_provider_confirmation()
    {
        var service = new FakeGoogleOAuthService
        {
            ConnectedAccount = Account,
            HasCredential = true,
            DisconnectResult = new GoogleDisconnectResult(
                ProviderRevocationConfirmed: false,
                LocalCredentialsDeleted: true,
                ErrorMessage: "Google did not confirm revocation.")
        };
        var viewModel = CreateViewModel(service);
        await viewModel.LoadAsync();

        await viewModel.DisconnectAsync();

        viewModel.Account.Should().BeNull();
        viewModel.HasCredential.Should().BeFalse();
        viewModel.StatusMessage.Should().Contain("Local credentials were deleted")
            .And.Contain("did not confirm");
    }

    [Fact]
    public void Service_switch_persists_and_reports_restart_requirement()
    {
        string? savedService = null;
        bool? savedValue = null;
        var viewModel = new GoogleAccountViewModel(
            new FakeGoogleOAuthService(),
            new GoogleEndpointPolicy(enabled: true),
            new GoogleOAuthOptions(),
            (serviceName, enabled) =>
            {
                savedService = serviceName;
                savedValue = enabled;
            });

        viewModel.CalendarEnabled = false;

        savedService.Should().Be("calendar");
        savedValue.Should().BeFalse();
        viewModel.CalendarEnabled.Should().BeFalse();
        viewModel.StatusMessage.Should().Contain("Restart Jarvis");
    }

    [Fact]
    public void Service_switch_save_failure_does_not_change_the_displayed_setting()
    {
        var viewModel = new GoogleAccountViewModel(
            new FakeGoogleOAuthService(),
            new GoogleEndpointPolicy(enabled: true),
            new GoogleOAuthOptions(),
            (_, _) => throw new IOException("settings file is unavailable"));

        viewModel.CalendarEnabled = false;

        viewModel.CalendarEnabled.Should().BeTrue();
        viewModel.StatusMessage.Should().Contain("Could not save");
    }

    [Fact]
    public async Task Workspace_data_is_loaded_only_after_explicit_refresh()
    {
        var accountService = new FakeGoogleOAuthService
        {
            ConnectedAccount = Account,
            HasCredential = true
        };
        var account = CreateViewModel(accountService);
        await account.LoadAsync();
        var reader = new FakeGoogleWorkspaceReader();
        var workspace = new GoogleWorkspaceViewModel(
            reader,
            new GoogleEndpointPolicy(enabled: true),
            account);

        workspace.CanLoadCalendar.Should().BeTrue();
        workspace.CanLoadGmail.Should().BeTrue();
        reader.CalendarReadCount.Should().Be(0);
        reader.GmailReadCount.Should().Be(0);

        await workspace.LoadCalendarAsync();
        await workspace.LoadGmailAsync();

        reader.CalendarReadCount.Should().Be(1);
        reader.GmailReadCount.Should().Be(1);
        workspace.CalendarEvents.Should().ContainSingle()
            .Which.Summary.Should().Be("Planning");
        workspace.GmailMessages.Should().ContainSingle()
            .Which.Subject.Should().Be("Hello");
    }

    private static GoogleAccountInfo Account =>
        new("subject", "test@example.com", "Test User", DateTimeOffset.UtcNow);

    private static GoogleAccountViewModel CreateViewModel(
        FakeGoogleOAuthService service) =>
        new(
            service,
            new GoogleEndpointPolicy(enabled: true),
            new GoogleOAuthOptions(),
            (_, _) => { });

    private sealed class FakeGoogleOAuthService : IGoogleOAuthService
    {
        public GoogleAccountInfo? ConnectedAccount { get; set; }
        public bool HasCredential { get; set; }
        public GoogleDisconnectResult DisconnectResult { get; set; } =
            new(false, true, null);

        public Task<GoogleAccountInfo> ConnectAsync(
            CancellationToken cancellationToken = default)
        {
            ConnectedAccount = Account;
            HasCredential = true;
            return Task.FromResult(Account);
        }

        public Task<GoogleAccountInfo?> GetConnectedAccountAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ConnectedAccount);

        public Task<UserCredential> GetCredentialAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> HasStoredCredentialAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(HasCredential);

        public Task<GoogleDisconnectResult> DisconnectAsync(
            CancellationToken cancellationToken = default)
        {
            ConnectedAccount = null;
            HasCredential = false;
            return Task.FromResult(DisconnectResult);
        }

    }

    private sealed class FakeGoogleWorkspaceReader : IGoogleWorkspaceReader
    {
        public int CalendarReadCount { get; private set; }
        public int GmailReadCount { get; private set; }

        public Task<IReadOnlyList<GoogleCalendarEvent>> ListUpcomingEventsAsync(
            int maxResults = 10,
            CancellationToken cancellationToken = default)
        {
            CalendarReadCount++;
            return Task.FromResult<IReadOnlyList<GoogleCalendarEvent>>(
                [new GoogleCalendarEvent("Planning", "2026-11-10T09:00:00", null, null)]);
        }

        public Task<IReadOnlyList<GoogleGmailMessage>> ListInboxMessagesAsync(
            int maxResults = 10,
            CancellationToken cancellationToken = default)
        {
            GmailReadCount++;
            return Task.FromResult<IReadOnlyList<GoogleGmailMessage>>(
                [new GoogleGmailMessage("message-id", "Hello", "sender", "Today", "Preview")]);
        }
    }
}
