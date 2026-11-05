# Phase 2b Report: Executors, Queue, API Client, and CLI (C#)

## 1. Summary
Phase 2b completes the execution engine, asynchronous queue, HTTP API client, child process launcher, and CLI harness for the Jarvis Windows Host, strictly following TDD and safety invariants:
- **Tool Executors (`Jarvis.Tools`):** Implemented concrete `IToolExecutor` classes for `fs.search_files`, `fs.create_folder`, `fs.create_file`, and `app.launch`. Every executor calls `AuthorizationService.Consume` first and operates strictly on `canonical_args`.
- **Use-Time Path & Reparse Point Validation (`Win32PathResolver`):** Implemented Win32 handle resolution with `CreateFileW` (using `FILE_FLAG_BACKUP_SEMANTICS`) and `GetFinalPathNameByHandleW(FILE_NAME_NORMALIZED | VOLUME_NAME_DOS)`. Strips `\\?\` volume namespaces and validates the canonical target against `PathPolicy`, catching TOCTOU junction swaps in real-time.
- **Atomic File Creation:** `CreateFileExecutor` writes to a temporary file (`.tmp_{guid}`) in the target folder and executes an atomic move with `overwrite: false`, strictly preventing overwrites and leaving no partial state.
- **Bounded Search:** `SearchFilesExecutor` implements bounded directory search (depth clamped to 10, max results clamped to 500), enforces cancellation tokens, and ignores reparse points (`FileAttributes.ReparsePoint`).
- **Application Launch Security:** `AppLaunchExecutor` maps allowlisted application IDs (`apps.allowlist.json`) to verified executable paths, prohibits free-form arguments, forces `UseShellExecute = false`, and refuses execution when invoked from an elevated (Administrator) process.
- **Execution Queue (`Jarvis.Core.ExecutionQueue`):** Channel-based sequential FIFO queue (`System.Threading.Channels`). Restricts concurrency with a dedicated `SemaphoreSlim(1, 1)` lock for high-risk steps, enforces per-step timeouts with linked `CancellationTokenSource`, and handles task cancellation cleanly.
- **Task Orchestrator (`Jarvis.Core.TaskOrchestrator`):** Wires planning, safety evaluation, user approval, sequential queuing, tool execution, and verification. Enforces that every execution outcome reaches a terminal state (`Completed`, `Failed`, `Cancelled`, `Timeout`). With `StubVerifier` (returning `ESCALATE`), tasks transition to `Failed` and never reach `Completed`.
- **SQLite Audit Store (`Jarvis.Infrastructure.SqliteAuditStore`):** Implemented `IAuditStore` backed by `Microsoft.Data.Sqlite`, creating all nine DESIGN.md section 10 tables: `tasks`, `steps`, `executions`, `verifications`, `evidence`, `api_calls`, `state_transitions`, `safety_decisions`, and `auth_lifecycles`.
- **Structured JSON Logging (`Jarvis.Infrastructure.JarvisLogging`):** Configured Serilog with `CompactJsonFormatter` writing rolling `.jsonl` files enriched with `trace_id`, `task_id`, and `step_id`.
- **HTTP Client (`Jarvis.Infrastructure.ApiClient`):** Implemented `IApiClient` connecting strictly to loopback (`127.0.0.1`). Injects `X-Jarvis-Token` and `X-Trace-Id` headers, enforces a 90s plan timeout, provides exactly one automatic retry for transient planning errors, and sets the `Idempotency-Key` header to `task_id` on report requests.
- **Zero-Orphan Process Launcher (`Jarvis.Infrastructure.AiServerLauncher`):** Dynamically allocates a free loopback port, generates a 256-bit cryptographically secure token passed strictly via `JARVIS_TOKEN` environment variable (never logged), starts `uvicorn`, binds process lifetime to the host via a Win32 Job Object with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`, and polls `/health` before returning.
- **CLI Harness (`Jarvis.Cli`):** Implemented `run <text>`, `history`, and `doctor` commands. Console approval prompts show full canonical arguments. Proves that task output prints `"not verified"` and never `"completed"`.

---

## 2. Gate Checklist

