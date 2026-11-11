using FluentAssertions;
using Jarvis.Contracts;
using Jarvis.Infrastructure;
using Jarvis.Verification;
using Xunit;

namespace Jarvis.Verification.Tests;

public sealed class BrowserSearchVerifierTests
{
    private static AuthorizationRecordDto Record(string query = "cpp tutorial") => new(
        1, "auth-1", "task-1", "step-1", "browser.search",
        new Dictionary<string, object?> { ["query"] = query },
        new Dictionary<string, object?>(), ["browser:search"], [], "medium", "user",
        "2026-01-01T00:00:00Z", "2026-01-01T01:00:00Z", "2026-01-01T00:05:00Z", "2026-01-01T00:10:00Z",
        "nonce-1", "hmac");

    private static BrowserSearchVerifier Verifier(bool authorized = true) => new(
        new FakeAuthorization(authorized),
        new InMemoryAuditStore(),
        SystemClock.Instance,
        TimeSpan.Zero,
        TimeSpan.Zero,
        (_, _) => Task.CompletedTask);

    [Theory]
    [InlineData("https://www.youtube.com/results?search_query=cpp+tutorial", "VERIFIED")]
    [InlineData("https://www.youtube.com/results?search_query=cpp%20TUTORIAL", "VERIFIED")]
    [InlineData("https://www.youtube.com/results?search_query=java", "ESCALATE")]
    [InlineData("https://www.youtube.com/", "FAILED")]
    [InlineData("https://evil.example/results?q=cpp+tutorial", "ESCALATE")]
    [InlineData(null, "ESCALATE")]
    public async Task Judges_only_from_browser_address(string? after, string expected)
    {
        var result = await Verifier().VerifyAsync(
            Record(), new Source(after), new BrowserSearchBaseline("https://www.youtube.com/"));
        result.Status.Should().Be(expected);
    }

    [Fact]
    public async Task Query_already_in_baseline_cannot_prove_the_action()
    {
        var url = "https://www.youtube.com/results?search_query=cpp+tutorial";
        var result = await Verifier().VerifyAsync(Record(), new Source(url), new BrowserSearchBaseline(url));
        result.Status.Should().Be("ESCALATE");
    }

    [Fact]
    public async Task Unconsumed_authorization_is_failed()
    {
        var result = await Verifier(authorized: false).VerifyAsync(
            Record(), new Source("https://www.youtube.com/results?search_query=cpp+tutorial"),
            new BrowserSearchBaseline("https://www.youtube.com/"));
        result.Status.Should().Be("FAILED");
    }

    private sealed class Source(string? url) : IBrowserEvidenceSource
    {
        public Task<BrowserPageEvidence> CaptureAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new BrowserPageEvidence(url, false));
    }

    private sealed class FakeAuthorization(bool ok) : IAuthorizationVerifier
    {
        public Task VerifyAndMarkVerifiedAsync(AuthorizationRecordDto authorization, CancellationToken cancellationToken = default) =>
            ok ? Task.CompletedTask : throw new AuthorizationVerificationException("not consumed");
    }
}
