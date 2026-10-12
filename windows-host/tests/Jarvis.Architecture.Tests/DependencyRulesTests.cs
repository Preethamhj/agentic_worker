using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Jarvis.Architecture.Tests;

public class DependencyRulesTests
{
    [Fact]
    public void Verification_Must_Not_Reference_Tools()
    {
        var result = Types.InAssembly(typeof(Jarvis.Verification.VerificationPlaceholder).Assembly)
            .ShouldNot()
            .HaveDependencyOn("Jarvis.Tools")
            .GetResult();

        result.IsSuccessful.Should().BeTrue("Jarvis.Verification must never depend on Jarvis.Tools");
    }

    [Fact]
    public void Contracts_Must_Not_Reference_Other_Jarvis_Projects()
    {
        var result = Types.InAssembly(typeof(Jarvis.Contracts.PlanRequestDto).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("Jarvis.Core", "Jarvis.Safety", "Jarvis.Tools", "Jarvis.Verification", "Jarvis.Infrastructure")
            .GetResult();

        result.IsSuccessful.Should().BeTrue("Jarvis.Contracts must be a leaf project referencing no other Jarvis projects");
    }

    [Fact]
    public void Tools_Must_Reference_Safety()
    {
        // Tools assembly types should have dependency or reference Safety
        var assembly = typeof(Jarvis.Tools.IToolExecutor).Assembly;
        var referencedAssemblies = assembly.GetReferencedAssemblies();
        referencedAssemblies.Should().Contain(a => a.Name == "Jarvis.Safety", "Jarvis.Tools must reference Jarvis.Safety");
    }

    [Fact]
    public void VerifiedAuthorization_Cannot_Be_Constructed_Outside_Jarvis_Safety()
    {
        var type = typeof(Jarvis.Safety.VerifiedAuthorization);
        var constructors = type.GetConstructors(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        constructors.Should().NotBeEmpty("VerifiedAuthorization must have at least one constructor");
        constructors.Should().OnlyContain(c => c.IsAssembly, "VerifiedAuthorization constructors must be strictly internal (assembly-scoped) to prevent instantiation outside Jarvis.Safety");
    }
}
