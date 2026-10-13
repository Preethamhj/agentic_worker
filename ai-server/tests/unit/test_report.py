from pathlib import Path

import pytest
from fastapi.testclient import TestClient

import jarvis_ai.api.routes_report as routes_report_module
from jarvis_ai.api.main import app
from jarvis_ai.memory.session_store import SessionStore

client = TestClient(app)

_REPORT_TOKEN = "test-secret-token"
_TASK_ID = "task-idemp-test"
_SESSION_ID = "session-idemp-test"


@pytest.fixture(autouse=True)
def isolate_report_state(monkeypatch: pytest.MonkeyPatch, tmp_path: Path) -> None:
    """
    Per-test isolation:
    - Set the JARVIS_TOKEN env var.
    - Swap session_store for a fresh in-memory SQLite instance (tmp file).
    - Clear the module-level idempotency cache.
    """
    monkeypatch.setenv("JARVIS_TOKEN", _REPORT_TOKEN)

    tmp_db = str(tmp_path / "session_test.db")
    fresh_store = SessionStore(db_path=tmp_db)

    monkeypatch.setattr(routes_report_module, "session_store", fresh_store)
    routes_report_module._idempotency_cache.clear()


def _report_payload(task_id: str = _TASK_ID, session_id: str = _SESSION_ID) -> dict:
    return {
        "schema_version": 1,
        "task_id": task_id,
        "session_id": session_id,
        "steps": [
            {
                "step_id": "step-1",
                "status": "completed",
                "verification_status": "VERIFIED",
                "summary": "created file notes.txt",
            }
        ],
    }


# ── Basic endpoint test ───────────────────────────────────────────────────────

def test_report_endpoint_basic() -> None:
    response = client.post(
        "/v1/report",
        headers={"X-Jarvis-Token": _REPORT_TOKEN},
        json=_report_payload("task-basic-1", "session-basic-1"),
    )
    assert response.status_code == 200
    data = response.json()
    assert data["schema_version"] == 1
    assert "message" in data
    assert data["memory_updated"] is True


# ── Idempotency-Key deduplication ─────────────────────────────────────────────

def test_report_endpoint_honors_idempotency_key() -> None:
    payload = _report_payload()
    headers = {
        "X-Jarvis-Token": _REPORT_TOKEN,
        "Idempotency-Key": "unique-key-abc-123",
    }

    # First call: should succeed and record in session store
    res1 = client.post("/v1/report", headers=headers, json=payload)
    assert res1.status_code == 200
    data1 = res1.json()

    # Session store has exactly one entry for this task
    fresh_store = routes_report_module.session_store
    summaries = fresh_store.get_task_summaries(_SESSION_ID)
    assert len(summaries) == 1, "First call must persist to session store"
    assert summaries[0]["task_id"] == _TASK_ID

    # Second duplicate call with same Idempotency-Key
    res2 = client.post("/v1/report", headers=headers, json=payload)
    assert res2.status_code == 200
    data2 = res2.json()

    # Response must be identical
    assert data1 == data2, "Idempotent calls must return the same response"

    # Session store must still have exactly ONE entry (not duplicated)
    summaries_after = fresh_store.get_task_summaries(_SESSION_ID)
    assert len(summaries_after) == 1, "Duplicate call must not add a second session store entry"


def test_report_endpoint_different_idempotency_keys_create_two_entries() -> None:
    headers_a = {"X-Jarvis-Token": _REPORT_TOKEN, "Idempotency-Key": "key-A"}
    headers_b = {"X-Jarvis-Token": _REPORT_TOKEN, "Idempotency-Key": "key-B"}

    res_a = client.post("/v1/report", headers=headers_a, json=_report_payload("task-a", "sess-diff"))
    res_b = client.post("/v1/report", headers=headers_b, json=_report_payload("task-b", "sess-diff"))

    assert res_a.status_code == 200
    assert res_b.status_code == 200

    fresh_store = routes_report_module.session_store
    summaries = fresh_store.get_task_summaries("sess-diff")
    assert len(summaries) == 2, "Different idempotency keys must produce separate entries"


def test_report_endpoint_without_idempotency_key_creates_entries_each_time() -> None:
    headers = {"X-Jarvis-Token": _REPORT_TOKEN}

    client.post("/v1/report", headers=headers, json=_report_payload("task-no-key", "sess-no-key"))
    client.post("/v1/report", headers=headers, json=_report_payload("task-no-key", "sess-no-key"))

    fresh_store = routes_report_module.session_store
    summaries = fresh_store.get_task_summaries("sess-no-key")
    assert len(summaries) == 2, "Without an Idempotency-Key, every call creates a new session entry"
