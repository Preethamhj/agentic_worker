using System.Text.Json;
using FluentAssertions;
using Jarvis.Browser.Core;
using Jarvis.UI.Browser;
using Xunit;

namespace Jarvis.UI.Tests;

public sealed class CdpLinkObserverTests
{
    [Fact]
    public async Task Capture_And_Revalidate_Same_Node_After_Unrelated_Change()
    {
        var dom = new FakeDom();
        var observer = new CdpLinkObserver(dom.CallAsync, dom.ObserveAsync, () => dom.Generation);
        var captured = await observer.CaptureAsync();
        var disclosure = BrowserPageDisclosure.Create(captured.Observation);
        var selected = disclosure.Links.Single();
        dom.Version++;
        dom.PrependUnrelated = true;

        (await observer.IsCurrentAsync(captured, disclosure, selected)).Should().BeTrue();
    }

    [Fact]
    public async Task Revalidation_Rejects_Replacement_Target_Change_And_Navigation()
    {
        var dom = new FakeDom();
        var observer = new CdpLinkObserver(dom.CallAsync, dom.ObserveAsync, () => dom.Generation);
        var captured = await observer.CaptureAsync();
        var disclosure = BrowserPageDisclosure.Create(captured.Observation);
        var selected = disclosure.Links.Single();
        dom.BackendId = 99;
        (await observer.IsCurrentAsync(captured, disclosure, selected)).Should().BeFalse();
        dom.BackendId = 12;
        dom.Href = "https://example.test/other";
        (await observer.IsCurrentAsync(captured, disclosure, selected)).Should().BeFalse();
        dom.Href = "https://example.test/mobiles";
        dom.Generation++;
        (await observer.IsCurrentAsync(captured, disclosure, selected)).Should().BeFalse();
    }

