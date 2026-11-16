using System.Text.Json;
using FluentAssertions;
using Jarvis.UI.Browser;
using Xunit;

namespace Jarvis.UI.Tests;

public sealed class CdpSearchObserverTests
{
    [Fact]
    public async Task Capture_Emits_Only_Indices_And_Revalidates_The_Same_Live_Node()
    {
        var dom = new FakeDom();
        dom.Attributes = ["type", "search", "aria-label", "Search", "value", "secret-value"];
        var observer = new CdpSearchObserver(dom.CallAsync, () => dom.Generation);

        var observation = await observer.CaptureAsync();
        var json = JsonSerializer.Serialize(observation);

        observation.Candidates.Should().ContainSingle().Which.Should().Be(0);
        json.Should().NotContain("secret");
        json.Should().NotContain("Search");
        (await observer.IsCurrentAsync(observation.ObservationId, 0)).Should().BeTrue();
    }

    [Fact]
    public async Task Revalidation_Rejects_Replaced_Node_Navigation_And_Unknown_Observation()
    {
        var dom = new FakeDom();
        var observer = new CdpSearchObserver(dom.CallAsync, () => dom.Generation);
        var observation = await observer.CaptureAsync();

        dom.BackendNodeId++;
        (await observer.IsCurrentAsync(observation.ObservationId, 0)).Should().BeFalse();
        dom.BackendNodeId--;
        dom.Generation++;
        (await observer.IsCurrentAsync(observation.ObservationId, 0)).Should().BeFalse();
        (await observer.IsCurrentAsync(Guid.NewGuid(), 0)).Should().BeFalse();
    }

    [Fact]
    public async Task Revalidation_Rejects_Removed_Or_Changed_Field()
    {
        var dom = new FakeDom();
        var observer = new CdpSearchObserver(dom.CallAsync, () => dom.Generation);
        var observation = await observer.CaptureAsync();

        dom.Attributes = ["type", "password", "aria-label", "Search"];
        (await observer.IsCurrentAsync(observation.ObservationId, 0)).Should().BeFalse();
        dom.Attributes = ["type", "search"];
        (await observer.IsCurrentAsync(observation.ObservationId, 0)).Should().BeFalse();
        observation = await observer.CaptureAsync();
        dom.HasNode = false;
        (await observer.IsCurrentAsync(observation.ObservationId, 0)).Should().BeFalse();
    }

    [Theory]
    [InlineData("hidden", "1", 100, 25, true)]
    [InlineData("visible", "0", 100, 25, true)]
    [InlineData("visible", "1", 0, 25, true)]
    [InlineData("visible", "1", 100, 0, true)]
    [InlineData("visible", "1", 100, 25, false)]
    public async Task Capture_Rejects_Fields_Without_Visible_Layout(
        string visibility, string opacity, int width, int height, bool hasLayout)
    {
        var dom = new FakeDom { Visibility = visibility, Opacity = opacity, Width = width, Height = height, HasLayout = hasLayout };
        var observer = new CdpSearchObserver(dom.CallAsync, () => dom.Generation);

        (await observer.CaptureAsync()).Candidates.Should().BeEmpty();
    }

    [Fact]
    public async Task Revalidation_Rejects_A_Field_That_Becomes_Hidden()
    {
        var dom = new FakeDom();
        var observer = new CdpSearchObserver(dom.CallAsync, () => dom.Generation);
        var observation = await observer.CaptureAsync();
        dom.Visibility = "hidden";

        (await observer.IsCurrentAsync(observation.ObservationId, 0)).Should().BeFalse();
    }

    [Fact]
    public async Task Capture_Rejects_A_Field_With_A_Transparent_Ancestor()
    {
        var dom = new FakeDom { ParentOpacity = "0" };
        var observer = new CdpSearchObserver(dom.CallAsync, () => dom.Generation);

        (await observer.CaptureAsync()).Candidates.Should().BeEmpty();
    }

    [Fact]
    public async Task New_Observation_And_Explicit_Invalidation_Reject_Old_References()
    {
        var dom = new FakeDom();
        var observer = new CdpSearchObserver(dom.CallAsync, () => dom.Generation);
        var first = await observer.CaptureAsync();
        var second = await observer.CaptureAsync();

        (await observer.IsCurrentAsync(first.ObservationId, 0)).Should().BeFalse();
        observer.Invalidate();
        (await observer.IsCurrentAsync(second.ObservationId, 0)).Should().BeFalse();
    }

