import re
from typing import Any, Literal

from jarvis_ai.registry.loader import get_tool_registry

DATA_FIELDS = {"content", "message"}
ALLOWED_APPS = {"notepad", "calc", "calculator", "mspaint", "paint"}


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
        tool = self.registry.get_tool(action)
        if not tool:
            return False, "unsupported", f"Tool '{action}' is not supported", {}

        if not tool.enabled:
            return False, "unsupported", f"Tool '{action}' is currently disabled", {}

        # 1. Validate args against registry schema (types, required fields, additionalProperties)
        valid, err = self.registry.validate_args(action, raw_args)
        if not valid:
            return False, "clarify", err, {}

        # 2. Validate app.launch app_id pattern against allowlist
        if action == "app.launch":
            app_id = str(raw_args.get("app_id", "")).lower()
            if app_id not in ALLOWED_APPS:
                return (
                    False,
                    "unsupported",
                    f"Application '{app_id}' is not in the allowlist ({', '.join(sorted(ALLOWED_APPS))})",
                    {},
                )

        # 3. Scan non-data parameter fields for command injection patterns
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
            if key in DATA_FIELDS:
                # Do NOT scan data fields (e.g. file content) for command words
                continue
            is_danger, match_str = contains_dangerous_cmd(val)
            if is_danger:
                return (
                    False,
                    "unsupported",
                    f"Command-like or dangerous argument detected in parameter '{key}': '{match_str}'",
                    {},
                )

        return True, "ok", None, raw_args
