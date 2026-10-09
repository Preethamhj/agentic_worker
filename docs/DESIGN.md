# Jarvis: Verified Windows Agent - Design Specification

Version 1.0 - October 2026. Built from HLD/LLD specification.

---

## 1. System Definition

**One sentence:** A local-first Windows assistant that converts natural language into allowlisted tool calls, executes them only after safety checks and approval, and reports success only after an independent verifier confirms that the real system state matches what was authorized.

**Research question (drives every scope decision):** Does independent, authorization-anchored verification catch false-success reports and unauthorized side effects that exit-code checks, in-process self-checks and predicate-only checks miss, for local-LLM agents on Windows?

---

## 2. Core Principles & Design Decisions

### Core Principles and Code Enforcement

| Principle | Enforcement in Code |
| :--- | :--- |
| **Verification before completion** | The state machine allows `completed` only from `verifying` with result `VERIFIED`. A unit test tries every other path. |
| **Safety before execution** | Every `ToolExecutor` method requires a signed `AuthorizationRecord`. Missing or invalid signature throws. |
| **Local-first** | AI server binds 127.0.0.1 only, no outbound network calls. A test blocks sockets to non-loopback addresses. |
| **Structured outputs only** | Every LLM reply is validated against a schema. Invalid output triggers retry, then fallback. |
| **No direct LLM execution** | The LLM may emit only a `tool_id` plus typed args. Command strings are never accepted from the LLM. |
| **Modular replaceable components** | Interfaces plus dependency injection: `ILlmClient`, `IToolExecutor`, `IEvidenceCollector`, `IVerifier`, `ISessionStore`. |
| **Session isolation** | Every memory query is filtered by `session_id`. A test proves no cross-session leakage. |
| **Fail gracefully** | Typed errors, timeouts and degraded modes. A task can never be left in a non-terminal state. |

### Decisions Resolving Inconsistencies

| Topic | Original PDF / Baseline | Decision |
| :--- | :--- | :--- |
| **API stack** | FastAPI, Rust, microservices, gRPC later | FastAPI only. REST and JSON on 127.0.0.1. Modular monolith: one Python process and one Windows host. gRPC is out of scope. |
| **Verification location** | AI plans, C# executes verification, but repo had Python verifiers | Python only produces the verification plan. All evidence collection and decisions run in C# (`Jarvis.Verification`). Existing Python verifiers are migrated or removed. |
| **Lifecycle length** | Says 10 stages, lists 8 | Fixed at 10 stages (Section 6). |
| **Decision precedence** | FAILED beats ESCALATE beats PARTIAL beats VERIFIED | Kept. Phases 3 to 5 ship VERIFIED, FAILED and ESCALATE. PARTIAL arrives in Phase 6. |
| **Safety** | Keyword blocklist | (+) Allowlist of typed tools, path policy and signed authorization. The keyword list stays only as an extra risk hint. |
| **PowerShell** | Tools generate PowerShell commands | (+) Native .NET tool implementations. `shell.run_powershell` exists but is disabled by default, HIGH risk, confirm-only. |
| **Calendar** | Hard-coded `.ics` file | (+) Task Scheduler reminder with a toast in Phase 3. Outlook and Calendar COM deferred. |
| **Intent speed** | Under 3 s parse | (+) Rule fast path under 50 ms. The LLM path is measured and reported, not promised. |
| **Memory** | SQLite and JSON, vector DB later | Kept. Vector DB deferred. |
| **Execution** | Sequential, one task at a time | Kept. Sequential, background queue, one active high-risk task. |
| **State ownership** | Not defined | (+) C# owns task state. Python is stateless except session memory. |
| **LLM framework** | LangChain in repo | (+) Official ollama client with JSON-schema output. Drop LangChain unless clearly needed. |
| **API security** | Wildcard CORS | (+) No CORS, loopback bind, shared-secret header (`X-Jarvis-Token`). |

---

## 3. Architecture

