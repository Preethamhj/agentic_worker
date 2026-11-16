using System.Globalization;
using System.Text.Json;

namespace Jarvis.UI.Browser;

internal static class CdpLayoutVisibility
{
    public const string SnapshotArguments = """{"computedStyles":["visibility","opacity"]}""";
    private const int MaximumSnapshotCharacters = 12_000_000;

    public static HashSet<int> FindRenderedNodes(string json, IReadOnlySet<int> candidates)
    {
        if (json.Length > MaximumSnapshotCharacters)
            throw new InvalidOperationException("The page layout snapshot exceeds the observation limit.");

        using var snapshot = JsonDocument.Parse(json);
        var strings = snapshot.RootElement.GetProperty("strings");
        var documents = snapshot.RootElement.GetProperty("documents");
        if (documents.GetArrayLength() == 0)
            throw new JsonException("The page layout snapshot omitted the main document.");

        var document = documents[0];
        var nodes = document.GetProperty("nodes");
        var backendIds = nodes.GetProperty("backendNodeId");
        var parents = nodes.GetProperty("parentIndex");
        var types = nodes.GetProperty("nodeType");
        if (parents.GetArrayLength() != backendIds.GetArrayLength()
            || types.GetArrayLength() != backendIds.GetArrayLength())
            throw new JsonException("The page layout snapshot has inconsistent node columns.");
        var layout = document.GetProperty("layout");
        var nodeIndices = layout.GetProperty("nodeIndex");
        var styles = layout.GetProperty("styles");
        var bounds = layout.GetProperty("bounds");
        if (nodeIndices.GetArrayLength() != styles.GetArrayLength()
            || nodeIndices.GetArrayLength() != bounds.GetArrayLength())
            throw new JsonException("The page layout snapshot has inconsistent layout columns.");

        var layoutStates = new Dictionary<int, (bool Visible, bool HasSize)>();
        for (var index = 0; index < nodeIndices.GetArrayLength(); index++)
        {
            var nodeIndex = nodeIndices[index].GetInt32();
            if (nodeIndex < 0 || nodeIndex >= backendIds.GetArrayLength())
                throw new JsonException("The page layout snapshot has an invalid node index.");
            var style = styles[index];
            var rectangle = bounds[index];
            if (style.GetArrayLength() == 0 && types[nodeIndex].GetInt32() != 1)
                continue;
            if (style.GetArrayLength() != 2 || rectangle.GetArrayLength() != 4)
                throw new JsonException("The page layout snapshot has invalid style or geometry data.");
            var visibilityIndex = style[0].GetInt32();
            var opacityIndex = style[1].GetInt32();
            if (visibilityIndex < 0 || visibilityIndex >= strings.GetArrayLength()
                || opacityIndex < 0 || opacityIndex >= strings.GetArrayLength())
                throw new JsonException("The page layout snapshot has an invalid style index.");
            var visibility = strings[visibilityIndex].GetString();
            var opacity = strings[opacityIndex].GetString();
            var visible = visibility == "visible"
                && double.TryParse(opacity, NumberStyles.Float, CultureInfo.InvariantCulture, out var alpha)
                && alpha > 0;
            var hasSize = rectangle[2].GetDouble() > 0 && rectangle[3].GetDouble() > 0;
            if (layoutStates.TryGetValue(nodeIndex, out var previous))
                layoutStates[nodeIndex] = (previous.Visible && visible, previous.HasSize || hasSize);
            else
                layoutStates.Add(nodeIndex, (visible, hasSize));
        }

        var rendered = new HashSet<int>();
        foreach (var (nodeIndex, state) in layoutStates)
        {
            if (!candidates.Contains(backendIds[nodeIndex].GetInt32()) || !state.Visible || !state.HasSize)
                continue;
            var parent = parents[nodeIndex].GetInt32();
            var steps = 0;
            var visible = true;
            while (parent != -1)
            {
                if (parent < 0 || parent >= backendIds.GetArrayLength() || ++steps > backendIds.GetArrayLength())
                    throw new JsonException("The page layout snapshot has an invalid parent chain.");
                if (types[parent].GetInt32() == 1
                    && (!layoutStates.TryGetValue(parent, out var ancestor) || !ancestor.Visible))
                {
                    visible = false;
                    break;
                }
                parent = parents[parent].GetInt32();
            }

            if (visible)
                rendered.Add(backendIds[nodeIndex].GetInt32());
        }
        return rendered;
    }
}
