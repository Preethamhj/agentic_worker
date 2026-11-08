using System.Text.Json;
using Jarvis.Contracts;

namespace Jarvis.Composition;

/// <summary>Independently collected search paths for a verified step.</summary>
public sealed record VerifiedSearchResult(string StepId, IReadOnlyList<string> Paths);

/// <summary>Extracts search results only from successful independent verification evidence.</summary>
public static class VerifiedSearchResults
{
    /// <summary>Returns verified search results, excluding executor receipts and failed evidence.</summary>
    public static IReadOnlyList<VerifiedSearchResult> From(IEnumerable<VerificationResultDto> verifications)
    {
        ArgumentNullException.ThrowIfNull(verifications);
        var results = new List<VerifiedSearchResult>();
        foreach (var verification in verifications)
        {
            if (!string.Equals(verification.Status, "VERIFIED", StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (var evidence in verification.Evidence)
            {
                if (!evidence.Match
                    || !string.Equals(evidence.StepId, verification.StepId, StringComparison.Ordinal)
                    || !string.Equals(evidence.Collector, "SearchEvidenceCollector", StringComparison.Ordinal))
                    continue;

                if (!evidence.Observed.TryGetValue("enumerated_paths", out var value))
                    throw new InvalidDataException("Verified search evidence is missing enumerated paths.");

                var paths = value switch
                {
                    IEnumerable<string> strings => strings.ToArray(),
                    JsonElement { ValueKind: JsonValueKind.Array } array => ReadPaths(array),
                    _ => throw new InvalidDataException("Verified search evidence has invalid enumerated paths.")
                };
                results.Add(new VerifiedSearchResult(verification.StepId, Array.AsReadOnly(paths)));
            }
        }
        return results.AsReadOnly();
    }

    private static string[] ReadPaths(JsonElement array) =>
        array.EnumerateArray().Select(item =>
            item.ValueKind == JsonValueKind.String
                ? item.GetString() ?? throw new InvalidDataException("Search evidence contains a null path.")
                : throw new InvalidDataException("Search evidence contains a non-string path.")).ToArray();
}
