using System.Text;
using Jarvis.Browser.Core;

namespace Jarvis.UI.Browser;

/// <summary>Formats a local page observation for the browser workspace.</summary>
public static class BrowserSnapshotFormatter
{
    /// <summary>Returns a human-readable snapshot without sending it anywhere.</summary>
    public static string Format(ObservationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var snapshot = result.Snapshot;
        var output = new StringBuilder();
        output.AppendLine("Local snapshot (not live; recapture after the page changes)");
        output.AppendLine($"Title: {snapshot.Title}");
        output.AppendLine($"Frames captured: {snapshot.Frames.Count}");
        output.AppendLine($"Interactive elements: {snapshot.Elements.Count}");
        if (snapshot.IsTruncated)
            output.AppendLine("Warning: snapshot was truncated.");
        if (result.Issues.Count > 0)
        {
            output.AppendLine("Warning: some frames could not be fully inspected:");
            foreach (var issue in result.Issues)
                output.AppendLine($"- {issue.FrameId}: {issue.Code}");
        }

        output.AppendLine();
        output.AppendLine("Frames:");
        foreach (var frame in snapshot.Frames)
            output.AppendLine($"- {frame.FrameId}: {frame.Title}");
        output.AppendLine();
        output.AppendLine("Page text:");
        output.AppendLine(snapshot.VisibleText);
        output.AppendLine();
        output.AppendLine("Interactive elements:");
        foreach (var element in snapshot.Elements)
        {
            output.AppendLine($"[{element.ElementId}] {element.Role}: {element.Name}");
            output.AppendLine($"  Frame: {element.FrameId}; visible: {element.IsVisible}; enabled: {element.IsEnabled}");
            if (!string.IsNullOrWhiteSpace(element.VisibleText)
                && !string.Equals(element.Name, element.VisibleText, StringComparison.Ordinal))
                output.AppendLine($"  Text: {element.VisibleText}");
            foreach (var attribute in element.Attributes)
            {
                if (!string.Equals(attribute.Key, "href", StringComparison.OrdinalIgnoreCase))
                    output.AppendLine($"  {attribute.Key}: {attribute.Value}");
            }
        }

        return output.ToString();
    }
}