```
+------------------- Windows Host (C#, .NET 8) -------------------+
| WPF UI / CLI                                                      |
|   |                                                               |
| TaskOrchestrator --(state machine, queue)--> SafetyGate          |
|   |   |                                      (signs auth record)  |
|   |   |                                        |                  |
|   |   |                                  ToolExecutor --> OS      |
|   |   |                                        | receipt          |
|   |   |                                        v (untrusted)      |
|   |   +-----------------------------> VerificationEngine --> OS   |
|   |                                   (own Win32 / COM collectors)|
| ApiClient                             AuditStore (SQLite)+Serilog|
+------+-----------------------------------------------------------+
       | REST/JSON, 127.0.0.1, shared-secret header
+------v------------- AI Server (Python, FastAPI) ----------------+
| /v1/plan    session context -> normalizer -> intent (rules, LLM)  |
|             -> entities -> risk -> decompose -> plan -> tool map  |
|             -> verification plan (template based)                 |
| /v1/report  memory update -> response generator                   |
| Ollama (JSON-schema output)   SQLite session memory   prompts     |
+------------------------------------------------------------------+
```

### Runtime Sequence
1. User types text. The UI hands it to `TaskOrchestrator`, which creates a Task (state `created`) and resolves the session.
2. Orchestrator calls `POST /v1/plan`. The AI server returns intent, advisory risk, and steps with tool calls and verification plans (or clarify/unsupported).
3. SafetyGate re-validates every tool call against the C# registry and policy. The AI risk is advisory. C# decides the final risk.
4. Approval follows the risk matrix (section 9). On approval SafetyGate issues one signed `AuthorizationRecord` per step.
5. Steps enter the sequential queue. The `ToolExecutor` runs step i and returns an untrusted `ExecutionReceipt`.
6. The Orchestrator triggers `VerificationEngine` for step i. The engine runs the 10 stages and returns a `VerificationResult`.
7. VERIFIED: continue. FAILED: stop the task, mark it failed, no automatic retry for mutating steps. ESCALATE: ask the user, and on timeout it becomes FAILED.
8. After the last step the Orchestrator calls `POST /v1/report`. The AI server updates memory and writes the user-facing message.
9. The UI shows the message, step statuses and the evidence behind each verdict.

### Trust Boundaries
- LLM output is untrusted until validated against schema and registry.
- `ExecutionReceipt` is always untrusted.
- `AuthorizationRecord` is trusted because SafetyGate signs it.
- Evidence is trusted only if it was produced inside `Jarvis.Verification`.

---

## 4. Repository Structure

```
jarvis/
├── AGENTS.md                   # master prompt for coding agent
├── README.md
├── LICENSE
├── .gitignore .editorconfig .gitattributes
├── .github/workflows/ci.yml     # windows-latest: dotnet test + pytest + lint
├── docs/
│   ├── DESIGN.md               # this document
│   ├── architecture/           # HLD.md, LLD.md, adr/ADR-0001-*.md
│   ├── phases/                 # PHASE-0.md ... PHASE-6.md
│   ├── threat-model.md
│   └── research/               # protocol.md, related-work.md
├── contracts/                  # SINGLE SOURCE OF TRUTH
│   ├── schemas/                # *.schema.json
│   └── examples/               # valid and invalid fixtures
├── config/
│   ├── settings.json
│   ├── tools.registry.json
│   ├── safety.policy.json
│   ├── apps.allowlist.json
│   └── prompts/                # intent.v1.txt, decompose.v1.txt, response.v1.txt
├── ai-server/                  # Python 3.11
│   ├── pyproject.toml
│   ├── src/jarvis_ai/
│   │   ├── api/                # main.py, routes_plan.py, routes_report.py, auth.py
│   │   ├── core/               # config.py, logging.py, errors.py, ids.py
│   │   ├── llm/                # client.py (Ollama), structured.py, fake.py
│   │   ├── nlu/                # normalizer.py, rules.py, intent_parser.py, entities.py, risk.py
│   │   ├── planning/           # decomposer.py, planner.py, tool_mapper.py
│   │   ├── registry/           # loader.py, models.py
│   │   ├── verification_planning/ # planner.py (template based)
│   │   ├── memory/             # session_store.py (SQLite), summarizer.py
│   │   ├── response/           # generator.py
│   │   └── schemas/            # Pydantic models matching contracts/
│   └── tests/                  # unit/, integration/, contract/
├── windows-host/               # .NET 8
│   ├── Jarvis.sln
│   ├── src/
│   │   ├── Jarvis.Contracts/   # DTOs only (leaf project)
│   │   ├── Jarvis.Core/        # TaskOrchestrator, StateMachine, ExecutionQueue
│   │   ├── Jarvis.Safety/      # PathPolicy, ToolPolicy, SafetyGate, AuthorizationService
│   │   ├── Jarvis.Tools/       # IToolExecutor and fs / app / task executors
│   │   ├── Jarvis.Verification/# independent verifier (must NOT reference Jarvis.Tools)
│   │   ├── Jarvis.Infrastructure/ # ApiClient, Serilog setup, SQLite audit store, DPAPI key store
│   │   ├── Jarvis.Cli/         # console harness for headless end-to-end runs
│   │   ├── Jarvis.UI/          # WPF + MVVM
│   │   └── Jarvis.Eval/        # fault injectors, baselines, runner (Phase 5)
│   └── tests/
│       ├── Jarvis.Core.Tests/
│       ├── Jarvis.Safety.Tests/
│       ├── Jarvis.Tools.Tests/
│       ├── Jarvis.Verification.Tests/
│       ├── Jarvis.Architecture.Tests/ # NetArchTest dependency rules
│       └── Jarvis.E2E.Tests/
├── eval/
│   ├── tasks/                  # 50 task definitions (json)
│   ├── results/                # raw csv and json per run
│   └── analysis/               # python scripts for tables and plots
├── scripts/                    # setup-windows.ps1, run-dev.ps1, check-all.ps1, clean.ps1
└── logs/                       # git-ignored
```

