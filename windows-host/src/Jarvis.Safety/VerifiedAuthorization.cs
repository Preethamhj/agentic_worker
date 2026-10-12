using Jarvis.Contracts;

namespace Jarvis.Safety;

/// <summary>
/// Type-safe authorization proof.
/// The internal constructor guarantees compile-time / assembly enforcement:
/// only AuthorizationService within Jarvis.Safety can instantiate VerifiedAuthorization.
/// Executors in later phases must accept this type to guarantee authorization prior to execution.
/// </summary>
public sealed class VerifiedAuthorization
{
    public AuthorizationRecordDto Record { get; }

    internal VerifiedAuthorization(AuthorizationRecordDto record)
    {
        Record = record ?? throw new ArgumentNullException(nameof(record));
    }
}
