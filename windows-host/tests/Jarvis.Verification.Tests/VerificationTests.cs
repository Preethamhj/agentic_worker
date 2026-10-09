using FluentAssertions;
using Xunit;

namespace Jarvis.Verification.Tests;

public class VerificationTests
{
    [Fact]
    public void Verification_Placeholder_Should_Have_Name()
    {
        VerificationPlaceholder.Name.Should().Be("Jarvis.Verification");
    }
}
