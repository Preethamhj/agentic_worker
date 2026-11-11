import re
from typing import Any, Literal
from urllib.parse import urlsplit

from pydantic import BaseModel, ConfigDict, Field, field_validator, model_validator


class PlanRequestModel(BaseModel):
    model_config = ConfigDict(extra="forbid")

    schema_version: int = Field(default=1)
    session_id: str
    text: str
    locale: str = Field(default="en-US")


class BrowserResolveRequestModel(BaseModel):
    model_config = ConfigDict(extra="forbid")

    schema_version: int = Field(default=1)
    session_id: str = Field(min_length=1, max_length=80)
    text: str = Field(min_length=1, max_length=512)


class BrowserPageLinkModel(BaseModel):
    model_config = ConfigDict(extra="forbid")

    id: str = Field(pattern=r"^l[0-9a-f]{12}$")
    label: str = Field(min_length=1, max_length=500)

    @field_validator("label")
    @classmethod
    def reject_urls_and_credentials(cls, value: str) -> str:
        if _contains_url_or_credential(value):
            raise ValueError("Page link labels must not contain URLs or credential-like content.")
        return value


class BrowserPageContextModel(BaseModel):
    model_config = ConfigDict(extra="forbid")

    title: str = Field(max_length=500)
    visible_text: str = Field(max_length=15_000)
    links: list[BrowserPageLinkModel] = Field(max_length=300)

    @field_validator("title", "visible_text")
    @classmethod
    def reject_urls_and_credentials(cls, value: str) -> str:
        if _contains_url_or_credential(value):
            raise ValueError("Page context must not contain URLs or credential-like content.")
        return value

    @model_validator(mode="after")
    def reject_duplicate_ids(self) -> "BrowserPageContextModel":
        ids = [link.id for link in self.links]
        if len(set(ids)) != len(ids):
            raise ValueError("Temporary page link IDs must be unique.")
        return self


def _contains_url_or_credential(value: str) -> bool:
    return bool(
        re.search(
            r"(?i)\b(?:https?://|www\.)\S+|\b(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,}(?::\d+)?(?:/[^\s]*)?(?:\?[^\s]*)?(?:#[^\s]*)?|\b(?:\d{1,3}\.){3}\d{1,3}(?::\d+)?(?:/[^\s]*)?",
            value,
        )
        or re.search(
            r"(?i)\b(?:password|passwd|passcode|secret|client[_ -]?secret|credential|access[_ -]?token|refresh[_ -]?token|id[_ -]?token|token|jwt|api[_ -]?key|authorization|cookie|session[_ -]?id|auth(?:entication)?[_ -]?code|verification[_ -]?code)\b",
            value,
        )
        or re.search(
            r"\b(?:(?=[A-Za-z0-9_+/=-]{24,}\b)(?=[A-Za-z0-9_+/=-]*[A-Z])(?=[A-Za-z0-9_+/=-]*[a-z])(?=[A-Za-z0-9_+/=-]*\d)[A-Za-z0-9_+/=-]{24,}|(?=[A-Za-z0-9_-]{32,}\b)(?=[A-Za-z0-9_-]*\d)[A-Za-z0-9_-]{32,})\b",
            value,
        )
    )


class BrowserPlanRequestModel(BaseModel):
    model_config = ConfigDict(extra="forbid")

    schema_version: int = Field(default=1)
    session_id: str = Field(min_length=1, max_length=80)
    text: str = Field(min_length=1, max_length=512)
    page: BrowserPageContextModel | None = None


class BrowserResolveResponseModel(BaseModel):
    model_config = ConfigDict(extra="forbid")

    schema_version: int = Field(default=1)
    status: Literal["open", "clarify", "error"]
    url: str | None = None
    message: str = Field(max_length=240)

    @field_validator("url")
    @classmethod
    def validate_url(cls, value: str | None) -> str | None:
        if value is None:
            return None
        parsed = urlsplit(value)
        if (
            parsed.scheme.lower() not in {"http", "https"}
            or not parsed.hostname
            or parsed.username is not None
            or parsed.password is not None
        ):
            raise ValueError("Browser destinations must be credential-free HTTP or HTTPS URLs.")
        try:
            parsed.port
        except ValueError as error:
            raise ValueError("Browser destination has an invalid port.") from error
        return value

    @model_validator(mode="after")
    def validate_status_fields(self) -> "BrowserResolveResponseModel":
        if (self.status == "open") != (self.url is not None):
            raise ValueError("Only open responses may include a URL, and open responses require one.")
        if self.status == "clarify" and not self.message.strip():
            raise ValueError("Clarification responses require a message.")
        return self


class BrowserSearchQueryResponseModel(BaseModel):
    model_config = ConfigDict(extra="forbid")

    schema_version: int = Field(default=1)
    status: Literal["ready", "fallback"]
    query: str = Field(min_length=1, max_length=256)
    message: str = Field(max_length=240)

    @field_validator("query")
    @classmethod
    def validate_query(cls, value: str) -> str:
        normalized = value.strip()
        if not normalized or any(ord(character) < 32 for character in normalized):
            raise ValueError("Search query must be a non-empty single-line string.")
        return normalized


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
    reason: str = Field(min_length=1, max_length=240)


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
