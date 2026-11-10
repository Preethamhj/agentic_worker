
using Google.Apis.Auth.OAuth2;

namespace Jarvis.Infrastructure.Google;

public interface IGoogleOAuthService
{
    Task<GoogleAccountInfo> ConnectAsync(
        CancellationToken cancellationToken = default);

    Task<GoogleAccountInfo?> GetConnectedAccountAsync(
        CancellationToken cancellationToken = default);

    Task<UserCredential> GetCredentialAsync(
        CancellationToken cancellationToken = default);

    Task<bool> HasStoredCredentialAsync(
        CancellationToken cancellationToken = default);

    Task<GoogleDisconnectResult> DisconnectAsync(
        CancellationToken cancellationToken = default);
}
