import json
import os
from typing import Any
from urllib.parse import urlparse


def is_loopback(host_or_url: str) -> bool:
    if not host_or_url:
        return False
    if "://" in host_or_url:
        parsed = urlparse(host_or_url)
        hostname = parsed.hostname or ""
    else:
        clean = host_or_url.strip("[]")
        if ":" in clean and clean.count(":") == 1:
            hostname = clean.split(":")[0]
        else:
            hostname = clean
    hostname = hostname.strip("[]")
    return hostname in ("127.0.0.1", "localhost", "::1", "0:0:0:0:0:0:0:1", "localhost.localdomain")



class Settings:

    def __init__(self, settings_path: str | None = None) -> None:
        if settings_path is None:
            settings_path = os.path.abspath(
                os.path.join(os.path.dirname(__file__), "../../../../config/settings.json")
            )
        self.settings_path = settings_path
        self._data: dict[str, Any] = {}
        self.reload()

    def reload(self) -> None:
        if os.path.exists(self.settings_path):
            with open(self.settings_path, "r", encoding="utf-8") as f:
                self._data = json.load(f)
        else:
            self._data = {
                "schema_version": 1,
                "model": "qwen2.5:7b-instruct",
                "ai_server_port": 8000,
                "ai_server_host": "127.0.0.1",
                "ollama_host": "http://127.0.0.1:11434",
                "timeouts": {
                    "llm_seconds": 30,
                    "step_execution_seconds": 60,
                    "escalation_seconds": 120,
                    "verification_seconds": 30,
                },
                "windows": {"verification_window_seconds": 30},
            }
        self.validate()

    def validate(self) -> None:
        if not is_loopback(self.ai_server_host):
            raise ValueError(
                f"ai_server_host '{self.ai_server_host}' is not a valid loopback address. Outbound non-loopback bindings are forbidden."
            )
        if not is_loopback(self.ollama_host):
            raise ValueError(
                f"ollama_host '{self.ollama_host}' is not a valid loopback address. Outbound non-loopback calls are forbidden."
            )

    @property
    def model(self) -> str:
        return str(self._data.get("model", "qwen2.5:7b-instruct"))

    @property
    def ai_server_port(self) -> int:
        return int(self._data.get("ai_server_port", 8000))

    @property
    def ai_server_host(self) -> str:
        return str(self._data.get("ai_server_host", "127.0.0.1"))

    @property
    def ollama_host(self) -> str:
        return str(self._data.get("ollama_host", "http://127.0.0.1:11434"))

    @property
    def llm_timeout_seconds(self) -> int:
        timeouts = self._data.get("timeouts", {})
        return int(timeouts.get("llm_seconds", 30))


_settings_instance: Settings | None = None


def get_settings() -> Settings:
    global _settings_instance
    if _settings_instance is None:
        _settings_instance = Settings()
    return _settings_instance

