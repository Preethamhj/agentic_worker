using FluentAssertions;
using Xunit;

namespace Jarvis.Safety.Tests;

public class SafetyTests
{
    [Fact]
    public void Safety_Placeholder_Should_Have_Name()
    {
        SafetyPlaceholder.Name.Should().Be("Jarvis.Safety");
    }
}
