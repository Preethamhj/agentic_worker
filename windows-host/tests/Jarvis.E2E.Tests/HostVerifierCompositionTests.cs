using Jarvis.Cli;
using Jarvis.Contracts;
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

    private sealed class UnusedAuthorizationVerifier : IAuthorizationVerifier
    {
        public Task VerifyAndMarkVerifiedAsync(
            AuthorizationRecordDto authorization,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Verification composition test must not verify an authorization.");
    }
}
