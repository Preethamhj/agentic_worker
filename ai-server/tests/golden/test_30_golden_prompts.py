import pytest

from jarvis_ai.llm.fake import FakeLlmClient
from jarvis_ai.nlu.intent_parser import IntentParser
from jarvis_ai.planning.planner import Planner
from jarvis_ai.schemas.models import PlanRequestModel

GOLDEN_PROMPTS = [
    # 1-5: fs.create_file
    ("create file doc.txt with content Hello World", "ok", "fs.create_file"),
    ("create file notes.txt with content test notes", "ok", "fs.create_file"),
    ("create file ಕಡತ.txt with content ಕನ್ನಡ ಪಠ್ಯ", "ok", "fs.create_file"),  # Kannada Unicode
    ("create file report.md with content # Header", "ok", "fs.create_file"),
    ("write file data.csv content 1,2,3", "ok", "fs.create_file"),

    # 6-10: fs.create_folder
    ("create folder Projects", "ok", "fs.create_folder"),
    ("create folder WorkDocs", "ok", "fs.create_folder"),
    ("create folder ಫೋಲ್ಡರ್", "ok", "fs.create_folder"),  # Kannada Unicode
    ("make directory TempData", "ok", "fs.create_folder"),
    ("create directory Archives", "ok", "fs.create_folder"),

    # 11-15: fs.search_files
    ("search files *.txt", "ok", "fs.search_files"),
    ("find files *.pdf", "ok", "fs.search_files"),
    ("list files *.png", "ok", "fs.search_files"),
    ("search files *.log in C:\\Logs", "ok", "fs.search_files"),
    ("find files *.docx", "ok", "fs.search_files"),

    # 16-20: app.launch
    ("open app notepad", "ok", "app.launch"),
    ("launch app calc", "ok", "app.launch"),
    ("open mspaint", "ok", "app.launch"),
    ("launch calculator", "ok", "app.launch"),
    ("open paint", "ok", "app.launch"),

    # 21-25: task.schedule_reminder
    ("remind me Standup Meeting at 9:00 am", "ok", "task.schedule_reminder"),
    ("schedule reminder Buy Milk at 6:00 pm", "ok", "task.schedule_reminder"),
    ("remind me ಜ್ಞಾಪನೆ at 10:00 am", "ok", "task.schedule_reminder"),  # Kannada text
    ("schedule reminder Submit Report at 5:00 pm", "ok", "task.schedule_reminder"),
    ("remind me Team Sync at 2:00 pm", "ok", "task.schedule_reminder"),

    # 26-27: Raw commands / command injection (must be rejected / unsupported)
    ("powershell -c Remove-Item -Recurse C:\\Windows", "unsupported", "unsupported"),
    ("cmd.exe /c del /f /q C:\\*", "unsupported", "unsupported"),

    # 28-30: Unknown / unsupported requests
    ("tell me a joke about computers", "unsupported", "unsupported"),
    ("what is the weather in Bengaluru today", "unsupported", "unsupported"),
    ("play some relaxed jazz music", "unsupported", "unsupported"),
]

assert len(GOLDEN_PROMPTS) == 30, "Golden prompts list must contain exactly 30 prompts"


@pytest.fixture
def planner() -> Planner:
    fake_llm = FakeLlmClient()
    intent_parser = IntentParser(fake_llm)
    return Planner(intent_parser)


@pytest.mark.parametrize("prompt_text,expected_status,expected_action", GOLDEN_PROMPTS)
def test_golden_prompt(planner: Planner, prompt_text: str, expected_status: str, expected_action: str) -> None:
    req = PlanRequestModel(schema_version=1, session_id="golden-sess-001", text=prompt_text, locale="en-US")
    response = planner.create_plan(req)

    assert response.schema_version == 1
    assert response.status == expected_status
    if expected_status == "ok":
        assert len(response.steps) > 0
        assert response.steps[0].tool_call.tool_id == expected_action
    else:
        assert len(response.steps) == 0


LLM_RETRY_PROMPTS = [
    # 5 prompts that bypass rule fast-path and trigger LLM retry logic (bad JSON first, valid JSON second)
    (
        "Please locate all documents ending with .docx in my root folder",
        "fs.search_files",
        "INVALID_JSON_MOCK_1: {bad json",
        {
            "schema_version": 1,
            "intent_id": "intent-001",
            "kind": "query",
            "action": "fs.search_files",
            "confidence": 0.95,
            "entities": {"pattern": "*.docx", "root_path": ""},
            "risk": {"level": "low", "reasons": []},
            "source": "llm",
        },
    ),
    (
        "Can you generate a folder named ProjectsArchive for me",
        "fs.create_folder",
        '{"action": "fs.create_folder"}',  # Missing required fields 'kind' & 'confidence'
        {
            "schema_version": 1,
            "intent_id": "intent-002",
            "kind": "command",
            "action": "fs.create_folder",
            "confidence": 0.9,
            "entities": {"path": "ProjectsArchive"},
            "risk": {"level": "medium", "reasons": ["creates directory"]},
            "source": "llm",
        },
    ),
    (
        "Write a note called todo.txt saying Buy groceries",
        "fs.create_file",
        "MALFORMED_OUTPUT_NOT_JSON",
        {
            "schema_version": 1,
            "intent_id": "intent-003",
            "kind": "command",
            "action": "fs.create_file",
            "confidence": 0.92,
            "entities": {"path": "todo.txt", "content": "Buy groceries"},
            "risk": {"level": "medium", "reasons": ["writes file"]},
            "source": "llm",
        },
    ),
    (
        "Could you launch the text editor for me",
        "app.launch",
        '{"intent_id": 123}',  # Wrong data type for intent_id
        {
            "schema_version": 1,
            "intent_id": "intent-004",
            "kind": "command",
            "action": "app.launch",
            "confidence": 0.88,
            "entities": {"app_id": "notepad"},
            "risk": {"level": "medium", "reasons": ["launches application"]},
            "source": "llm",
        },
    ),
    (
        "Set up a calendar alert for team sync at 4pm today",
        "task.schedule_reminder",
        "Not a JSON object",
        {
            "schema_version": 1,
            "intent_id": "intent-005",
            "kind": "command",
            "action": "task.schedule_reminder",
            "confidence": 0.95,
            "entities": {"task_name": "Reminder", "time_iso": "2026-10-08T16:00:00Z", "message": "team sync"},
            "risk": {"level": "medium", "reasons": ["schedules task"]},
            "source": "llm",
        },
    ),
]



@pytest.mark.parametrize("prompt_text,expected_tool,bad_response,valid_response", LLM_RETRY_PROMPTS)
def test_llm_path_retry_logic(
    prompt_text: str, expected_tool: str, bad_response: str, valid_response: dict
) -> None:
    fake_llm = FakeLlmClient()
    # Script response sequence: bad JSON first, valid JSON second
    fake_llm.set_response(prompt_text, [bad_response, valid_response])

    parser = IntentParser(fake_llm)
    planner = Planner(parser)

    req = PlanRequestModel(schema_version=1, session_id="retry-test-sess", text=prompt_text, locale="en-US")
    response = planner.create_plan(req)

    assert response.status == "ok"
    assert len(response.steps) > 0
    assert response.steps[0].tool_call.tool_id == expected_tool

    # Verify LLM was called twice (attempt 0 failed schema validation, attempt 1 succeeded after retry)
    assert len(fake_llm.call_history) == 2
    # Verify retry prompt includes validation error feedback
    retry_prompt = fake_llm.call_history[1]["prompt"]
    assert "Previous attempt produced invalid output" in retry_prompt
