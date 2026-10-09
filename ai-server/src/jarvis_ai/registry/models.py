from typing import Any, Literal

from pydantic import BaseModel, ConfigDict, Field


class ToolDefinition(BaseModel):
    model_config = ConfigDict(extra="forbid")

    tool_id: str
    version: str
    risk: Literal["low", "medium", "high"]
    enabled: bool
    description: str
    args_schema: dict[str, Any]
    verification_template: dict[str, Any]


class ToolRegistryModel(BaseModel):
    model_config = ConfigDict(extra="forbid")

    schema_version: int = Field(default=1)
    tools: list[ToolDefinition]
