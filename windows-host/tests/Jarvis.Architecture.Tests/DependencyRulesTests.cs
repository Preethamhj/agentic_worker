using FluentAssertions;
using NetArchTest.Rules;
using Xunit;

namespace Jarvis.Architecture.Tests;

public class DependencyRulesTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public DependencyRulesTests(Xunit.Abstractions.ITestOutputHelper output)
    {
        _output = output;
    }

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
    public void Transitive_Closure_Of_Verification_Must_Not_Reach_Tools()
    {
        var verificationAsm = typeof(Jarvis.Verification.VerificationPlaceholder).Assembly;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<System.Reflection.Assembly>();

        var rootName = verificationAsm.GetName().Name!;
        visited.Add(rootName);
        queue.Enqueue(verificationAsm);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var refName in current.GetReferencedAssemblies())
            {
                if (refName.Name != null && visited.Add(refName.Name))
                {
                    try
                    {
                        var loaded = System.Reflection.Assembly.Load(refName);
                        queue.Enqueue(loaded);
                    }
                    catch (System.IO.FileNotFoundException) when (!refName.Name.StartsWith("Jarvis.", StringComparison.OrdinalIgnoreCase))
                    {
                    }
                    catch (System.IO.FileLoadException) when (!refName.Name.StartsWith("Jarvis.", StringComparison.OrdinalIgnoreCase))
                    {
                    }
                    catch (BadImageFormatException) when (!refName.Name.StartsWith("Jarvis.", StringComparison.OrdinalIgnoreCase))
                    {
                    }
                }
            }
        }

        _output.WriteLine($"Transitive assembly reference closure for {rootName} ({visited.Count} assemblies):");
        foreach (var asm in visited.OrderBy(x => x))
        {
            _output.WriteLine($"  - {asm}");
        }

        visited.Should().NotContain("Jarvis.Tools", "Jarvis.Verification must never have transitive reachability to Jarvis.Tools");
        visited.Should().NotContain("Jarvis.Safety", "Jarvis.Verification must not transitively depend on Jarvis.Safety");
        visited.Should().NotContain("Jarvis.Core", "Jarvis.Verification must not transitively depend on Jarvis.Core");

        var directReferences = verificationAsm.GetReferencedAssemblies()
            .Where(reference => reference.Name?.StartsWith("Jarvis.", StringComparison.OrdinalIgnoreCase) == true)
            .Select(reference => reference.Name)
            .OrderBy(name => name)
            .ToArray();
        directReferences.Should().BeEquivalentTo("Jarvis.Contracts", "Jarvis.Infrastructure");
    }

    [Fact]
    public void Print_Project_Reference_Graph()
    {
        var assemblies = new[]
        {
            typeof(Jarvis.Contracts.PlanRequestDto).Assembly,
            typeof(Jarvis.Core.StateMachine).Assembly,
            typeof(Jarvis.Safety.PathPolicy).Assembly,
            typeof(Jarvis.Tools.IToolExecutor).Assembly,
            typeof(Jarvis.Verification.VerificationPlaceholder).Assembly,
            typeof(Jarvis.Infrastructure.SqliteAuditStore).Assembly,
            typeof(Jarvis.Cli.ConsoleApprovalHandler).Assembly
        };

        _output.WriteLine("=================== PROJECT REFERENCE GRAPH ===================");
        foreach (var asm in assemblies)
        {
            var name = asm.GetName().Name!;
            var jarvisRefs = asm.GetReferencedAssemblies()
                .Where(r => r.Name != null && r.Name.StartsWith("Jarvis.", StringComparison.OrdinalIgnoreCase))
                .Select(r => r.Name!)
                .OrderBy(x => x)
                .ToList();

            var refsStr = jarvisRefs.Count > 0 ? string.Join(", ", jarvisRefs) : "(leaf / no Jarvis references)";
            _output.WriteLine($"{name} -> [ {refsStr} ]");
        }
        _output.WriteLine("===============================================================");
    }

    [Fact]
    public void Contracts_Must_Not_Reference_Other_Jarvis_Projects()
    {
        var result = Types.InAssembly(typeof(Jarvis.Contracts.PlanRequestDto).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("Jarvis.Core", "Jarvis.Safety", "Jarvis.Tools", "Jarvis.Verification", "Jarvis.Infrastructure", "Jarvis.Cli")
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

    [Fact]
    public void Host_Projects_Must_Not_Reference_Evaluation_Harness()
    {
        var hostAssemblies = new[]
        {
            typeof(Jarvis.Cli.ConsoleApprovalHandler).Assembly,
            typeof(Jarvis.Core.StateMachine).Assembly,
            typeof(Jarvis.Safety.PathPolicy).Assembly,
            typeof(Jarvis.Tools.IToolExecutor).Assembly,
            typeof(Jarvis.Verification.VerificationPlaceholder).Assembly,
            typeof(Jarvis.Infrastructure.SqliteAuditStore).Assembly,
            typeof(Jarvis.Contracts.PlanRequestDto).Assembly
        };

        foreach (var assembly in hostAssemblies)
        {
            assembly.GetReferencedAssemblies()
                .Should()
                .NotContain(reference => string.Equals(
                    reference.Name,
                    "Jarvis.Eval",
                    StringComparison.OrdinalIgnoreCase),
                    $"{assembly.GetName().Name} must not reference Jarvis.Eval");
        }
    }
}
