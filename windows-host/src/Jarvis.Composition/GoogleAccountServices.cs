using Jarvis.Infrastructure.Google;

namespace Jarvis.Composition;

/// <summary>Composes Google account management with the shared endpoint policy.</summary>
public sealed class GoogleAccountServices : IDisposable
{
    private readonly GoogleCredentialStore _credentialStore;
    private bool _disposed;

    /// <summary>Creates Google account services from the application's shared settings.</summary>
    /// <param name="settingsPath">The path to config/settings.json.</param>
    public GoogleAccountServices(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "Google credentials require Windows DPAPI.");

        SettingsPath = Path.GetFullPath(settingsPath);
        Policy = GoogleEndpointPolicy.FromSettingsFile(SettingsPath);
        Options = new GoogleOAuthOptions();
        _credentialStore = new GoogleCredentialStore(Options);
        ClientFactory = new GoogleApiClientFactory(Policy);
        OAuthService = new GoogleOAuthService(
            Options,
            _credentialStore,
            ClientFactory);
        var reader = new GoogleWorkspaceReader(OAuthService, ClientFactory);
        WorkspaceReader = reader;
        MailSearcher = reader;
        MailSummarizer = new OllamaMailSummarizer(SettingsPath);
    }

    /// <summary>Gets the Gmail search and full-message reader.</summary>
    public IGoogleMailSearcher MailSearcher { get; }

    /// <summary>Gets the loopback-only local-model mail summarizer.</summary>
    public IMailSummarizer MailSummarizer { get; }

    /// <summary>Gets the shared settings file path.</summary>
    public string SettingsPath { get; }

    /// <summary>Gets the configured local OAuth paths and account key.</summary>
    public GoogleOAuthOptions Options { get; }

    /// <summary>Gets the immutable endpoint and per-service policy loaded at startup.</summary>
    public GoogleEndpointPolicy Policy { get; }

    /// <summary>Gets the OAuth account service using the controlled Google transport.</summary>
    public IGoogleOAuthService OAuthService { get; }

    /// <summary>Gets the shared restricted factory used for all Google HTTP requests.</summary>
    public GoogleApiClientFactory ClientFactory { get; }

    /// <summary>Gets the read-only Calendar and Gmail reader.</summary>
    public IGoogleWorkspaceReader WorkspaceReader { get; }

    /// <summary>Persists one service switch; the running policy takes effect after restart.</summary>
    public void SetServiceEnabled(string serviceName, bool enabled)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        GoogleEndpointPolicy.SetServiceEnabled(
            SettingsPath,
            serviceName,
            enabled);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
            return;

        _credentialStore.Dispose();
        _disposed = true;
    }
}
