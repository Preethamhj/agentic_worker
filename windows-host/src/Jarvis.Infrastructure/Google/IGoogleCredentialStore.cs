
using Google.Apis.Util.Store;

namespace Jarvis.Infrastructure.Google;

public interface IGoogleCredentialStore : IDataStore
{
    Task StoreAccountAsync(
        GoogleAccountInfo account,
        CancellationToken cancellationToken = default);

    Task<GoogleAccountInfo?> GetAccountAsync(
        CancellationToken cancellationToken = default);

    Task ClearAccountAsync(
        CancellationToken cancellationToken = default);
}
