# Phase 5 Progress

Branch: `phase-5`, based on `5b68602` (`phase-3c-done`).

## Part records

- Setup: created and pushed the `phase-5` branch from `5b68602`.
- Part A: `bf1f64c`; frozen protocol and phase part list; documentation-only, no tests applicable.
- Part B: `460489c`; `RunCase_Executes_Once_And_Gives_All_Verifiers_The_Same_Context`, `RunCase_Shuffles_Verifier_Order_Deterministically_From_Seed`, `RunCase_Records_Verifier_Exception_As_Error_Not_Verified` — 3 passed; Release solution build passed (0 warnings/errors).
- Part C: `cb0ad22`; `Receipt_Verifier_Accepts_A_NoOp_Success_Receipt`, `Self_Check_Verifier_Accepts_A_Deleted_After_Success_Result`, `Predicate_Verifier_Catches_Wrong_Content_But_Does_Not_Check_Extra_Files`, `Control_Gives_Verified_For_All_Four_Verifiers` and 4 `Engine_Verifier_Uses_Real_VerificationEngine_For_Control_And_File_Faults` cases (F0/F1/F2/F6) — 8 passed; Release solution build passed (0 warnings/errors).
- Part D: `d54aa52`; `Injector_Produces_Declared_Fault_State_And_Ground_Truth` — 20 fault rows passed, `Injector_Rejects_A_Fault_That_Does_Not_Apply_To_The_Tool` passed; `Host_Projects_Must_Not_Reference_Evaluation_Harness` passed; Release solution build passed (0 warnings/errors).
- Part E: `4966bf1`; `Checked_In_Catalog_Is_Deterministic_And_Valid_Against_Its_Schema`, `Catalog_Has_Exact_Tool_Counts_And_Unique_Fixed_Seed_Ids`, `Every_Task_Includes_Its_Applicable_Faults_And_Catalogue_Bounds` — 3 passed; catalogue contains 50 tasks and 360 cases per repetition (310 matched plus 50 F18 diagnostic); Release solution build passed (0 warnings/errors).

## Scope still outstanding

- Part C is partial: the real `VerificationEngine` is exercised for file control/no-op/wrong-content/removed-target cases, but that isolated fixture uses a permissive `IAuthorizationVerifier` and a placeholder signature rather than the host's signed authorization lifecycle. Predicate-only checks cover files, search results, and process image presence; reminder predicates require a scheduler reader that is not yet wired.
- Part D is partial: fault injection currently produces and tests deterministic modeled post-execution states in `EvalFaultState`; it is not yet connected to real executor decorators or OS-state mutation. The host assembly reference guard is implemented and passed.
- Parts F, G, and H are not implemented. Part F must connect the real tool executors and actual fault injection to a bounded, resumable sandbox runner before any evaluation results are interpreted.
