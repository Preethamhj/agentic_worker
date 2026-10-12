using System.Text.Json.Serialization;

namespace Jarvis.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record PlanRequestDto(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("session_id")] string SessionId,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("locale")] string Locale = "en-US"
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record RiskDto(
    [property: JsonPropertyName("level")] string Level,
    [property: JsonPropertyName("reasons")] List<string> Reasons
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record IntentDto(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("intent_id")] string IntentId,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("confidence")] double Confidence,
    [property: JsonPropertyName("entities")] Dictionary<string, object?> Entities,
    [property: JsonPropertyName("risk")] RiskDto Risk,
    [property: JsonPropertyName("source")] string Source
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record ToolCallDto(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("tool_id")] string ToolId,
    [property: JsonPropertyName("args")] Dictionary<string, object?> Args
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record CheckDto(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("params")] Dictionary<string, object?> Params
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record VerificationPlanDto(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("checks")] List<CheckDto> Checks,
    [property: JsonPropertyName("watch_scopes")] List<string> WatchScopes,
    [property: JsonPropertyName("window_seconds")] int WindowSeconds = 30
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record PlanStepDto(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("step_id")] string StepId,
    [property: JsonPropertyName("order")] int Order,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("depends_on")] List<string> DependsOn,
    [property: JsonPropertyName("tool_call")] ToolCallDto ToolCall,
    [property: JsonPropertyName("verification_plan")] VerificationPlanDto VerificationPlan,
    [property: JsonPropertyName("advisory_risk")] string AdvisoryRisk
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record PlanResponseDto(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("trace_id")] string TraceId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("intent")] IntentDto Intent,
    [property: JsonPropertyName("steps")] List<PlanStepDto> Steps,
    [property: JsonPropertyName("message")] string Message
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record AuthorizationRecordDto(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("auth_id")] string AuthId,
    [property: JsonPropertyName("task_id")] string TaskId,
    [property: JsonPropertyName("step_id")] string StepId,
    [property: JsonPropertyName("tool_id")] string ToolId,
    [property: JsonPropertyName("canonical_args")] Dictionary<string, object?> CanonicalArgs,
    [property: JsonPropertyName("expected")] Dictionary<string, object?> Expected,
    [property: JsonPropertyName("allowed_effects")] List<string> AllowedEffects,
    [property: JsonPropertyName("watch_scopes")] List<string> WatchScopes,
    [property: JsonPropertyName("risk")] string Risk,
    [property: JsonPropertyName("approved_by")] string ApprovedBy,
    [property: JsonPropertyName("issued_at")] string IssuedAt,
    [property: JsonPropertyName("expires_at")] string ExpiresAt,
    [property: JsonPropertyName("execute_before")] string ExecuteBefore,
    [property: JsonPropertyName("verify_before")] string VerifyBefore,
    [property: JsonPropertyName("nonce")] string Nonce,
    [property: JsonPropertyName("hmac")] string Hmac
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record ExecutionReceiptDto(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("step_id")] string StepId,
    [property: JsonPropertyName("auth_id")] string AuthId,
    [property: JsonPropertyName("started_at")] string StartedAt,
    [property: JsonPropertyName("completed_at")] string CompletedAt,
    [property: JsonPropertyName("exit_code")] int ExitCode,
    [property: JsonPropertyName("reported_success")] bool ReportedSuccess,
    [property: JsonPropertyName("stdout")] string Stdout,
    [property: JsonPropertyName("stderr")] string Stderr
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record EvidenceDto(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("evidence_id")] string EvidenceId,
    [property: JsonPropertyName("step_id")] string StepId,
    [property: JsonPropertyName("check")] CheckDto Check,
    [property: JsonPropertyName("collector")] string Collector,
    [property: JsonPropertyName("collected_at")] string CollectedAt,
    [property: JsonPropertyName("observed")] Dictionary<string, object?> Observed,
    [property: JsonPropertyName("expected")] Dictionary<string, object?> Expected,
    [property: JsonPropertyName("match")] bool Match,
    [property: JsonPropertyName("nonce")] string Nonce,
    [property: JsonPropertyName("record_sha256")] string RecordSha256
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record VerificationStageResultDto(
    [property: JsonPropertyName("stage")] int Stage,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("passed")] bool Passed,
    [property: JsonPropertyName("detail")] string Detail
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record VerificationResultDto(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("step_id")] string StepId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("stages")] List<VerificationStageResultDto> Stages,
    [property: JsonPropertyName("evidence")] List<EvidenceDto> Evidence,
    [property: JsonPropertyName("side_effects")] List<string> SideEffects,
    [property: JsonPropertyName("reasons")] List<string> Reasons,
    [property: JsonPropertyName("duration_ms")] double DurationMs
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record ReportStepSummaryDto(
    [property: JsonPropertyName("step_id")] string StepId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("verification_status")] string VerificationStatus,
    [property: JsonPropertyName("summary")] string Summary
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record ReportRequestDto(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("session_id")] string SessionId,
    [property: JsonPropertyName("task_id")] string TaskId,
    [property: JsonPropertyName("steps")] List<ReportStepSummaryDto> Steps
);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public record ReportResponseDto(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("memory_updated")] bool MemoryUpdated
);
