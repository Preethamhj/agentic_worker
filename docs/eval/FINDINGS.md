# Evaluation findings

## W1 smoke — D false negative on GOOD controls

- Case IDs: `594b5994f68f93ac1ff88e89cc566eac-r1-F0`, `51d91a1c0f04b1184c753a0007643a06-r1-F0`
- Fault: F0 control (`GOOD`)
- Fault: verifier D returned `ESCALATE`, not `VERIFIED`.
- Evidence: raw smoke rows report `Snapshot collection failed: Snapshot I/O failed: The process cannot ...` during independent verification.
- Run: W1 smoke, 2 file tasks, 1 repetition, 64 verdict rows plus metadata.
- Action: recorded only; production verifier, executor, safety, and host code were not changed.

### X1 diagnosis — harness construction difference resolved

The original F0 escalation reproduced in the prior runner because `EvalFileRun` opened `audit.db` inside the watched sandbox root before `VerificationEngine.BeginStepAsync`. The Phase 3c control `CreateFile_Real_Executor_Verifies_And_Removed_File_Fails` uses `InMemoryAuditStore`, constructs the same `AuthorizationService` with `InMemoryDpapiKeyStore`, uses the same `StepExecutor.PrepareAuthorization`, real `CreateFileExecutor`, `VerificationEngine`, `FileEvidenceCollector`, `SnapshotService`, and a bounded temporary root. The eval differed in its audit store and therefore included an open SQLite file in the watched snapshot scope.

Classification: **(1) harness construction difference**. Replacing the eval-only audit store with `InMemoryAuditStore` fixed the issue without changing production verification, execution, safety, or host code.

Diagnosis command: `dotnet windows-host\src\Jarvis.Eval\bin\Release\net10.0-windows\Jarvis.Eval.dll --diagnose-case 594b5994f68f93ac1ff88e89cc566eac-r1-F0`

Evidence from three runs: D returned `Verified` each time. Stages 1–6, 9, and 10 were `PASSED`; stages 7 and 8 were `NOT_RUN` because they are deferred to Phase 6. No evidence exception was reported after the harness fix.

### X2 smoke — C false negative on GOOD search control

- Tool: `fs.search_files`
- Fault: F0 control (`GOOD`)
- Evidence: X2 search smoke raw rows reported C=`FAILED` for both F0 task cases while D=`VERIFIED` for both.
- Cause: the eval context supplied an empty `ReportedPaths` list instead of deserializing the real search executor receipt. This was corrected in `Jarvis.Eval`; D was unaffected and all D F0 controls remained VERIFIED.