    [Fact]
    public async Task Capture_Rejects_Disabled_Readonly_And_Non_Search_Inputs()
    {
        var dom = new FakeDom();
        var observer = new CdpSearchObserver(dom.CallAsync, () => dom.Generation);
        foreach (var attributes in new[]
        {
            new[] { "type", "search", "disabled", "" },
            new[] { "type", "search", "readonly", "" },
            new[] { "type", "search", "aria-disabled", "true" },
            new[] { "type", "search", "hidden", "" },
            new[] { "type", "search", "aria-hidden", "true" },
            new[] { "type", "password", "aria-label", "Search" },
            new[] { "type", "text", "aria-label", "Email" }
        })
        {
            dom.Attributes = attributes;
            (await observer.CaptureAsync()).Candidates.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Revalidation_Rejects_Navigation_During_The_Check()
    {
        var dom = new FakeDom();
        var observer = new CdpSearchObserver(dom.CallAsync, () => dom.Generation);
        var observation = await observer.CaptureAsync();
        dom.NavigateOnQuery = true;

        (await observer.IsCurrentAsync(observation.ObservationId, 0)).Should().BeFalse();
    }

    [Fact]
    public async Task Capture_Rejects_Invalidation_During_Cdp_Request()
    {
        var dom = new FakeDom { BlockQuery = true };
        var observer = new CdpSearchObserver(dom.CallAsync, () => dom.Generation);
        var capture = observer.CaptureAsync();
        await dom.QueryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        observer.Invalidate();
        dom.ReleaseQuery.TrySetResult();

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await capture);
    }

    [Fact]
    public async Task Revalidation_Rejects_Invalidation_During_Cdp_Request()
    {
        var dom = new FakeDom();
        var observer = new CdpSearchObserver(dom.CallAsync, () => dom.Generation);
        var observation = await observer.CaptureAsync();
        dom.BlockQuery = true;
        var checking = observer.IsCurrentAsync(observation.ObservationId, 0);
        await dom.QueryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        observer.Invalidate();
        dom.ReleaseQuery.TrySetResult();

        (await checking).Should().BeFalse();
    }

    [Fact]
    public async Task Revalidation_Failure_Invalidates_Observation()
    {
        var dom = new FakeDom();
        var observer = new CdpSearchObserver(dom.CallAsync, () => dom.Generation);
        var observation = await observer.CaptureAsync();
        dom.FailQuery = true;

        await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await observer.IsCurrentAsync(observation.ObservationId, 0));
        dom.FailQuery = false;
        (await observer.IsCurrentAsync(observation.ObservationId, 0)).Should().BeFalse();
    }

    [Fact]
    public async Task Revalidation_Cancellation_Invalidates_Observation()
    {
        var dom = new FakeDom();
        var observer = new CdpSearchObserver(dom.CallAsync, () => dom.Generation);
        var observation = await observer.CaptureAsync();
        dom.BlockQuery = true;
        using var cancellation = new CancellationTokenSource();
        var checking = observer.IsCurrentAsync(observation.ObservationId, 0, cancellation.Token);
        await dom.QueryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await checking);
        dom.ReleaseQuery.TrySetResult();
        dom.BlockQuery = false;
        (await observer.IsCurrentAsync(observation.ObservationId, 0)).Should().BeFalse();
    }

    [Fact]
    public async Task Capture_Fails_If_Navigation_Occurs_During_Observation()
    {
        var dom = new FakeDom { NavigateOnQuery = true };
        var observer = new CdpSearchObserver(dom.CallAsync, () => dom.Generation);

        var act = () => observer.CaptureAsync();

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Capture_Respects_Cancellation()
    {
        var dom = new FakeDom();
        var observer = new CdpSearchObserver(dom.CallAsync, () => dom.Generation);
        await observer.CaptureAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = () => observer.CaptureAsync(cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class FakeDom
    {
        public long Generation { get; set; }
        public int BackendNodeId { get; set; } = 42;
        public bool HasNode { get; set; } = true;
        public bool NavigateOnQuery { get; set; }
        public bool BlockQuery { get; set; }
        public bool FailQuery { get; set; }
        public string Visibility { get; set; } = "visible";
        public string Opacity { get; set; } = "1";
        public string ParentOpacity { get; set; } = "1";
        public int Width { get; set; } = 100;
        public int Height { get; set; } = 25;
        public bool HasLayout { get; set; } = true;
        public TaskCompletionSource QueryStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseQuery { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string[] Attributes { get; set; } = ["type", "search"];

        public async Task<string> CallAsync(string method, string parameters)
        {
            if (method == "DOM.querySelectorAll" && NavigateOnQuery)
                Generation++;
            if (method == "DOM.querySelectorAll" && FailQuery)
                throw new InvalidOperationException("CDP unavailable");
            if (method == "DOM.querySelectorAll" && BlockQuery)
            {
                QueryStarted.TrySetResult();
                await ReleaseQuery.Task;
            }
            return method switch
            {
                "DOM.getDocument" => """{"root":{"nodeId":1}}""",
                "DOM.querySelectorAll" => HasNode ? """{"nodeIds":[2]}""" : """{"nodeIds":[]}""",
                "DOM.getAttributes" => JsonSerializer.Serialize(new { attributes = Attributes }),
                "DOM.describeNode" => JsonSerializer.Serialize(new { node = new { backendNodeId = BackendNodeId } }),
                "DOMSnapshot.captureSnapshot" => JsonSerializer.Serialize(new
                {
                    strings = new[] { Visibility, Opacity, "visible", ParentOpacity },
                    documents = new[]
                    {
                        new
                        {
                            nodes = new
                            {
                                backendNodeId = new[] { 10, 11, BackendNodeId },
                                parentIndex = new[] { -1, 0, 1 },
                                nodeType = new[] { 9, 1, 1 }
                            },
                            layout = new
                            {
                                nodeIndex = HasLayout ? new[] { 1, 2 } : new[] { 1 },
                                styles = HasLayout ? new[] { new[] { 2, 3 }, new[] { 0, 1 } } : new[] { new[] { 2, 3 } },
                                bounds = HasLayout ? new[] { new[] { 0, 0, 200, 200 }, new[] { 0, 0, Width, Height } } : new[] { new[] { 0, 0, 200, 200 } }
                            }
                        }
                    }
                }),
                _ => throw new InvalidOperationException($"Unexpected CDP method {method}.")
            };
        }
    }
}
