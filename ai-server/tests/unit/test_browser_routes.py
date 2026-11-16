from fastapi.testclient import TestClient

from jarvis_ai.api.main import app
from jarvis_ai.api import routes_browser
from jarvis_ai.llm.fake import FakeLlmClient


client = TestClient(app)

def test_browser_plan_matches_explicit_link_label_when_model_would_clarify(monkeypatch) -> None:
    monkeypatch.setenv("JARVIS_TOKEN", "test-token")
    llm = FakeLlmClient()
    llm.set_response(
        "open external  hdd link",
        {"status": "clarify", "query": None, "link_id": None, "message": "Please clarify."},
    )
    monkeypatch.setattr(routes_browser, "browser_llm_client", llm)
    response = client.post(
        "/v1/browser/plan",
        headers={"X-Jarvis-Token": "test-token"},
        json={
            "session_id": "test-session",
            "text": "open external  hdd link",
            "page": {
                "title": "Test",
                "visible_text": "Available categories",
                "links": [
                    {"id": "l123456789abc", "label": "Mobiles"},
                    {"id": "labcdef123456", "label": "External HDD"},
                ],
            },
        },
    )
    assert response.status_code == 200
    assert response.json()["status"] == "open_link"
    assert response.json()["link_id"] == "labcdef123456"
    assert not llm.call_history


def test_browser_plan_does_not_guess_between_duplicate_labels(monkeypatch) -> None:
    monkeypatch.setenv("JARVIS_TOKEN", "test-token")
    llm = FakeLlmClient()
    llm.set_response(
        "open mobiles link",
        {"status": "open_link", "query": None, "link_id": "l123456789abc", "message": ""},
    )
    monkeypatch.setattr(routes_browser, "browser_llm_client", llm)
    response = client.post(
        "/v1/browser/plan",
        headers={"X-Jarvis-Token": "test-token"},
        json={
            "session_id": "test-session",
            "text": "open mobiles link",
            "page": {
                "title": "Test",
                "visible_text": "",
                "links": [
                    {"id": "l123456789abc", "label": "Mobiles"},
                    {"id": "labcdef123456", "label": "Mobiles"},
                ],
            },
        },
    )
    assert response.json()["status"] == "clarify"
    assert response.json()["link_id"] is None


def test_browser_plan_prefers_exact_requested_label_to_wrong_model_proposal(monkeypatch) -> None:
    monkeypatch.setenv("JARVIS_TOKEN", "test-token")
    llm = FakeLlmClient()
    llm.set_response(
        "open mobiles link",
        {"status": "open_link", "query": None, "link_id": "labcdef123456", "message": ""},
    )
    monkeypatch.setattr(routes_browser, "browser_llm_client", llm)
    response = client.post(
        "/v1/browser/plan",
        headers={"X-Jarvis-Token": "test-token"},
        json={
            "session_id": "test-session",
            "text": "open mobiles link",
            "page": {
                "title": "Test",
                "visible_text": "",
                "links": [
                    {"id": "l123456789abc", "label": "Mobiles"},
                    {"id": "labcdef123456", "label": "Other"},
                ],
            },
        },
    )
    assert response.json()["status"] == "open_link"
    assert response.json()["link_id"] == "l123456789abc"


def test_browser_plan_does_not_resolve_label_from_a_multi_action_instruction(monkeypatch) -> None:
    monkeypatch.setenv("JARVIS_TOKEN", "test-token")
    llm = FakeLlmClient()
    llm.set_response(
        "open mobiles link and buy it",
        {"status": "clarify", "query": None, "link_id": None, "message": "Please clarify."},
    )
    monkeypatch.setattr(routes_browser, "browser_llm_client", llm)
    response = client.post(
        "/v1/browser/plan",
        headers={"X-Jarvis-Token": "test-token"},
        json={
            "session_id": "test-session",
            "text": "open mobiles link and buy it",
            "page": {
                "title": "Test",
                "visible_text": "",
                "links": [{"id": "l123456789abc", "label": "Mobiles"}],
            },
        },
    )
    assert response.json()["status"] == "clarify"
    assert response.json()["link_id"] is None


