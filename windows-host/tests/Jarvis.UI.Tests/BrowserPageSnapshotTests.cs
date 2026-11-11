using FluentAssertions;
using Jarvis.Browser.Core;
using Xunit;

namespace Jarvis.UI.Tests;

public sealed class BrowserPageSnapshotTests
{
    [Fact]
    public void Create_Assigns_Observation_And_Tab_Scoped_Element_References()
    {
        var state = new BrowserTabState(
            BrowserTabId.New(),
            new Uri("https://example.com/"),
            "Example",
            false,
            false,
            false);
        var frames = new[]
        {
            new BrowserFrameObservation(
                "main",
                state.Address,
                "Example",
                string.Empty,
                IsMainFrame: true,
                DocumentToken: "main-document",
                DomVersion: 3,
                "Main page text",
                [new BrowserElementObservation(
                    "link",
                    "Details",
                    "Details",
                    true,
                    true,
                    new Dictionary<string, string>())]),
            new BrowserFrameObservation(
                "frame-a",
                new Uri("https://example.com/frame"),
                "Frame",
                "content",
                IsMainFrame: false,
                DocumentToken: "frame-document",
                DomVersion: 1,
                "Frame text",
                [new BrowserElementObservation(
                    "button",
                    "Continue",
                    "Continue",
                    true,
                    true,
                    new Dictionary<string, string>())])
        };

        var snapshot = BrowserObservationMapper.Create(state, isActive: true, frames);

        snapshot.TabId.Should().Be(state.TabId);
        snapshot.IsActive.Should().BeTrue();
        snapshot.Frames.Should().HaveCount(2);
        snapshot.Elements.Should().HaveCount(2);
        snapshot.Elements.Select(element => element.ElementId).Should().OnlyHaveUniqueItems();
        snapshot.Elements.Should().OnlyContain(element =>
            element.TabId == snapshot.TabId && element.ObservationId == snapshot.ObservationId);
        snapshot.Elements.Select(element => element.FrameId).Should().BeEquivalentTo("main", "frame-a");
        snapshot.ContainsReference(snapshot.Elements[0]).Should().BeTrue();
    }

    [Fact]
    public void Create_Uses_A_New_Observation_Id_For_Each_Capture()
    {
        var state = new BrowserTabState(
            BrowserTabId.New(),
            new Uri("https://example.com/"),
            "Example",
            false,
            false,
            false);
        var frames = new[]
        {
            new BrowserFrameObservation(
                "main",
                state.Address,
                "Example",
                string.Empty,
                IsMainFrame: true,
                DocumentToken: "document",
                DomVersion: 0,
                "Page text",
                [])
        };

        var first = BrowserObservationMapper.Create(state, isActive: false, frames);
        var second = BrowserObservationMapper.Create(state, isActive: false, frames);

        first.ObservationId.Should().NotBe(second.ObservationId);
    }

    [Fact]
    public void Create_Requires_Exactly_One_Main_Frame()
    {
        var state = new BrowserTabState(
            BrowserTabId.New(),
            null,
            "New tab",
            false,
            false,
            false);

        var act = () => BrowserObservationMapper.Create(state, isActive: true, []);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_Reports_Truncation_And_Rejects_References_From_Another_Observation()
    {
        var state = new BrowserTabState(
            BrowserTabId.New(),
            new Uri("https://example.com/"),
            "Example",
            false,
            false,
            false);
        var elements = Enumerable.Range(0, BrowserObservationMapper.MaximumElements + 1)
            .Select(index => new BrowserElementObservation(
                "button", $"Button {index}", null, true, true, new Dictionary<string, string>()))
            .ToArray();
        var frames = new[]
        {
            new BrowserFrameObservation(
                "main", state.Address, "Example", string.Empty, true, "document", 0,
                new string('x', BrowserObservationMapper.MaximumVisibleTextLength + 1),
                elements)
        };

        var snapshot = BrowserObservationMapper.Create(state, isActive: true, frames);
        var other = BrowserObservationMapper.Create(state, isActive: true, frames);

        snapshot.Elements.Should().HaveCount(BrowserObservationMapper.MaximumElements);
        snapshot.VisibleText.Should().HaveLength(BrowserObservationMapper.MaximumVisibleTextLength);
        snapshot.IsTruncated.Should().BeTrue();
        snapshot.ContainsReference(other.Elements[0]).Should().BeFalse();
    }

    [Fact]
    public void Create_Defensively_Copies_Element_Attributes()
    {
        var state = new BrowserTabState(BrowserTabId.New(), null, "Example", false, false, false);
        var attributes = new Dictionary<string, string> { ["role"] = "button" };
        var frames = new[]
        {
            new BrowserFrameObservation(
                "main", null, "Example", string.Empty, true, "document", 0, string.Empty,
                [new BrowserElementObservation("button", "Save", null, true, true, attributes)])
        };

        var snapshot = BrowserObservationMapper.Create(state, isActive: true, frames);
        attributes["role"] = "link";

        snapshot.Elements[0].Attributes["role"].Should().Be("button");
    }
}
