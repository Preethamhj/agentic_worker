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
    public void Verified_Authorization_Cannot_Be_Replayed()
    {
        var record = CreateSampleRecord();
        _authService.Consume(record, "step-001");
        _authService.Verify(record).Should().BeTrue();

        Action replay = () => _authService.Verify(record);
        replay.Should().Throw<AuthorizationSecurityException>()
            .WithMessage("*already verified*");
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

    [Fact]
    public async Task Atomic_Consume_With_50_Concurrent_Calls_Gives_Exactly_One_Success()
    {
        var auditStore = new InMemoryAuditStore();
        var authService = new AuthorizationService(_keyStore, _clock, auditStore);

        var record = authService.IssueAuthorization(
            taskId: "task-concurrency",
            stepId: "step-concurrency",
            toolId: "fs.create_file",
            rawArgs: new Dictionary<string, object?> { ["path"] = @"C:\test.txt", ["content"] = "hello" },
            risk: "medium",
            approvedBy: "user");

        const int callerCount = 50;
        var successCount = 0;
        var failureCount = 0;
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        var verifiedTokens = new System.Collections.Concurrent.ConcurrentBag<VerifiedAuthorization>();

        var tasks = Enumerable.Range(0, callerCount).Select(_ => Task.Run(() =>
        {
            try
            {
                var auth = authService.Consume(record, "step-concurrency");
                Interlocked.Increment(ref successCount);
                verifiedTokens.Add(auth);
            }
            catch (AuthorizationSecurityException ex)
            {
                Interlocked.Increment(ref failureCount);
                exceptions.Add(ex);
            }
        })).ToArray();

        await Task.WhenAll(tasks);

        successCount.Should().Be(1, "Exactly one concurrent consume call must succeed");
        failureCount.Should().Be(49, "All 49 other concurrent calls must be refused");
        verifiedTokens.Should().HaveCount(1);
        exceptions.Should().OnlyContain(e => e.Message.Contains("already consumed") || e.Message.Contains("single-use nonce"));

        // Check IAuditStore lifecycle persistence
        var lifecycles = auditStore.GetAuthorizationLifecycle(record.AuthId);
        lifecycles.Should().ContainSingle(l => l.State == "Issued");
        lifecycles.Should().ContainSingle(l => l.State == "Consumed");
    }

    [Fact]
    public void Forged_Copy_Of_VerifiedAuthorization_Is_Refused()
    {
        var record = CreateSampleRecord();
        var validAuth = _authService.Consume(record, "step-001");

        // Executor verification of valid auth succeeds
        _authService.Consume(validAuth, "step-001");

        // Attempt to forge a copy via reflection with tampered args
        var tamperedArgs = new Dictionary<string, object?>(record.CanonicalArgs)
        {
            ["path"] = @"C:\tampered\path.txt"
        };
        var forgedRecord = record with { CanonicalArgs = tamperedArgs };

        var ctor = typeof(VerifiedAuthorization).GetConstructors(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)[0];
        var forgedAuth = (VerifiedAuthorization)ctor.Invoke(new object[] { forgedRecord });

        // Executor calling AuthorizationService.Consume(forgedAuth) must refuse it!
        Action act = () => _authService.Consume(forgedAuth, "step-001");
        act.Should().Throw<AuthorizationSecurityException>()
            .WithMessage("*HMAC*");
    }

    [Fact]
    public void Restart_Test_Consuming_Persisted_Nonce_On_New_Service_Fails()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"jarvis_audit_restart_{Guid.NewGuid():N}.db");
        try
        {
            var keyStore = new InMemoryDpapiKeyStore();
            AuthorizationRecordDto record;

            // 1. Initial service instance issues and consumes the nonce
            using (var store1 = new SqliteAuditStore(tempDb))
            {
                var authService1 = new AuthorizationService(keyStore, SystemClock.Instance, store1);
                record = authService1.IssueAuthorization(
                    taskId: "task-restart-01",
                    stepId: "step-01",
                    toolId: "fs.create_file",
                    rawArgs: new Dictionary<string, object?> { ["path"] = @"C:\allowed\doc.txt", ["content"] = "hello" },
                    risk: "medium"
                );

                var verified = authService1.Consume(record, "step-01");
                verified.Should().NotBeNull();
            }

            // 2. Restart: Create a completely new SqliteAuditStore and AuthorizationService on the same SQLite file
            using (var store2 = new SqliteAuditStore(tempDb))
            {
                var authService2 = new AuthorizationService(keyStore, SystemClock.Instance, store2);

                // Consuming the same record again on the new service instance must fail
                Action act = () => authService2.Consume(record, "step-01");
                act.Should().Throw<AuthorizationSecurityException>()
                    .WithMessage("*already consumed*");
            }
        }
        finally
        {
            try
            {
                if (File.Exists(tempDb))
                {
                    File.Delete(tempDb);
                }
            }
            catch { }
        }
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