def test_browser_plan_does_not_replace_missing_explicit_label_with_another_link(monkeypatch) -> None:
    monkeypatch.setenv("JARVIS_TOKEN", "test-token")
    llm = FakeLlmClient()
    llm.set_response(
        "open phones link",
        {"status": "open_link", "query": None, "link_id": "l123456789abc", "message": ""},
    )
    monkeypatch.setattr(routes_browser, "browser_llm_client", llm)
    response = client.post(
        "/v1/browser/plan",
        headers={"X-Jarvis-Token": "test-token"},
        json={
            "session_id": "test-session",
            "text": "open phones link",
            "page": {
                "title": "Test",
                "visible_text": "",
                "links": [{"id": "l123456789abc", "label": "Other"}],
            },
        },
    )
    assert response.json()["status"] == "clarify"
    assert response.json()["link_id"] is None


def test_browser_resolve_returns_validated_url(monkeypatch) -> None:
    monkeypatch.setenv("JARVIS_TOKEN", "test-token")
    llm = FakeLlmClient()
    llm.set_response(
        "Gmail",
        {"status": "open", "url": "https://mail.google.com/", "message": ""},
    )
    monkeypatch.setattr(routes_browser, "browser_llm_client", llm)

    response = client.post(
        "/v1/browser/resolve",
        headers={"X-Jarvis-Token": "test-token"},
        json={"schema_version": 1, "session_id": "test-session", "text": "Gmail"},
    )

    assert response.status_code == 200
    assert response.json() == {
        "schema_version": 1,
        "status": "open",
        "url": "https://mail.google.com/",
        "message": "",
    }
    assert llm.call_history[0]["json_schema"] is not None
    assert llm.call_history[0]["temperature"] == 0.0


def test_browser_resolve_rejects_malformed_url_and_clarifies(monkeypatch) -> None:
    monkeypatch.setenv("JARVIS_TOKEN", "test-token")
    llm = FakeLlmClient()
    llm.set_response(
        "Notepad",
        [
            {"status": "open", "url": "javascript:alert(1)", "message": ""},
            {"status": "clarify", "url": None, "message": "Name a website or web app."},
        ],
    )
    monkeypatch.setattr(routes_browser, "browser_llm_client", llm)

    response = client.post(
        "/v1/browser/resolve",
        headers={"X-Jarvis-Token": "test-token"},
        json={"schema_version": 1, "session_id": "test-session", "text": "Notepad"},
    )

    assert response.status_code == 200
    assert response.json()["status"] == "clarify"
    assert response.json()["url"] is None
    assert "website" in response.json()["message"]
    assert len(llm.call_history) == 2


def test_browser_resolve_requires_authentication(monkeypatch) -> None:
    monkeypatch.setenv("JARVIS_TOKEN", "test-token")

    response = client.post(
        "/v1/browser/resolve",
        json={"schema_version": 1, "session_id": "test-session", "text": "Gmail"},
    )

    assert response.status_code == 401


def test_browser_search_query_returns_ollama_refined_query(monkeypatch) -> None:
    monkeypatch.setenv("JARVIS_TOKEN", "test-token")
    llm = FakeLlmClient()
    llm.set_response(
        "best C++ tutorial",
        {"query": "best beginner C++ tutorial modern C++"},
    )
    monkeypatch.setattr(routes_browser, "browser_llm_client", llm)

    response = client.post(
        "/v1/browser/search-query",
        headers={"X-Jarvis-Token": "test-token"},
        json={
            "schema_version": 1,
            "session_id": "test-session",
            "text": "find me the best C++ tutorial in the internet",
        },
    )

    assert response.status_code == 200
    assert response.json()["status"] == "ready"
    assert response.json()["query"] == "best beginner C++ tutorial modern C++"


