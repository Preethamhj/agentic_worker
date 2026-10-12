using FluentAssertions;
using Jarvis.Contracts;
using Jarvis.Safety;
using Jarvis.Tools;
using Xunit;

namespace Jarvis.Tools.Tests;

public class ToolsTests
{
    private class FakeToolExecutor : IToolExecutor
    {
        public string ToolId => "fs.create_file";

        public Task<ExecutionReceiptDto> ExecuteAsync(VerifiedAuthorization auth, CancellationToken cancellationToken = default)
        {
            var receipt = new ExecutionReceiptDto(
                SchemaVersion: 1,
                StepId: auth.Record.StepId,
                AuthId: auth.Record.AuthId,
                StartedAt: DateTimeOffset.UtcNow.ToString("o"),
                CompletedAt: DateTimeOffset.UtcNow.ToString("o"),
                ExitCode: 0,
                ReportedSuccess: true,
                Stdout: string.Empty,
                Stderr: string.Empty
            );

            return Task.FromResult(receipt);
        }
    }

    [Fact]
    public void IToolExecutor_ExecuteAsync_Requires_VerifiedAuthorization_Parameter()
    {
        var method = typeof(IToolExecutor).GetMethod(nameof(IToolExecutor.ExecuteAsync));
        method.Should().NotBeNull();

        var parameters = method!.GetParameters();
        parameters.Should().NotBeEmpty();
        parameters[0].ParameterType.Should().Be(typeof(VerifiedAuthorization), "IToolExecutor must enforce VerifiedAuthorization in its method signature");
    }

    [Fact]
    public void FakeToolExecutor_Implements_IToolExecutor_Contract()
    {
        var executor = new FakeToolExecutor();
        executor.ToolId.Should().Be("fs.create_file");
        executor.Should().BeAssignableTo<IToolExecutor>();
    }

    [Fact]
    public void VerifiedAuthorization_Has_No_Copy_Path_And_No_With_Expression_Support()
    {
        var type = typeof(VerifiedAuthorization);
        type.IsSealed.Should().BeTrue("VerifiedAuthorization must be a sealed class");
        type.IsValueType.Should().BeFalse("VerifiedAuthorization must be a class, not a struct");

        // In C#, records generate a compiler method `<Clone>$`. A sealed class does not, which causes `auth with { ... }` to fail compilation with CS8858.
        var cloneMethod = type.GetMethod("<Clone>$", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        cloneMethod.Should().BeNull("VerifiedAuthorization must not be a record or have a clone/copy method");

        // All properties must be get-only with no setter or init accessor
        foreach (var prop in type.GetProperties())
        {
            prop.CanRead.Should().BeTrue();
            prop.SetMethod.Should().BeNull($"Property {prop.Name} must be get-only with no setter or init accessor");
        }
    }
}
