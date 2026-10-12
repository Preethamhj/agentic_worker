using System.Diagnostics;
using System.Runtime.InteropServices;
using FluentAssertions;
using Jarvis.Infrastructure;
using Xunit;

namespace Jarvis.E2E.Tests;

public class AiServerLauncherTests
{
    [Fact]
    public void FreeLoopbackPort_Returns_Valid_Port_Number()
    {
        var port = AiServerLauncher.GetFreeLoopbackPort();
        port.Should().BeInRange(1024, 65535);
    }

    [Fact]
    public void GenerateSecureToken_Produces_64_Character_Hex_String()
    {
        var token = AiServerLauncher.GenerateSecureToken();
        token.Should().HaveLength(64);
        token.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void JobObject_Kills_Child_Process_When_Job_Handle_Is_Closed()
    {
        if (!OperatingSystem.IsWindows()) return;

        var jobHandle = AiServerLauncher.CreateKillOnCloseJob();
        jobHandle.Should().NotBe(IntPtr.Zero, "Job Object should be successfully created with KILL_ON_JOB_CLOSE");

        // Start a long-running dummy child process (cmd.exe waiting 30 seconds)
        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c timeout /t 30 /nobreak > nul",
            CreateNoWindow = true,
            UseShellExecute = false
        };

        using var childProc = Process.Start(psi);
        childProc.Should().NotBeNull();
        childProc!.HasExited.Should().BeFalse();

        // Assign to Job Object
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

        var assigned = AssignProcessToJobObject(jobHandle, childProc.Handle);
        assigned.Should().BeTrue("Process should be assigned to Job Object");

        // Close the job handle -> Windows must immediately kill all child processes in the job
        AiServerLauncher.CloseHandle(jobHandle);

        childProc.WaitForExit(4000);
        childProc.HasExited.Should().BeTrue("Child process must be killed when the Job Object handle is closed");
    }
}
