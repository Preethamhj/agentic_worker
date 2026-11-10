namespace Jarvis.Infrastructure.Google;

/// <summary>
/// Local paths and account key for the Google OAuth connection.
/// </summary>
public sealed record GoogleOAuthOptions
{
    /// <summary>
    /// Gets the display name for the future Google authorization request.
    /// </summary>
    public string ApplicationName { get; init; } = "Jarvis";

    /// <summary>
    /// Gets the path to the user-provided OAuth desktop-client JSON file.
    /// </summary>
    public string OAuthClientFilePath { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Jarvis",
        "Google",
        "oauth-client.json");

    /// <summary>
    /// Gets the directory for DPAPI-protected Google credentials.
    /// </summary>
    public string CredentialDirectoryPath { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Jarvis",
        "Google",
        "credentials");

    /// <summary>
    /// Gets the stable key used to identify the single supported account.
    /// </summary>
    public string UserKey { get; init; } = "jarvis-google-primary";

    /// <summary>
    /// Validates the local OAuth settings without opening or transmitting credentials.
    /// </summary>
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ApplicationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(OAuthClientFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(CredentialDirectoryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(UserKey);
    }
}