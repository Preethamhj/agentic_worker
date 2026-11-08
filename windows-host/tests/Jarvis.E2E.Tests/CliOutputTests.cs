using Jarvis.Cli;
using Jarvis.Composition;
using Jarvis.Contracts;
using Jarvis.Core;
using Jarvis.Infrastructure;
using Xunit;

namespace Jarvis.E2E.Tests;

public sealed class CliOutputTests
{
    [Fact]
    public void Search_Summary_Displays_Independent_Results_Not_Executor_Claims()
    {
        var result = SearchResult("VERIFIED", TaskState.Completed);

        var output = Program.RenderTaskSummary(result);

        Assert.Contains(@"C:\workspace\real.txt", output);
        Assert.DoesNotContain(@"C:\workspace\fake.txt", output);
        Assert.Contains("Independently verified search results", output);
    }

    [Theory]
    [InlineData("FAILED")]
    [InlineData("ESCALATE")]
    public void Search_Summary_Does_Not_Label_Unverified_Evidence_As_Results(string status)
    {
        var output = Program.RenderTaskSummary(SearchResult(status, TaskState.Failed));

        Assert.DoesNotContain("Independently verified search results", output);
        Assert.DoesNotContain(@"C:\workspace\real.txt", output);
        Assert.DoesNotContain(@"C:\workspace\fake.txt", output);
    }

    [Fact]
    public void Search_Results_Handle_Json_Evidence_And_Empty_Results()
    {
        var verification = SearchResult("VERIFIED", TaskState.Completed).VerificationResults.Single();
        using var document = System.Text.Json.JsonDocument.Parse("[]");
        verification.Evidence.Single().Observed["enumerated_paths"] = document.RootElement.Clone();

        var search = Assert.Single(VerifiedSearchResults.From([verification]));

        Assert.Empty(search.Paths);
    }

    [Fact]
    public void Search_Results_Reject_Malformed_Independent_Evidence()
    {
        var verification = SearchResult("VERIFIED", TaskState.Completed).VerificationResults.Single();
        verification.Evidence.Single().Observed["enumerated_paths"] = "not a path array";

        Assert.Throws<InvalidDataException>(() => VerifiedSearchResults.From([verification]));
    }

    private static TaskOrchestratorResult SearchResult(string status, TaskState state)
    {
        var evidence = new EvidenceDto(
            1, "evidence-1", "step-1", new CheckDto("paths_match", []),
            "SearchEvidenceCollector", DateTimeOffset.UtcNow.ToString("O"),
            new Dictionary<string, object?> { ["enumerated_paths"] = new List<string> { @"C:\workspace\real.txt" } },
            [], true, "nonce", "hash");
        var verification = new VerificationResultDto(1, "step-1", status, [], [evidence], [], [], 1);
        var receipt = new ExecutionReceiptDto(
            1, "step-1", "auth-1", "", "", 0, true, @"C:\workspace\fake.txt", "");
        return new TaskOrchestratorResult("task-1", state, [receipt], [verification], "done");
    }

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
