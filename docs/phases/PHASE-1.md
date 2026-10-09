# Phase 1 Report: AI Server (Python)

## 1. Summary
Phase 1 implements the complete FastAPI AI server (`jarvis_ai`), including core configuration, JSON structured logging, error handling, Pydantic contract schemas, tool registry loader, LLM client abstractions (Ollama & Fake client), NLU normalization, regex fast-path rules (<50ms execution), entity extraction, keyword/LLM advisory risk detection, tool mapping, plan decomposition, verification planning, SQLite session memory with strict isolation, response generation, API authentication via mandatory `JARVIS_TOKEN` with `hmac.compare_digest` returning exact HTTP 401 on missing/invalid headers, socket-level network guard with IPv6 support, strict argument schema validation with data field exclusion, dedicated `Jarvis.Contracts.Tests` project, 30 golden prompt test scenarios, 5 structured LLM retry tests, and labelled evaluation of `qwen2.5:7b-instruct`.

## 2. Files Added or Changed
- `config/settings.json`: Configured primary model to `qwen2.5:7b-instruct`.
- `global.json`: Pinned .NET 8 SDK configuration (`rollForward: latestFeature`).
- `docs/architecture/adr/`: Added `ADR-0002-dotnet-sdk-versioning.md` and `ADR-0003-model-selection.md`.
- `ai-server/pyproject.toml` & `ai-server/requirements.lock`: Pinned exact stable dependencies and committed lockfile.
- `ai-server/src/jarvis_ai/core/`: `config.py`, `network_guard.py` (`install_network_guard()` called at startup in `main.py`).
- `ai-server/src/jarvis_ai/api/`: `auth.py` (refuses startup when `JARVIS_TOKEN` is unset; returns 401 on missing/wrong token), `main.py`.
- `ai-server/src/jarvis_ai/planning/`: `tool_mapper.py` (excludes data fields like `content` from command scanning; enforces `app_id` allowlist pattern matching).
- `ai-server/tests/`:
  - `contract/test_contracts.py`: Migrated from deprecated `RefResolver` to `referencing.Registry` & `Resource`.
  - `unit/test_network_guard.py`: Added IPv6 loopback tests and socket restoration fixture.
  - `unit/test_tool_mapper.py`: Added tests verifying data fields accept code/command text while parameter fields reject dangerous injections.
  - `unit/test_auth.py`: Asserts exact 401 status code responses.
- `windows-host/tests/Jarvis.Contracts.Tests/`: Created dedicated test project `Jarvis.Contracts.Tests.csproj` and moved contract fixture tests out of `Jarvis.Verification.Tests`.
- `windows-host/Jarvis.slnx`: Updated solution to include `Jarvis.Contracts.Tests.csproj`.
- `scripts/eval_llm_json.py`: Config-driven evaluation harness for 20 labelled prompts reporting tool accuracy and argument accuracy.

## 3. Test Results

### Python Pytest Suite (98 Tests Total)
- `tests/golden/test_30_golden_prompts.py`: 35 Passed (30 Golden prompts + 5 LLM-path retry prompts verifying bad JSON recovery & prompt feedback)
- `tests/contract/test_contracts.py`: 36 Passed (0 deprecation warnings using `referencing` library)
- `tests/unit/test_tool_mapper.py`: 12 Passed (Data fields accept code/commands; parameter fields block command injections; app allowlist enforced)
- `tests/unit/test_nlu_rules.py`: 5 Passed
- `tests/unit/test_auth.py`: 5 Passed (Startup exception check + exact HTTP 401 assertions)
- `tests/unit/test_network_guard.py`: 3 Passed (IPv4 & IPv6 loopback allowed; `8.8.8.8` blocked with `PermissionError`; socket restored post-test)
- `tests/unit/test_memory_session_store.py`: 1 Passed (Session isolation test)
- `tests/unit/test_health.py`: 1 Passed
- `ruff check`: Clean (0 errors across 29 source & test files)
- `mypy src`: Clean (0 errors across 29 source files)

### Real Ollama Model Evaluation (`eval_llm_json.py`)
- Target Model: `qwen2.5:7b-instruct` (read from `config/settings.json`, autodetected via `ollama list`)
- Total Prompts Tested: 20 (100% non-rule prompts hitting the LLM path)
- Prompts Matched by Fast-Path Rules: 0
- Prompts Sent to LLM: 20
- **LLM JSON Validity Rate: 100.0%**
- **LLM Tool Action Accuracy: 100.0%**
- **LLM Argument Accuracy: 100.0%**
- **Total LLM Retries: 0**
- **LLM Path p50 Latency: 16980.6 ms**
- **LLM Path p95 Latency: 28401.1 ms**

### C# xUnit Test Suite (44 Tests Total)
- `Jarvis.Contracts.Tests`: 36 Passed (Contract fixture validation moved to dedicated assembly)
- `Jarvis.Verification.Tests`: 1 Passed (Verification engine unit test)
- `Jarvis.Architecture.Tests`: 3 Passed (Dependency rules)
- `Jarvis.Core.Tests`: 1 Passed
- `Jarvis.Safety.Tests`: 1 Passed
- `Jarvis.Tools.Tests`: 1 Passed
- `Jarvis.E2E.Tests`: 1 Passed
- C# Formatting Check (`dotnet format --verify-no-changes` line 59 in `scripts/check-all.ps1`): Passed cleanly.

## 4. Decisions & Deviations from DESIGN.md
- Configured primary model to `qwen2.5:7b-instruct` in `config/settings.json` (documented in `docs/architecture/adr/ADR-0003-model-selection.md`).
- Documented .NET SDK versioning and `TargetFramework` alignment in `docs/architecture/adr/ADR-0002-dotnet-sdk-versioning.md`.

## 5. Known Limitations
- C# Windows Host Core (Phase 2) not yet connected to the Python AI server via HTTP (scheduled for Phase 2).

## 6. Exact Commands to Reproduce
```powershell
# 1. Run Master CI Checks
powershell -ExecutionPolicy Bypass -File scripts\check-all.ps1

# 2. Run Real Model Labelled Benchmark Evaluation
ai-server\.venv\Scripts\python.exe scripts\eval_llm_json.py
```

## 7. Gate Checklist
- [x] 30 golden prompts pass with the fake LLM
- [x] Real-model JSON validity at least 95 percent after retries on 20 prompts (Achieved 100.0%)
- [x] Session isolation test passes
- [x] Unknown or disabled tool is rejected
- [x] pytest, ruff, and mypy clean