### Dependency Rules (Enforced by `Jarvis.Architecture.Tests`)
- `Jarvis.Contracts` references nothing.
- `Jarvis.Verification` references `Contracts` and `Infrastructure` only. It **never** references `Jarvis.Tools`. This is the structural proof of independence.
- `Jarvis.Tools` references `Contracts` and `Safety`, because it must require a signed authorization.
- `Jarvis.UI` and `Jarvis.Cli` use `Core` through interfaces and DI.
- Python and C# never import each other. They share only `contracts/`.

---

## 5. Contracts

JSON Schemas in `contracts/schemas/` are the ground truth. Python (Pydantic) and C# DTOs are hand-written. Shared fixtures in `contracts/examples/` are validated by both test suites. Every object has `schema_version: 1` and rejects unknown fields (`additionalProperties: false`).

- **PlanRequest:** `{ session_id, text, locale }`
- **PlanResponse:** `{ trace_id, status: ok | clarify | unsupported | error, intent, steps: [PlanStep], message }`
- **Intent:** `{ intent_id, kind: command | query | chat, action, confidence: 0..1, entities: map, risk: {level: low | medium | high, reasons: [...]}, source: rules | llm }`
- **PlanStep:** `{ step_id, order, description, depends_on: [step_id], tool_call, verification_plan, advisory_risk }`
- **ToolCall:** `{ tool_id, args: map }`
- **VerificationPlan:** `{ checks: [Check], watch_scopes: [path or key], window_seconds: 30 }`
- **Check:** `{ type: file_exists | file_hash | dir_exists | process_running | task_exists | trigger_matches, params: map }`
- **AuthorizationRecord:** `{ auth_id, task_id, step_id, tool_id, canonical_args, expected, allowed_effects, watch_scopes, risk, approved_by: auto | user, issued_at, expires_at, nonce, hmac }`
- **ExecutionReceipt (UNTRUSTED):** `{ step_id, auth_id, started_at, completed_at, exit_code, reported_success, stdout, stderr }`
- **Evidence:** `{ evidence_id, step_id, check, collector, collected_at, observed, expected, match, nonce, record_sha256 }`
- **VerificationResult:** `{ step_id, status: VERIFIED | FAILED | ESCALATE | PARTIAL, stages: [{stage, name, passed, detail}], evidence: [Evidence], side_effects: [...], reasons: [...], duration_ms }`
- **ReportRequest:** `{ session_id, task_id, steps: [{step_id, status, verification_status, summary}] }`
- **ReportResponse:** `{ message, memory_updated }`

---

## 6. Verification Engine: 10 Stages

| Stage # | Stage Name | What It Does | Phase |
| :---: | :--- | :--- | :---: |
| 1 | **Trigger validation** | `step_id` and `tool_id` are known, an `ExecutionReceipt` exists, task state is executing | 3 |
| 2 | **Authorization retrieval** | Load record, check HMAC, expiry, and single-use nonce | 3 |
| 3 | **Independent evidence collection** | Collectors inside `Jarvis.Verification` read OS state through Win32/COM, never tool output | 3 |
| 4 | **Evidence validation** | `collected_at` inside window after `completed_at`, nonce bound, record hash stored, replay rejected | 3 |
| 5 | **Attribute verification** | Compare each observed attribute with `authorization.expected` | 3 |
| 6 | **Side-effect detection** | Snapshot `watch_scopes` before and after, any change not in `allowed_effects` is a failure | 3 |
| 7 | **Persistence check** | Diff HKCU Run keys, Startup folder, and scheduled tasks for every action | 6 |
| 8 | **Drift check** | Changes in watch scopes not attributable to any authorized step | 6 |
| 9 | **Decision aggregation** | Combine stage outcomes with strict precedence | 3 |
| 10 | **Audit and report** | Persist result and evidence, return to orchestrator | 3 |