    [Fact]
    public async Task Capture_Rejects_Page_Changes_During_Binding()
    {
        var dom = new FakeDom { MutateDuringCapture = true };
        var observer = new CdpLinkObserver(dom.CallAsync, dom.ObserveAsync, () => dom.Generation);

        var action = async () => await observer.CaptureAsync();
        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Retry_Can_Bind_After_A_Transient_Unrelated_Mutation()
    {
        var dom = new FakeDom { MutateDuringCapture = true };
        var observer = new CdpLinkObserver(dom.CallAsync, dom.ObserveAsync, () => dom.Generation);

        var captured = await observer.CaptureWithRetryAsync();
        BrowserPageDisclosure.Create(captured.Observation).Links.Should().ContainSingle();
    }

    [Fact]
    public async Task Retry_Fails_Closed_When_Page_Never_Stabilizes()
    {
        var dom = new FakeDom { AlwaysMutate = true };
        var observer = new CdpLinkObserver(dom.CallAsync, dom.ObserveAsync, () => dom.Generation);

        var action = async () => await observer.CaptureWithRetryAsync();
        await action.Should().ThrowAsync<CdpLinkCaptureException>();
    }

    [Fact]
    public async Task Retry_Does_Not_Cross_Navigation()
    {
        var dom = new FakeDom { NavigateDuringCapture = true };
        var observer = new CdpLinkObserver(dom.CallAsync, dom.ObserveAsync, () => dom.Generation);

        var action = async () => await observer.CaptureWithRetryAsync();
        await action.Should().ThrowAsync<CdpLinkCaptureException>();
        dom.QueryCalls.Should().Be(2);
    }

    [Fact]
    public async Task Capture_Rejects_Mutation_During_Final_Node_Read()
    {
        var dom = new FakeDom { MutateDuringFinalRead = true };
        var observer = new CdpLinkObserver(dom.CallAsync, dom.ObserveAsync, () => dom.Generation);
        var action = async () => await observer.CaptureAsync();
        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Revalidation_Rejects_Hidden_Disabled_Or_Relabelled_Link()
    {
        var dom = new FakeDom();
        var observer = new CdpLinkObserver(dom.CallAsync, dom.ObserveAsync, () => dom.Generation);
        var captured = await observer.CaptureAsync();
        var disclosure = BrowserPageDisclosure.Create(captured.Observation);
        var link = disclosure.Links.Single();

        dom.Visible = false;
        (await observer.IsCurrentAsync(captured, disclosure, link)).Should().BeFalse();
        dom.Visible = true;
        dom.Enabled = false;
        (await observer.IsCurrentAsync(captured, disclosure, link)).Should().BeFalse();
        dom.Enabled = true;
        dom.Name = "Different";
        (await observer.IsCurrentAsync(captured, disclosure, link)).Should().BeFalse();
    }

    [Fact]
    public async Task Capture_Honors_Cancellation()
    {
        var dom = new FakeDom();
        var observer = new CdpLinkObserver(dom.CallAsync, dom.ObserveAsync, () => dom.Generation);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var action = async () => await observer.CaptureAsync(cancellation.Token);
        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Revalidation_Rejects_Removed_Link_And_Changed_Document()
    {
        var dom = new FakeDom();
        var observer = new CdpLinkObserver(dom.CallAsync, dom.ObserveAsync, () => dom.Generation);
        var captured = await observer.CaptureAsync();
        var disclosure = BrowserPageDisclosure.Create(captured.Observation);
        var link = disclosure.Links.Single();
        dom.Removed = true;
        (await observer.IsCurrentAsync(captured, disclosure, link)).Should().BeFalse();
        dom.Removed = false;
        dom.DocumentToken = "another-document";
        (await observer.IsCurrentAsync(captured, disclosure, link)).Should().BeFalse();
    }

    [Fact]
    public async Task Capture_Rejects_Duplicate_Backend_Node_Identities()
    {
        var dom = new FakeDom { PrependUnrelated = true, BackendId = 22 };
        var observer = new CdpLinkObserver(dom.CallAsync, dom.ObserveAsync, () => dom.Generation);
        var action = async () => await observer.CaptureAsync();
        await action.Should().ThrowAsync<InvalidOperationException>();
    }

    private sealed class FakeDom
    {
        private readonly BrowserTabId _tabId = BrowserTabId.New();
        public long Version { get; set; }
        public long Generation { get; set; }
        public int BackendId { get; set; } = 12;
        public string Href { get; set; } = "https://example.test/mobiles";
        public bool PrependUnrelated { get; set; }
        public bool MutateDuringCapture { get; set; }
        public bool MutateDuringFinalRead { get; set; }
        public bool AlwaysMutate { get; set; }
        public bool NavigateDuringCapture { get; set; }
        public bool Visible { get; set; } = true;
        public bool Enabled { get; set; } = true;
        public string Name { get; set; } = "Mobiles";
        public string DocumentToken { get; set; } = "document";
        public bool Removed { get; set; }
        private int _calls;
        public int QueryCalls => _calls;

        public Task<string> CallAsync(string method, string arguments)
        {
            if (method == "DOM.getDocument")
            {
                var children = new List<object>();
                if (PrependUnrelated)
                    children.Add(new { nodeId = 2, backendNodeId = 22 });
                if (!Removed)
                    children.Add(new { nodeId = 3, backendNodeId = BackendId });
                return Task.FromResult(JsonSerializer.Serialize(new
                {
                    root = new { nodeId = 1, backendNodeId = 1, children }
                }));
            }
            if (method == "DOM.querySelectorAll")
            {
                _calls++;
                if (NavigateDuringCapture && _calls == 1)
                    Generation++;
                if (AlwaysMutate || (MutateDuringCapture && _calls == 1) || (MutateDuringFinalRead && _calls == 2))
                    Version++;
                return Task.FromResult(JsonSerializer.Serialize(new
                {
                    nodeIds = Removed ? (PrependUnrelated ? new[] { 2 } : Array.Empty<int>())
                        : (PrependUnrelated ? new[] { 2, 3 } : new[] { 3 })
                }));
            }
            throw new InvalidOperationException($"Unexpected CDP method: {method}");
        }

        public Task<ObservationResult> ObserveAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var elements = new List<BrowserElementObservation>();
            if (PrependUnrelated)
                elements.Add(new BrowserElementObservation("button", "Other", null, true, true, new Dictionary<string, string>()));
            if (!Removed)
                elements.Add(new BrowserElementObservation(
                    "link", Name, null, Visible, Enabled, new Dictionary<string, string> { ["href"] = Href }));
            var state = new BrowserTabState(_tabId, new Uri("https://example.test/"), "Shop", false, false, false);
            var snapshot = BrowserObservationMapper.Create(state, true,
                [new BrowserFrameObservation("main", state.Address, "Shop", "", true, DocumentToken, Version, "Shop", elements)]);
            return Task.FromResult(new ObservationResult(snapshot, []));
        }
    }
}
