namespace Jarvis.Verification;

internal sealed record IndependentEvidenceObservation(
    string CheckType,
    string Collector,
    Dictionary<string, object?> Observed,
    bool Match,
    string Detail,
    string? Outcome = null);
