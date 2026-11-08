import datetime

import httpx
import ollama
import pytest

from jarvis_ai.llm.fake import FakeLlmClient
from jarvis_ai.nlu.intent_parser import IntentParser
from jarvis_ai.planning.planner import Planner
from jarvis_ai.schemas.models import PlanRequestModel


@pytest.mark.parametrize("prompt", [
    "remind me to drink water at 25:00 today",
    "remind me to drink water at an unspecified time",
    "remind me to drink water at 12:99 pm today",
])
def test_invalid_absolute_reminder_time_requires_clarification(prompt: str) -> None:
    client = FakeLlmClient()
    response = Planner(IntentParser(client)).create_plan(
        PlanRequestModel(session_id="test", text=prompt)
    )
    assert response.status == "clarify"
    assert not response.steps
    assert not client.call_history


def test_past_today_reminder_requires_clarification(monkeypatch: pytest.MonkeyPatch) -> None:
    real_datetime = datetime.datetime

    class FrozenDateTime(real_datetime):
        @classmethod
        def now(cls, tz: datetime.tzinfo | None = None) -> datetime.datetime:
            local = real_datetime.now().astimezone().replace(hour=12, minute=0, second=0, microsecond=0)
            return local.astimezone(tz) if tz else local.replace(tzinfo=None)

    monkeypatch.setattr(datetime, "datetime", FrozenDateTime)
    prompt = "remind me to drink water at 11:00 today"
    response = Planner(IntentParser(FakeLlmClient())).create_plan(
        PlanRequestModel(session_id="test", text=prompt)
    )
    assert response.status == "clarify"
    assert not response.steps


@pytest.mark.parametrize("prompt", [
    "schedule a task of drinking water at 3 am every day",
    "remind me to drink water at 3 am daily",
    "schedule reminder meeting at 9 am every weekday",
])
def test_recurring_reminder_is_not_silently_planned_as_one_time(prompt: str) -> None:
    client = FakeLlmClient()
    response = Planner(IntentParser(client)).create_plan(
        PlanRequestModel(session_id="test", text=prompt)
    )
    assert response.status == "clarify"
    assert "recurring" in response.message.lower()
    assert not response.steps


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
