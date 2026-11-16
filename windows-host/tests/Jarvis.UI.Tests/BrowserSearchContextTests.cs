using System.Text.Json;
using FluentAssertions;
using Jarvis.Browser.Core;
using Xunit;

namespace Jarvis.UI.Tests;

public sealed class BrowserSearchContextTests
{
    [Fact]
    public void Create_Exposes_Only_Bounded_Search_Field_Metadata()
    {
        var snapshot = CreateSnapshot(
            new BrowserElementObservation("textbox", "Search", "secret-page-text", true, true,
                new Dictionary<string, string> { ["placeholder"] = "Search", ["value"] = "secret-token" }),
            new BrowserElementObservation("textbox", "Password", "secret-password", true, true,
                new Dictionary<string, string> { ["type"] = "password" }),
            new BrowserElementObservation("textbox", "Search", null, false, true,
                new Dictionary<string, string>()),
            new BrowserElementObservation("textbox", "Search", null, true, false,
                new Dictionary<string, string>()));

        var context = BrowserSearchContext.Create(snapshot);
        var json = JsonSerializer.Serialize(context);

        context.SearchFields.Should().ContainSingle().Which.ElementIndex.Should().Be(0);
        json.Should().NotContain("secret");
        json.Should().NotContain("example.com");
        json.Should().NotContain("Password");
    }

    [Fact]
    public void Create_Recognizes_Search_Inputs_But_Rejects_Misleading_And_Sensitive_Types()
    {
        var snapshot = CreateSnapshot(
            new BrowserElementObservation("textbox", "anything", null, true, true,
                new Dictionary<string, string> { ["type"] = "search" }),
            new BrowserElementObservation("button", "Search", null, true, true,
                new Dictionary<string, string>()),
            new BrowserElementObservation("textbox", "Search password", null, true, true,
                new Dictionary<string, string>()),
            new BrowserElementObservation("textbox", "Search", null, true, true,
                new Dictionary<string, string> { ["type"] = "password" }));

        BrowserSearchContext.Create(snapshot).SearchFields
            .Select(field => field.ElementIndex).Should().Equal(0);
    }

    [Fact]
    public void Create_Does_Not_Offer_Fields_For_Inactive_Or_Loading_Tabs()
    {
        var snapshot = CreateSnapshot(
            new BrowserElementObservation("textbox", "Search", null, true, true,
                new Dictionary<string, string>()));

        BrowserSearchContext.Create(snapshot with { IsActive = false }).SearchFields.Should().BeEmpty();
        BrowserSearchContext.Create(snapshot with { IsLoading = true }).SearchFields.Should().BeEmpty();
    }

    [Fact]
    public void Create_Bounds_The_Number_Of_Candidates()
    {
        var elements = Enumerable.Range(0, 30)
            .Select(_ => new BrowserElementObservation("textbox", "Search", null, true, true,
                new Dictionary<string, string>()))
            .ToArray();

        BrowserSearchContext.Create(CreateSnapshot(elements)).SearchFields.Should().HaveCount(20);
    }

    private static PageSnapshot CreateSnapshot(params BrowserElementObservation[] elements)
    {
        var state = new BrowserTabState(
            BrowserTabId.New(), new Uri("https://example.com/?token=secret"), "secret-title",
            false, false, false);
        return BrowserObservationMapper.Create(state, true,
        [
            new BrowserFrameObservation("main", state.Address, "secret-title", "secret-name",
                true, "document", 0, "secret-page-text", elements)
        ]);
    }
}