### Aggregation Rules
- Any failed stage gives `FAILED`.
- Insufficient or ambiguous evidence gives `ESCALATE`.
- `ESCALATE` unresolved after timeout (default 120 s) becomes `FAILED`. It can never become `VERIFIED`.
- `VERIFIED` requires every stage to pass.
- Precedence: `FAILED` > `ESCALATE` > `PARTIAL` > `VERIFIED`.

### Evidence Rules
- Collected only by `Jarvis.Verification`, never taken from tool `stdout`, `stderr`, or `exit_code`.
- Valid only inside time window (default 30 s after `completed_at`). Stale evidence is rejected.
- Each evidence record is hashed and stored immutably. A nonce is used once.

---

## 7. Tool Registry v1 & Verification Matrix

| Tool ID | Risk | Executor Does | Independent Verification | Phase |
| :--- | :---: | :--- | :--- | :---: |
| `fs.search_files` | low | Enumerate allowed roots by pattern | Every reported path exists, matches pattern, lies in allowed roots | 2-3 |
| `fs.create_folder` | medium | Create directory inside allowed roots | Exists via Win32 attributes, is directory, not reparse point, created after t0, parent snapshot diff | 2-3 |
| `fs.create_file` | medium | Write temp file then atomic move | Exists, SHA-256 equals expected, size, timestamps, no extra files in watch scope | 2-3 |
| `app.launch` | medium | Start an app ID from `apps.allowlist.json` | Process alive, image path in allowlist, start time after t0, not elevated, parent is host | 2-3 |
| `task.schedule_reminder` | medium | Per-user Task Scheduler task in a Jarvis folder that shows a toast | Task XML read via raw COM / schtasks export, trigger time, action, enabled match exactly | 3 |
| `shell.run_powershell` | high | Disabled by default | Command and output check, only when explicitly enabled | 6 |
| `registry.set_value` | high | HKCU only, allowlisted keys | Exact value and type, adjacent key diff | 6 |

---

## 8. Task State Machine

Transitions:
`created` -> `context_resolved` -> `parsed` -> `planned` -> `validated` -> `awaiting_approval` -> `approved` -> `queued` -> `executing` -> `verifying` -> `completed`

Exit states from any non-terminal state: `failed`, `cancelled`, `timeout`.
Terminal states: `completed`, `failed`, `cancelled`, `timeout`.
Legal transition table lives in `Jarvis.Core/StateMachine.cs`. Every transition is logged to `audit.db`.

---

## 9. Safety Model

| Risk Level | Examples | Behavior |
| :--- | :--- | :--- |
| **Low** | Search, read-only queries | Auto-run, logged |
| **Medium** | Create file/folder, launch app, schedule reminder | Plan shown, one-click approval, logged |
| **High** | Delete, registry, shell | Modal confirm showing canonical args, only 1 active high-risk task, deny on timeout |