| Gate Item | Status | Evidence |
| :--- | :---: | :--- |
| **CLI runs each tool end to end up to the stub verifier** | **PASS** | `CliEndToEndTests.cs`: `Cli_Runs_FsCreateFile_EndToEnd_UpTo_StubVerifier`, `Cli_Runs_FsCreateFolder_EndToEnd_UpTo_StubVerifier`, `Cli_Runs_FsSearchFiles_EndToEnd_UpTo_StubVerifier`, `Cli_Runs_AppLaunch_EndToEnd_UpTo_StubVerifier`. Interactive CLI run verified in console. |
| **No path from a task to completed without VERIFIED** | **PASS** | `StateMachineTests.cs` (6 theory tests), `OrchestratorAndQueueTests.cs`: `TaskOrchestrator_Never_Reaches_Completed_With_StubVerifier`, `CliEndToEndTests.cs` (asserts `FinalState != Completed`). Invariant checked in `Program.cs`. |
| **Junction swap, forged and reused authorization are refused in tests** | **PASS** | `ExecutorTests.cs`: `Junction_Swapped_In_At_Use_Time_Is_Caught_By_GetFinalPathNameByHandle`, `Executor_Refuses_Forged_Or_Reused_Authorization`. `SafetyGateTests.cs`: `Consume_ReVerifies_Hmac_And_Detects_Forged_Copy`, `Consume_With_Already_Consumed_Auth_Throws_Exception`. |
| **No orphan python process after the CLI exits** | **PASS** | `AiServerLauncherTests.cs`: `JobObject_Kills_Child_Process_When_Job_Handle_Is_Closed`. Live check with `Get-Process python` returned exit code 1 (0 running processes). |
| **`check-all.ps1` passes, PHASE-2b.md written, new tag** | **PASS** | `scripts/check-all.ps1` succeeded: 224 C# tests passed, 98 Python tests passed, ruff & mypy clean, formatting clean. |

---

## 3. Files Added or Changed

### C# Windows Host Implementation
- `windows-host/src/Jarvis.Tools/`:
  - `Win32PathResolver.cs`: Win32 `CreateFileW` with `FILE_FLAG_BACKUP_SEMANTICS` and `GetFinalPathNameByHandleW` to catch junction swaps and resolve canonical volume paths.
  - `CreateFileExecutor.cs`: Atomic file creation (temp write + move without overwrite), use-time path verification, and signed authorization consumption.
  - `CreateFolderExecutor.cs`: Directory creation within allowed roots with post-creation reparse verification.
  - `SearchFilesExecutor.cs`: Bounded depth (max 10), max results (max 500), timeout, cancellation, and reparse skipping.
  - `AppLaunchExecutor.cs`: Allowlist mapping (`apps.allowlist.json`), no free-form args, elevation check (`WindowsPrincipal.IsInRole(Administrator)`), `UseShellExecute = false`.
- `windows-host/src/Jarvis.Core/`:
  - `IVerifier.cs`: Independent verification contract.
  - `StubVerifier.cs`: Stub verifier returning `ESCALATE` across all stages for Phase 2b.
  - `ISafetyGate.cs`: Safety evaluation abstraction.
  - `IApprovalHandler.cs`: User approval contract with canonical args presentation.
  - `IPlanner.cs`: Planning abstraction.
  - `IStepExecutor.cs`: Step execution abstraction.
  - `ExecutionQueue.cs`: Channel-based sequential queue with `SemaphoreSlim(1,1)` high-risk lock, step timeout, and cancellation.
  - `TaskOrchestrator.cs`: Wires plan, safety, approval, queue, execute, and verify, guaranteeing terminal state upon any outcome.
- `windows-host/src/Jarvis.Infrastructure/`:
  - `SqliteAuditStore.cs`: SQLite audit engine managing all nine audit tables listed above.
  - `JarvisLogging.cs`: Serilog compact JSON setup with `trace_id`, `task_id`, `step_id` correlation enrichment.
  - `JarvisApiException.cs`: Typed exception for HTTP client and protocol errors.
  - `ApiClient.cs`: HTTP client for `/v1/plan` (90s timeout, 1 retry), `/v1/report` (Idempotency-Key), and `/health`.
  - `AiServerLauncher.cs`: Process launcher with port picker, crypto token, Win32 Job Object with `KILL_ON_JOB_CLOSE`, and health polling.
- `windows-host/src/Jarvis.Safety/`:
  - `ToolPolicy.cs`: Extended with `AllowedAppDefinition` retention and `TryGetAllowedApp` lookup.
  - `SafetyGate.cs`: Implemented `ISafetyGate`, added `EvaluateStepAsync`, injected `PathPolicy`, and recorded audit decisions.
- `windows-host/src/Jarvis.Cli/`:
  - `ConsoleApprovalHandler.cs`: Formats and displays canonical arguments, supports `JARVIS_AUTO_APPROVE`.
  - `StepExecutor.cs`: Wires authorization issuance, consumption, and executor delegation.
  - `ApiClientPlanner.cs`: Adapts `IApiClient` to `IPlanner`.
  - `Program.cs`: CLI entry point implementing `run <text>`, `history`, and `doctor`.

