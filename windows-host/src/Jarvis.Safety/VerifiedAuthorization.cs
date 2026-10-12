using Jarvis.Contracts;

namespace Jarvis.Safety;

/// <summary>
/// Type-safe authorization proof.
/// Sealed class with get-only properties and no copy path.
/// The internal constructor guarantees compile-time / assembly enforcement:
/// only AuthorizationService within Jarvis.Safety can instantiate VerifiedAuthorization.
/// Executors in later phases must accept this type to guarantee authorization prior to execution.
/// </summary>
public sealed class VerifiedAuthorization
{
    public string AuthId { get; }
    public string TaskId { get; }
    public string StepId { get; }
    public string ToolId { get; }
    public IReadOnlyDictionary<string, object?> CanonicalArgs { get; }
    public IReadOnlyDictionary<string, object?> Expected { get; }
    public IReadOnlyList<string> AllowedEffects { get; }
    public IReadOnlyList<string> WatchScopes { get; }
    public string Risk { get; }
    public string ApprovedBy { get; }
    public string IssuedAt { get; }
    public string ExpiresAt { get; }
    public string ExecuteBefore { get; }
    public string VerifyBefore { get; }
    public string Nonce { get; }
    public string Hmac { get; }

    public AuthorizationRecordDto Record { get; }

    internal VerifiedAuthorization(AuthorizationRecordDto record)
    {
        Record = record ?? throw new ArgumentNullException(nameof(record));
        AuthId = record.AuthId;
        TaskId = record.TaskId;
        StepId = record.StepId;
        ToolId = record.ToolId;
        CanonicalArgs = record.CanonicalArgs;
        Expected = record.Expected;
        AllowedEffects = record.AllowedEffects;
        WatchScopes = record.WatchScopes;
        Risk = record.Risk;
        ApprovedBy = record.ApprovedBy;
        IssuedAt = record.IssuedAt;
        ExpiresAt = record.ExpiresAt;
        ExecuteBefore = record.ExecuteBefore;
        VerifyBefore = record.VerifyBefore;
        Nonce = record.Nonce;
        Hmac = record.Hmac;
    }
}
