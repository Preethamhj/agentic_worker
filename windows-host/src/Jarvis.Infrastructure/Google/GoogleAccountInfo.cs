namespace Jarvis.Infrastructure.Google;

/// <summary>
/// Non-secret identity metadata for a connected Google account.
/// </summary>
/// <param name="AccountId">Stable provider subject identifier.</param>
/// <param name="Email">Account email address.</param>
/// <param name="DisplayName">Optional provider display name.</param>
/// <param name="ConnectedAtUtc">Time the account was connected.</param>
public sealed record GoogleAccountInfo(
    string AccountId,
    string Email,
    string? DisplayName,
    DateTimeOffset ConnectedAtUtc);