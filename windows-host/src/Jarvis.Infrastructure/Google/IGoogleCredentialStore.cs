namespace Jarvis.Infrastructure.Google;

/// <summary>
/// Stores OAuth token data and account metadata protected for the current Windows user.
/// </summary>
public interface IGoogleCredentialStore
{
    /// <summary>
    /// Stores one serialized token record.
    /// </summary>
    Task StoreAsync<T>(
        string key,
        T value,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads one token record, if present.
    /// </summary>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes one token record.
    /// </summary>
    Task DeleteAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists non-secret account metadata.
    /// </summary>
    Task StoreAccountAsync(
        GoogleAccountInfo account,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads persisted account metadata, if present.
    /// </summary>
    Task<GoogleAccountInfo?> GetAccountAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes persisted account metadata.
    /// </summary>
    Task ClearAccountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes all persisted OAuth-library token data.
    /// </summary>
    Task ClearAsync(CancellationToken cancellationToken = default);
}