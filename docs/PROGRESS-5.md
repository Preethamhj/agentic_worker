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

## Scope still outstanding

- Part C is partial: the real `VerificationEngine` is exercised for signed file authorization and search-fault cases. Full all-tool signed lifecycle/control coverage and the reminder fixture through the production XML collector are still outstanding.
- Part D is partial: real file and search fault injectors are covered; the older modeled-state tests remain, and actual app/reminder mutations and F18 stale-evidence replay are outstanding.
- Parts F, G, and H are not implemented. Part F must connect the real tool executors and actual fault injection to a bounded, resumable sandbox runner before any evaluation results are interpreted.
