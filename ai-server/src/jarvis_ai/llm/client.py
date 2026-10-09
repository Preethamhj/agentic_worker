from typing import Any, Protocol

import ollama

from jarvis_ai.core.config import get_settings


class ILlmClient(Protocol):

    def generate(
        self,
        prompt: str,
        system_prompt: str | None = None,
        json_schema: dict[str, Any] | None = None,
        temperature: float = 0.0,
    ) -> str:
        ...

    def is_healthy(self) -> bool:
        ...


class OllamaClient:

    def __init__(self, host: str | None = None) -> None:
        self.settings = get_settings()
        self.client = ollama.Client(host=host)

    def generate(
        self,
        prompt: str,
        system_prompt: str | None = None,
        json_schema: dict[str, Any] | None = None,
        temperature: float = 0.0,
    ) -> str:
        messages = []
        if system_prompt:
            messages.append({"role": "system", "content": system_prompt})
        messages.append({"role": "user", "content": prompt})

        format_arg: Any = "json"
        if json_schema is not None:
            format_arg = json_schema

        response = self.client.chat(
            model=self.settings.model,
            messages=messages,
            format=format_arg,
            options={"temperature": temperature},
        )
        return str(response["message"]["content"])

    def is_healthy(self) -> bool:
        try:
            self.client.list()
            return True
        except Exception:  # noqa: BLE001
            return False
