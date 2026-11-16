using FluentAssertions;
using Jarvis.Browser.Core;
using Xunit;

namespace Jarvis.UI.Tests;

public sealed class BrowserPageDisclosureTests
{
    [Fact]
    public void Create_Exposes_Visible_Link_Ids_And_Labels_But_Not_Targets()
    {
        var disclosure = BrowserPageDisclosure.Create(CreateObservation(
            "Useful page at https://private.example/path?page=1",
            [
                Link("Visible private label", "https://private.example/next?page=2", isVisible: true, isEnabled: true),
                Link("Restricted target", "https://private.example/next?session=hidden", isVisible: true, isEnabled: true),
                Link("Hidden link", "https://hidden.example/", isVisible: false, isEnabled: true),
                Link("Disabled link", "https://disabled.example/", isVisible: true, isEnabled: false),
                Link("Unsafe link", "javascript:alert(1)", isVisible: true, isEnabled: true)
            ]));

        disclosure.Links.Should().ContainSingle();
        disclosure.Links[0].Id.Should().StartWith("l");
        disclosure.Links[0].Label.Should().Be("Visible private label");
        disclosure.Links[0].Target.AbsoluteUri.Should().Be("https://private.example/next?page=2");
        disclosure.VisibleText.Should().NotContain("private.example");
        var preview = disclosure.FormatPreview();
        preview.Should().Contain(disclosure.Links[0].Id);
        preview.Should().Contain("Visible private label");
        preview.Should().NotContain("private.example");
        preview.Should().NotContain("session=hidden");
        preview.Should().NotContain("href");
    }

    [Fact]
    public void Create_Omits_Empty_And_Whitespace_Labels_From_Preview_And_Actionable_Links()
    {
        var disclosure = BrowserPageDisclosure.Create(CreateObservation(
            "Page",
            [
                Link("", "https://example.test/empty", isVisible: true, isEnabled: true),
                Link("  \t ", "https://example.test/blank", isVisible: true, isEnabled: true),
                Link("Mobiles", "https://example.test/mobiles", isVisible: true, isEnabled: true)
            ]));

        disclosure.Links.Should().ContainSingle()
            .Which.Label.Should().Be("Mobiles");
        disclosure.FormatPreview().Should().Contain("Mobiles").And.NotContain("example.test");
    }

    [Fact]
    public void Create_Bounds_Labels_And_Title_After_Redaction()
    {
        var original = CreateObservation(
            "Page",
            [Link(new string('a', 490) + " x.co", "https://example.test/link", true, true)]);
        var observation = original with
        {
            Snapshot = original.Snapshot with { Title = new string('a', 490) + " x.co" }
        };

        var disclosure = BrowserPageDisclosure.Create(observation);

        disclosure.Title.Length.Should().BeLessThanOrEqualTo(500);
        disclosure.Links.Single().Label.Length.Should().BeLessThanOrEqualTo(500);
        disclosure.IsLinkCurrent(observation, disclosure.Links.Single()).Should().BeTrue();
    }

    [Fact]
    public void IsLinkCurrent_Rejects_Changed_Raw_Label_Even_When_Redaction_Is_The_Same()
    {
        var target = "https://example.test/link";
        var initial = CreateObservation("Page", [Link("Visit first.example", target, true, true)]);
        var disclosure = BrowserPageDisclosure.Create(initial);
        var changed = CreateObservation(
            "Page",
            [Link("Visit second.example", target, true, true)],
            tabId: disclosure.TabId);

        disclosure.Links.Single().Label.Should().Be("Visit [URL REDACTED]");
        disclosure.IsLinkCurrent(initial, disclosure.Links.Single()).Should().BeTrue();
        disclosure.IsLinkCurrent(changed, disclosure.Links.Single()).Should().BeFalse();
    }

    [Fact]
    public void Create_Blocks_Credential_Like_Page_Content()
    {
        var observation = CreateObservation("Sign in password=do-not-send", []);

        var act = () => BrowserPageDisclosure.Create(observation);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*credential content*");
    }

    [Fact]
    public void Create_Blocks_Opaque_Token_Like_Page_Content()
    {
        var observation = CreateObservation("Reference: a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6", []);

        var act = () => BrowserPageDisclosure.Create(observation);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*credential content*");
    }

    [Fact]
    public void Create_Discloses_Truncated_Observations_With_A_Preview_Warning()
    {
        var observation = CreateObservation("Page", [], isTruncated: true);

        var disclosure = BrowserPageDisclosure.Create(observation);

        disclosure.Warnings.Should().ContainSingle()
            .Which.Should().Contain("truncated");
        disclosure.FormatPreview().Should().Contain("only part of the page");
    }

