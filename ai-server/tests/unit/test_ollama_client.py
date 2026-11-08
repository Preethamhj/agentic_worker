from unittest.mock import patch

import pytest

from jarvis_ai.core.config import get_settings
from jarvis_ai.llm.client import OllamaClient


def test_default_client_uses_configured_host_and_model() -> None:
    settings = get_settings()
    with patch("jarvis_ai.llm.client.ollama.Client") as client_factory:
        client = OllamaClient()

    client_factory.assert_called_once_with(
        host=settings.ollama_host, timeout=settings.llm_timeout_seconds
    )
    assert client.model == settings.model


def test_client_rejects_non_loopback_host() -> None:
    with patch("jarvis_ai.llm.client.ollama.Client") as client_factory:
        with pytest.raises(ValueError, match="loopback"):
            OllamaClient(host="http://example.invalid:11434")
    client_factory.assert_not_called()


def test_client_accepts_explicit_loopback_host() -> None:
    with patch("jarvis_ai.llm.client.ollama.Client") as client_factory:
        OllamaClient(host="http://127.0.0.1:12345")
    client_factory.assert_called_once_with(
        host="http://127.0.0.1:12345", timeout=get_settings().llm_timeout_seconds
    )
