import httpx
import ollama
import pytest

from jarvis_ai.llm.fake import FakeLlmClient
from jarvis_ai.nlu.intent_parser import IntentParser
from jarvis_ai.planning.planner import Planner
from jarvis_ai.schemas.models import PlanRequestModel


def test_invalid_model_output_returns_clarification_after_retries() -> None:
    client = FakeLlmClient()
    client.set_response("Please find matching documents", "invalid json")
    response = Planner(IntentParser(client)).create_plan(
        PlanRequestModel(session_id="test", text="Please find matching documents")
    )
    assert response.status == "clarify"
    assert not response.steps
    assert len(client.call_history) == 3


def test_model_timeout_returns_clear_error_without_executable_steps() -> None:
    class TimeoutClient(FakeLlmClient):
        def generate(self, *args: object, **kwargs: object) -> str:
            raise httpx.ReadTimeout("model timed out")

    response = Planner(IntentParser(TimeoutClient())).create_plan(
        PlanRequestModel(session_id="test", text="Please find matching documents")
    )
    assert response.status == "error"
    assert "timed out" in response.message.lower()
    assert not response.steps


@pytest.mark.parametrize(
    "error",
    [httpx.ConnectError("unavailable"), httpx.RemoteProtocolError("bad response"), ollama.ResponseError("missing model")],
)
def test_model_connection_failure_returns_error_without_executable_steps(error: Exception) -> None:
    class UnavailableClient(FakeLlmClient):
        def generate(self, *args: object, **kwargs: object) -> str:
            raise error

    response = Planner(IntentParser(UnavailableClient())).create_plan(
        PlanRequestModel(session_id="test", text="Please find matching documents")
    )
    assert response.status == "error"
    assert "unavailable" in response.message.lower()
    assert not response.steps