- Authoritative risk is `max(Python advisory risk, C# policy risk)`. C# is authoritative.
- **Path Policy (`Jarvis.Safety.PathPolicy`):** Allowed roots come from config (default: `Jarvis` folder under user's Documents). Expand env vars, resolve full path, normalize Unicode NFC, case-insensitive. Reject `..`, junctions, symlinks (reparse points), UNC paths, device paths, reserved names (`CON`, `NUL`), alternate data streams.
- **API Hardening:** Bind 127.0.0.1 only. No CORS. HMAC key in DPAPI. Run non-elevated.

---

## 10. Observability, Configuration & Targets

- **Correlation IDs:** `trace_id`, `session_id`, `task_id`, `step_id` on every log line and API call (`X-Trace-Id` header).
- **Sinks:** Serilog compact JSON to `logs/host-*.jsonl`, SQLite `audit.db`, Python `structlog` JSON to `logs/ai-server.jsonl`.
- **Targets:** Rule fast path < 50 ms, Verification per file step < 500 ms, Outbound network calls = 0, Startup ready < 30 s.

---

## 11. Blockers and Mitigations

| Blocker | Phase | Mitigation |
| :--- | :---: | :--- |
| Small local LLMs return invalid JSON | 1 | Ollama JSON-schema format, temperature 0, Pydantic validation, 2 retries feeding error back, clarify fallback. Rule fast path skips LLM for common intents. |
| Slow CPU inference | 1, 4 | Default to qwen2.5:7b-instruct (fallback llama3.2:3b), keep_alive, warm-up at startup, stream progress to UI. |
| LLM invents tools / raw commands | 1 | ToolMapper rejects unknown tool_id and invalid args. Shell tool is disabled. |
| Verifier is not really independent | 3 | Separate C# assembly with no reference to `Jarvis.Tools` (architecture test). Win32/COM APIs used directly. Receipt fields ignored. |
| Stale or replayed evidence | 3 | Nonce per authorization, window check against `completed_at`, single-use nonce, evidence SHA-256 hash. |
| Authorization forged or edited | 2 | HMAC-SHA256 with DPAPI key, expiry, single-use nonce. |
| Python and C# contracts drift | 0 | Shared JSON schemas and fixtures validated by both test suites in CI. |
| Ollama missing / stopped | 0, 4 | Setup script installs and pulls. Startup health check. Degraded rule-only mode banner. |
| Windows path pitfalls | 2 | Known-folder APIs, NFC normalization, reparse-point rejection, long paths enabled. |
| Scheduled tasks need rights | 3 | Per-user tasks with interactive token, no admin. Verified through raw COM. |
| Elevation and UAC | 2 | Run non-elevated. Refuse changes needing admin. |
| WPF threading / deadlocks | 4 | Async all the way, no `.Result` or `.Wait()`, Dispatcher only in UI layer. |
| Failed mutating step leaves partial state | 3 | No automatic retry for mutating steps. Report observed state. |
| Side-effect detection too noisy | 3 | Narrow watch scopes per tool, noise exclusions in config. |
| Running out of time | All | Cut order: Phase 6 first, then UI polish, then `task.schedule_reminder`. |
| AI-written code quality | All | Tests first for state machine, safety, verification. Review checklist in B3. |

---

## 12. Phase Roadmap & Gates

| Phase | Name | Main Output | Gate (Must pass before next phase) |
| :---: | :--- | :--- | :--- |
| **0** | **Foundation** | Repo skeleton, setup script, CI, contracts, fixtures, ADRs | `check-all.ps1` passes on clean clone. Both languages validate fixtures identically. Architecture test passes. CI green. Setup script idempotent. |
| **1** | **AI Server** | `/health`, `/v1/plan`, `/v1/report`, rules + LLM intent, planner, session memory | 30 golden prompts pass with Fake LLM. Real model JSON validity >= 95% on 20 prompts. Session isolation test passes. Unknown tool rejected. Pytest, ruff, mypy clean. |
| **2** | **Windows Host Core** | State machine, queue, safety gate, 4 executors, ApiClient, audit store, CLI | CLI runs each executor end-to-end (stub verifier). Tampered/expired auth refused. 20 malicious paths blocked, 10 valid paths accepted. Illegal state transitions throw. Dotnet build, test, format clean. |
| **3** | **Verification Engine** | Stages 1-6, 9-10, collectors, snapshots, ESCALATE timeout | Every fault fixture detected. 0 false failures in 50 clean runs per tool. ESCALATE timeout resolves to FAILED. Architecture tests pass. Latency reported (<500ms). |
| **4** | **Integration & UI** | WPF app, approval dialog, evidence panel, bootstrap/shutdown | 10 scripted scenarios pass & recorded. Ollama kill gives degraded mode. Closing app leaves 0 orphan processes. UI never shows success unless status is VERIFIED. |
| **5** | **Evaluation Harness** | `Jarvis.Eval`, 4 verifiers, fault injectors, 50 tasks, metrics | 1 script reproduces `results.csv` and summary table. Controls show false-failure rate per verifier. Figures generated. Limitations section written. |
| **6** | **Hardening & Extensions** | PARTIAL state, stages 7-8, registry/shell tools, threat model | New stage fault fixtures pass. Evaluation re-run and updated. Threat model written. Publish script works on clean VM. |

---
