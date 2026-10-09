from typing import Any

from jarvis_ai.registry.loader import get_tool_registry
from jarvis_ai.schemas.models import CheckModel, VerificationPlanModel


class VerificationPlanner:

    def __init__(self) -> None:
        self.registry = get_tool_registry()

    def build_verification_plan(
        self, tool_id: str, args: dict[str, Any]
    ) -> VerificationPlanModel:
        tool = self.registry.get_tool(tool_id)
        checks: list[CheckModel] = []
        watch_scopes: list[str] = []

        if tool and tool.verification_template:
            template_checks = tool.verification_template.get("checks", [])
            for check in template_checks:
                check_type = check.get("type", "dir_exists")
                raw_params = check.get("params", {})
                params: dict[str, Any] = {}

                # Resolve template param references
                for key, val in raw_params.items():
                    if isinstance(val, str) and val.endswith("_arg"):
                        arg_name = val.replace("_arg", "")
                        params[key] = args.get(arg_name, "")
                    else:
                        params[key] = val

                checks.append(CheckModel(type=check_type, params=params))

        # Build watch scopes based on path or task args
        if "path" in args:
            watch_scopes.append(str(args["path"]))
        elif "root_path" in args:
            watch_scopes.append(str(args["root_path"]))

        return VerificationPlanModel(
            checks=checks, watch_scopes=watch_scopes, window_seconds=30
        )
