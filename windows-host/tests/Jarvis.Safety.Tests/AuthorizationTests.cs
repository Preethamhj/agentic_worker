using FluentAssertions;
using Jarvis.Contracts;
using Jarvis.Core;
using Jarvis.Infrastructure;
using Jarvis.Safety;
using Xunit;

namespace Jarvis.Safety.Tests;

public class AuthorizationTests
{
    private class TestClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 9, 10, 0, 0, TimeSpan.Zero);
    }

    private readonly TestClock _clock;
    private readonly InMemoryDpapiKeyStore _keyStore;
    private readonly AuthorizationService _authService;

    public AuthorizationTests()
    {
        _clock = new TestClock();
        _keyStore = new InMemoryDpapiKeyStore();
        _authService = new AuthorizationService(_keyStore, _clock);
    }

    [Fact]
    public void Full_Happy_Path_Issue_Consume_Verify_Succeeds()
    {
        var record = _authService.IssueAuthorization(
            taskId: "task-001",
            stepId: "step-001",
            toolId: "fs.create_file",
            rawArgs: new Dictionary<string, object?> { ["path"] = @"C:\test.txt", ["content"] = "hello" },
            expected: new Dictionary<string, object?> { ["exists"] = true },
            allowedEffects: new List<string> { @"create:C:\test.txt" },
            watchScopes: new List<string> { @"C:\test.txt" },
            risk: "medium",
            approvedBy: "user");

        record.Should().NotBeNull();
        record.AuthId.Should().StartWith("auth-");
        record.Nonce.Should().NotBeNullOrEmpty();
        record.Hmac.Should().NotBeNullOrEmpty();

        // 1. Consume before deadline
        _clock.UtcNow = _clock.UtcNow.AddSeconds(10);
        var verifiedAuth = _authService.Consume(record, "step-001");
        verifiedAuth.Should().NotBeNull();
        verifiedAuth.Record.AuthId.Should().Be(record.AuthId);

        // 2. Verify before deadline
        _clock.UtcNow = _clock.UtcNow.AddSeconds(10);
        var verifyResult = _authService.Verify(record);
        verifyResult.Should().BeTrue();
    }

    [Fact]
    public void Flip_One_Byte_In_Hmac_Fails()
    {
        var record = CreateSampleRecord();

        // Flip one character in the HMAC string
        var originalHmac = record.Hmac;
        var flippedChar = originalHmac[0] == 'a' ? 'b' : 'a';
        var tamperedHmac = flippedChar + originalHmac[1..];
        var tamperedRecord = record with { Hmac = tamperedHmac };

        Action consumeAct = () => _authService.Consume(tamperedRecord, "step-001");
        consumeAct.Should().Throw<AuthorizationSecurityException>()
            .WithMessage("*HMAC*");

        Action verifyAct = () => _authService.Verify(tamperedRecord);
        verifyAct.Should().Throw<AuthorizationSecurityException>()
            .WithMessage("*HMAC*");
    }

    [Fact]
    public void Expired_Execute_Deadline_Fails_Consume()
    {
        var record = CreateSampleRecord();

        // Fast-forward past execute_before
        var executeBefore = DateTimeOffset.Parse(record.ExecuteBefore);
        _clock.UtcNow = executeBefore.AddSeconds(1);

        Action act = () => _authService.Consume(record, "step-001");
        act.Should().Throw<AuthorizationSecurityException>()
            .WithMessage("*deadline expired*");
    }

    [Fact]
    public void Expired_Verify_Deadline_Fails_Verify()
    {
        var record = CreateSampleRecord();

        // Consume within execute deadline
        _authService.Consume(record, "step-001");

        // Fast-forward past verify_before
        var verifyBefore = DateTimeOffset.Parse(record.VerifyBefore);
        _clock.UtcNow = verifyBefore.AddSeconds(1);

        Action act = () => _authService.Verify(record);
        act.Should().Throw<AuthorizationSecurityException>()
            .WithMessage("*deadline expired*");
    }

    [Fact]
    public void Reused_Nonce_Fails_Second_Consume()
    {
        var record = CreateSampleRecord();

        // First consume succeeds
        var auth1 = _authService.Consume(record, "step-001");
        auth1.Should().NotBeNull();

        // Second consume with the same record / nonce must fail
        Action act = () => _authService.Consume(record, "step-001");
        act.Should().Throw<AuthorizationSecurityException>()
            .WithMessage("*already consumed*");
    }

    [Fact]
    public void Wrong_Step_Fails_Consume()
    {
        var record = CreateSampleRecord();

        // Attempting to consume with a stepId different from the authorized step_id
        Action act = () => _authService.Consume(record, "step-WRONG-999");
        act.Should().Throw<AuthorizationSecurityException>()
            .WithMessage("*Step ID mismatch*");
    }

    [Fact]
    public void Never_Consumed_Record_Must_Not_Verify()
    {
        var record = CreateSampleRecord();

        // Attempting to verify without first having been consumed by an executor
        Action act = () => _authService.Verify(record);
        act.Should().Throw<AuthorizationSecurityException>()
            .WithMessage("*never consumed*");
    }

    [Fact]
    public void Tampered_Canonical_Args_Fails()
    {
        var record = CreateSampleRecord();

        // Tamper with canonical_args
        var tamperedArgs = new Dictionary<string, object?>(record.CanonicalArgs)
        {
            ["path"] = @"C:\different\path.txt"
        };
        var tamperedRecord = record with { CanonicalArgs = tamperedArgs };

        Action consumeAct = () => _authService.Consume(tamperedRecord, "step-001");
        consumeAct.Should().Throw<AuthorizationSecurityException>()
            .WithMessage("*HMAC*");

        Action verifyAct = () => _authService.Verify(tamperedRecord);
        verifyAct.Should().Throw<AuthorizationSecurityException>()
            .WithMessage("*HMAC*");
    }

    [Fact]
    public void Key_Regenerated_Invalidates_Existing_Authorizations()
    {
        var record = CreateSampleRecord();

        // Key is regenerated in key store
        _keyStore.RegenerateKey();

        Action consumeAct = () => _authService.Consume(record, "step-001");
        consumeAct.Should().Throw<AuthorizationSecurityException>()
            .WithMessage("*HMAC*");

        Action verifyAct = () => _authService.Verify(record);
        verifyAct.Should().Throw<AuthorizationSecurityException>()
            .WithMessage("*HMAC*");
    }

    private AuthorizationRecordDto CreateSampleRecord()
    {
        return _authService.IssueAuthorization(
            taskId: "task-001",
            stepId: "step-001",
            toolId: "fs.create_file",
            rawArgs: new Dictionary<string, object?> { ["path"] = @"C:\test.txt", ["content"] = "hello" },
            expected: new Dictionary<string, object?> { ["exists"] = true },
            allowedEffects: new List<string> { @"create:C:\test.txt" },
            watchScopes: new List<string> { @"C:\test.txt" },
            risk: "medium",
            approvedBy: "user");
    }
}