def test_browser_search_query_falls_back_to_original_when_ollama_fails(monkeypatch) -> None:
    monkeypatch.setenv("JARVIS_TOKEN", "test-token")
    llm = FakeLlmClient()
    llm.set_response("best C++ tutorial", "not valid json")
    monkeypatch.setattr(routes_browser, "browser_llm_client", llm)

    response = client.post(
        "/v1/browser/search-query",
        headers={"X-Jarvis-Token": "test-token"},
        json={
            "schema_version": 1,
            "session_id": "test-session",
            "text": "best C++ tutorial",
        },
    )

    assert response.status_code == 200
    assert response.json()["status"] == "fallback"
    assert response.json()["query"] == "best C++ tutorial"
    assert response.json()["message"]


def test_browser_plan_clarification_refuses_unsupported_page_inspection(monkeypatch) -> None:
    monkeypatch.setenv("JARVIS_TOKEN", "test-token")
    llm = FakeLlmClient()
    llm.set_response(
        "inspect the complete web page",
        {"status": "clarify", "query": None, "message": "What would you like to inspect?"},
    )
    monkeypatch.setattr(routes_browser, "browser_llm_client", llm)

    response = client.post(
        "/v1/browser/plan",
        headers={"X-Jarvis-Token": "test-token"},
        json={"session_id": "test-session", "text": "inspect the complete web page"},
    )

    assert response.status_code == 200
    assert response.json()["status"] == "clarify"
    assert "Form filling" in response.json()["message"]
    assert "search" in response.json()["message"]


def test_browser_plan_preserves_model_clarification_without_proposing_an_action(monkeypatch) -> None:
    monkeypatch.setenv("JARVIS_TOKEN", "test-token")
    llm = FakeLlmClient()
    llm.set_response(
        "open the external drive",
        {
            "status": "clarify",
            "query": None,
            "link_id": None,
            "message": "Which external drive link did you mean?",
        },
    )
    monkeypatch.setattr(routes_browser, "browser_llm_client", llm)

    response = client.post(
        "/v1/browser/plan",
        headers={"X-Jarvis-Token": "test-token"},
        json={"session_id": "test-session", "text": "open the external drive"},
    )

    assert response.status_code == 200
    assert response.json()["status"] == "clarify"
    assert response.json()["query"] is None
    assert response.json()["link_id"] is None
    assert "Which external drive link did you mean?" in response.json()["message"]
    assert "Form filling" in response.json()["message"]


def test_browser_plan_bounds_long_model_clarification(monkeypatch) -> None:
    monkeypatch.setenv("JARVIS_TOKEN", "test-token")
    llm = FakeLlmClient()
    llm.set_response(
        "open the external drive",
        {"status": "clarify", "query": None, "link_id": None, "message": "a" * 240},
    )
    monkeypatch.setattr(routes_browser, "browser_llm_client", llm)

    response = client.post(
        "/v1/browser/plan",
        headers={"X-Jarvis-Token": "test-token"},
        json={"session_id": "test-session", "text": "open the external drive"},
    )

    assert response.status_code == 200
    assert response.json()["status"] == "clarify"
    assert len(response.json()["message"]) <= 240
    assert "... I can search" in response.json()["message"]


def test_browser_plan_accepts_validated_find_search(monkeypatch) -> None:
    monkeypatch.setenv("JARVIS_TOKEN", "test-token")
    llm = FakeLlmClient()
    llm.set_response(
        "find blue traditional wear for men",
        {"status": "search", "query": "blue traditional wear for men", "message": ""},
    )
    monkeypatch.setattr(routes_browser, "browser_llm_client", llm)

    response = client.post(
        "/v1/browser/plan",
        headers={"X-Jarvis-Token": "test-token"},
        json={"session_id": "test-session", "text": "find blue traditional wear for men"},
    )

    assert response.status_code == 200
    assert response.json()["status"] == "search"
    assert response.json()["query"] == "blue traditional wear for men"
    assert "find" in llm.call_history[0]["prompt"]


def test_browser_plan_retries_when_model_puts_query_in_message(monkeypatch) -> None:
    monkeypatch.setenv("JARVIS_TOKEN", "test-token")
    llm = FakeLlmClient()
    llm.set_response(
        "find blue traditional wear for men",
        [
            {"status": "search", "message": "blue traditional wear for men"},
            {"status": "search", "query": "blue traditional wear for men", "message": ""},
        ],
    )
    monkeypatch.setattr(routes_browser, "browser_llm_client", llm)

    response = client.post(
        "/v1/browser/plan",
        headers={"X-Jarvis-Token": "test-token"},
        json={"session_id": "test-session", "text": "find blue traditional wear for men"},
    )

    assert response.status_code == 200
    assert response.json()["query"] == "blue traditional wear for men"
    assert len(llm.call_history) == 2


