import re
from typing import Any, Literal

from jarvis_ai.registry.loader import get_tool_registry


class ToolMapper:

    def __init__(self) -> None:
        self.registry = get_tool_registry()

    def map_and_validate(
        self, action: str, raw_args: dict[str, Any]
    ) -> tuple[bool, Literal["ok", "clarify", "unsupported", "error"], str | None, dict[str, Any]]:
        """
        Validates tool_id and args.
        Returns (is_valid, status, error_message, validated_args).
        Status can be: 'ok', 'clarify', 'unsupported', 'error'.
        """
        def contains_dangerous_cmd(val: Any) -> tuple[bool, str]:
            if isinstance(val, str):
                if re.search(
                    r"(?:powershell|cmd\.exe|Invoke-Expression|iex\s|rm\s+-rf|del\s+/f)",
                    val,
                    re.IGNORECASE,
                ):
                    return True, val
            elif isinstance(val, list):
                for item in val:
                    found, match = contains_dangerous_cmd(item)
                    if found:
                        return True, match
            elif isinstance(val, dict):
                for v in val.values():
                    found, match = contains_dangerous_cmd(v)
                    if found:
                        return True, match
            return False, ""

        for key, val in raw_args.items():
            is_danger, match_str = contains_dangerous_cmd(val)
            if is_danger:
                return (
                    False,
                    "unsupported",
                    f"Command-like or dangerous argument detected in parameter '{key}': '{match_str}'",
                    {},
                )


        tool = self.registry.get_tool(action)
        if not tool:
            return False, "unsupported", f"Tool '{action}' is not supported", {}

        if not tool.enabled:
            return False, "unsupported", f"Tool '{action}' is currently disabled", {}

        # Validate args against registry schema
        valid, err = self.registry.validate_args(action, raw_args)
        if not valid:
            return False, "clarify", err, {}

        return True, "ok", None, raw_args
