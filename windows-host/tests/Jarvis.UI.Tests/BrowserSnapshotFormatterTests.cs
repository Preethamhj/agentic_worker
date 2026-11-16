using FluentAssertions;
using Jarvis.Browser.Core;
using Jarvis.UI.Browser;
using Xunit;

namespace Jarvis.UI.Tests;

public sealed class BrowserSnapshotFormatterTests
{
    [Fact]
    public void Format_Includes_Page_Elements_And_Partial_Capture_Warnings()
    {
        var state = new BrowserTabState(
            BrowserTabId.New(), new Uri("https://example.com/"), "Example", false, false, false);
        var frame = new BrowserFrameObservation(
            "main", state.Address, "Example", "", true, "document", 0, "Example page text",
            [new BrowserElementObservation(
                "button", "Continue", "Next step", true, false, new Dictionary<string, string> { ["href"] = "https://private.example/path?token=secret" })],
            IsTruncated: true);
        var snapshot = BrowserObservationMapper.Create(state, true, [frame]);
        var result = new ObservationResult(snapshot, [new ObservationIssue("frame-1", "FrameUnavailable")]);

        var formatted = BrowserSnapshotFormatter.Format(result);

        formatted.Should().Contain("Example page text");
        formatted.Should().Contain("[e0] button: Continue");
        formatted.Should().Contain("enabled: False");
        formatted.Should().Contain("Next step");
        formatted.Should().Contain("snapshot was truncated");
        formatted.Should().Contain("FrameUnavailable");
        formatted.Should().Contain("not live");
        formatted.Should().NotContain("private.example");
        formatted.Should().NotContain("token=secret");
    }

    [Fact]
    public void Format_Rejects_Null_Observation()
    {
        var action = () => BrowserSnapshotFormatter.Format(null!);

        action.Should().Throw<ArgumentNullException>();
    }
}
