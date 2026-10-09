import json
import os
from typing import Any

from jarvis_ai.core.errors import RegistryError
from jarvis_ai.registry.models import ToolDefinition, ToolRegistryModel


class ToolRegistry:

    def __init__(self, registry_path: str | None = None) -> None:
        if registry_path is None:
            registry_path = os.path.abspath(
                os.path.join(
                    os.path.dirname(__file__), "../../../../config/tools.registry.json"
                )
            )
        self.registry_path = registry_path
        self._tools: dict[str, ToolDefinition] = {}
        self.reload()

    def reload(self) -> None:
        if not os.path.exists(self.registry_path):
            raise RegistryError(f"Tool registry file not found: {self.registry_path}")

        try:
            with open(self.registry_path, "r", encoding="utf-8") as f:
                data = json.load(f)
            model = ToolRegistryModel.model_validate(data)
            self._tools = {tool.tool_id: tool for tool in model.tools}
        except Exception as e:
            raise RegistryError(f"Failed to load tool registry: {e}") from e

    def get_tool(self, tool_id: str) -> ToolDefinition | None:
        return self._tools.get(tool_id)

    def is_enabled(self, tool_id: str) -> bool:
        tool = self.get_tool(tool_id)
        return tool.enabled if tool else False

    def validate_args(self, tool_id: str, args: dict[str, Any]) -> tuple[bool, str | None]:
        tool = self.get_tool(tool_id)
        if not tool:
            return False, f"Tool '{tool_id}' not found in registry"
        if not tool.enabled:
            return False, f"Tool '{tool_id}' is disabled"

        import jsonschema

        schema = tool.args_schema
        try:
            jsonschema.validate(instance=args, schema=schema)
            return True, None
        except jsonschema.ValidationError as err:
            return False, f"Argument validation failed for tool '{tool_id}': {err.message}"



_registry_instance: ToolRegistry | None = None


def get_tool_registry() -> ToolRegistry:
    global _registry_instance
    if _registry_instance is None:
        _registry_instance = ToolRegistry()
    return _registry_instance
