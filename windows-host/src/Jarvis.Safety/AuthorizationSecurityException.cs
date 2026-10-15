using Jarvis.Contracts;

namespace Jarvis.Safety;

/// <summary>
/// Thrown when authorization verification, signing, or consumption fails.
/// </summary>
public sealed class AuthorizationSecurityException : AuthorizationVerificationException
{
    public AuthorizationSecurityException(string message) : base(message) { }

    public AuthorizationSecurityException(string message, Exception innerException) : base(message, innerException) { }
}
