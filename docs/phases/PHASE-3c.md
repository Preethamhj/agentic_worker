# Phase 3c: Host Verification Engine

Replace the host's `StubVerifier` with the independent `VerificationEngine`. A task may reach `completed` only when the engine returns `VERIFIED`.

Parts:
- A: Locate and document the verifier integration seams.
- B: Add escalation handling for `ESCALATE`.
- C: Wire the real verification engine into the host.
- D: Report true verification status to the AI server.
- E: Display verification evidence in CLI output and history.
- F: Add real-engine end-to-end verification tests for each tool.
- G: Prove executor-reported success cannot override failed independent evidence.
- H: Run regression gates and document the fault matrix.

## Fault matrix

| Fault | Expected status | Test name |
|---|---|---|
| Search executor receipt lists a missing file | FAILED | `SearchFiles_Real_Executor_Verifies_And_Faulted_Search_Output_Fails` |
| Created folder is removed before evidence collection | FAILED | `CreateFolder_Real_Executor_Verifies_And_Removed_Folder_Fails` |
| Created file is removed before evidence collection | FAILED | `CreateFile_Real_Executor_Verifies_And_Removed_File_Fails` |
| Notepad process is stopped before evidence collection | FAILED | `AppLaunch_Notepad_Nonmatching_Process_Fails_Within_Timeout` |
| Reminder XML changes signed `StartWhenAvailable` | FAILED | `ScheduleReminder_Fake_Scheduler_Real_Engine_Verifies_And_Tampered_Task_Fails` |
| Debug executor reports success without creating the signed file | FAILED | `Debug_Executor_Lie_Reports_Success_But_Real_Verifier_Fails` |

No-fault controls in the per-tool end-to-end tests end `VERIFIED`; the Release assembly inspection is covered by `Release_Executor_Assembly_Does_Not_Contain_Debug_Lie_Switch`.
