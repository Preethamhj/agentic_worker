using System.IO.Enumeration;
using System.Text.Json;
using Jarvis.Contracts;

namespace Jarvis.Verification;

internal sealed class SearchEvidenceCollector(FileEvidenceCollector fileCollector)
{
    public IndependentEvidenceObservation Collect(
        AuthorizationRecordDto authorization,
        string reportedPaths,
        CancellationToken cancellationToken)
    {
        if (!authorization.CanonicalArgs.TryGetValue("root_path", out var rootValue)
            || rootValue is not string rootPath
            || !authorization.CanonicalArgs.TryGetValue("pattern", out var patternValue)
            || patternValue is not string pattern
            || authorization.WatchScopes.Count == 0)
        {
            throw new ArgumentException("Search authorization is missing a signed root, pattern, or allowed root.");
        }

        var claimsValid = TryReadClaims(reportedPaths, out var claims);
        var results = new List<Dictionary<string, object?>>(claims.Count);
        var allMatch = claimsValid;

        foreach (var claim in claims)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Path.IsPathRooted(claim))
            {
                results.Add(new Dictionary<string, object?>
                {
                    ["reported_path"] = claim,
                    ["exists"] = false,
                    ["inside_allowed_root"] = false,
                    ["matches_pattern"] = false
                });
                allMatch = false;
                continue;
            }

            FileSystemObservation file;
            try
            {
                file = fileCollector.Collect(
                    claim,
                    authorization.WatchScopes,
                    cancellationToken,
                    hashFile: false);
            }
            catch (ArgumentException)
            {
                results.Add(new Dictionary<string, object?>
                {
                    ["reported_path"] = claim,
                    ["exists"] = false,
                    ["inside_allowed_root"] = false,
                    ["matches_pattern"] = false
                });
                allMatch = false;
                continue;
            }
            catch (NotSupportedException)
            {
                results.Add(new Dictionary<string, object?>
                {
                    ["reported_path"] = claim,
                    ["exists"] = false,
                    ["inside_allowed_root"] = false,
                    ["matches_pattern"] = false
                });
                allMatch = false;
                continue;
            }
            var pathMatchesPattern = FileSystemName.MatchesSimpleExpression(
                pattern,
                Path.GetFileName(claim),
                ignoreCase: true);
            var match = file.Exists
                && !file.IsDirectory
                && !file.IsReparsePoint
                && file.IsInsideAllowedRoot
                && pathMatchesPattern
                && IsWithinRoot(Path.GetFullPath(claim), Path.GetFullPath(rootPath));
            results.Add(new Dictionary<string, object?>
            {
                ["reported_path"] = claim,
                ["exists"] = file.Exists,
                ["is_directory"] = file.IsDirectory,
                ["is_reparse_point"] = file.IsReparsePoint,
                ["inside_allowed_root"] = file.IsInsideAllowedRoot,
                ["final_path"] = file.FinalPath,
                ["size"] = file.Size,
                ["sha256"] = file.Sha256,
                ["creation_time_utc"] = file.CreationTimeUtc,
                ["last_write_time_utc"] = file.LastWriteTimeUtc,
                ["matches_pattern"] = pathMatchesPattern,
                ["inside_signed_search_root"] = IsWithinRoot(Path.GetFullPath(claim), Path.GetFullPath(rootPath))
            });
            allMatch &= match;
        }

        var observed = new Dictionary<string, object?>
        {
            ["root_path"] = rootPath,
            ["pattern"] = pattern,
            ["claims_valid_json_array"] = claimsValid,
            ["reported_path_count"] = claims.Count,
            ["paths"] = results
        };
        return new IndependentEvidenceObservation(
            "paths_match",
            nameof(SearchEvidenceCollector),
            observed,
            allMatch,
            allMatch
                ? "Every reported path was re-opened from the OS and matched the signed root and pattern."
                : "One or more reported paths were invalid, missing, outside the signed root, or did not match the pattern.");
    }

    private static bool TryReadClaims(string value, out List<string> claims)
    {
        try
        {
            var parsed = JsonSerializer.Deserialize<string?[]>(value);
            if (parsed is null || parsed.Any(path => path is null))
            {
                claims = parsed?.Where(path => path is not null).Select(path => path!).ToList() ?? [];
                return false;
            }
            claims = parsed.Select(path => path!).ToList();
            return true;
        }
        catch (JsonException)
        {
            claims = [];
            return false;
        }
    }

    private static bool IsWithinRoot(string target, string root)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(root);
        var normalizedTarget = Path.TrimEndingDirectorySeparator(target);
        return string.Equals(normalizedTarget, normalizedRoot, StringComparison.OrdinalIgnoreCase)
            || normalizedTarget.StartsWith(
                normalizedRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }
}
