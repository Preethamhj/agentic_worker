using System.Net;
using System.Net.Sockets;

namespace Jarvis.Infrastructure.Google;

/// <summary>Enforces approved Google HTTPS destinations on every HTTP request.</summary>
public sealed class GoogleHttpMessageHandler : DelegatingHandler
{
    private readonly GoogleEndpointPolicy _policy;

    /// <summary>Creates a handler with direct, policy-checked DNS and TLS connections.</summary>
    /// <param name="policy">The Google endpoint policy to enforce.</param>
    public GoogleHttpMessageHandler(GoogleEndpointPolicy policy)
        : this(policy, automaticDecompression: false)
    {
    }

    internal GoogleHttpMessageHandler(
        GoogleEndpointPolicy policy,
        bool automaticDecompression)
        : this(policy, CreateSocketsHandler(policy, automaticDecompression))
    {
    }

    internal GoogleHttpMessageHandler(
        GoogleEndpointPolicy policy,
        HttpMessageHandler innerHandler)
        : base(innerHandler ?? throw new ArgumentNullException(nameof(innerHandler)))
    {
        _policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        _policy.EnsureEnabled();

        var requestUri = request.RequestUri;
        if (!_policy.IsAllowedRequest(requestUri, request.Method) ||
            !HasMatchingHostHeader(request, requestUri!))
        {
            throw new HttpRequestException(
                "Request blocked by the Google endpoint policy.");
        }

        var response = await base.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);

        var statusCode = (int)response.StatusCode;
        if (statusCode is >= 300 and < 400 &&
            response.StatusCode != HttpStatusCode.NotModified)
        {
            if (response.Headers.Location is not { } location)
            {
                response.Dispose();
                throw new HttpRequestException(
                    "Google returned a redirect without a destination.");
            }

            Uri redirectUri;
            try
            {
                redirectUri = location.IsAbsoluteUri
                    ? location
                    : new Uri(requestUri!, location);
            }
            catch (UriFormatException)
            {
                response.Dispose();
                throw new HttpRequestException(
                    "Google returned an invalid redirect destination.");
            }

            if (!_policy.IsAllowed(redirectUri))
            {
                response.Dispose();
                throw new HttpRequestException(
                    "Google returned a redirect blocked by the endpoint policy.");
            }
        }

        return response;
    }

    internal static bool IsPublicInternetAddress(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            var value = ((uint)bytes[0] << 24) |
                ((uint)bytes[1] << 16) |
                ((uint)bytes[2] << 8) |
                bytes[3];

            return !IsInRange(value, 0x00000000, 0xff000000) &&
                !IsInRange(value, 0x0a000000, 0xff000000) &&
                !IsInRange(value, 0x64400000, 0xffc00000) &&
                !IsInRange(value, 0x7f000000, 0xff000000) &&
                !IsInRange(value, 0xa9fe0000, 0xffff0000) &&
                !IsInRange(value, 0xac100000, 0xfff00000) &&
                !IsInRange(value, 0xc0000000, 0xffffff00) &&
                !IsInRange(value, 0xc0000200, 0xffffff00) &&
                !IsInRange(value, 0xc0586300, 0xffffff00) &&
                !IsInRange(value, 0xc0a80000, 0xffff0000) &&
                !IsInRange(value, 0xc6120000, 0xfffe0000) &&
                !IsInRange(value, 0xc6336400, 0xffffff00) &&
                !IsInRange(value, 0xcb007100, 0xffffff00) &&
                !IsInRange(value, 0xe0000000, 0xf0000000) &&
                !IsInRange(value, 0xf0000000, 0xf0000000);
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6 ||
            address.Equals(IPAddress.IPv6Any) ||
            address.Equals(IPAddress.IPv6Loopback) ||
            address.IsIPv6LinkLocal ||
            address.IsIPv6SiteLocal ||
            address.IsIPv6Multicast)
        {
            return false;
        }

        var ipv6 = address.GetAddressBytes();
        var isGlobalUnicast = (ipv6[0] & 0xe0) == 0x20;
        var isDocumentationRange =
            ipv6[0] == 0x20 &&
            ipv6[1] == 0x01 &&
            ipv6[2] == 0x0d &&
            ipv6[3] == 0xb8;
        var isUniqueLocal = (ipv6[0] & 0xfe) == 0xfc;
        return isGlobalUnicast && !isDocumentationRange && !isUniqueLocal;
    }

    private static SocketsHttpHandler CreateSocketsHandler(
        GoogleEndpointPolicy policy,
        bool automaticDecompression)
    {
        ArgumentNullException.ThrowIfNull(policy);

        return new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            ConnectTimeout = policy.RequestTimeout,
            AutomaticDecompression = automaticDecompression
                ? DecompressionMethods.GZip | DecompressionMethods.Deflate
                : DecompressionMethods.None,
            ConnectCallback = (context, cancellationToken) =>
                ConnectToApprovedHostAsync(
                    policy,
                    context.DnsEndPoint,
                    cancellationToken)
        };
    }

    private static async ValueTask<Stream> ConnectToApprovedHostAsync(
        GoogleEndpointPolicy policy,
        DnsEndPoint endpoint,
        CancellationToken cancellationToken)
    {
        if (endpoint.Port != 443 ||
            !policy.IsApprovedHost(endpoint.Host))
        {
            throw new HttpRequestException(
                "DNS connection blocked by the Google endpoint policy.");
        }

        var addresses = await Dns.GetHostAddressesAsync(
            endpoint.Host,
            cancellationToken).ConfigureAwait(false);
        var publicAddresses = addresses
            .Where(IsPublicInternetAddress)
            .ToArray();
        if (publicAddresses.Length == 0)
        {
            throw new HttpRequestException(
                "DNS returned no public address for an approved Google endpoint.");
        }

        SocketException? lastSocketError = null;
        foreach (var address in publicAddresses)
        {
            var socket = new Socket(
                address.AddressFamily,
                SocketType.Stream,
                ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(
                    new IPEndPoint(address, endpoint.Port),
                    cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException exception)
            {
                socket.Dispose();
                lastSocketError = exception;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        throw new HttpRequestException(
            "Could not connect to an approved Google endpoint.",
            lastSocketError);
    }

    private static bool HasMatchingHostHeader(
        HttpRequestMessage request,
        Uri requestUri)
    {
        if (request.Headers.Host is not { } hostHeader)
            return true;

        var expected = requestUri.IsDefaultPort
            ? requestUri.IdnHost
            : requestUri.Authority;
        return string.Equals(
            hostHeader,
            expected,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsInRange(uint address, uint network, uint mask)
    {
        return (address & mask) == network;
    }
}
