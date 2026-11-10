using Google.Apis.Http;
using Google.Apis.Services;

namespace Jarvis.Infrastructure.Google;

/// <summary>Creates Google OAuth and API clients using the restricted HTTP transport.</summary>
public sealed class GoogleApiClientFactory : global::Google.Apis.Http.IHttpClientFactory
{
    private readonly GoogleEndpointPolicy _policy;
    private readonly Func<bool, HttpMessageHandler> _transportFactory;

    /// <summary>Creates Google clients with the production policy-enforcing transport.</summary>
    /// <param name="policy">The endpoint and timeout policy.</param>
    public GoogleApiClientFactory(GoogleEndpointPolicy policy)
        : this(policy, useCompression =>
            new GoogleHttpMessageHandler(policy, useCompression))
    {
    }

    internal GoogleApiClientFactory(
        GoogleEndpointPolicy policy,
        Func<HttpMessageHandler> transportFactory)
        : this(policy, _ => transportFactory())
    {
    }

    private GoogleApiClientFactory(
        GoogleEndpointPolicy policy,
        Func<bool, HttpMessageHandler> transportFactory)
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        _transportFactory = transportFactory ??
            throw new ArgumentNullException(nameof(transportFactory));
    }

    /// <summary>Throws if Google network access is disabled.</summary>
    public void EnsureEnabled()
    {
        _policy.EnsureEnabled();
    }

    /// <summary>Gets whether external Google requests are currently enabled.</summary>
    public bool IsEnabled => _policy.Enabled;

    /// <summary>Gets whether the Calendar integration is enabled.</summary>
    public bool CalendarEnabled => _policy.CalendarEnabled;

    /// <summary>Gets whether the Gmail integration is enabled.</summary>
    public bool GmailEnabled => _policy.GmailEnabled;

    /// <summary>Gets whether the Drive integration is enabled.</summary>
    public bool DriveEnabled => _policy.DriveEnabled;

    /// <summary>Gets whether the Meet integration is enabled.</summary>
    public bool MeetEnabled => _policy.MeetEnabled;

    /// <summary>Throws if the Calendar integration is disabled.</summary>
    public void EnsureCalendarEnabled()
    {
        _policy.EnsureEnabled();
        _policy.EnsureCalendarEnabled();
    }

    /// <summary>Throws if the Gmail integration is disabled.</summary>
    public void EnsureGmailEnabled()
    {
        _policy.EnsureEnabled();
        _policy.EnsureGmailEnabled();
    }

    /// <summary>Creates a Google API service configured to use this restricted transport.</summary>
    /// <typeparam name="TService">The generated Google API service type.</typeparam>
    /// <param name="serviceFactory">The service constructor.</param>
    /// <param name="applicationName">The application name sent to Google.</param>
    /// <param name="httpClientInitializer">The credential initializer for API requests.</param>
    /// <returns>A configured Google API service.</returns>
    public TService CreateService<TService>(
        Func<BaseClientService.Initializer, TService> serviceFactory,
        string applicationName,
        IConfigurableHttpClientInitializer httpClientInitializer)
    {
        ArgumentNullException.ThrowIfNull(serviceFactory);
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        ArgumentNullException.ThrowIfNull(httpClientInitializer);
        _policy.EnsureEnabled();

        return serviceFactory(new BaseClientService.Initializer
        {
            ApplicationName = applicationName,
            HttpClientFactory = this,
            HttpClientInitializer = httpClientInitializer
        });
    }

    /// <inheritdoc />
    public ConfigurableHttpClient CreateHttpClient(CreateHttpClientArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        _policy.EnsureEnabled();

        var configurableHandler = new ConfigurableMessageHandler(
            _transportFactory(args.GZipEnabled))
        {
            ApplicationName = args.ApplicationName,
            GoogleApiClientHeader = args.GoogleApiClientHeader,
            UniverseDomain = args.UniverseDomain
        };
        var client = new ConfigurableHttpClient(configurableHandler);

        try
        {
            foreach (var initializer in args.Initializers)
                initializer.Initialize(client);

            client.Timeout = _policy.RequestTimeout;
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }
}
