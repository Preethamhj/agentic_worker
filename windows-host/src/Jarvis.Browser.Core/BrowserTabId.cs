namespace Jarvis.Browser.Core;

/// <summary>Stable identifier for one tab within a browser runtime instance.</summary>
public readonly record struct BrowserTabId(Guid Value)
{
    /// <summary>Creates a new, non-empty tab identifier.</summary>
    public static BrowserTabId New() => new(Guid.NewGuid());

    /// <inheritdoc />
    public override string ToString() => Value.ToString("N");
}
