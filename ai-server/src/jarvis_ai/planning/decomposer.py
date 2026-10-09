from typing import Any

from jarvis_ai.schemas.models import IntentModel


class Decomposer:

    def decompose(self, intent: IntentModel) -> list[dict[str, Any]]:
        """Decomposes an intent into sub-step tool invocations."""
        if intent.action == "unsupported":
            return []

        # Rules path is deterministic
        return [
            {
                "tool_id": intent.action,
                "args": intent.entities,
                "description": f"Execute action {intent.action}",
            }
        ]
