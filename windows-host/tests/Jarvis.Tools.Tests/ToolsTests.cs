using FluentAssertions;
using Xunit;

namespace Jarvis.Tools.Tests;

public class ToolsTests
{
    [Fact]
    public void Tools_Placeholder_Should_Have_Name()
    {
        ToolsPlaceholder.Name.Should().Be("Jarvis.Tools");
    }
}
