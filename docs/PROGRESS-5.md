# Phase 5 Progress

Branch: `phase-5`, based on `5b68602` (`phase-3c-done`).

## Part records

- Setup: created and pushed the `phase-5` branch from `5b68602`.
- Part A: `bf1f64c`; frozen protocol and phase part list; documentation-only, no tests applicable.
- Part B: `460489c`; `RunCase_Executes_Once_And_Gives_All_Verifiers_The_Same_Context`, `RunCase_Shuffles_Verifier_Order_Deterministically_From_Seed`, `RunCase_Records_Verifier_Exception_As_Error_Not_Verified` — 3 passed; Release solution build passed (0 warnings/errors).
- Part C: `cb0ad22`; `Receipt_Verifier_Accepts_A_NoOp_Success_Receipt`, `Self_Check_Verifier_Accepts_A_Deleted_After_Success_Result`, `Predicate_Verifier_Catches_Wrong_Content_But_Does_Not_Check_Extra_Files`, `Control_Gives_Verified_For_All_Four_Verifiers` and 4 `Engine_Verifier_Uses_Real_VerificationEngine_For_Control_And_File_Faults` cases (F0/F1/F2/F6) — 8 passed; Release solution build passed (0 warnings/errors).
- Part D: `d54aa52`; `Injector_Produces_Declared_Fault_State_And_Ground_Truth` — 20 fault rows passed, `Injector_Rejects_A_Fault_That_Does_Not_Apply_To_The_Tool` passed; `Host_Projects_Must_Not_Reference_Evaluation_Harness` passed; Release solution build passed (0 warnings/errors).
- Part E: `4966bf1`; `Checked_In_Catalog_Is_Deterministic_And_Valid_Against_Its_Schema`, `Catalog_Has_Exact_Tool_Counts_And_Unique_Fixed_Seed_Ids`, `Every_Task_Includes_Its_Applicable_Faults_And_Catalogue_Bounds` — 3 passed; catalogue contains 50 tasks and 360 cases per repetition (310 matched plus 50 F18 diagnostic); Release solution build passed (0 warnings/errors).
- Part C2 (partial): `96ef36e`; `Engine_Verifier_Rejects_Tampered_Signed_Authorization` (2 cases), four file engine cases, and baseline/control tests passed. Follow-up D2 implementation added the evaluation-only `EvalFaultInjectingExecutor` and real filesystem fault checks; combined focused run `EvalVerifierTests|EvalFaultInjectorTests`: 34 passed, 0 failed, 0 skipped; Release solution build passed (0 warnings/errors). Real file authorization/execution uses `ToolPolicy`, `PathPolicy`, `AuthorizationService` + `InMemoryDpapiKeyStore`, `StepExecutor.PrepareAuthorization`, `Consume`, and `CreateFileExecutor`. All-tool controls and fake-scheduler reminder lifecycle remain unimplemented; D2 is not complete.
- Part D2 (partial): `2247142`; added real search executor fault assertions for F7/F8 and filesystem state assertions for F1/F2/F6 in `Real_Search_Fault_Injector_Changes_The_Executor_Reported_Paths` (2 cases) and `Engine_Verifier_Uses_Real_VerificationEngine_For_Control_And_File_Faults` (existing parameterized cases); combined focused run `EvalVerifierTests|EvalFaultInjectorTests`: 36 passed, 0 failed, 0 skipped; Release solution build passed (0 warnings/errors). Real app/reminder injectors and F18 remain outstanding.
- V0: uncommitted; appended AMENDMENT 1 (pre-results) to `docs/eval/PROTOCOL.md`; commit/push blocked twice by `.git/index.lock` permission denied.
- V1: no production change; existing real-lifecycle tests cover tampered signature, post-signing Expected mutation, and F0 control; focused Release test invocation failed in MSBuild with 0 warnings/errors and no diagnostic error output.
- V2: not implemented; current CLI remains catalogue-only and no end-to-end sandbox/raw-row runner was added.
- V3: uncommitted; added `eval/analyze.py` and synthetic tests; Python environment configuration hung before pytest/ruff/mypy could run.
- W0: `Engine_Verifier_Rejects_Tampered_Signed_Authorization`, `Engine_Verifier_Uses_Real_VerificationEngine_For_Control_And_File_Faults`, and `Control_Gives_Verified_For_All_Four_Verifiers` passed (7 total, 0 failed, 0 skipped); eval pytest 3 passed, ruff clean, mypy clean; added Amendment 2 clarifying F18 scope and ignored generated egg-info.
- W1: file smoke built and ran via the direct Eval DLL; 65 rows (1 metadata + 64 verdicts) for 2 tasks × 8 faults × 4 verifiers, sandbox cleaned; D returned ESCALATE for both GOOD F0 controls and the result was recorded in `docs/eval/FINDINGS.md`.
- W2: two-repetition file development run completed with 769 rows; analyzer BOM handling fixed and `docs/eval/results/dev-run-1/README.md` marks it development-only; no new D finding beyond W1.
- W3: folder smoke produced 49 rows and search smoke 41 rows; real folder F4/F5/F6 and search F7/F8 runner paths built successfully; focused eval tests passed 36/36.
- X1: diagnosed the prior F0 D escalation three times; all returned VERIFIED with stages 1–6, 9, and 10 passing; classified as harness construction difference and fixed only Jarvis.Eval by using InMemoryAuditStore.
- X2: file/folder/search smoke completed with 64/48/40 verdict rows; all D F0 controls were VERIFIED; recorded and fixed an eval-only C search-control context defect.
- X2 dev-run-2: completed with 769 rows and 384 distinct collapsed rows; summary starts with control verdicts per verifier, all 12 controls VERIFIED for A/B/C/D; README marks it development-only and preserves dev-run-1.
- X3: app smoke completed with 49 rows (1 metadata + 48 verdicts); D verified both F0 controls and rejected F1/F9/F10/F11; tracked Notepad PIDs were cleaned in finally; focused eval tests passed 36/36.
- X4: not implemented; fake scheduler injection is blocked by the production collector interface being internal, and production changes are prohibited.
- X5: added eval Python gates to check-all and CI, expanded Wilson/collapse tests, and sanitized sandbox paths in raw verdict reasons; close-out validation remains to run.
- X5 gates: eval pytest 5 passed, ruff clean, mypy clean, Eval Release build passed, focused Eval tests 36 passed; raw result path scan found no absolute user paths.
- X5 close-out: full Release solution build passed; non-real-scheduler solution tests passed 416/416; ai-server plus eval pytest passed 131 tests; mypy and dotnet format verification passed; check-all and CI were not run because check-all enables real-scheduler tests.
- Y0: reproduced the evaluation Python gate locally (pytest 5 passed, ruff clean, mypy clean); CI maps to `scripts/check-all.ps1` from the repository root, with its eval gate equivalent to `ai-server\\.venv\\Scripts\\python.exe -m pytest eval/tests`, `ruff.exe check eval`, and `mypy.exe --strict --explicit-package-bases eval`.
- Y1: appended Amendment 3 documenting the dev-run-1 audit-root false failure, the C search-path correction, the frozen B/F6 pre-registration deviation, the pending F9 fidelity fix, and the no-tag release prerequisite.
- Y2: analysis now parses the frozen pre-registration table, puts control verdicts first, reports false-success/false-failure rates, deviations, D-not-better cases, and instability; eval tests 8 passed, ruff and strict mypy passed.
- Y3: app injector now uses tracked Process objects, StartTime comparisons, and harmless `ping.exe` mutations for F9/F11; Release Eval build and focused E2E tests passed 36/36. Elevated app smoke passed, but D falsely returned VERIFIED on both BAD F11 cases; recorded in FINDINGS.md and did not weaken D.
- Y4: file, folder, and search smoke runs passed; the first search smoke exposed and the Eval-only runner fixed an empty F1 receipt deserialization bug. The combined app development batch was interrupted after app smoke exceeded the practical run window; partial dev-run-3 output and logs were retained, and dev-run-1/dev-run-2 are marked superseded.
- Y5: completed the fake scheduler and reminder mutations F0/F1/F12-F17b through the production XML collector; fake reminder smoke produced D=VERIFIED for both F0 controls and D=FAILED for every BAD fault. Added `--scheduler fake|real`; real mode is guarded and was not run.
- Y6: Release solution build passed; non-real-scheduler solution tests passed 416/416; check-all passed with its RealScheduler category tests 13/13; Python ai-server/eval tests passed 121/8, ruff and both mypy gates passed, and dotnet format verification passed.
- Y6 follow-up: real scheduler mode now has the explicit environment/flag guard and restricts execution to reminder F0/F12/F14/F15 at one repetition; it was not executed.

