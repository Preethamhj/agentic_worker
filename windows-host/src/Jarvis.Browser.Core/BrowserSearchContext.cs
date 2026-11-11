namespace Jarvis.Browser.Core;

/// <summary>Describes a search-field candidate by its position in one local snapshot.</summary>
/// <remarks>The index is not a live DOM handle or authorization to perform an action.</remarks>
public sealed record BrowserSearchField(int ElementIndex);

/// <summary>Contains bounded structural search metadata, without page text or URLs.</summary>
public sealed record BrowserSearchContext
{
    private const int MaximumFields = 20;

    private BrowserSearchContext(IReadOnlyList<BrowserSearchField> searchFields) =>
        SearchFields = searchFields;

    /// <summary>Gets potential search fields in the snapshot's main frame.</summary>
    public IReadOnlyList<BrowserSearchField> SearchFields { get; }

    /// <summary>Derives metadata without copying untrusted page strings into the result.</summary>
    public static BrowserSearchContext Create(PageSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!snapshot.IsActive || snapshot.IsLoading)
            return new BrowserSearchContext(Array.Empty<BrowserSearchField>());

        var mainFrame = snapshot.Frames.Single(frame => frame.IsMainFrame);
        var fields = snapshot.Elements
            .Select((element, index) => (element, index))
            .Where(item => item.element.FrameId == mainFrame.FrameId && IsSearchField(item.element))
            .Take(MaximumFields)
            .Select(item => new BrowserSearchField(item.index))
            .ToArray();
        return new BrowserSearchContext(Array.AsReadOnly(fields));
    }

    private static bool IsSearchField(ElementReference element)
    {
        if (!element.IsVisible || !element.IsEnabled
            || !(EqualsIgnoreCase(element.Role, "textbox")
                || EqualsIgnoreCase(element.Role, "searchbox")
                || EqualsIgnoreCase(element.Role, "combobox")))
            return false;

        element.Attributes.TryGetValue("type", out var type);
        if (!string.IsNullOrEmpty(type)
            && !EqualsIgnoreCase(type, "text")
            && !EqualsIgnoreCase(type, "search"))
            return false;

        return EqualsIgnoreCase(type, "search")
            || EqualsIgnoreCase(element.Name.Trim(), "search")
            || HasSearchAttribute(element, "name")
            || HasSearchAttribute(element, "placeholder")
            || HasSearchAttribute(element, "aria-label");
    }

    private static bool HasSearchAttribute(ElementReference element, string name) =>
        element.Attributes.TryGetValue(name, out var value)
        && EqualsIgnoreCase(value.Trim(), "search");

    private static bool EqualsIgnoreCase(string? left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
