using System.Diagnostics;
using FluentAssertions;
using Jarvis.TestSupport;
using Xunit;

namespace Jarvis.E2E.Tests;

public sealed class ProcessSandboxTests
{
    [Fact]
    public void Dispose_Kills_What_The_Sandbox_Started()
    {
        var sandbox = new ProcessSandbox();
        var process = sandbox.Start();
        var processId = process.Id;

        sandbox.Dispose();
        Process.GetProcessesByName("Jarvis.TestApp")
            .Should()
            .NotContain(candidate => candidate.Id == processId);
        process.Dispose();
    }

    [Fact]
    public void Dispose_Does_Not_Kill_An_Unrelated_Process_With_The_Same_Name()
    {
        using var unrelated = Process.Start(new ProcessStartInfo(TestApps.Locate())
        {
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Could not start unrelated Jarvis.TestApp.");
        var unrelatedId = unrelated.Id;
        var unrelatedStartTime = unrelated.StartTime.ToUniversalTime();

        try
        {
            using (var sandbox = new ProcessSandbox())
                sandbox.Start();

            unrelated.Refresh();
            unrelated.HasExited.Should().BeFalse();
            unrelated.StartTime.ToUniversalTime().Should().Be(unrelatedStartTime);
        }
        finally
        {
            unrelated.Refresh();
            if (!unrelated.HasExited
                && unrelated.Id == unrelatedId
                && unrelated.StartTime.ToUniversalTime() == unrelatedStartTime)
            {
                unrelated.Kill(entireProcessTree: true);
                unrelated.WaitForExit(5_000).Should().BeTrue();
            }
        }
    }

    [Fact]
    public void Dispose_Leaves_No_Sandbox_Helper_Alive()
    {
        var processId = 0;
        using (var sandbox = new ProcessSandbox())
        {
            processId = sandbox.Start().Id;
        }

        Process.GetProcessesByName("Jarvis.TestApp")
            .Should()
            .NotContain(candidate => candidate.Id == processId);
    }
}
