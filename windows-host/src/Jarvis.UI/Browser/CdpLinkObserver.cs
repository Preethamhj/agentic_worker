using System.Text.Json;
using Jarvis.Browser.Core;

namespace Jarvis.UI.Browser;

internal sealed record CdpBoundObservation(
    ObservationResult Observation,
    long Generation,
    IReadOnlyList<int> Nodes);

internal class CdpLinkCaptureException(string message) : InvalidOperationException(message);
internal sealed class CdpLinkChangedException() : CdpLinkCaptureException("The page changed while link identities were captured.");

internal sealed class CdpLinkObserver(
    Func<string, string, Task<string>> call,
    Func<CancellationToken, Task<ObservationResult>> observe,
    Func<long> generation)
{
    private const int MaximumResponseLength = 16_000_000;

    public async Task<CdpBoundObservation> CaptureWithRetryAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        var initialGeneration = generation();
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var captured = await CaptureAsync(timeout.Token);
                if (captured.Generation != initialGeneration)
                    throw new CdpLinkCaptureException("The page navigated while link identities were captured.");
                return captured;
            }
            catch (CdpLinkChangedException) when (attempt < 2 && generation() == initialGeneration)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(150), timeout.Token);
            }
        }
    }

    public async Task<CdpBoundObservation> CaptureAsync(CancellationToken cancellationToken = default)
    {
        var initialGeneration = generation();
        var before = await observe(cancellationToken);
        var firstNodes = await ReadNodesAsync(cancellationToken);
        var after = await observe(cancellationToken);
        var secondNodes = await ReadNodesAsync(cancellationToken);
        var final = await observe(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var firstFrame = before.Snapshot.Frames.Single(frame => frame.IsMainFrame);
        var secondFrame = after.Snapshot.Frames.Single(frame => frame.IsMainFrame);
        var finalFrame = final.Snapshot.Frames.Single(frame => frame.IsMainFrame);
        var mainCount = before.Snapshot.Elements.Count(element => element.FrameId == firstFrame.FrameId);
        if (initialGeneration != generation()
            || before.Snapshot.TabId != after.Snapshot.TabId
            || !before.Snapshot.IsActive || before.Snapshot.IsLoading
            || !after.Snapshot.IsActive || after.Snapshot.IsLoading
            || !final.Snapshot.IsActive || final.Snapshot.IsLoading
            || final.Snapshot.TabId != before.Snapshot.TabId
            || firstFrame.DocumentToken != secondFrame.DocumentToken
            || firstFrame.DocumentToken != finalFrame.DocumentToken
            || firstFrame.DomVersion != secondFrame.DomVersion
            || firstFrame.DomVersion != finalFrame.DomVersion
            || !firstNodes.SequenceEqual(secondNodes)
            || firstNodes.Count < mainCount
            || firstNodes.Distinct().Count() != firstNodes.Count)
            throw new CdpLinkChangedException();
        return new CdpBoundObservation(before, initialGeneration, firstNodes);
    }

    public async Task<bool> IsCurrentAsync(
        CdpBoundObservation captured,
        BrowserPageDisclosure disclosure,
        BrowserDisclosureLink link,
        CancellationToken cancellationToken = default)
    {
        if (generation() != captured.Generation || captured.Observation.Snapshot.ObservationId != disclosure.ObservationId)
            return false;
        var original = captured.Observation.Snapshot.Elements
            .Select((element, index) => (element, index))
            .SingleOrDefault(item => item.element.ElementId == link.ElementId
                && item.element.FrameId == disclosure.MainFrameId);
        if (original.element is null || original.index >= captured.Nodes.Count)
            return false;
        var frame = captured.Observation.Snapshot.Frames.Single(item => item.IsMainFrame);
        var binding = CdpLinkNodeBinding.Capture(frame.DocumentToken, frame.DomVersion, frame.DomVersion,
            captured.Nodes, captured.Nodes, original.index);
        var current = await CaptureWithRetryAsync(cancellationToken);
        if (generation() != captured.Generation || current.Generation != captured.Generation)
            return false;
        var currentFrame = current.Observation.Snapshot.Frames.Single(item => item.IsMainFrame);
        var position = binding.FindCurrentPosition(currentFrame.DocumentToken, current.Nodes);
        if (position is null || position.Value >= current.Observation.Snapshot.Elements.Count)
            return false;
        var element = current.Observation.Snapshot.Elements[position.Value];
        return disclosure.CheckBoundLink(current.Observation, link, element) == BrowserLinkFreshness.Current;
    }

    private async Task<IReadOnlyList<int>> ReadNodesAsync(CancellationToken cancellationToken)
    {
        var rawDocument = await call("DOM.getDocument", """{"depth":-1}""").WaitAsync(cancellationToken);
        if (rawDocument.Length > MaximumResponseLength)
            throw new CdpLinkCaptureException("The page is too large to safely bind link identities.");
        using var document = JsonDocument.Parse(rawDocument, new JsonDocumentOptions { MaxDepth = 256 });
        var root = document.RootElement.GetProperty("root");
        var nodeMap = new Dictionary<int, int>();
        var pending = new Stack<JsonElement>();
        pending.Push(root);
        while (pending.TryPop(out var node))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node.TryGetProperty("nodeId", out var nodeId) && node.TryGetProperty("backendNodeId", out var backend))
            {
                if (!nodeMap.TryAdd(nodeId.GetInt32(), backend.GetInt32()))
                    throw new CdpLinkCaptureException("The page returned duplicate browser node identities.");
            }
            if (node.TryGetProperty("children", out var children))
                foreach (var child in children.EnumerateArray())
                    pending.Push(child);
        }
        var query = JsonSerializer.Serialize(new
        {
            nodeId = root.GetProperty("nodeId").GetInt32(),
            selector = BrowserObservationScripts.InteractiveSelector
        });
        var rawMatches = await call("DOM.querySelectorAll", query).WaitAsync(cancellationToken);
        if (rawMatches.Length > MaximumResponseLength)
            throw new CdpLinkCaptureException("The page has too many interactive elements to safely inspect.");
        using var matches = JsonDocument.Parse(rawMatches);
        var nodes = new List<int>();
        foreach (var item in matches.RootElement.GetProperty("nodeIds").EnumerateArray()
                     .Take(BrowserObservationMapper.MaximumElements))
        {
            if (!nodeMap.TryGetValue(item.GetInt32(), out var backend) || backend <= 0)
                throw new CdpLinkCaptureException("A browser node identity could not be resolved.");
            nodes.Add(backend);
        }
        return nodes.AsReadOnly();
    }
}
