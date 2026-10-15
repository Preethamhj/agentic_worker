import pytest

from jarvis_ai.planning.tool_mapper import ToolMapper


@pytest.fixture
def tool_mapper() -> ToolMapper:
    return ToolMapper()


def test_tool_mapper_valid_args(tool_mapper: ToolMapper) -> None:
    is_valid, status, err, args = tool_mapper.map_and_validate(
        "fs.create_file", {"path": "test.txt", "content": "hello"}
    )
    assert is_valid is True
    assert status == "ok"
    assert err is None
    assert args == {"path": "test.txt", "content": "hello"}


def test_tool_mapper_allows_command_words_in_data_fields(tool_mapper: ToolMapper) -> None:
    # Command words in file content or reminder message are DATA fields and MUST NOT be rejected
    is_valid, status, err, _ = tool_mapper.map_and_validate(
        "fs.create_file",
        {"path": "script.py", "content": "import subprocess\nsubprocess.run(['powershell', '-c', 'rm -rf /'])\ncmd.exe del /f"},
    )
    assert is_valid is True
    assert status == "ok"
    assert err is None


def test_tool_mapper_wrong_arg_types(tool_mapper: ToolMapper) -> None:
    # path expected string, given int
    is_valid, status, err, _ = tool_mapper.map_and_validate("fs.create_folder", {"path": 12345})
    assert is_valid is False
    assert status == "clarify"
    assert "validation failed" in err.lower()

    # app_id expected string, given dict
    is_valid, status, err, _ = tool_mapper.map_and_validate("app.launch", {"app_id": {"nested": "calc"}})
    assert is_valid is False
    assert status == "clarify"

    # pattern expected string, given list
    is_valid, status, err, _ = tool_mapper.map_and_validate("fs.search_files", {"pattern": ["*.txt", "*.doc"]})
    assert is_valid is False
    assert status == "clarify"


def test_tool_mapper_extra_unallowed_fields(tool_mapper: ToolMapper) -> None:
    is_valid, status, err, _ = tool_mapper.map_and_validate(
        "fs.create_folder", {"path": "my_folder", "unsupported_extra_field": "injected"}
    )
    assert is_valid is False
    assert status == "clarify"
    assert "unexpected" in err.lower() or "additional" in err.lower() or "validation failed" in err.lower()


def test_tool_mapper_missing_required_fields(tool_mapper: ToolMapper) -> None:
    # missing 'content' for fs.create_file
    is_valid, status, err, _ = tool_mapper.map_and_validate("fs.create_file", {"path": "test.txt"})
    assert is_valid is False
    assert status == "clarify"
    assert "required" in err.lower() or "validation failed" in err.lower()


@pytest.mark.parametrize(
    "message",
    [
        "line one\nline two",
        "line one\rline two",
        "column\tvalue",
        "null\0byte",
        "x" * 201,
    ],
)
def test_tool_mapper_rejects_invalid_reminder_message(
    tool_mapper: ToolMapper, message: str
) -> None:
    is_valid, status, err, _ = tool_mapper.map_and_validate(
        "task.schedule_reminder",
        {
            "task_name": "Review",
            "time_iso": "2026-10-16T09:00:00Z",
            "message": message,
        },
    )

    assert is_valid is False
    assert status == "clarify"
    assert err is not None


def test_tool_mapper_accepts_unicode_reminder_message_within_limit(
    tool_mapper: ToolMapper,
) -> None:
    message = 'Quote " & 100% ಕನ್ನಡ ನಮಸ್ಕಾರ'
    is_valid, status, err, validated = tool_mapper.map_and_validate(
        "task.schedule_reminder",
        {
            "task_name": "Review",
            "time_iso": "2026-10-16T09:00:00Z",
            "message": message,
        },
    )

    assert is_valid is True
    assert status == "ok"
    assert err is None
    assert validated["message"] == message


@pytest.mark.parametrize(
    "tool_id,dangerous_args",
    [
        ("fs.create_file", {"path": "powershell.exe", "content": "safe"}),
        ("fs.create_folder", {"path": "C:\\Windows\\cmd.exe"}),
        ("fs.search_files", {"pattern": "*.txt; rm -rf /", "root_path": "C:\\"}),
        ("app.launch", {"app_id": "iex dangerous_script"}),
        ("task.schedule_reminder", {"task_name": "del /f /q C:\\*", "time_iso": "2026-10-08T10:00:00Z", "message": "msg"}),
    ],
)
def test_tool_mapper_command_injection_rejection(
    tool_mapper: ToolMapper, tool_id: str, dangerous_args: dict
) -> None:
    is_valid, status, err, _ = tool_mapper.map_and_validate(tool_id, dangerous_args)
    assert is_valid is False
    assert status == "unsupported"
    assert "command-like or dangerous argument detected" in err.lower() or "not in the allowlist" in err.lower()


def test_tool_mapper_disabled_tool(tool_mapper: ToolMapper) -> None:
    is_valid, status, err, _ = tool_mapper.map_and_validate(
        "shell.run_powershell", {"script": "Get-Process"}
    )
    assert is_valid is False
    assert status == "unsupported"
    assert "disabled" in err.lower()


def test_tool_mapper_unknown_tool(tool_mapper: ToolMapper) -> None:
    is_valid, status, err, _ = tool_mapper.map_and_validate(
        "unknown.nonexistent_tool", {"arg": "val"}
    )
    assert is_valid is False
    assert status == "unsupported"
    assert "not supported" in err.lower()
