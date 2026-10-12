namespace Jarvis.Infrastructure;

/// <summary>
/// Abstraction for managing the HMAC signing key backed by DPAPI.
/// </summary>
public interface IDpapiKeyStore
{
    /// <summary>
    /// Gets the current HMAC key, generating and saving a new key encrypted with DPAPI if none exists.
    /// </summary>
    byte[] GetOrCreateKey();

    /// <summary>
    /// Regenerates the HMAC key and saves it encrypted with DPAPI.
    /// </summary>
    void RegenerateKey();
}