def test_browser_plan_uses_page_projection_without_link_destinations(monkeypatch) -> None:
    monkeypatch.setenv("JARVIS_TOKEN", "test-token")
    llm = FakeLlmClient()
    llm.set_response(
        "open the pricing page",
        {"status": "open_link", "query": None, "link_id": "l123456789abc", "message": ""},
    )
    monkeypatch.setattr(routes_browser, "browser_llm_client", llm)

    response = client.post(
        "/v1/browser/plan",
        headers={"X-Jarvis-Token": "test-token"},
        json={
            "schema_version": 1,
            "session_id": "test-session",
            "text": "open the pricing page",
            "page": {
                "title": "Product",
                "visible_text": "The pricing page is available below.",
                "links": [{"id": "l123456789abc", "label": "Pricing"}],
            },
        },
    )

    assert response.status_code == 200
    assert response.json()["status"] == "open_link"
    assert response.json()["link_id"] == "l123456789abc"
    assert "Pricing" in llm.call_history[0]["prompt"]
    assert "href" not in llm.call_history[0]["prompt"]
    assert "https://" not in llm.call_history[0]["prompt"]


def test_browser_plan_rejects_link_id_not_in_approved_snapshot(monkeypatch) -> None:
    monkeypatch.setenv("JARVIS_TOKEN", "test-token")
    llm = FakeLlmClient()
    llm.set_response(
        "open a page",
        {"status": "open_link", "query": None, "link_id": "l000000000000", "message": ""},
    )
    monkeypatch.setattr(routes_browser, "browser_llm_client", llm)

    response = client.post(
        "/v1/browser/plan",
        headers={"X-Jarvis-Token": "test-token"},
        json={
            "schema_version": 1,
            "session_id": "test-session",
            "text": "open a page",
            "page": {
                "title": "Page",
                "visible_text": "Page content",
                "links": [{"id": "l123456789abc", "label": "Known link"}],
            },
        },
    )

    assert response.status_code == 200
    assert response.json()["status"] == "clarify"
    assert response.json()["link_id"] is None


def test_browser_plan_rejects_raw_link_destination_in_page_projection(monkeypatch) -> None:
    monkeypatch.setenv("JARVIS_TOKEN", "test-token")

    response = client.post(
        "/v1/browser/plan",
        headers={"X-Jarvis-Token": "test-token"},
        json={
            "schema_version": 1,
            "session_id": "test-session",
            "text": "open the pricing page",
            "page": {
                "title": "Product",
                "visible_text": "Pricing page",
                "links": [{"id": "l123456789abc", "label": "Pricing", "url": "https://private.example"}],
            },
        },
    )

    assert response.status_code == 422


def test_browser_plan_rejects_credential_like_page_text(monkeypatch) -> None:
    monkeypatch.setenv("JARVIS_TOKEN", "test-token")

    response = client.post(
        "/v1/browser/plan",
        headers={"X-Jarvis-Token": "test-token"},
        json={
            "schema_version": 1,
            "session_id": "test-session",
            "text": "open a page",
            "page": {
                "title": "Account",
                "visible_text": "password=do-not-send",
                "links": [],
            },
        },
    )

    assert response.status_code == 422


def test_browser_plan_rejects_opaque_token_like_page_text(monkeypatch) -> None:
    monkeypatch.setenv("JARVIS_TOKEN", "test-token")

    response = client.post(
        "/v1/browser/plan",
        headers={"X-Jarvis-Token": "test-token"},
        json={
            "schema_version": 1,
            "session_id": "test-session",
            "text": "open a page",
            "page": {
                "title": "Account",
                "visible_text": "Reference: a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6",
                "links": [],
            },
        },
    )

    assert response.status_code == 422
