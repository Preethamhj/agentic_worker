# Phase 4: WPF UI

## Summary

Added a WPF host that submits prompts through the same shared Jarvis orchestration session as `jarvis run`. The UI contains task and verification-stage status, evidence reasons, audit history, and read-only settings/doctor views. Approval defaults to denial; dialog closure denies, approval timeout records `timeout`, escalation timeout aborts, and retry requests verification only. The view and view-model layer does not call tool, safety, or verifier implementations.

The shared composition is in `windows-host/src/Jarvis.Composition`. The CLI now uses this session factory as well. See [ADR-0006](../architecture/adr/ADR-0006-ui-composition.md) for the composition decision.

## Files changed

- Added `windows-host/src/Jarvis.Composition` with the shared session factory, task session, and host-facing interfaces.
- Updated `windows-host/src/Jarvis.Cli/Program.cs` and its project references to use the shared composition.
- Added the main WPF window, prompt/task/history/evidence/settings views, WPF decision dialog presenter, dispatcher adapter, and main view model under `windows-host/src/Jarvis.UI`.
- Restricted direct `Jarvis.UI` project references to Contracts, Core, Infrastructure, and Composition.
- Added `windows-host/tests/Jarvis.UI.Tests/MainWindowViewModelTests.cs` and extended architecture tests for UI/Eval boundaries.
- Added this summary and [the owner manual checklist](./PHASE-4-manual-test.md).

## Test results

All commands below ran on the Phase 4 working branch. Release solution build succeeded with 0 warnings and 0 errors.

Release non-RealScheduler solution tests: **489 passed, 0 failed, 0 skipped**:

| Project | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Jarvis.Architecture.Tests | 10 | 0 | 0 |
| Jarvis.Contracts.Tests | 47 | 0 | 0 |
| Jarvis.Core.Tests | 61 | 0 | 0 |
| Jarvis.E2E.Tests | 134 | 0 | 0 |
| Jarvis.Safety.Tests | 118 | 0 | 0 |
| Jarvis.Tools.Tests | 11 | 0 | 0 |
| Jarvis.Verification.Tests | 85 | 0 | 0 |
| Jarvis.UI.Tests | 23 | 0 | 0 |

Phase 4 test names include:

- `ApprovalViewModel_Preserves_Canonical_Arguments_And_Displays_Risk_And_Image_Path`
- `ApprovalViewModel_Displays_Reminder_Local_Time_Iso_And_Countdown`
- `ApprovalViewModel_Default_And_Close_Are_Deny`
- `ApprovalViewModel_Approve_And_Deny_Commands_Record_Human_Decisions`
- `ApprovalViewModel_Timeout_Uses_Timeout_Approver`
- `EscalationViewModel_Exposes_Evidence_And_Retry_Only_Decision`
- `EscalationViewModel_Abort_And_Timeout_Are_Not_Retry`
- `TaskViewModel_Shows_Timeline_Stages_And_Not_Verified_Unless_Verification_Passed`
- `TaskViewModel_Only_Reports_Verified_For_Completed_Verified_Result`
- `TaskViewModel_Never_Reports_Verified_For_NonVerified_Result` (3 cases)
- `HistoryViewModel_Combines_Audit_Events_And_Renders_Legacy_Safety_Rows`
- `SettingsViewModel_Exposes_Read_Only_Roots_Model_And_Doctor_Results`
- `WpfApprovalHandler_Dispatches_Dialog_And_Returns_Human_Approval`
- `WpfApprovalHandler_Maps_Window_Close_To_Deny`
- `WpfApprovalHandler_Maps_Timeout_To_Timeout_Approver`
- `WpfApprovalHandler_Matches_Console_Debug_LowRisk_AutoApproval_Only`
- `WpfApprovalHandler_Propagates_Cancellation_Without_Blocking`
- `WpfEscalationHandler_Dispatches_Dialog_And_Returns_Retry_Only`
- `WpfEscalationHandler_Abort_And_Timeout_Do_Not_Request_Retry`
- `RunPromptAsync_Uses_Shared_Orchestration_Session_And_Refreshes_Task_History`
- `RunPromptAsync_Rejects_Empty_Prompt_Without_Starting_Host`
- `Ui_Must_Reference_Only_Approved_Host_Projects`
- `Ui_Types_Must_Not_Call_Tool_Executors`
- `Evaluation_Harness_Must_Not_Reference_Ui`

The CLI compatibility selection (`CliEndToEndTests` and `CliOutputTests`) passed **9 tests**. The UI test project passed **23 tests** in both Debug and Release, including a test that verifies Debug-only low-risk auto-approval parity and no Release auto-approval. AI-server Python tests passed **121** (4 dependency deprecation warnings); evaluation Python tests passed **12**. Ruff and mypy passed for both Python areas. .NET format verification passed.

Commands to reproduce:

```powershell
dotnet build windows-host\Jarvis.sln --configuration Release --disable-build-servers -m:1 --no-restore -v:minimal
dotnet test windows-host\Jarvis.sln --configuration Release --no-build --no-restore --disable-build-servers -m:1 --filter "Category!=RealScheduler" -v:minimal
dotnet test windows-host\tests\Jarvis.E2E.Tests\Jarvis.E2E.Tests.csproj --disable-build-servers -m:1 --no-restore --filter "FullyQualifiedName~CliEndToEndTests|FullyQualifiedName~CliOutputTests" -v:minimal
dotnet test windows-host\tests\Jarvis.Architecture.Tests\Jarvis.Architecture.Tests.csproj --disable-build-servers -m:1 -v:minimal
Push-Location ai-server
.\.venv\Scripts\pytest.exe
.\.venv\Scripts\ruff.exe check src tests
.\.venv\Scripts\mypy.exe src
Pop-Location
.\ai-server\.venv\Scripts\python.exe -m pytest eval\tests
.\ai-server\.venv\Scripts\ruff.exe check eval
.\ai-server\.venv\Scripts\mypy.exe --strict --explicit-package-bases eval
dotnet format windows-host\Jarvis.sln --verify-no-changes --no-restore --verbosity minimal
```

## Decisions and deviations

- The existing `Jarvis.Contracts.IApprovalHandler` is used instead of the prompt's `IApprovalProvider`; no `IApprovalProvider` contract exists. This keeps policy and orchestration contracts unchanged.
- WPF uses a lightweight existing-style `INotifyPropertyChanged`/`ICommand` implementation; no NuGet dependency was added.
- The architecture-approved `Jarvis.Composition` library references the host implementation projects to construct the existing dependency graph. `Jarvis.UI` itself has only the four permitted direct Jarvis references.
- `scripts/check-all.ps1` was **not run**: it unconditionally enables `JARVIS_REAL_SCHEDULER=1` and executes the real-scheduler category. The close-out scope explicitly excludes RealScheduler, so the equivalent build, filtered solution tests, Python, lint/type checks, and format verification were run individually instead. The RealScheduler category and the wrapper script are therefore not verified for this Phase 4 head.

## Known limitations and open checks

- The owner must execute the 10-step interactive checklist in [PHASE-4-manual-test.md](./PHASE-4-manual-test.md). Interactive WPF usability was not exercised in this automated run.
- Real Task Scheduler tests were not run.
- CI was not checked.
- No tag was created or changed.

## Phase commits

- U0: `73a1f51`
- U1: `cd54696`
- U2: `80ec897`
- U3: `d1df091`
- U4: `01fa235`
- U5 close-out documentation and status are recorded in the corresponding follow-up commit.
