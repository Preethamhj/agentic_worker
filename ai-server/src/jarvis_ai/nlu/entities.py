import datetime
import re
from typing import Any


def extract_entities(text: str, action: str | None = None) -> dict[str, Any]:
    entities: dict[str, Any] = {}

    # File / folder path extraction
    path_match = re.search(r'(?:path|folder|file|in)\s+["\']?([a-zA-Z0-9_\-\.\:\/\\ ]+)["\']?', text, re.IGNORECASE)
    if path_match:
        entities["path"] = path_match.group(1).strip()

    # Content extraction
    content_match = re.search(r'with content\s+["\']?([^"\']+)["\']?', text, re.IGNORECASE)
    if content_match:
        entities["content"] = content_match.group(1)

    # Search pattern extraction
    pattern_match = re.search(r'(?:search|find|pattern)\s+["\']?([^\s"\'\?]+)["\']?', text, re.IGNORECASE)
    if pattern_match:
        entities["pattern"] = pattern_match.group(1)

    # App name extraction
    app_match = re.search(r'(?:launch|open|start)\s+(notepad|calc|mspaint|calculator|paint)', text, re.IGNORECASE)
    if app_match:
        app_raw = app_match.group(1).lower()
        if app_raw in ("calc", "calculator"):
            entities["app_id"] = "calc"
        elif app_raw in ("mspaint", "paint"):
            entities["app_id"] = "mspaint"
        else:
            entities["app_id"] = "notepad"

    # Time ISO extraction
    time_match = re.search(r'at\s+(\d{1,2}:\d{2}\s*(?:am|pm)?)', text, re.IGNORECASE)
    if time_match:
        entities["time_iso"] = datetime.datetime.now(datetime.UTC).isoformat()

    return entities
