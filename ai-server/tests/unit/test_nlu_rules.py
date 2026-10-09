from jarvis_ai.nlu.rules import parse_rules_fast_path


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
    assert intent.entities["message"] == "buy groceries"
