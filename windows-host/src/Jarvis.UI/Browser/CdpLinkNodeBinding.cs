namespace Jarvis.UI.Browser;

internal sealed class CdpLinkNodeBinding
{
    private readonly string _documentToken;
    private readonly int _backendNodeId;

    private CdpLinkNodeBinding(string documentToken, int backendNodeId)
    {
        _documentToken = documentToken;
        _backendNodeId = backendNodeId;
    }

    public static CdpLinkNodeBinding Capture(
        string documentToken,
        long beforeVersion,
        long afterVersion,
        IReadOnlyList<int> beforeNodes,
        IReadOnlyList<int> afterNodes,
        int position)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentToken);
        ArgumentNullException.ThrowIfNull(beforeNodes);
        ArgumentNullException.ThrowIfNull(afterNodes);
        if (beforeVersion != afterVersion
            || !beforeNodes.SequenceEqual(afterNodes)
            || position < 0
            || position >= beforeNodes.Count
            || beforeNodes[position] <= 0
            || beforeNodes.Count(id => id == beforeNodes[position]) != 1)
            throw new InvalidOperationException("The page changed while the link identity was captured.");

        return new CdpLinkNodeBinding(documentToken, beforeNodes[position]);
    }

    public int? FindCurrentPosition(string documentToken, IReadOnlyList<int> currentNodes)
    {
        ArgumentNullException.ThrowIfNull(currentNodes);
        if (!string.Equals(documentToken, _documentToken, StringComparison.Ordinal))
            return null;

        int? position = null;
        for (var index = 0; index < currentNodes.Count; index++)
        {
            if (currentNodes[index] != _backendNodeId)
                continue;
            if (position is not null)
                return null;
            position = index;
        }
        return position;
    }
}
