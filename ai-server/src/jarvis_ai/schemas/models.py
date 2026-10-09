from typing import Any, Literal

from pydantic import BaseModel, ConfigDict, Field


class PlanRequestModel(BaseModel):
    model_config = ConfigDict(extra="forbid")

    schema_version: int = Field(default=1)
    session_id: str
    text: str
    locale: str = Field(default="en-US")


class RiskModel(BaseModel):
    model_config = ConfigDict(extra="forbid")

    level: Literal["low", "medium", "high"]
    reasons: list[str]


class IntentModel(BaseModel):
    model_config = ConfigDict(extra="forbid")

    schema_version: int = Field(default=1)
    intent_id: str
    kind: Literal["command", "query", "chat"]
    action: str
    confidence: float = Field(ge=0.0, le=1.0)
    entities: dict[str, Any]
    risk: RiskModel
    source: Literal["rules", "llm"]


class ToolCallModel(BaseModel):
    model_config = ConfigDict(extra="forbid")

    schema_version: int = Field(default=1)
    tool_id: str
    args: dict[str, Any]


class CheckModel(BaseModel):
    model_config = ConfigDict(extra="forbid")

    type: Literal["file_exists", "file_hash", "dir_exists", "process_running", "task_exists", "trigger_matches"]
    params: dict[str, Any]


class VerificationPlanModel(BaseModel):
    model_config = ConfigDict(extra="forbid")

    schema_version: int = Field(default=1)
    checks: list[CheckModel]
    watch_scopes: list[str]
    window_seconds: int = Field(default=30)


class PlanStepModel(BaseModel):
    model_config = ConfigDict(extra="forbid")

    schema_version: int = Field(default=1)
    step_id: str
    order: int
    description: str
    depends_on: list[str]
    tool_call: ToolCallModel
    verification_plan: VerificationPlanModel
    advisory_risk: Literal["low", "medium", "high"]


class PlanResponseModel(BaseModel):
    model_config = ConfigDict(extra="forbid")

    schema_version: int = Field(default=1)
    trace_id: str
    status: Literal["ok", "clarify", "unsupported", "error"]
    intent: IntentModel
    steps: list[PlanStepModel]
    message: str


class ReportStepSummaryModel(BaseModel):
    model_config = ConfigDict(extra="forbid")

    step_id: str
    status: str
    verification_status: str
    summary: str


class ReportRequestModel(BaseModel):
    model_config = ConfigDict(extra="forbid")

    schema_version: int = Field(default=1)
    session_id: str
    task_id: str
    steps: list[ReportStepSummaryModel]


class ReportResponseModel(BaseModel):
    model_config = ConfigDict(extra="forbid")

    schema_version: int = Field(default=1)
    message: str
    memory_updated: bool
