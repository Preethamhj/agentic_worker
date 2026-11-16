using FluentAssertions;
using Jarvis.UI;
using Xunit;

namespace Jarvis.UI.Tests;

public sealed class BrowserPromptParserTests
{
    [Theory]
    [InlineData(
        "find me the best c++ tutorial in the internet use the browser feature to find ti",
        "the best c++ tutorial")]
    [InlineData("search for beginner watercolor lessons on the web", "beginner watercolor lessons")]
    [InlineData("browser search for reliable weather forecast", "reliable weather forecast")]
    [InlineData("Google search for C# async best practices", "C# async best practices")]
    public void TryParseSearch_Extracts_Internet_Search_Query(string prompt, string expectedQuery)
    {
        BrowserPromptParser.TryParseSearch(prompt, out var query).Should().BeTrue();
        query.Should().Be(expectedQuery);
    }

    [Theory]
    [InlineData("search for files named report")]
    [InlineData("search my Gmail for invoices")]
    [InlineData("find me the best tutorial")]
    [InlineData("browser open Gmail")]
    public void TryParseSearch_Leaves_Local_And_Unqualified_Requests_Alone(string prompt)
    {
        BrowserPromptParser.TryParseSearch(prompt, out var query).Should().BeFalse();
        query.Should().BeEmpty();
    }

    [Theory]
    [InlineData("in browser open Gmail", "Gmail")]
    [InlineData("browser open Gmail", "Gmail")]
    [InlineData("use browser to open Gmail", "Gmail")]
    [InlineData("Open https://example.com in browser", "https://example.com")]
    [InlineData("open the amazon.in website in browser", "amazon.in")]
    [InlineData("in browser open amazon.in", "amazon.in")]
    [InlineData("Open the Microsoft 365 web app in the browser", "the Microsoft 365 web app")]
    public void TryParse_Recognizes_Explicit_Browser_Open_Requests(string prompt, string target)
    {
        BrowserPromptParser.TryParse(prompt, out var parsedTarget).Should().BeTrue();
        parsedTarget.Should().Be(target);
    }

    [Theory]
    [InlineData("open Notepad")]
    [InlineData("search for Gmail")]
    [InlineData("create a file in the browser folder")]
    public void TryParse_Does_Not_Claim_Non_Browser_Tasks(string prompt)
    {
        BrowserPromptParser.TryParse(prompt, out var parsedTarget).Should().BeFalse();
        parsedTarget.Should().BeEmpty();
    }
}
