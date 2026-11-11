using System.Text.Json;

namespace Jarvis.UI.Browser;

internal sealed record CdpSearchObservation(Guid ObservationId, IReadOnlyList<int> Candidates, bool IsTruncated);

internal sealed class CdpSearchObserver
{
    private const int MaxInputs = 200;
    private const int MaxCandidates = 20;
    private readonly Func<string, string, Task<string>> _call;
    private readonly Func<long> _navigationGeneration;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _stateLock = new();
    private long _revision;
    private Guid _observationId;
    private long _generation;
    private IReadOnlyList<int> _backendNodeIds = [];

    public CdpSearchObserver(Func<string, string, Task<string>> call, Func<long> navigationGeneration)
    {
        _call = call ?? throw new ArgumentNullException(nameof(call));
        _navigationGeneration = navigationGeneration ?? throw new ArgumentNullException(nameof(navigationGeneration));
    }

    public async Task<CdpSearchObservation> CaptureAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            long revision;
            lock (_stateLock)
            {
                InvalidateCore();
                revision = _revision;
            }
            cancellationToken.ThrowIfCancellationRequested();
            var generation = _navigationGeneration();
            var (nodes, truncated) = await CollectAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            EnsureSameDocument(generation);
            lock (_stateLock)
            {
                if (revision != _revision || generation != _navigationGeneration())
                    throw new InvalidOperationException("The search observation was invalidated while it was being captured.");
                _generation = generation;
                _backendNodeIds = nodes;
                _observationId = Guid.NewGuid();
                return new CdpSearchObservation(
                    _observationId, Array.AsReadOnly(Enumerable.Range(0, nodes.Count).ToArray()), truncated);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> IsCurrentAsync(Guid observationId, int candidate, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            long revision;
            long generation;
            int backendNodeId;
            lock (_stateLock)
            {
                if (observationId == Guid.Empty || observationId != _observationId
                    || candidate < 0 || candidate >= _backendNodeIds.Count
                    || _generation != _navigationGeneration())
                    return false;
                revision = _revision;
                generation = _generation;
                backendNodeId = _backendNodeIds[candidate];
            }

            try
            {
                var (current, _) = await CollectAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                lock (_stateLock)
                {
                    if (revision != _revision || observationId != _observationId)
                        return false;
                    if (generation == _navigationGeneration() && current.Contains(backendNodeId))
                        return true;
                    InvalidateCore();
                    return false;
                }
            }
            catch
            {
                lock (_stateLock)
                {
                    if (revision == _revision)
                        InvalidateCore();
                }
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Invalidate()
    {
        lock (_stateLock)
            InvalidateCore();
    }

    /// <summary>
    /// Types the query into one revalidated candidate and presses Enter. Completion only means the
    /// events were dispatched; it is not evidence that the search happened.
    /// </summary>
    public async Task SubmitQueryAsync(
        Guid observationId,
        int candidate,
        string query,
        CancellationToken cancellationToken = default)
    {
        if (!await IsCurrentAsync(observationId, candidate, cancellationToken))
            throw new InvalidOperationException("The search field changed before the query could be entered.");

        int backendNodeId;
        long generation;
        lock (_stateLock)
        {
            if (observationId != _observationId || candidate < 0 || candidate >= _backendNodeIds.Count)
                throw new InvalidOperationException("The search field changed before the query could be entered.");
            backendNodeId = _backendNodeIds[candidate];
            generation = _generation;
        }

        await _call("DOM.focus", JsonSerializer.Serialize(new { backendNodeId })).WaitAsync(cancellationToken);
        using (var resolved = await CallAsync("DOM.resolveNode", JsonSerializer.Serialize(new { backendNodeId }), cancellationToken))
        {
            var objectId = resolved.RootElement.GetProperty("object").GetProperty("objectId").GetString()
                ?? throw new JsonException("CDP returned no object for the search field.");
            var select = JsonSerializer.Serialize(new { objectId, functionDeclaration = "function(){this.select();}" });
            await _call("Runtime.callFunctionOn", select).WaitAsync(cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (generation != _navigationGeneration())
            throw new InvalidOperationException("The page navigated before the query could be entered.");

        await _call("Input.insertText", JsonSerializer.Serialize(new { text = query })).WaitAsync(cancellationToken);
        foreach (var type in new[] { "keyDown", "keyUp" })
        {
            var key = JsonSerializer.Serialize(new
            {
                type,
                key = "Enter",
                code = "Enter",
                windowsVirtualKeyCode = 13,
                nativeVirtualKeyCode = 13,
                text = type == "keyDown" ? "\r" : null
            });
            await _call("Input.dispatchKeyEvent", key).WaitAsync(cancellationToken);
        }
    }

    private void InvalidateCore()
    {
        _revision++;
        _observationId = Guid.Empty;
        _backendNodeIds = [];
    }

    private async Task<(IReadOnlyList<int> Nodes, bool Truncated)> CollectAsync(CancellationToken cancellationToken)
    {
        using var root = await CallAsync("DOM.getDocument", """{"depth":0}""", cancellationToken);
        var rootId = root.RootElement.GetProperty("root").GetProperty("nodeId").GetInt32();
        var query = JsonSerializer.Serialize(new { nodeId = rootId, selector = "input" });
        using var result = await CallAsync("DOM.querySelectorAll", query, cancellationToken);
        var nodeIds = result.RootElement.GetProperty("nodeIds").EnumerateArray()
            .Select(node => node.GetInt32()).ToArray();
        var backendIds = new List<int>();
        var truncated = nodeIds.Length > MaxInputs;
        foreach (var nodeId in nodeIds.Take(MaxInputs))
        {
            var parameters = JsonSerializer.Serialize(new { nodeId });
            using var attributes = await CallAsync("DOM.getAttributes", parameters, cancellationToken);
            if (!IsSearchInput(attributes.RootElement.GetProperty("attributes")))
                continue;
            using var description = await CallAsync("DOM.describeNode", parameters, cancellationToken);
            var backendId = description.RootElement.GetProperty("node").GetProperty("backendNodeId").GetInt32();
            if (backendId <= 0 || backendIds.Contains(backendId))
                throw new JsonException("CDP returned an invalid or duplicate backend node identifier.");
            backendIds.Add(backendId);
        }

        if (backendIds.Count == 0)
            return (Array.Empty<int>(), truncated);

        cancellationToken.ThrowIfCancellationRequested();
        var snapshotJson = await _call("DOMSnapshot.captureSnapshot", CdpLayoutVisibility.SnapshotArguments)
            .WaitAsync(cancellationToken);
        var renderedNodes = CdpLayoutVisibility.FindRenderedNodes(snapshotJson, backendIds.ToHashSet());
        var visible = backendIds.Where(renderedNodes.Contains).ToArray();
        return (Array.AsReadOnly(visible.Take(MaxCandidates).ToArray()),
            truncated || visible.Length > MaxCandidates);
    }

    private async Task<JsonDocument> CallAsync(string method, string parameters, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var json = await _call(method, parameters).WaitAsync(cancellationToken);
        return JsonDocument.Parse(json);
    }

    private static bool IsSearchInput(JsonElement attributes)
    {
        var values = attributes.EnumerateArray().Select(value => value.GetString() ?? string.Empty).ToArray();
        if (values.Length % 2 != 0)
            throw new JsonException("CDP returned an invalid attribute list.");

        var type = string.Empty;
        var label = string.Empty;
        var placeholder = string.Empty;
        var name = string.Empty;
        var disabled = false;
        for (var index = 0; index < values.Length; index += 2)
        {
            switch (values[index].ToLowerInvariant())
            {
                case "type": type = values[index + 1]; break;
                case "aria-label": label = values[index + 1]; break;
                case "placeholder": placeholder = values[index + 1]; break;
                case "name": name = values[index + 1]; break;
                case "disabled": disabled = true; break;
                case "readonly": disabled = true; break;
                case "hidden": disabled = true; break;
                case "aria-hidden": disabled |= EqualsIgnoreCase(values[index + 1], "true"); break;
                case "aria-disabled": disabled |= EqualsIgnoreCase(values[index + 1], "true"); break;
            }
        }

        if (disabled || !(type.Length == 0 || EqualsIgnoreCase(type, "text") || EqualsIgnoreCase(type, "search")))
            return false;

        return EqualsIgnoreCase(type, "search")
            || EqualsIgnoreCase(label.Trim(), "search")
            || EqualsIgnoreCase(placeholder.Trim(), "search")
            || EqualsIgnoreCase(name.Trim(), "search");
    }

    private void EnsureSameDocument(long generation)
    {
        if (generation != _navigationGeneration())
            throw new InvalidOperationException("The tab navigated while search fields were being observed.");
    }

    private static bool EqualsIgnoreCase(string? left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
