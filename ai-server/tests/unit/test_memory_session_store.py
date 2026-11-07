import os
import tempfile
from pathlib import Path

import pytest

from jarvis_ai.memory.session_store import SessionStore


def test_session_isolation() -> None:
    with tempfile.TemporaryDirectory() as tmp_dir:
        db_path = os.path.join(tmp_dir, "test_session_memory.db")
        store = SessionStore(db_path=db_path)

        session_a = "session-A-123"
        session_b = "session-B-456"

        store.add_message(session_a, "user", "Secret message for Session A")
        store.add_message(session_b, "user", "Secret message for Session B")

        messages_a = store.get_recent_messages(session_a)
        messages_b = store.get_recent_messages(session_b)

        assert len(messages_a) == 1
        assert len(messages_b) == 1
        assert messages_a[0]["content"] == "Secret message for Session A"
        assert messages_b[0]["content"] == "Secret message for Session B"

        # Assert zero leakage
        assert not any("Session B" in m["content"] for m in messages_a)
        assert not any("Session A" in m["content"] for m in messages_b)


def test_session_store_uses_absolute_configured_database_path(
    monkeypatch, tmp_path: Path
) -> None:
    db_path = tmp_path / "session_memory.db"
    monkeypatch.setenv("JARVIS_SESSION_DB_PATH", str(db_path))

    store = SessionStore()

    assert store.db_path == str(db_path)
    assert db_path.is_file()


def test_session_store_rejects_relative_configured_database_path(monkeypatch) -> None:
    monkeypatch.setenv("JARVIS_SESSION_DB_PATH", "session_memory.db")

    with pytest.raises(ValueError, match="must be an absolute path"):
        SessionStore()
