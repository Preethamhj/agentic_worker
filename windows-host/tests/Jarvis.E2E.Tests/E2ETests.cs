using FluentAssertions;
using Xunit;

namespace Jarvis.E2E.Tests;

public class E2ETests
{
    [Fact]
    public void E2E_Placeholder_Passes()
    {
        true.Should().BeTrue();
    }
}
