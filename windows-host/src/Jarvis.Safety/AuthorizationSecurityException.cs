namespace Jarvis.Safety;

/// <summary>
/// Thrown when authorization verification, signing, or consumption fails.
/// </summary>
public sealed class AuthorizationSecurityException : Exception
{
    public AuthorizationSecurityException(string message) : base(message) { }

    public AuthorizationSecurityException(string message, Exception innerException) : base(message, innerException) { }
}
