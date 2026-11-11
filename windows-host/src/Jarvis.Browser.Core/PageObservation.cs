using System.Collections.ObjectModel;

namespace Jarvis.Browser.Core;

/// <summary>Raw, frame-local DOM data produced by a browser engine observer.</summary>
public sealed record BrowserFrameObservation(
    string FrameId,
    Uri? Address,
    string Title,
    string Name,
    bool IsMainFrame,
    string DocumentToken,
    long DomVersion,
    string VisibleText,
    IReadOnlyList<BrowserElementObservation> Elements,
    bool IsTruncated = false);

/// <summary>Raw interactive-element data produced by a browser engine observer.</summary>
public sealed record BrowserElementObservation(
    string Role,
    string Name,
    string? VisibleText,
    bool IsVisible,
    bool IsEnabled,
    IReadOnlyDictionary<string, string> Attributes);

/// <summary>
/// Identifies an element in one observation only. This descriptive reference is not
/// a validated live DOM handle and must not be used to authorize or execute actions.
/// </summary>
public sealed record ElementReference(
    string ElementId,
    BrowserTabId TabId,
    Guid ObservationId,
    string FrameId,
    string DocumentToken,
    long DomVersion,
    string Role,
    string Name,
    string? VisibleText,
    bool IsVisible,
    bool IsEnabled,
    IReadOnlyDictionary<string, string> Attributes);

/// <summary>Describes a frame included in a page observation.</summary>
public sealed record BrowserFrame(
    string FrameId,
    Uri? Address,
    string Title,
    string Name,
    bool IsMainFrame,
    bool IsAccessible,
    string DocumentToken,
    long DomVersion);

/// <summary>A bounded, immutable view of one tab's current page.</summary>
public sealed record PageSnapshot(
    Guid ObservationId,
    BrowserTabId TabId,
    Uri? Address,
    string Title,
    bool IsActive,
    bool IsLoading,
    IReadOnlyList<BrowserFrame> Frames,
    IReadOnlyList<ElementReference> Elements,
    string VisibleText,
    bool IsTruncated)
{
    /// <summary>Checks snapshot membership only; it does not establish that the DOM is unchanged.</summary>
    public bool ContainsReference(ElementReference reference) =>
        reference.TabId == TabId
        && reference.ObservationId == ObservationId
        && Elements.Contains(reference);
}

/// <summary>Describes a frame that could not be inspected completely.</summary>
public sealed record ObservationIssue(string FrameId, string Code);

/// <summary>The page snapshot and any explicitly reported partial-observation issues.</summary>
public sealed record ObservationResult(
    PageSnapshot Snapshot,
    IReadOnlyList<ObservationIssue> Issues);

/// <summary>Builds tab- and observation-scoped references from frame observations.</summary>
public static class BrowserObservationMapper
{
    /// <summary>Maximum number of interactive elements retained in one snapshot.</summary>
    public const int MaximumElements = 300;

    /// <summary>Maximum combined visible text retained in one snapshot.</summary>
    public const int MaximumVisibleTextLength = 15_000;

    /// <summary>Creates a bounded page snapshot from the current tab and frame data.</summary>
    public static PageSnapshot Create(
        BrowserTabState tabState,
        bool isActive,
        IReadOnlyList<BrowserFrameObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(tabState);
        ArgumentNullException.ThrowIfNull(observations);

        var mainFrames = observations.Where(frame => frame.IsMainFrame).ToArray();
        if (mainFrames.Length != 1)
            throw new ArgumentException("A page observation must contain exactly one main frame.", nameof(observations));

        if (observations.Any(frame =>
                string.IsNullOrWhiteSpace(frame.FrameId)
                || string.IsNullOrWhiteSpace(frame.DocumentToken)
                || frame.DomVersion < 0))
        {
            throw new ArgumentException("Frame identifiers and document tokens are required.", nameof(observations));
        }

        if (observations.Select(frame => frame.FrameId).Distinct(StringComparer.Ordinal).Count() != observations.Count)
            throw new ArgumentException("Frame identifiers must be unique within an observation.", nameof(observations));

        var observationId = Guid.NewGuid();
        var frames = observations.Select(frame => new BrowserFrame(
            frame.FrameId,
            frame.Address,
            Limit(frame.Title, 500, out _),
            Limit(frame.Name, 200, out _),
            frame.IsMainFrame,
            true,
            frame.DocumentToken,
            frame.DomVersion)).ToArray();

        var allElements = observations
            .SelectMany(frame => frame.Elements.Select(element => (Frame: frame, Element: element)))
            .ToArray();
        var elementReferences = new List<ElementReference>(Math.Min(allElements.Length, MaximumElements));
        var nextElementNumber = 0;
        var isTruncated = observations.Any(frame => frame.IsTruncated);
        foreach (var (frame, element) in allElements.Take(MaximumElements))
        {
            var name = Limit(element.Name, 500, out var nameTruncated);
            var visibleText = Limit(element.VisibleText ?? string.Empty, 1_000, out var textTruncated);
            var role = Limit(element.Role, 100, out var roleTruncated);
            elementReferences.Add(new ElementReference(
                $"e{nextElementNumber++}",
                tabState.TabId,
                observationId,
                frame.FrameId,
                frame.DocumentToken,
                frame.DomVersion,
                role,
                name,
                visibleText,
                element.IsVisible,
                element.IsEnabled,
                new ReadOnlyDictionary<string, string>(
                    new Dictionary<string, string>(element.Attributes, StringComparer.Ordinal))));
            isTruncated |= nameTruncated || textTruncated || roleTruncated;
        }

        var pageVisibleText = string.Join(
            Environment.NewLine,
            observations.Select(frame => frame.VisibleText).Where(text => !string.IsNullOrWhiteSpace(text)));
        pageVisibleText = Limit(pageVisibleText, MaximumVisibleTextLength, out var pageTextTruncated);

        var mainFrame = mainFrames[0];
        return new PageSnapshot(
            observationId,
            tabState.TabId,
            mainFrame.Address ?? tabState.Address,
            Limit(mainFrame.Title, 500, out var titleTruncated),
            isActive,
            tabState.IsLoading,
            Array.AsReadOnly(frames),
            Array.AsReadOnly(elementReferences.ToArray()),
            pageVisibleText,
            allElements.Length > MaximumElements || isTruncated || pageTextTruncated || titleTruncated);
    }

    private static string Limit(string? value, int maximumLength, out bool truncated)
    {
        var normalized = value?.Trim() ?? string.Empty;
        truncated = normalized.Length > maximumLength;
        return truncated ? normalized[..maximumLength] : normalized;
    }
}
