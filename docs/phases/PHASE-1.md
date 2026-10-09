# Phase 1 Report: AI Server (Python)

## 1. Summary
Phase 1 implements the complete FastAPI AI server (`jarvis_ai`), including core configuration, JSON structured logging, error handling, Pydantic contract schemas, tool registry loader, LLM client abstractions (Ollama & Fake client), NLU normalization, regex fast-path rules (<50ms execution), entity extraction, keyword/LLM advisory risk detection, tool mapping, plan decomposition, verification planning, SQLite session memory with strict isolation, response generation, API authentication via mandatory `JARVIS_TOKEN` with `hmac.compare_digest`, 30 golden prompt test scenarios, 5 structured LLM retry tests, socket-level network guard, comprehensive ToolMapper validation, and real Ollama model evaluation on 20 non-rule prompts.

## 2. Files Added or Changed
- `config/prompts/`: `intent.v1.txt`, `decompose.v1.txt`, `response.v1.txt`
- `global.json`: Pinned .NET 8 SDK configuration with `rollForward: latestMajor`.
- `ai-server/src/jarvis_ai/core/`: `config.py`, `network_guard.py`, `errors.py`, `ids.py`, `logging.py`
- `ai-server/src/jarvis_ai/schemas/`: `models.py`, `__init__.py`
- `ai-server/src/jarvis_ai/registry/`: `models.py`, `loader.py`
- `ai-server/src/jarvis_ai/llm/`: `client.py`, `structured.py`, `fake.py`
- `ai-server/src/jarvis_ai/nlu/`: `normalizer.py`, `entities.py`, `risk.py`, `rules.py`, `intent_parser.py`
- `ai-server/src/jarvis_ai/planning/`: `decomposer.py`, `tool_mapper.py`, `planner.py`
- `ai-server/src/jarvis_ai/verification_planning/`: `planner.py`
- `ai-server/src/jarvis_ai/memory/`: `session_store.py`, `summarizer.py`
- `ai-server/src/jarvis_ai/response/`: `generator.py`
- `ai-server/src/jarvis_ai/api/`: `auth.py`, `routes_plan.py`, `routes_report.py`, `main.py`
- `ai-server/tests/`: `unit/test_health.py`, `unit/test_nlu_rules.py`, `unit/test_memory_session_store.py`, `unit/test_auth.py`, `unit/test_network_guard.py`, `unit/test_tool_mapper.py`, `golden/test_30_golden_prompts.py`
- `scripts/eval_llm_json.py`: Real model JSON validity evaluation script for LLM-path prompts with `ollama list` verification.

## 3. Test Results

### Python Pytest Suite (97 Tests Total)
- `tests/golden/test_30_golden_prompts.py`: 35 Passed (30 Golden prompts + 5 LLM-path retry prompts verifying bad JSON recovery & prompt feedback)
- `tests/contract/test_contracts.py`: 36 Passed (All 36 contract fixtures validated against JSON schemas)
- `tests/unit/test_tool_mapper.py`: 12 Passed (Wrong arg types, extra fields, missing required fields, command injection in all arg fields, disabled/unknown tools)
- `tests/unit/test_nlu_rules.py`: 5 Passed (Fast path regex rules for all 5 v1 tools)
- `tests/unit/test_auth.py`: 4 Passed (Unset `JARVIS_TOKEN` env error, missing `X-Jarvis-Token` header, wrong token 401 via `hmac.compare_digest`, valid token 200)
- `tests/unit/test_network_guard.py`: 3 Passed (Socket-level `PermissionError` on `8.8.8.8`, loopback allowed, non-loopback `ai_server_host` and `ollama_host` config rejection)
- `tests/unit/test_memory_session_store.py`: 1 Passed (Session isolation test: zero cross-session data leakage)
- `tests/unit/test_health.py`: 1 Passed (`GET /health` returns status, version, and LLM status)
- `ruff check`: Clean (0 errors across 29 source & test files)
- `mypy src`: Clean (0 errors across 29 source files)

### Real Ollama Model Evaluation (`eval_llm_json.py`)
- Target Model: `llama3.2:latest` (autodetected & verified via `ollama list`)
- Total Prompts Tested: 20 (100% non-rule prompts hitting the LLM path)
- Prompts Matched by Fast-Path Rules: 0
- Prompts Sent to LLM: 20
- **LLM JSON Validity Rate: 100.0%**
- **Total LLM Retries: 0**
- **LLM Path p50 Latency: 4925.3 ms**
- **LLM Path p95 Latency: 15240.0 ms**

### C# xUnit Test Suite (44 Tests Total)
- All 44 C# tests passing cleanly across 6 test assemblies.
- C# Formatting Check (`dotnet format --verify-no-changes`): Passed cleanly.

## 4. Decisions & Deviations from DESIGN.md
- Configured default model fallback to `llama3.2:latest` in `config/settings.json` when `qwen2.5:7b-instruct` is not locally pulled.
- Mandatory `JARVIS_TOKEN` environment variable enforcement in server authentication without default-dev-token fallback.

## 5. Known Limitations
- C# Windows Host Core (Phase 2) not yet connected to the Python AI server via HTTP (scheduled for Phase 2).

## 6. Exact Commands to Reproduce
```powershell
# 1. Run Master CI Checks
powershell -ExecutionPolicy Bypass -File scripts\check-all.ps1

# 2. Run Real Model JSON Evaluation
ai-server\.venv\Scripts\python.exe scripts\eval_llm_json.py
```

## 7. Gate Checklist
- [x] 30 golden prompts pass with the fake LLM
- [x] Real-model JSON validity at least 95 percent after retries on 20 prompts (Achieved 100.0%)
- [x] Session isolation test passes
- [x] Unknown or disabled tool is rejected
- [x] pytest, ruff, and mypy clean
