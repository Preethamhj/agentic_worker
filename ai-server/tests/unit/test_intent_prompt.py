import json

from jarvis_ai.llm.fake import FakeLlmClient
from jarvis_ai.nlu.intent_parser import IntentParser
from jarvis_ai.registry.loader import get_tool_registry


def test_llm_receives_registry_argument_schema_for_search() -> None:
    client = FakeLlmClient()
    client.set_response("Please find matching documents", {
        "intent_id": "test-intent",
        "kind": "query",
        "action": "fs.search_files",
        "confidence": 0.9,
        "entities": {"root_path": r"C:\test", "pattern": "*.txt"},
        "risk": {"level": "low", "reasons": []},
        "source": "llm",
    })

    intent = IntentParser(client).parse("Please find matching documents")

    assert intent.action == "fs.search_files"
    prompt = client.call_history[0]["system_prompt"]
    tool = get_tool_registry().get_tool("fs.search_files")
    assert tool is not None
    assert json.dumps(tool.args_schema, sort_keys=True) in prompt
    assert "shell.run_powershell" not in prompt
