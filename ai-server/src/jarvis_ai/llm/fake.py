import json
from typing import Any


class FakeLlmClient:

    def __init__(self, healthy: bool = True) -> None:
        self.healthy = healthy
        self.responses: dict[str, list[str]] = {}
        self.call_history: list[dict[str, Any]] = []

    def set_response(self, prompt_substring: str, response_json_or_str: Any) -> None:
        if isinstance(response_json_or_str, list):
            seq = [json.dumps(x) if isinstance(x, (dict, list)) else str(x) for x in response_json_or_str]
            self.responses[prompt_substring] = seq
        elif isinstance(response_json_or_str, dict):
            self.responses[prompt_substring] = [json.dumps(response_json_or_str)]
        else:
            self.responses[prompt_substring] = [str(response_json_or_str)]

    def generate(
        self,
        prompt: str,
        system_prompt: str | None = None,
        json_schema: dict[str, Any] | None = None,
        temperature: float = 0.0,
    ) -> str:
        self.call_history.append(
            {
                "prompt": prompt,
                "system_prompt": system_prompt,
                "json_schema": json_schema,
                "temperature": temperature,
            }
        )

        for key, response_list in self.responses.items():
            if key.lower() in prompt.lower() and len(response_list) > 0:
                if len(response_list) > 1:
                    return response_list.pop(0)
                return response_list[0]

        # Default fallback JSON
        return json.dumps({"status": "ok", "message": "Fake LLM default response"})

    def is_healthy(self) -> bool:
        return self.healthy
