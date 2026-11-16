using System.Text.Json;
using FluentAssertions;
using Jarvis.UI.Browser;
using Xunit;

namespace Jarvis.UI.Tests;

public sealed class CdpLayoutVisibilityTests
{
    [Fact]
    public void Oversized_Snapshot_Is_Rejected()
    {
        var act = () => CdpLayoutVisibility.FindRenderedNodes(new string('x', 12_000_001), new HashSet<int>());

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Malformed_Layout_Columns_Are_Rejected()
    {
        const string snapshot = """
            {"strings":[],"documents":[{"nodes":{"backendNodeId":[1],"parentIndex":[-1],"nodeType":[9]},
            "layout":{"nodeIndex":[0],"styles":[],"bounds":[]}}]}
            """;

        var act = () => CdpLayoutVisibility.FindRenderedNodes(snapshot, new HashSet<int>());

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Repeated_Layout_Fragments_For_The_Same_Node_Are_Combined()
    {
        const string snapshot = """
            {"strings":["visible","1"],"documents":[{"nodes":{"backendNodeId":[1,2],"parentIndex":[-1,0],"nodeType":[9,1]},
            "layout":{"nodeIndex":[1,1],"styles":[[0,1],[0,1]],"bounds":[[0,0,0,0],[0,0,20,10]]}}]}
            """;

        CdpLayoutVisibility.FindRenderedNodes(snapshot, new HashSet<int> { 2 }).Should().BeEquivalentTo(new[] { 2 });
    }

    [Fact]
    public void Repeated_Layout_Fragments_With_Inconsistent_Visibility_Are_Rejected_As_Not_Visible()
    {
        const string snapshot = """
            {"strings":["visible","1","hidden"],"documents":[{"nodes":{"backendNodeId":[1,2],"parentIndex":[-1,0],"nodeType":[9,1]},
            "layout":{"nodeIndex":[1,1],"styles":[[0,1],[2,1]],"bounds":[[0,0,20,10],[0,0,20,10]]}}]}
            """;

        CdpLayoutVisibility.FindRenderedNodes(snapshot, new HashSet<int> { 2 }).Should().BeEmpty();
    }
}