### Test Suites
- `windows-host/tests/Jarvis.Tools.Tests/`:
  - `ExecutorTests.cs`: 6 executor tests (atomic file move, folder creation, bounded search, elevation rejection, forged/reused auth refusal, junction swap use-time detection).
- `windows-host/tests/Jarvis.Core.Tests/`:
  - `OrchestratorAndQueueTests.cs`: 8 tests covering queue FIFO ordering, high-risk lock concurrency, step timeouts, cancellation, stub verifier unreachability of completed, safety rejections, user approval denials, and execution timeouts.
- `windows-host/tests/Jarvis.E2E.Tests/`:
  - `SqliteAuditStoreTests.cs`: 3 tests for persistence and retrieval of state transitions, safety decisions, and authorization lifecycles.
  - `ApiClientTests.cs`: 6 tests covering headers, 401 unauthorized, malformed JSON, timeouts, transient retry, and idempotency keys.
  - `AiServerLauncherTests.cs`: 3 tests covering free port selection, token format, and child process termination upon Job Object closure.
  - `CliEndToEndTests.cs`: 6 tests for end-to-end execution of all 4 tools up to stub verifier, doctor command, and history command.

---

## 4. Test Results & Metrics

### C# Test Suite: 224 Tests Passed, 0 Failed, 0 Skipped
- **`Jarvis.Contracts.Tests`:** 46 Passed, 0 Skipped
- **`Jarvis.Core.Tests`:** 55 Passed, 0 Skipped
- **`Jarvis.Verification.Tests`:** 1 Passed, 0 Skipped
- **`Jarvis.Safety.Tests`:** 90 Passed, 0 Skipped
- **`Jarvis.Tools.Tests`:** 9 Passed, 0 Skipped
- **`Jarvis.Architecture.Tests`:** 4 Passed, 0 Skipped
- **`Jarvis.E2E.Tests`:** 19 Passed, 0 Skipped

### Python Test Suite: 98 Tests Passed, 0 Failed
- `pytest` on `ai-server/tests`: 98 passed in 7.47s.
- `ruff` linting: All checks passed.
- `mypy` strict type checking: Success (0 errors in 29 source files).

---

## 5. Architectural Decisions & Deviations

1. **Dependency Inversion for Orchestrator Abstractions:**
   - In accordance with NetArchTest rules, `Jarvis.Core` does not directly depend on `Jarvis.Tools` or `Jarvis.Safety`.
   - `Jarvis.Core` defines `ISafetyGate`, `IVerifier`, and `IStepExecutor`. Concrete implementations are wired in `Jarvis.Cli` and test doubles in unit tests.
2. **Win32 Handle-Based Path Resolution (`Win32PathResolver`):**
   - Implemented via `CreateFileW` with `FILE_FLAG_BACKUP_SEMANTICS` and `GetFinalPathNameByHandleW`.
   - To catch TOCTOU junction swaps without following junctions to unauthorized targets, parent and target reparse attributes are inspected, normalized volume paths are verified against `PathPolicy`, and non-allowed final targets are rejected immediately.
3. **Windows Job Object for Zero-Orphan AI Server:**
   - To eliminate orphan python processes, `AiServerLauncher` creates an anonymous Win32 Job Object configured with `JobObjectExtendedLimitInformation` and `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`.
   - Tested and verified: closing the host or job object handle immediately terminates child uvicorn processes.

---

## 6. Known Limitations
- Verification engine remains stubbed (`StubVerifier`) returning `ESCALATE` across all stages, as specified for Phase 2b. Tasks never transition to `Completed`. Full multi-stage Win32/COM verification collectors will be implemented in Phase 3.
- Shell and registry executors are intentionally absent and disabled by policy.

---

## 7. Exact Commands to Reproduce

```powershell
# 1. Run all checks (build, tests, format, lint)
powershell -ExecutionPolicy Bypass -File scripts/check-all.ps1

# 2. Run Jarvis doctor diagnostic
dotnet run --project windows-host/src/Jarvis.Cli doctor

# 3. View task audit history
dotnet run --project windows-host/src/Jarvis.Cli history

# 4. Run an end-to-end task (auto-approve for headless run)
$env:JARVIS_AUTO_APPROVE = "1"
dotnet run --project windows-host/src/Jarvis.Cli run "create folder my_test_folder"
```
