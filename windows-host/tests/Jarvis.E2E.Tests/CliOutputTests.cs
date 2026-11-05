using Jarvis.Cli;
using Jarvis.Contracts;
using Jarvis.Core;
using Jarvis.Infrastructure;
using Xunit;

namespace Jarvis.E2E.Tests;

public sealed class CliOutputTests
{
    [Fact]
    public void Run_Summary_Shows_Final_Failed_Status_And_Check_Reasons()
    {
        var verification = new VerificationResultDto(
            1,
            "step-1",
            "FAILED",
            [new VerificationStageResultDto(3, "IndependentEvidenceCollection", false, "Target file was missing.", "FAILED")],
            [],
            [],
            ["Target file was missing."],
            1);
        var result = new TaskOrchestratorResult(
            "task-1",
            TaskState.Failed,
            [],
            [verification],
            "Verification failed.");

        var output = Program.RenderTaskSummary(result);

        Assert.Contains("[STATUS] FAILED", output);
        Assert.Contains("[Check] IndependentEvidenceCollection: FAILED - Target file was missing.", output);
        Assert.Contains("[Reason] Target file was missing.", output);
    }

    [Fact]
    public void History_Shows_Verification_Status_And_Matching_Approver()
    {
        var verifiedAt = DateTimeOffset.UtcNow;
        var verification = new VerificationAuditRecord(
            "step-1",
            "FAILED",
            "File evidence did not match.",
            verifiedAt,
            "task-1");
        var approval = new ApprovalDecisionRecord(
            "task-1",
            "step-1",
            true,
            "alice",
            null,
            verifiedAt.AddMinutes(-1));

        var output = Program.RenderVerificationHistory([verification], [approval]);

        Assert.Contains("Verification FAILED", output);
        Assert.Contains("Approver: alice", output);
        Assert.Contains("File evidence did not match.", output);
    }
}
