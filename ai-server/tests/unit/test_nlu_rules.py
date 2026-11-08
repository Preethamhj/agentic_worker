import datetime

import pytest

from jarvis_ai.nlu.rules import parse_rules_fast_path


@pytest.mark.parametrize(
    ("text", "expected_hour", "expected_minute"),
    [
        ("remind me drink water at 5:30 pm tomorrow", 17, 30),
        ("remind me drink water at 3 am tomorrow", 3, 0),
        ("remind me drink water at 14:15 tomorrow", 14, 15),
    ],
)
def test_absolute_reminder_uses_requested_local_time(
    text: str, expected_hour: int, expected_minute: int
) -> None:
    before = datetime.datetime.now().astimezone()
    intent = parse_rules_fast_path(text)
    after = datetime.datetime.now().astimezone()
    assert intent is not None
    result = datetime.datetime.fromisoformat(intent.entities["time_iso"]).astimezone()
    assert result.date() in {
        before.date() + datetime.timedelta(days=1),
        after.date() + datetime.timedelta(days=1),
    }
    assert (result.hour, result.minute, result.second) == (expected_hour, expected_minute, 0)


@pytest.mark.parametrize(("hour", "day_offset"), [(11, 1), (13, 0)])
def test_absolute_reminder_without_date_uses_next_occurrence(
    monkeypatch: pytest.MonkeyPatch, hour: int, day_offset: int
) -> None:
    real_datetime = datetime.datetime
    noon = real_datetime.now().astimezone().replace(hour=12, minute=0, second=0, microsecond=0)

    class FrozenDateTime(real_datetime):
        @classmethod
        def now(cls, tz: datetime.tzinfo | None = None) -> datetime.datetime:
            return noon.astimezone(tz) if tz else noon.replace(tzinfo=None)

    monkeypatch.setattr(datetime, "datetime", FrozenDateTime)
    intent = parse_rules_fast_path(f"remind me drink water at {hour}:00")
    assert intent is not None
    result = real_datetime.fromisoformat(intent.entities["time_iso"]).astimezone()
    assert result.date() == noon.date() + datetime.timedelta(days=day_offset)
    assert (result.hour, result.minute) == (hour, 0)


def test_rule_create_file() -> None:
    text = "create file test.txt with content hello world"
    intent = parse_rules_fast_path(text)
    assert intent is not None
    assert intent.action == "fs.create_file"
    assert intent.entities["path"] == "test.txt"
    assert intent.entities["content"] == "hello world"
    assert intent.risk.level == "medium"


def test_rule_create_folder() -> None:
    text = "create folder MyFolder"
    intent = parse_rules_fast_path(text)
    assert intent is not None
    assert intent.action == "fs.create_folder"
    assert intent.entities["path"] == "MyFolder"


def test_rule_search_files() -> None:
    text = "search files *.txt"
    intent = parse_rules_fast_path(text)
    assert intent is not None
    assert intent.action == "fs.search_files"
    assert intent.entities["pattern"] == "*.txt"


def test_rule_launch_app() -> None:
    text = "open app notepad"
    intent = parse_rules_fast_path(text)
    assert intent is not None
    assert intent.action == "app.launch"
    assert intent.entities["app_id"] == "notepad"


def test_rule_schedule_reminder() -> None:
    text = "remind me buy groceries at 5:00 pm"
    intent = parse_rules_fast_path(text)
    assert intent is not None
    assert intent.action == "task.schedule_reminder"
    assert "task_name" not in intent.entities
    assert intent.entities["message"] == "buy groceries"


def test_rule_schedule_reminder_in_relative_minutes() -> None:
    before = datetime.datetime.now(datetime.UTC)
    intent = parse_rules_fast_path("remind me Jarvis smoke delivery in 2 minutes")
    after = datetime.datetime.now(datetime.UTC)

    assert intent is not None
    assert intent.action == "task.schedule_reminder"
    assert intent.entities["message"] == "Jarvis smoke delivery"
    scheduled_at = datetime.datetime.fromisoformat(intent.entities["time_iso"])
    assert scheduled_at.microsecond == 0
    assert before + datetime.timedelta(minutes=2) - datetime.timedelta(seconds=1) <= scheduled_at
    assert scheduled_at <= after + datetime.timedelta(minutes=2)


@pytest.mark.parametrize("delay", ["0", "1441"])
def test_rule_schedule_reminder_rejects_out_of_range_relative_delay(delay: str) -> None:
    intent = parse_rules_fast_path(f"remind me unsupported in {delay} minutes")

    assert intent is None
