using FluentAssertions;
using Jarvis.UI.Browser;
using Xunit;

namespace Jarvis.UI.Tests;

public sealed class CdpLinkNodeBindingTests
{
    [Fact]
    public void Capture_Binds_The_Approved_Position_To_A_Stable_Node()
    {
        var binding = CdpLinkNodeBinding.Capture("document", 3, 3, [11, 12, 13], [11, 12, 13], 1);

        binding.FindCurrentPosition("document", [44, 12, 13]).Should().Be(1);
        binding.FindCurrentPosition("document", [44, 11, 12, 13]).Should().Be(2);
    }

    [Fact]
    public void Capture_Rejects_Mutation_Or_Node_Reordering_During_Binding()
    {
        var changedVersion = () => CdpLinkNodeBinding.Capture("document", 3, 4, [11, 12], [11, 12], 1);
        var changedNodes = () => CdpLinkNodeBinding.Capture("document", 3, 3, [11, 12], [12, 11], 1);

        changedVersion.Should().Throw<InvalidOperationException>();
        changedNodes.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Revalidation_Rejects_Navigation_Removal_Replacement_And_Duplicate_Identity()
    {
        var binding = CdpLinkNodeBinding.Capture("document", 3, 3, [11, 12], [11, 12], 1);

        binding.FindCurrentPosition("other", [11, 12]).Should().BeNull();
        binding.FindCurrentPosition("document", [11]).Should().BeNull();
        binding.FindCurrentPosition("document", [11, 99]).Should().BeNull();
        binding.FindCurrentPosition("document", [12, 12]).Should().BeNull();
    }

    [Fact]
    public void Capture_Rejects_Missing_Or_Ambiguous_Node_Identity()
    {
        var missing = () => CdpLinkNodeBinding.Capture("document", 3, 3, [0], [0], 0);
        var duplicated = () => CdpLinkNodeBinding.Capture("document", 3, 3, [12, 12], [12, 12], 0);
        var outOfRange = () => CdpLinkNodeBinding.Capture("document", 3, 3, [12], [12], 1);

        missing.Should().Throw<InvalidOperationException>();
        duplicated.Should().Throw<InvalidOperationException>();
        outOfRange.Should().Throw<InvalidOperationException>();
    }
}
