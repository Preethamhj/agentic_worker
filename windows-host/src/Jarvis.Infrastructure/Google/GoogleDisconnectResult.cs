namespace Jarvis.Infrastructure.Google;

/// <summary>Reports provider revocation and local credential deletion separately.</summary>
/// <param name="ProviderRevocationConfirmed">Whether Google confirmed token revocation.</param>
/// <param name="LocalCredentialsDeleted">Whether local tokens and account metadata were deleted.</param>
/// <param name="ErrorMessage">A sanitized explanation for any incomplete outcome.</param>
public sealed record GoogleDisconnectResult(
    bool ProviderRevocationConfirmed,
    bool LocalCredentialsDeleted,
    string? ErrorMessage);