    [Fact]
    public void Create_Allows_Partial_Child_Frame_Capture_With_Preview_Warning()
    {
        var observation = CreateObservation(
            "Main frame text",
            [],
            issues: [new ObservationIssue("frame-1", "FrameUnavailable")]);

        var disclosure = BrowserPageDisclosure.Create(observation);

        disclosure.VisibleText.Should().Be("Main frame text");
        disclosure.Warnings.Should().ContainSingle()
            .Which.Should().Contain("Some page frames");
        disclosure.FormatPreview().Should().Contain("not included");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Create_Still_Rejects_Inactive_Or_Loading_Pages(bool isActive, bool isLoading)
    {
        var observation = CreateObservation("Page", [], isActive: isActive, isLoading: isLoading);

        var act = () => BrowserPageDisclosure.Create(observation);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void IsLinkCurrent_Rejects_Stale_Dom_And_Changed_Targets()
    {
        var link = Link("Pricing", "https://example.test/pricing", isVisible: true, isEnabled: true);
        var initial = CreateObservation("Page", [link]);
        var disclosure = BrowserPageDisclosure.Create(initial);
        var selected = disclosure.Links.Single();
        var current = CreateObservation("Page", [link], tabId: disclosure.TabId);
        var changedVersion = CreateObservation("Page", [link], domVersion: 2, tabId: disclosure.TabId);
        var changedTarget = CreateObservation(
            "Page",
            [Link("Pricing", "https://example.test/other", isVisible: true, isEnabled: true)],
            tabId: disclosure.TabId);

        selected.ElementId.Should().Be(current.Snapshot.Elements[0].ElementId);
        current.Snapshot.TabId.Should().Be(disclosure.TabId);
        current.Snapshot.Frames.Single().DocumentToken.Should().Be(selected.DocumentToken);
        current.Snapshot.Frames.Single().DomVersion.Should().Be(selected.DomVersion);
        current.Snapshot.Elements.Single().Attributes["href"].Should().Be(selected.Target.AbsoluteUri);
        disclosure.IsLinkCurrent(current, selected).Should().BeTrue();
        disclosure.IsLinkCurrent(changedVersion, selected).Should().BeFalse();
        disclosure.IsLinkCurrent(changedTarget, selected).Should().BeFalse();
    }

    [Fact]
    public void LinkFreshness_Identifies_Page_Mutation_And_Target_Changes_Without_Exposing_Urls()
    {
        var link = Link("Mobiles", "https://example.test/mobiles", true, true);
        var initial = CreateObservation("Page", [link]);
        var disclosure = BrowserPageDisclosure.Create(initial);
        var selected = disclosure.Links.Single();
        var changedVersion = CreateObservation("Page", [link], domVersion: 2, tabId: disclosure.TabId);
        var changedTarget = CreateObservation(
            "Page",
            [Link("Mobiles", "https://example.test/other", true, true)],
            tabId: disclosure.TabId);

        disclosure.CheckLinkFreshness(initial, selected).Should().Be(BrowserLinkFreshness.Current);
        disclosure.CheckLinkFreshness(changedVersion, selected).Should().Be(BrowserLinkFreshness.PageChanged);
        disclosure.CheckLinkFreshness(changedTarget, selected).Should().Be(BrowserLinkFreshness.TargetChanged);
    }

    [Fact]
    public void LinkFreshness_Identifies_Removed_And_Hidden_Links()
    {
        var link = Link("Mobiles", "https://example.test/mobiles", true, true);
        var disclosure = BrowserPageDisclosure.Create(CreateObservation("Page", [link]));
        var selected = disclosure.Links.Single();
        var removed = CreateObservation("Page", [], tabId: disclosure.TabId);
        var hidden = CreateObservation(
            "Page",
            [Link("Mobiles", "https://example.test/mobiles", false, true)],
            tabId: disclosure.TabId);

        disclosure.CheckLinkFreshness(removed, selected).Should().Be(BrowserLinkFreshness.TargetMissing);
        disclosure.CheckLinkFreshness(hidden, selected).Should().Be(BrowserLinkFreshness.TargetUnavailable);
    }

    [Fact]
    public void IsPageCurrent_Allows_A_Partial_Capture_When_Main_Document_Is_Unchanged()
    {
        var link = Link("Pricing", "https://example.test/pricing", isVisible: true, isEnabled: true);
        var initial = CreateObservation("Page", [link]);
        var disclosure = BrowserPageDisclosure.Create(initial);
        var partial = CreateObservation(
            "Page",
            [link],
            isTruncated: true,
            tabId: disclosure.TabId,
            issues: [new ObservationIssue("frame-1", "FrameUnavailable")]);

        disclosure.IsPageCurrent(partial).Should().BeTrue();
        disclosure.IsLinkCurrent(partial, disclosure.Links.Single()).Should().BeTrue();
    }

    [Fact]
    public void IsAmbiguous_Requires_Clarification_For_Same_Label_With_Different_Targets()
    {
        var disclosure = BrowserPageDisclosure.Create(CreateObservation(
            "Page",
            [
                Link("Continue", "https://example.test/first", isVisible: true, isEnabled: true),
                Link("Continue", "https://example.test/second", isVisible: true, isEnabled: true)
            ]));

        disclosure.Links.All(disclosure.IsAmbiguous).Should().BeTrue();
    }

    private static ObservationResult CreateObservation(
        string text,
        IReadOnlyList<BrowserElementObservation> elements,
        bool isTruncated = false,
        long domVersion = 1,
        BrowserTabId? tabId = null,
        IReadOnlyList<ObservationIssue>? issues = null,
        bool isActive = true,
        bool isLoading = false)
    {
        var state = new BrowserTabState(
            tabId ?? BrowserTabId.New(),
            new Uri("https://example.test/"),
            "Example",
            isLoading,
            false,
            false);
        var snapshot = BrowserObservationMapper.Create(
            state,
            isActive,
            [
                new BrowserFrameObservation(
                    "main",
                    state.Address,
                    "Example",
                    string.Empty,
                    true,
                    "document",
                    domVersion,
                    text,
                    elements,
                    isTruncated)
            ]);
        return new ObservationResult(snapshot, issues ?? []);
    }

    private static BrowserElementObservation Link(
        string label,
        string target,
        bool isVisible,
        bool isEnabled) =>
        new(
            "link",
            label,
            label,
            isVisible,
            isEnabled,
            new Dictionary<string, string> { ["href"] = target });
}
