using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jarvis.Infrastructure.Google;

/// <summary>Defines the default-deny Google HTTPS destinations available to the Windows host.</summary>
public sealed class GoogleEndpointPolicy
{
    private static readonly IReadOnlyDictionary<string, string[]> ApprovedPaths =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["accounts.google.com"] =
            [
                "/o/oauth2/auth",
                "/o/oauth2/v2/auth",
                "/o/oauth2/token"
            ],
            ["oauth2.googleapis.com"] = ["/token", "/revoke"],
            ["www.googleapis.com"] =
            [
                "/oauth2/v3/certs",
                "/calendar/v3",
                "/drive/v3"
            ],
            ["gmail.googleapis.com"] = ["/gmail/v1"],
            ["meet.googleapis.com"] = ["/v2"]
        };

    /// <summary>Creates a network policy. External access is disabled unless explicitly enabled.</summary>
    /// <param name="enabled">Whether approved Google destinations may be contacted.</param>
    /// <param name="requestTimeout">The maximum duration of a Google HTTP request.</param>
    public GoogleEndpointPolicy(
        bool enabled = false,
        TimeSpan? requestTimeout = null,
        bool calendarEnabled = true,
        bool gmailEnabled = true,
        bool driveEnabled = true,
        bool meetEnabled = true)
    {
        Enabled = enabled;
        RequestTimeout = requestTimeout ?? TimeSpan.FromSeconds(30);
        CalendarEnabled = calendarEnabled;
        GmailEnabled = gmailEnabled;
        DriveEnabled = driveEnabled;
        MeetEnabled = meetEnabled;
        if (RequestTimeout < TimeSpan.FromSeconds(1) ||
            RequestTimeout > TimeSpan.FromMinutes(2))
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestTimeout),
                "Google request timeout must be between 1 and 120 seconds.");
        }
    }

    /// <summary>Gets whether Google network access is enabled.</summary>
    public bool Enabled { get; }

    /// <summary>Gets the maximum duration of a Google HTTP request.</summary>
    public TimeSpan RequestTimeout { get; }

    /// <summary>Gets whether the Calendar integration is enabled.</summary>
    public bool CalendarEnabled { get; }

    /// <summary>Gets whether the Gmail integration is enabled.</summary>
    public bool GmailEnabled { get; }

    /// <summary>Gets whether the Drive integration is enabled.</summary>
    public bool DriveEnabled { get; }

    /// <summary>Gets whether the Meet integration is enabled.</summary>
    public bool MeetEnabled { get; }

    /// <summary>Throws if Calendar access is disabled.</summary>
    public void EnsureCalendarEnabled() => EnsureServiceEnabled(CalendarEnabled, "Calendar");

    /// <summary>Throws if Gmail access is disabled.</summary>
    public void EnsureGmailEnabled() => EnsureServiceEnabled(GmailEnabled, "Gmail");

    /// <summary>Loads the optional Google network policy from the shared settings file.</summary>
    /// <param name="settingsPath">The path to config/settings.json.</param>
    /// <returns>A validated policy; missing policy configuration means deny.</returns>
    public static GoogleEndpointPolicy FromSettingsFile(string settingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);

        using var document = JsonDocument.Parse(File.ReadAllText(settingsPath));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Settings root must be a JSON object.");

        if (!root.TryGetProperty("integrations", out var integrations))
            return new GoogleEndpointPolicy();

        if (integrations.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Settings integrations must be a JSON object.");

        if (!integrations.TryGetProperty("google", out var google))
            return new GoogleEndpointPolicy();

        if (google.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException("Settings integrations.google must be a JSON object.");

        var googleEnabled = GetBoolean(google, "enabled", defaultValue: false);
        var calendarEnabled = GetServiceEnabled(google, "calendar");
        var gmailEnabled = GetServiceEnabled(google, "gmail");
        var driveEnabled = GetServiceEnabled(google, "drive");
        var meetEnabled = GetServiceEnabled(google, "meet");

        if (!google.TryGetProperty("network_policy", out var networkPolicy))
        {
            return new GoogleEndpointPolicy(
                calendarEnabled: calendarEnabled,
                gmailEnabled: gmailEnabled,
                driveEnabled: driveEnabled,
                meetEnabled: meetEnabled);
        }

        if (networkPolicy.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException(
                "Settings integrations.google.network_policy must be a JSON object.");

        foreach (var property in networkPolicy.EnumerateObject())
        {
            if (property.Name is not ("enabled" or "request_timeout_seconds"))
            {
                throw new InvalidDataException(
                    $"Unknown Google network-policy setting '{property.Name}'.");
            }
        }

        var enabled = false;
        if (networkPolicy.TryGetProperty("enabled", out var enabledValue))
        {
            if (enabledValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new InvalidDataException("Google network_policy.enabled must be a boolean.");

            enabled = enabledValue.GetBoolean();
        }

        var timeoutSeconds = 30;
        if (networkPolicy.TryGetProperty(
                "request_timeout_seconds",
                out var timeoutValue))
        {
            if (timeoutValue.ValueKind != JsonValueKind.Number ||
                !timeoutValue.TryGetInt32(out timeoutSeconds) ||
                timeoutSeconds is < 1 or > 120)
            {
                throw new InvalidDataException(
                    "Google network_policy.request_timeout_seconds must be an integer from 1 to 120.");
            }
        }

        return new GoogleEndpointPolicy(
            enabled && googleEnabled,
            TimeSpan.FromSeconds(timeoutSeconds),
            calendarEnabled,
            gmailEnabled,
            driveEnabled,
            meetEnabled);
    }

    /// <summary>Updates one Google service switch in the shared settings file.</summary>
    /// <param name="settingsPath">The path to config/settings.json.</param>
    /// <param name="serviceName">One of calendar, gmail, drive, or meet.</param>
    /// <param name="enabled">The new enabled state.</param>
    public static void SetServiceEnabled(
        string settingsPath,
        string serviceName,
        bool enabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsPath);
        if (serviceName is not ("calendar" or "gmail" or "drive" or "meet"))
            throw new ArgumentOutOfRangeException(nameof(serviceName));

        var root = JsonNode.Parse(File.ReadAllText(settingsPath))?.AsObject()
            ?? throw new InvalidDataException("Settings root must be a JSON object.");
        var integrations = root["integrations"]?.AsObject()
            ?? throw new InvalidDataException("Settings integrations must be a JSON object.");
        var google = integrations["google"]?.AsObject()
            ?? throw new InvalidDataException("Settings integrations.google must be a JSON object.");
        var service = google[serviceName]?.AsObject()
            ?? throw new InvalidDataException(
                $"Settings integrations.google.{serviceName} must be a JSON object.");
        service["enabled"] = enabled;

        var temporaryPath = settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(
                temporaryPath,
                root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, settingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    /// <summary>Returns whether a parsed URI matches an explicitly approved HTTPS endpoint.</summary>
    /// <param name="endpoint">The absolute request URI.</param>
    public bool IsAllowed(Uri? endpoint)
    {
        if (!Enabled ||
            endpoint is null ||
            !endpoint.IsAbsoluteUri ||
            !string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            endpoint.Port != 443 ||
            endpoint.UserInfo.Length != 0 ||
            endpoint.Fragment.Length != 0 ||
            endpoint.HostNameType != UriHostNameType.Dns)
        {
            return false;
        }

        var host = endpoint.IdnHost;
        if (!ApprovedPaths.TryGetValue(host, out var prefixes))
            return false;

        var path = endpoint.AbsolutePath;
        if ((host == "www.googleapis.com" &&
             path.StartsWith("/calendar/v3", StringComparison.Ordinal) &&
             !CalendarEnabled) ||
            (host == "www.googleapis.com" &&
             path.StartsWith("/drive/v3", StringComparison.Ordinal) &&
             !DriveEnabled) ||
            (host == "gmail.googleapis.com" && !GmailEnabled) ||
            (host == "meet.googleapis.com" && !MeetEnabled))
        {
            return false;
        }

        return prefixes.Any(prefix =>
            string.Equals(path, prefix, StringComparison.Ordinal) ||
            path.StartsWith(prefix + "/", StringComparison.Ordinal));
    }

    /// <summary>Returns whether an absolute URL matches an explicitly approved endpoint.</summary>
    /// <param name="address">The URL to validate.</param>
    public bool IsAllowedUrl(string? address)
    {
        return !string.IsNullOrWhiteSpace(address) &&
            Uri.TryCreate(address, UriKind.Absolute, out var endpoint) &&
            IsAllowed(endpoint);
    }

    /// <summary>Returns whether the URI and HTTP method are approved.</summary>
    /// <param name="endpoint">The absolute request URI.</param>
    /// <param name="method">The HTTP method.</param>
    public bool IsAllowedRequest(Uri? endpoint, HttpMethod method)
    {
        ArgumentNullException.ThrowIfNull(method);
        if (!IsAllowed(endpoint))
            return false;

        var host = endpoint!.IdnHost;
        var path = endpoint.AbsolutePath;
        if (host is "accounts.google.com" &&
            path is "/o/oauth2/auth" or "/o/oauth2/v2/auth")
        {
            return IsMethod(method, "GET");
        }

        if ((host == "accounts.google.com" &&
             path == "/o/oauth2/token") ||
            (host == "oauth2.googleapis.com" &&
             path is "/token" or "/revoke"))
        {
            return IsMethod(method, "POST");
        }

        if (host == "www.googleapis.com" &&
            path == "/oauth2/v3/certs")
        {
            return IsMethod(method, "GET");
        }

        return IsMethod(method, "GET") || IsMethod(method, "HEAD");
    }

    /// <summary>Throws if the policy is disabled.</summary>
    public void EnsureEnabled()
    {
        if (!Enabled)
            throw new InvalidOperationException("Google network access is disabled by policy.");
    }

    private static void EnsureServiceEnabled(bool enabled, string serviceName)
    {
        if (!enabled)
        {
            throw new InvalidOperationException(
                $"Google {serviceName} access is disabled by policy.");
        }
    }

    internal bool IsApprovedHost(string host)
    {
        return Enabled &&
            ApprovedPaths.ContainsKey(host);
    }

    private static bool IsMethod(HttpMethod method, string expected)
    {
        return string.Equals(
            method.Method,
            expected,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool GetBoolean(
        JsonElement element,
        string propertyName,
        bool defaultValue)
    {
        if (!element.TryGetProperty(propertyName, out var value))
            return defaultValue;

        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException(
                $"Settings integrations.google.{propertyName} must be a boolean.");

        return value.GetBoolean();
    }

    private static bool GetServiceEnabled(
        JsonElement google,
        string serviceName)
    {
        if (!google.TryGetProperty(serviceName, out var service))
            return false;
        if (service.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException(
                $"Settings integrations.google.{serviceName} must be a JSON object.");

        return GetBoolean(service, "enabled", defaultValue: false);
    }
}
