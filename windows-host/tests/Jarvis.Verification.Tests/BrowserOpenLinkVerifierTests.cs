using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Jarvis.Contracts;
using Jarvis.Infrastructure;
using Jarvis.Verification;
using Xunit;

namespace Jarvis.Verification.Tests;

public sealed class BrowserOpenLinkVerifierTests
{
    private const string Target = "https://target.example/private?session=local#section";
    private const string SourceTab = "22222222222222222222222222222222";
    private const string OtherTab = "33333333333333333333333333333333";
    private const string NewTab = "44444444444444444444444444444444";
    private static readonly string TargetHash = Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(new Uri(Target).AbsoluteUri))).ToLowerInvariant();

    private static AuthorizationRecordDto Record() => new(
        1, "auth-1", "task-1", "step-1", "browser.open_link",
        new Dictionary<string, object?>
        {
            ["link_id"] = "l123456789abc",
            ["observation_id"] = "11111111-1111-1111-1111-111111111111",
            ["tab_id"] = "22222222222222222222222222222222",
            ["target_hash"] = TargetHash,
            ["target_host"] = "target.example",
            ["mode"] = "new_tab"
        },
        new Dictionary<string, object?>(), ["browser:open-link:l123456789abc"], [], "medium", "user",
        "2026-01-01T00:00:00Z", "2026-01-01T01:00:00Z", "2026-01-01T00:05:00Z", "2026-01-01T00:10:00Z",
        "nonce-1", "hmac");

    private static BrowserOpenLinkVerifier Verifier(bool authorized = true) => new(
        new FakeAuthorization(authorized),
        new InMemoryAuditStore(),
        SystemClock.Instance,
        TimeSpan.Zero,
        TimeSpan.Zero,
        (_, _) => Task.CompletedTask);

    [Fact]
    public async Task Verifies_a_new_destination_tab_with_the_signed_target()
    {
        var source = new TopologySource([SourceTab, OtherTab], new(NewTab, Target, false));
        var verifier = Verifier();
        var baseline = await verifier.BeginAsync(source);
        source.TabIds.Add(NewTab);

        var result = await verifier.VerifyAsync(Record(), source, baseline);

        result.Status.Should().Be("VERIFIED");
    }

    [Theory]
    [InlineData(SourceTab)]
    [InlineData(OtherTab)]
    [InlineData("")]
    [InlineData(null)]
    public async Task Rejects_existing_or_missing_destination_identity(string? destinationId)
    {
        var source = new TopologySource([SourceTab, OtherTab], new(destinationId, Target, false));
        var verifier = Verifier();
        var baseline = await verifier.BeginAsync(source);

        var result = await verifier.VerifyAsync(Record(), source, baseline);

        result.Status.Should().NotBe("VERIFIED");
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("blank")]
    public async Task Rejects_incomplete_or_invalid_baseline(string kind)
    {
        var ids = kind switch
        {
            "missing" => new List<string> { OtherTab },
            "empty" => [],
            "duplicate" => [SourceTab, SourceTab],
            _ => [SourceTab, ""]
        };
        var source = new TopologySource(ids, new(NewTab, Target, false));
        var verifier = Verifier();
        var baseline = await verifier.BeginAsync(source);

        var result = await verifier.VerifyAsync(Record(), source, baseline);

        result.Status.Should().NotBe("VERIFIED");
    }

    [Theory]
    [InlineData("https://other.example/", false)]
    [InlineData("https://source.example/", false)]
    [InlineData(Target, true)]
    [InlineData(null, false)]
    public async Task Requires_stable_matching_destination_address(string? url, bool isLoading)
    {
        var source = new TopologySource([SourceTab], new(NewTab, url, isLoading));
        var verifier = Verifier();
        var baseline = await verifier.BeginAsync(source);

        var result = await verifier.VerifyAsync(Record(), source, baseline);

        result.Status.Should().Be("ESCALATE");
    }

    [Fact]
    public async Task Rejects_missing_signed_source_tab()
    {
        var record = Record();
        var args = new Dictionary<string, object?>(record.CanonicalArgs);
        args.Remove("tab_id");
        var source = new TopologySource([SourceTab], new(NewTab, Target, false));
        var verifier = Verifier();
        var baseline = await verifier.BeginAsync(source);

        var result = await verifier.VerifyAsync(record with { CanonicalArgs = args }, source, baseline);

        result.Status.Should().Be("FAILED");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("same_tab")]
    public async Task Rejects_invalid_mode_even_with_valid_new_tab_evidence(string? mode)
    {
        var record = Record();
        var args = new Dictionary<string, object?>(record.CanonicalArgs);
        if (mode is null)
            args.Remove("mode");
        else
            args["mode"] = mode;
        var source = new TopologySource([SourceTab], new(NewTab, Target, false));
        var verifier = Verifier();
        var baseline = await verifier.BeginAsync(source);

        var result = await verifier.VerifyAsync(record with { CanonicalArgs = args }, source, baseline);

        result.Status.Should().Be("FAILED");
    }

    [Fact]
    public async Task New_tab_may_have_same_url_as_source()
    {
        var source = new TopologySource([SourceTab], new(NewTab, Target, false));
        var verifier = Verifier();
        var baseline = await verifier.BeginAsync(source);

        var result = await verifier.VerifyAsync(Record(), source, baseline);

        result.Status.Should().Be("VERIFIED");
    }

    [Fact]
    public async Task New_tab_evidence_does_not_override_unconsumed_authorization()
    {
        var source = new TopologySource([SourceTab], new(NewTab, Target, false));
        var verifier = Verifier(authorized: false);
        var baseline = await verifier.BeginAsync(source);

        var result = await verifier.VerifyAsync(Record(), source, baseline);

        result.Status.Should().Be("FAILED");
    }

    [Fact]
    public async Task New_tab_evidence_observes_cancellation()
    {
        var source = new TopologySource([SourceTab], new(NewTab, Target, false));
        var verifier = Verifier();
        var baseline = await verifier.BeginAsync(source);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = () => verifier.VerifyAsync(Record(), source, baseline, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Cancellation_is_observed_even_when_topology_is_invalid()
    {
        var source = new TopologySource([], new(NewTab, Target, false));
        var verifier = Verifier();
        var baseline = await verifier.BeginAsync(source);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = () => verifier.VerifyAsync(Record(), source, baseline, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Matching_target_without_new_tab_identity_escalates()
    {
        var result = await Verifier().VerifyAsync(
            Record(),
            new Source(Target),
            new BrowserOpenLinkBaseline("https://source.example/"));

        result.Status.Should().Be("ESCALATE");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("same_tab")]
    [InlineData("NEW_TAB")]
    public async Task Rejects_missing_or_wrong_signed_mode(string? mode)
    {
        var record = Record();
        var args = new Dictionary<string, object?>(record.CanonicalArgs);
        if (mode is null)
            args.Remove("mode");
        else
            args["mode"] = mode;

        var result = await Verifier().VerifyAsync(
            record with { CanonicalArgs = args },
            new Source(Target),
            new BrowserOpenLinkBaseline("https://source.example/"));

        result.Status.Should().Be("FAILED");
    }

    [Fact]
    public async Task Unchanged_address_and_redirect_to_another_target_escalate_without_tab_identity()
    {
        var unchanged = await Verifier().VerifyAsync(
            Record(),
            new Source("https://source.example/"),
            new BrowserOpenLinkBaseline("https://source.example/"));
        var redirected = await Verifier().VerifyAsync(
            Record(),
            new Source("https://other.example/"),
            new BrowserOpenLinkBaseline("https://source.example/"));

        unchanged.Status.Should().Be("ESCALATE");
        redirected.Status.Should().Be("ESCALATE");
    }

    [Fact]
    public async Task Same_url_in_baseline_is_not_proof_that_no_new_tab_opened()
    {
        var result = await Verifier().VerifyAsync(
            Record(),
            new Source(Target),
            new BrowserOpenLinkBaseline(Target));

        result.Status.Should().Be("ESCALATE");
    }

    [Fact]
    public async Task Rejects_unconsumed_authorization()
    {
        var result = await Verifier(authorized: false).VerifyAsync(
            Record(),
            new Source(Target),
            new BrowserOpenLinkBaseline("https://source.example/"));

        result.Status.Should().Be("FAILED");
    }

    [Fact]
    public async Task Observes_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = () => Verifier().VerifyAsync(
            Record(),
            new Source(Target),
            new BrowserOpenLinkBaseline("https://source.example/"),
            cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class Source(string? url) : IBrowserEvidenceSource
    {
        public Task<BrowserPageEvidence> CaptureAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new BrowserPageEvidence(url, false));
    }

    private sealed class TopologySource(List<string> tabIds, BrowserDestinationEvidence destination)
        : IBrowserOpenLinkEvidenceSource
    {
        public List<string> TabIds { get; } = tabIds;

        public Task<BrowserTabTopologyEvidence> CaptureTopologyAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new BrowserTabTopologyEvidence(TabIds));

        public Task<BrowserDestinationEvidence> CaptureDestinationAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(destination);
    }

    private sealed class FakeAuthorization(bool ok) : IAuthorizationVerifier
    {
        public Task VerifyAndMarkVerifiedAsync(
            AuthorizationRecordDto authorization,
            CancellationToken cancellationToken = default) =>
            ok ? Task.CompletedTask : throw new AuthorizationVerificationException("not consumed");
    }
}
