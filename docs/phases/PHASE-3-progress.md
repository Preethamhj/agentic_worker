# Phase 3 Progress

| Step | Result | Evidence |
|---|---|---|
| 1 | RED (tests first) | Added fault-first file/folder verifier tests and a verification replay test. Initial targeted build fails as expected because `VerificationEngine`, `StepToken`, and `ExecutionObservation` are not implemented yet. Coverage includes absent/wrong/truncated/wrong-location outputs, unauthorized extra file, junction replacement, expired/unconsumed/replayed authorization, stale evidence, forged receipt identity, locked file, snapshot cap, persisted audit, and 50 clean runs per tool with latency reporting. |
| 2 | PASS | Added `ExecutionObservation`, `StepToken`, updated `IVerifier`, and `IAuthorizationVerifier`; `AuthorizationService` now rejects Verified-state replays. Core calls BeginStep before executing and supplies `IClock` timestamps around the executor call; receipt success fields no longer short-circuit verification. `StubVerifier` remains the host implementation. Targeted tests: Jarvis.Core.Tests 55/55 and Jarvis.Safety.Tests 92/92. |
| 3 | PASS | Implemented the independent verification engine for stages 1-6, 9, and 10; file/folder OS evidence collection, capped watch-scope snapshots and side-effect detection, nonce/hash/freshness validation, severity aggregation, and SQLite evidence/result persistence. The Safety authorization verifier now awaits the durable Verified lifecycle audit. Targeted verification tests: 19/19; full `scripts\\check-all.ps1`: 254 C# tests and 103 Python tests passed, formatting and lint passed. Clean runs: 50 file + 50 folder with zero false failures; measured p95 41.41 ms (file), 35.39 ms (folder). Architecture closure: 111 assemblies, with Jarvis.Tools, Jarvis.Safety, and Jarvis.Core unreachable. |

## Phase 3b preamble

| Step | Result | Evidence |
|---|---|---|
| 1 | PASS | Default create-tool watch scopes are the target parent and are enumerated non-recursively. Snapshots compare path, type, size, and mtime first; hashes are read only for metadata-changed entries and the authorized target. The 5,000-file test measured 30.29 ms, recorded 5,000 entries, and did not reach the default 10,000-entry cap. |
| 2 | PASS | `AppLaunchExecutor` now receives `IElevationChecker`; production uses `WindowsElevationChecker`, while tests inject fixed elevated/non-elevated implementations. The real-elevation app-launch test's successful-launch branch cannot run on the elevated CI runner; deterministic injected tests cover both decisions. |
| 3 | PASS | `Preexisting_Identical_File_Within_100ms_Tolerance_Is_Failed_When_Executor_Does_Nothing` confirms Stage 5 accepts the 50 ms timestamp tolerance but Stage 6 fails because the file was already in the before-snapshot. `docs/threat-model.md` records the tolerance and limitation. |
| 4 | PASS | `scripts\\setup-windows.ps1` installs `ai-server\\requirements.lock` before installing the local project with `--no-deps`. Final pins are `ollama==0.4.7`, `httpx==0.27.2`, and `pydantic==2.9.2`; the HTTPX pin preserves compatibility with locked Starlette 0.36.3, and `ci.yml` prints versions from the project venv. Workflow triggers remain all-branch pushes and pull requests. |

## Phase 3b scope audit

Commit `471f3a2` includes `AppLaunchExecutor` and the `IElevationChecker`/`WindowsElevationChecker` injection. Its app-launch tests are `AppLaunchExecutor_Maps_AppId_And_Refuses_FreeForm_Args_Or_Elevation`, `AppLaunchExecutor_Refuses_Executable_Resolved_Via_PATH_And_Requires_Allowlisted_Absolute_Path`, and `AppLaunchExecutor_Uses_Injected_Elevation_Checker_Deterministically`. They verify execution policy/elevation only. The commit did not add independent process evidence collection or app.launch verifier tests; those 3b verification requirements are now implemented separately.

## Phase 3b validation

Phase 3b implementation and validation are complete. `scripts\\check-all.ps1` passed: 274 C# tests, 103 Python tests, mypy clean for 29 source files, C# formatting passed, and zero C# build warnings/errors. No tests were skipped. The final locked Python evaluation with Ollama 0.4.7 / HTTPX 0.27.2 retained 100% JSON validity, tool accuracy, and argument accuracy across 20 prompts (zero retries); p50/p95 were 18,157.4/49,872.8 ms. See `PHASE-3b.md` for test names, limitations, CI status, and the complete Verification assembly closure.
