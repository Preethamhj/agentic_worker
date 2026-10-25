using Jarvis.Cli;
using Jarvis.Contracts;
using Jarvis.Core;
using Jarvis.Infrastructure;
using Xunit;

namespace Jarvis.E2E.Tests;

public sealed class HostVerifierCompositionTests
{
    [Fact]
    public void Host_Composition_Uses_Independent_VerificationEngine_Not_StubVerifier()
    {
        var verifier = Program.CreateVerifier(new UnusedAuthorizationVerifier(), new InMemoryAuditStore());

        Assert.Equal("Jarvis.Verification.VerificationEngine", verifier.GetType().FullName);
        Assert.NotEqual("Jarvis.Core.StubVerifier", verifier.GetType().FullName);
    }

    [Fact]
    public void CreateReport_Uses_Last_True_Verification_Status_And_Short_Reason()
    {
        var verification = new VerificationResultDto(
            1,
            "step-1",
            "FAILED",
            [],
            [],
            [],
            [new string('x', 300)],
            1);
        var result = new TaskOrchestratorResult(
            "task-1",
            TaskState.Failed,
            [],
            [verification],
            "Task failed.");

        var report = Program.CreateReport("session-1", result);

        Assert.Equal("FAILED", Assert.Single(report.Steps).VerificationStatus);
        Assert.Equal(240, report.Steps[0].Reason.Length);
    }

    private sealed class UnusedAuthorizationVerifier : IAuthorizationVerifier
    {
        public Task VerifyAndMarkVerifiedAsync(
            AuthorizationRecordDto authorization,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Verification composition test must not verify an authorization.");
    }
}
