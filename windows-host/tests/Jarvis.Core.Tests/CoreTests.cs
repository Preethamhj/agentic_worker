using FluentAssertions;
using Xunit;

namespace Jarvis.Core.Tests;

public class CoreTests
{
    [Fact]
    public void Core_Placeholder_Should_Have_Name()
    {
        CorePlaceholder.Name.Should().Be("Jarvis.Core");
    }
}
