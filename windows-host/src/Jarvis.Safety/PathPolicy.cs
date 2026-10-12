using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Jarvis.Safety;

public sealed record PathValidationResult(bool IsValid, string? NormalizedPath, string? ErrorMessage)
{
    public static PathValidationResult Success(string path) => new(true, path, null);
    public static PathValidationResult Failure(string error) => new(false, null, error);
}

/// <summary>
/// Enforces path security rules defined in DESIGN.md section 9.
/// Restricts access strictly to allowed roots, rejecting directory traversal,
/// device paths, UNC paths, DOS reserved names, alternate data streams,
/// trailing dots/spaces, mixed separators, and reparse points.
/// </summary>
public sealed class PathPolicy
{
    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    private static readonly char[] IllegalPathChars = new[] { '<', '>', '"', '|', '?' };

    private readonly List<string> _allowedRoots = new();

    public IReadOnlyList<string> AllowedRoots => _allowedRoots.AsReadOnly();

    public PathPolicy(IEnumerable<string>? allowedRoots = null)
    {
        if (allowedRoots != null)
        {
            foreach (var root in allowedRoots)
            {
                if (!string.IsNullOrWhiteSpace(root))
                {
                    var expanded = Environment.ExpandEnvironmentVariables(root.Trim());
                    var full = Path.GetFullPath(expanded).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    _allowedRoots.Add(full);
                }
            }
        }

        // Default root if none provided: %USERPROFILE%\Documents\Jarvis
        if (_allowedRoots.Count == 0)
        {
            var myDocs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            _allowedRoots.Add(Path.Combine(myDocs, "Jarvis"));
        }
    }

    public PathValidationResult ValidatePath(string? inputPath)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
            return PathValidationResult.Failure("Path cannot be empty or whitespace.");

        // 1. Check for null byte
        if (inputPath.Contains('\0'))
            return PathValidationResult.Failure("Path contains null byte characters.");

        // 2. Normalize Unicode (NFC)
        var path = inputPath.Normalize(NormalizationForm.FormC);

        // 3. Length check (MAX_PATH = 260)
        if (path.Length > 260)
            return PathValidationResult.Failure($"Path exceeds maximum allowed length of 260 characters (actual: {path.Length}).");

        // 4. Reject device paths (\\.\, \\?\, \??\)
        if (path.StartsWith(@"\\.\") || path.StartsWith(@"\\?\") || path.StartsWith(@"\??\"))
            return PathValidationResult.Failure("Device namespace paths are prohibited.");

        // 5. Reject UNC paths (\\)
        if (path.StartsWith(@"\\"))
            return PathValidationResult.Failure("UNC network paths are prohibited.");

        // 6. Mixed separators check
        if (path.Contains('/') && path.Contains('\\'))
            return PathValidationResult.Failure("Mixed path separators (/ and \\) are prohibited.");

        // 7. Check for illegal characters (<, >, ", |, ?)
        if (path.IndexOfAny(IllegalPathChars) >= 0)
            return PathValidationResult.Failure("Path contains illegal characters.");

        // 8. Alternate Data Streams (ADS) check: any colon after drive letter (index 1)
        var firstColon = path.IndexOf(':');
        if (firstColon >= 0)
        {
            if (firstColon != 1 || path.IndexOf(':', firstColon + 1) >= 0)
            {
                return PathValidationResult.Failure("Alternate Data Streams (ADS) are prohibited.");
            }
        }

        // 9. Trailing dots and spaces on any path segment
        var rawSegments = path.Split(new[] { '\\', '/' }, StringSplitOptions.None);
        foreach (var segment in rawSegments)
        {
            if (segment.Length > 0 && (segment.EndsWith('.') || segment.EndsWith(' ')))
            {
                return PathValidationResult.Failure($"Path segment '{segment}' cannot end with trailing dots or spaces.");
            }

            // 10. Check reserved DOS names
            var nameWithoutExtension = Path.GetFileNameWithoutExtension(segment);
            if (ReservedDeviceNames.Contains(nameWithoutExtension) || ReservedDeviceNames.Contains(segment))
            {
                return PathValidationResult.Failure($"Path contains reserved DOS device name '{segment}'.");
            }

            // 11. Directory traversal segment '..' check before full path resolution
            if (segment == "..")
            {
                return PathValidationResult.Failure("Directory traversal (..) is prohibited.");
            }
        }

        // 12. Expand environment variables and resolve full path
        string fullPath;
        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(path);
            fullPath = Path.GetFullPath(expanded);
        }
        catch (Exception ex)
        {
            return PathValidationResult.Failure($"Path resolution failed: {ex.Message}");
        }

        // 13. Allowed roots verification
        bool isAllowed = false;
        foreach (var allowedRoot in _allowedRoots)
        {
            var rootWithSlash = allowedRoot.EndsWith(Path.DirectorySeparatorChar)
                ? allowedRoot
                : allowedRoot + Path.DirectorySeparatorChar;

            if (fullPath.Equals(allowedRoot, StringComparison.OrdinalIgnoreCase) ||
                fullPath.StartsWith(rootWithSlash, StringComparison.OrdinalIgnoreCase))
            {
                isAllowed = true;
                break;
            }
        }

        if (!isAllowed)
        {
            return PathValidationResult.Failure($"Path '{fullPath}' is outside allowed roots.");
        }

        // 14. Reparse point check on existing components
        if (IsReparsePoint(fullPath))
        {
            return PathValidationResult.Failure("Path or an existing ancestor is a reparse point (junction or symlink).");
        }

        return PathValidationResult.Success(fullPath);
    }

    /// <summary>
    /// Checks if the path or any existing parent path is a reparse point (junction/symlink).
    /// </summary>
    public static bool IsReparsePoint(string path)
    {
        try
        {
            var current = new DirectoryInfo(path);
            while (current != null && current.Exists)
            {
                if (current.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    return true;
                }
                current = current.Parent;
            }

            if (File.Exists(path))
            {
                var fileInfo = new FileInfo(path);
                if (fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    return true;
                }
            }
        }
        catch
        {
            // If checking attributes fails due to permissions or missing path, it's not a verified reparse point
        }

        return false;
    }
}
