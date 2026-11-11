namespace Jarvis.Contracts;

/// <summary>Browser-reported page address read directly from the browser engine, not from an executor.</summary>
public sealed record BrowserPageEvidence(string? Url, bool IsLoading);

/// <summary>Reads browser state for independent verification; it must not depend on execution results.</summary>
public interface IBrowserEvidenceSource
{
    /// <summary>Captures the current address and loading state of the target page.</summary>
    Task<BrowserPageEvidence> CaptureAsync(CancellationToken cancellationToken = default);
}

/// <summary>Complete browser tab identities observed before a link action.</summary>
public sealed record BrowserTabTopologyEvidence(IReadOnlyList<string> TabIds);

/// <summary>Destination tab identity and browser-engine page state observed after navigation.</summary>
public sealed record BrowserDestinationEvidence(string? TabId, string? Url, bool IsLoading);

/// <summary>
/// Reads independent browser topology and destination state, not executor-reported results.
/// Topology must enumerate every existing tab and use stable, non-reused identities.
/// </summary>
public interface IBrowserOpenLinkEvidenceSource
{
    /// <summary>Captures the complete current set of tab IDs before creating the destination tab.</summary>
    Task<BrowserTabTopologyEvidence> CaptureTopologyAsync(CancellationToken cancellationToken = default);

    /// <summary>Captures the destination tab's identity and state directly from the browser.</summary>
    Task<BrowserDestinationEvidence> CaptureDestinationAsync(CancellationToken cancellationToken = default);
}
