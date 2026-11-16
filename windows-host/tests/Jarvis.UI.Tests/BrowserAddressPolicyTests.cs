using FluentAssertions;
using Jarvis.UI;
using Xunit;

namespace Jarvis.UI.Tests;

public sealed class BrowserAddressPolicyTests
{
    [Theory]
    [InlineData("example.com", "https://example.com/")]
    [InlineData("https://example.com/path?q=jarvis", "https://example.com/path?q=jarvis")]
    [InlineData("http://localhost:5000/", "http://localhost:5000/")]
    public void TryNormalize_Accepts_Web_Addresses(string input, string expected)
    {
        Uri? address;
        string error;
        BrowserAddressPolicy.TryNormalize(input, out address, out error).Should().BeTrue();
        address!.AbsoluteUri.Should().Be(expected);
        error.Should().BeEmpty();
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,hello")]
    [InlineData("file:///C:/Windows/win.ini")]
    [InlineData("https://user:password@example.com/")]
    [InlineData("")]
    [InlineData("   ")]
    public void TryNormalize_Rejects_Non_Web_Or_Credentialed_Addresses(string input)
    {
        Uri? address;
        string error;
        BrowserAddressPolicy.TryNormalize(input, out address, out error).Should().BeFalse();
        address.Should().BeNull();
        error.Should().NotBeNullOrWhiteSpace();
    }
}
