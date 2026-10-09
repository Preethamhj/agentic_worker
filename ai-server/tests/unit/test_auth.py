import pytest
from fastapi.testclient import TestClient

from jarvis_ai.api.main import app

client = TestClient(app)


def test_auth_unset_environment_token(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.delenv("JARVIS_TOKEN", raising=False)
    response = client.post(
        "/v1/plan",
        headers={"X-Jarvis-Token": "any-token"},
        json={"schema_version": 1, "session_id": "s1", "text": "create folder x", "locale": "en-US"},
    )
    assert response.status_code == 500
    assert "JARVIS_TOKEN environment variable is not configured" in response.json()["detail"]


def test_auth_missing_header(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("JARVIS_TOKEN", "correct-secret-token")
    response = client.post(
        "/v1/plan",
        json={"schema_version": 1, "session_id": "s1", "text": "create folder x", "locale": "en-US"},
    )
    assert response.status_code in (401, 422)


def test_auth_wrong_token(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("JARVIS_TOKEN", "correct-secret-token")
    response = client.post(
        "/v1/plan",
        headers={"X-Jarvis-Token": "wrong-token"},
        json={"schema_version": 1, "session_id": "s1", "text": "create folder x", "locale": "en-US"},
    )
    assert response.status_code == 401
    assert "Invalid or missing X-Jarvis-Token header" in response.json()["detail"]


def test_auth_valid_token(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setenv("JARVIS_TOKEN", "correct-secret-token")
    response = client.post(
        "/v1/plan",
        headers={"X-Jarvis-Token": "correct-secret-token"},
        json={"schema_version": 1, "session_id": "s1", "text": "create folder test_folder", "locale": "en-US"},
    )
    assert response.status_code == 200
    data = response.json()
    assert data["status"] == "ok"
