using System.Text.Json;
using FluentAssertions;
using Jarvis.Contracts;
using Xunit;

namespace Jarvis.Contracts.Tests;

public class ContractDtoTests
{
    private static readonly string ExamplesDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "contracts", "examples"));

    [Fact]
    public void PlanRequestDto_Deserializes_ValidFixture()
    {
        var json = File.ReadAllText(Path.Combine(ExamplesDir, "PlanRequest.valid1.json"));
        var dto = JsonSerializer.Deserialize<PlanRequestDto>(json);

        dto.Should().NotBeNull();
        dto!.SchemaVersion.Should().Be(1);
        dto.SessionId.Should().Be("sess-001");
        dto.Text.Should().Be("create folder test in documents");
        dto.Locale.Should().Be("en-US");
    }

    [Fact]
    public void PlanRequestDto_Rejects_UnknownProperties()
    {
        var json = """
        {
          "schema_version": 1,
          "session_id": "sess_1",
          "text": "hello",
          "locale": "en-US",
          "unknown_field": "disallowed"
        }
        """;

        Action act = () => JsonSerializer.Deserialize<PlanRequestDto>(json);
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void PlanResponseDto_Deserializes_ValidFixture()
    {
        var json = File.ReadAllText(Path.Combine(ExamplesDir, "PlanResponse.valid1.json"));
        var dto = JsonSerializer.Deserialize<PlanResponseDto>(json);

        dto.Should().NotBeNull();
        dto!.SchemaVersion.Should().Be(1);
        dto.TraceId.Should().Be("trace-001");
        dto.Status.Should().Be("ok");
        dto.Intent.Action.Should().Be("fs.create_folder");
    }

    [Fact]
    public void PlanResponseDto_Rejects_UnknownProperties()
    {
        var json = """
        {
          "schema_version": 1,
          "trace_id": "tr_1",
          "status": "ok",
          "intent": {
            "schema_version": 1,
            "intent_id": "i_1",
            "kind": "command",
            "action": "create",
            "confidence": 0.95,
            "entities": {},
            "risk": { "level": "low", "reasons": [] },
            "source": "rules"
          },
          "steps": [],
          "message": "ready",
          "unmapped_extra_key": 123
        }
        """;

        Action act = () => JsonSerializer.Deserialize<PlanResponseDto>(json);
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void AuthorizationRecordDto_Deserializes_ValidFixture()
    {
        var json = File.ReadAllText(Path.Combine(ExamplesDir, "AuthorizationRecord.valid1.json"));
        var dto = JsonSerializer.Deserialize<AuthorizationRecordDto>(json);

        dto.Should().NotBeNull();
        dto!.SchemaVersion.Should().Be(1);
        dto.AuthId.Should().Be("auth-001");
        dto.ToolId.Should().Be("fs.create_folder");
        dto.ExecuteBefore.Should().NotBeNullOrEmpty();
        dto.VerifyBefore.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void AuthorizationRecordDto_Rejects_UnknownProperties()
    {
        var json = """
        {
          "schema_version": 1,
          "auth_id": "auth_1",
          "task_id": "task_1",
          "step_id": "step_1",
          "tool_id": "fs.create_file",
          "canonical_args": {},
          "expected": {},
          "allowed_effects": [],
          "watch_scopes": [],
          "risk": "medium",
          "approved_by": "user",
          "issued_at": "2026-10-09T00:00:00Z",
          "expires_at": "2026-10-09T00:05:00Z",
          "execute_before": "2026-10-09T00:01:00Z",
          "verify_before": "2026-10-09T00:02:00Z",
          "nonce": "nonce_1",
          "hmac": "dummy_hmac",
          "bogus_extra_field": "malicious"
        }
        """;

        Action act = () => JsonSerializer.Deserialize<AuthorizationRecordDto>(json);
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void ExecutionReceiptDto_Deserializes_ValidFixture()
    {
        var json = File.ReadAllText(Path.Combine(ExamplesDir, "ExecutionReceipt.valid1.json"));
        var dto = JsonSerializer.Deserialize<ExecutionReceiptDto>(json);

        dto.Should().NotBeNull();
        dto!.SchemaVersion.Should().Be(1);
        dto.StepId.Should().Be("step-001");
        dto.ReportedSuccess.Should().BeTrue();
    }

    [Fact]
    public void VerificationResultDto_Deserializes_ValidFixture()
    {
        var json = File.ReadAllText(Path.Combine(ExamplesDir, "VerificationResult.valid1.json"));
        var dto = JsonSerializer.Deserialize<VerificationResultDto>(json);

        dto.Should().NotBeNull();
        dto!.SchemaVersion.Should().Be(1);
        dto.Status.Should().Be("VERIFIED");
        dto.Stages.Should().HaveCount(1);
    }

    [Fact]
    public void ReportRequestDto_Deserializes_ValidFixture()
    {
        var json = File.ReadAllText(Path.Combine(ExamplesDir, "ReportRequest.valid1.json"));
        var dto = JsonSerializer.Deserialize<ReportRequestDto>(json);

        dto.Should().NotBeNull();
        dto!.SchemaVersion.Should().Be(1);
        dto.TaskId.Should().Be("task-001");
        dto.Steps.Single().Reason.Should().Be("Directory evidence matched the signed authorization.");
    }

    [Fact]
    public void ReportResponseDto_Deserializes_ValidFixture()
    {
        var json = File.ReadAllText(Path.Combine(ExamplesDir, "ReportResponse.valid1.json"));
        var dto = JsonSerializer.Deserialize<ReportResponseDto>(json);

        dto.Should().NotBeNull();
        dto!.SchemaVersion.Should().Be(1);
        dto.MemoryUpdated.Should().BeTrue();
    }
}
