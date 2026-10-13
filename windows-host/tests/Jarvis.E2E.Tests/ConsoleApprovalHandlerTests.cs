using FluentAssertions;
using Jarvis.Cli;
using Jarvis.Contracts;
using Xunit;

namespace Jarvis.E2E.Tests;

public class ConsoleApprovalHandlerTests
{
    private static ApprovalRequest CreateRequest(string risk)
    {
        return new ApprovalRequest(
            TaskId: "task-test-approval",
            StepId: "step-1",
            ToolId: "fs.create_file",
            CanonicalArgs: new Dictionary<string, object?> { ["path"] = @"C:\allowed\test.txt" },
            Risk: risk
        );
    }

    [Fact]
    public async Task HighRisk_NeverAutoApproves_EvenWhen_ForceAutoApprove_IsTrue()
    {
        // Invariant: High risk must NEVER auto-approve under any circumstances
        var handler = new ConsoleApprovalHandler(autoApprove: true, reader: new StringReader("n\n"));
        var request = CreateRequest("high");

        var approved = await handler.RequestApprovalAsync(request);

        approved.Should().BeFalse("High risk must never auto-approve even if forceAutoApprove is true");
    }

    [Fact]
    public async Task HighRisk_NeverAutoApproves_EvenWith_JarvisAutoApprove_EnvVar()
    {
        Environment.SetEnvironmentVariable("JARVIS_AUTO_APPROVE", "1");
        try
        {
            var handler = new ConsoleApprovalHandler(reader: new StringReader("n\n"));
            var request = CreateRequest("high");

            var approved = await handler.RequestApprovalAsync(request);

            approved.Should().BeFalse("High risk must never auto-approve even when JARVIS_AUTO_APPROVE is set");
        }
        finally
        {
            Environment.SetEnvironmentVariable("JARVIS_AUTO_APPROVE", null);
        }
    }

    [Fact]
    public async Task MediumRisk_DoesNotAutoApprove_With_JarvisAutoApprove_EnvVar()
    {
        Environment.SetEnvironmentVariable("JARVIS_AUTO_APPROVE", "1");
        try
        {
            var handler = new ConsoleApprovalHandler(reader: new StringReader("n\n"));
            var request = CreateRequest("medium");

            var approved = await handler.RequestApprovalAsync(request);

            approved.Should().BeFalse("JARVIS_AUTO_APPROVE must be restricted strictly to risk=low; medium must not auto-approve");
        }
        finally
        {
            Environment.SetEnvironmentVariable("JARVIS_AUTO_APPROVE", null);
        }
    }

    [Fact]
    public async Task LowRisk_AutoApproves_With_JarvisAutoApprove_EnvVar_InDebug()
    {
        Environment.SetEnvironmentVariable("JARVIS_AUTO_APPROVE", "1");
        try
        {
            var handler = new ConsoleApprovalHandler(reader: new StringReader("n\n"));
            var request = CreateRequest("low");

            var approved = await handler.RequestApprovalAsync(request);

#if DEBUG
            approved.Should().BeTrue("JARVIS_AUTO_APPROVE allows risk=low in Debug builds");
#else
            approved.Should().BeFalse("JARVIS_AUTO_APPROVE is compiled out of Release builds");
#endif
        }
        finally
        {
            Environment.SetEnvironmentVariable("JARVIS_AUTO_APPROVE", null);
        }
    }

    [Fact]
    public async Task LowRisk_DoesNotAutoApprove_When_JarvisAutoApprove_IsUnset()
    {
        Environment.SetEnvironmentVariable("JARVIS_AUTO_APPROVE", null);

        var handler = new ConsoleApprovalHandler(reader: new StringReader("n\n"));
        var request = CreateRequest("low");

        var approved = await handler.RequestApprovalAsync(request);

        approved.Should().BeFalse("Without JARVIS_AUTO_APPROVE or forceAutoApprove, low risk must prompt / not auto-approve");
    }
}