## How CI maps to local commands

From the repository root, CI runs `scripts/setup-windows.ps1 -SkipOllamaPull -SkipOllamaCheck`, displays the locked `ollama` and `httpx` packages, runs `scripts/check-all.ps1`, then repeats the eval gate with the venv executables. The Y0 reproduction ran the eval commands in that order and each exited 0; the dry-run clean check showed only ignored caches, the venv, egg-info, and Python bytecode under `eval`/`ai-server`, none of which were removed.

## Scope still outstanding

- Part C is partial: the real `VerificationEngine` is exercised for signed file authorization and search-fault cases. Full all-tool signed lifecycle/control coverage and the reminder fixture through the production XML collector are still outstanding.
- Part D is partial: real file and search fault injectors are covered; the older modeled-state tests remain, and actual app/reminder mutations and F18 stale-evidence replay are outstanding.
- Parts F, G, and H are not implemented. Part F must connect the real tool executors and actual fault injection to a bounded, resumable sandbox runner before any evaluation results are interpreted.
* Z0: clean-clone CI reproduction reached the first build with missing assets; made restore explicit and constrained dotnet commands to single-node no-restore stages.
* Z0: the clean-clone gate then failed only in the separate real-scheduler category with access denied; check-all now runs the non-real-scheduler suite required for the CI gate, while retaining the category for explicit privileged runs.
* Z1: added 19 explicit E2E fault-case tests for folder, search, app, and reminder catalogue states; focused E2E total is 106 passing. Diagnose-case and PID-reuse cleanup tests remain for follow-up.
* Z2: documented F11 as a genuine D scope-limit finding: the collector filters to the authorized image, so the live unauthorized ping child is ignored; added Amendment 5 without changing D.
* Z3: added scripts/run-eval.ps1 for sequential fake-scheduler tool runs, per-tool logs/analysis, resumability, and final cleanup reporting. Full development runs remain user-run.
* Z4: release build, non-real-scheduler solution tests, Python tests/lint/type checks, architecture test, format verification, and final check-all all passed; no paper run or tag was created.
* Q0: restored the Category=RealScheduler pass in check-all with the normal user/admin desktop-session note; elevated local run passed 13 tests.
* Q1: added app preflight for existing Notepad/ping processes, explicit metadata override support, and a passing decoy-process preflight test; run-eval now fails early unless overridden.
* Q2: fresh fake-scheduler smoke completed for all five tools; fixed C's app time-window and reminder extra-task baseline defects in `2095b3b`, added Amendment 6, and classified all remaining deviations in FINDINGS.
