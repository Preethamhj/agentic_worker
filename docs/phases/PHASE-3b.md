# Phase 3b - Process, Search, and Scheduler Verification

## Summary

Implemented independent OS-evidence verification for `fs.search_files`, `app.launch`, and `task.schedule_reminder`, plus the real reminder executor and Windows Task Scheduler adapter. `Jarvis.Verification` still references only `Jarvis.Contracts` and `Jarvis.Infrastructure`; its own process enumeration and Task Scheduler COM/XML inspection are intentionally separate from the execution-side implementation in `Jarvis.Tools`.

The CLI now signs the allowlisted executable path for launches and registers the reminder executor. Reminder tasks are scheduled under the current user's `\Jarvis\<authorization-id>` task path and run the host in interactive, non-elevated mode. The action has exactly two arguments: `--jarvis-reminder` and a 32-hex task ID. Title and message are stored in a task-ID-keyed data file under `%LOCALAPPDATA%\Jarvis\reminders`, never in the command line. The notification host uses a `NotifyIcon` balloon. Verification stages 7 and 8 remain `NOT_RUN`; stages 1-6, 9, and 10 are applied as appropriate.

## Validation results

Phase 3b close-out validation on 2026-10-15 passed:

| Gate | Result |
|---|---:|
| C# build | Passed, 0 warnings, 0 errors |
| C# tests | 305 passed, 0 failed, 0 skipped |
| Python tests | 113 passed, 0 failed |
| mypy | No issues in 29 source files |
| C# formatting | Passed |
| Python warnings | 4 deprecation warnings from the existing Starlette/HTTPX TestClient stack |

The complete Verification suite passed 53/53 tests, including the real Task Scheduler reminder tests. The complete E2E suite passed 31/31 tests, including reminder dispatch tests. The class-specific reminder message cases passed 4/4 on the real executor, Windows Task Scheduler, and independent verifier.

### Phase 3b verifier tests

All 24 tests in `Phase3bVerificationTests` passed locally:

- `Search_Verifies_Only_Existing_Matching_Paths_Inside_Signed_Root`
- `Search_Verifies_A_Matching_Existing_Path_Without_Trusting_Receipt_Success_Fields`
- `Search_Fails_On_Malformed_Receipt_Path_Claims`
- `Search_UnderReporting_Is_Failed_By_Independent_FindFirstFileEx_Enumeration`
- `Search_OverReporting_Is_Failed_By_Independent_FindFirstFileEx_Enumeration`
- `Search_Reaching_Result_Cap_Escalates_Instead_Of_Passing`
- `Search_Exact_Kannada_Matches_In_Nested_Folders_Are_Verified`
- `Search_Exact_Clean_Results_Verify_In_50_Consecutive_Runs`
- `AppLaunch_Fails_When_Receipt_Claims_Success_But_No_Child_Process_Exists`
- `AppLaunch_Verifies_Live_NonElevated_Child_By_OS_Image_And_Parent`
- `ScheduleReminder_Verification_Fails_When_Task_Is_Missing`
- `ScheduleReminder_Executor_Rejects_Subsecond_Time_Without_Registering`
- `ScheduleReminder_With_Wrong_Time_Is_Failed`
- `ScheduleReminder_With_Wrong_Action_Is_Failed`
- `ScheduleReminder_Disabled_Task_Is_Failed`
- `ScheduleReminder_Extra_Unauthorized_Task_Is_Failed`
- `Reminder_Authorization_Derives_Action_And_Arguments_From_The_Record`
- `ScheduleReminder_Executor_And_Independent_Verifier_Accept_Safe_Messages` (`quotes`)
- `ScheduleReminder_Executor_And_Independent_Verifier_Accept_Safe_Messages` (`ampersand-percent`)
- `ScheduleReminder_Executor_And_Independent_Verifier_Accept_Safe_Messages` (`long-message`, exactly 200 characters)
- `ScheduleReminder_Executor_And_Independent_Verifier_Accept_Safe_Messages` (`kannada`)
- `ScheduleReminder_Verifier_Fails_When_Reminder_Data_Is_Missing`
- `ScheduleReminder_Verifier_Fails_When_Reminder_Data_Differs_From_Authorization`
- `ScheduleReminder_Executor_Rejects_Control_Characters_Without_Registering`

Search receipts are intentionally untrusted. The engine passes the receipt's reported path list to `SearchEvidenceCollector`, which compares the claimed path set against an independent `FindFirstFileEx` enumeration of the signed root. Under-reporting and over-reporting fail; reaching the signed result cap escalates; exact claims, including nested Kannada paths, verify. Fifty consecutive clean runs produced no false failures.

`AppLaunch_Verifies_Live_NonElevated_Child_By_OS_Image_And_Parent` uses `[NonElevatedFact]`; it ran locally with no skip. On an elevated Windows CI runner, this one fact is skipped because that test must verify a normal-integrity child. No test requires an elevated host, so `[ElevatedFact]` is not applied. The tests have no early returns.

Reminder argument/path authorization and verifier independence:

- `AuthorizationService` derives the task ID from its generated authorization ID and signs `action_path`, `action_arguments`, `task_id`, `task_path`, and `reminder_data_path`. The action path is the full path of the current trusted host executable; in production this is the Jarvis CLI host. The executor consumes these signed expected fields and canonical title/message/time, not a command or path from the plan.
- The verifier reads expected executable/arguments from the authorization record and compares them to the independently collected Task Scheduler XML. A fault test registers the same trigger time with a different executable and gets `FAILED`; extra arguments are not accepted by the exact argument comparison.
- Verifier argument normalization is only CRLF-to-LF and CR-to-LF. This accommodates line-ending serialization differences without forgiving changes to the executable arguments.
- Before `VERIFIED`, the verifier also reads the signed expected data path and checks its title/message against `CanonicalArgs`. Missing and altered payload fault tests both fail. This is point-in-time integrity evidence; a same-user process could alter the task or file after verification and before dispatch (see `docs/threat-model.md`).

The real Task Scheduler tests print the registered XML `Exec` element. For example:

```xml
<Exec xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task"><Command>C:\Users\ASUS\Desktop\agentic worker\windows-host\tests\Jarvis.Verification.Tests\bin\Debug\net10.0\testhost.exe</Command><Arguments>--jarvis-reminder 2b7da864774148a4b2aa756694fccb0d</Arguments><WorkingDirectory>C:\Users\ASUS\Desktop\agentic worker\windows-host\tests\Jarvis.Verification.Tests\bin\Debug\net10.0</WorkingDirectory></Exec>
```

This integration test runs inside `testhost.exe`, so the registered command is that test host. A production CLI invocation signs its own absolute host executable. In both cases the arguments contain only the reminder switch and task ID, never title or message.

`ReminderDispatcherTests` verifies valid dispatch displays the task-ID-keyed payload and deletes the file, while malformed and unknown IDs are refused without notification. Both the full suite and the focused real-scheduler tests cleaned up the task and payload they created.

The complete 53-test Verification suite also includes the prior file/folder fault fixtures, real executor round trips, snapshot tests, persistence checks, and clean-run latency case. The E2E suite covers CLI registration and resolved-path authorization; architecture tests guard both direct and transitive project dependencies.

### Close-out reproduction and cleanup

```powershell
dotnet test windows-host\tests\Jarvis.Verification.Tests\Jarvis.Verification.Tests.csproj --filter FullyQualifiedName~Phase3bVerificationTests --no-restore
.\scripts\check-all.ps1
schtasks.exe /Query /FO LIST | Select-String -Pattern 'Jarvis'
Get-Process -Name notepad,calc,CalculatorApp,mspaint -ErrorAction SilentlyContinue
```

After the complete suite, `schtasks /Query /FO LIST` filtered for Jarvis returned no matches. The suite left `CalculatorApp` PID 73332 running; it was stopped by that specific PID, after which `Get-Process -Name notepad,calc,CalculatorApp,mspaint` returned no processes. `%LOCALAPPDATA%\Jarvis\reminders` existed and contained no files. The subsequent focused real-scheduler run passed all four message cases and its post-run checks were also empty.

The 5,000-file snapshot remeasurement enumerated 5,000 entries in **30.29 ms**, below the 10,000-entry cap. A large user's immediate parent folder is still fully enumerated non-recursively; if it exceeds the configured cap, verification escalates rather than passing. Create-tool default watch scope is the target's parent folder, non-recursive.

### Python client / model evaluation

The final lock pins `ollama==0.4.7`, `httpx==0.27.2`, and `pydantic==2.9.2`. HTTPX 0.27.2 is compatible with the locked Starlette 0.36.3 TestClient; 0.28.1 caused test collection failures because it removed the `app` shortcut expected by this Starlette version. Ollama 0.4.7 supports JSON Schema objects through its `format` parameter.

The final 20-prompt evaluation using `qwen2.5:7b-instruct` reported:

| Measure | Result |
|---|---:|
| JSON validity | 100.0% |
| Tool action accuracy | 100.0% |
| Argument accuracy | 100.0% |
| Retries | 0 |
| p50 latency | 18,157.4 ms |
| p95 latency | 49,872.8 ms |

JSON validity and tool/argument accuracy remain unchanged at 100% from the Phase 1 baseline. Latency is higher than the historical Phase 1 p95 of 28,401.1 ms; this evaluation is local-model timing and did not affect the accuracy gate.

## Dependency graph and transitive closure

The architecture test printed this project reference graph:

```text
Jarvis.Contracts -> [ (leaf / no Jarvis references) ]
Jarvis.Core -> [ Jarvis.Contracts, Jarvis.Infrastructure ]
Jarvis.Safety -> [ Jarvis.Contracts, Jarvis.Infrastructure ]
Jarvis.Tools -> [ Jarvis.Contracts, Jarvis.Safety ]
Jarvis.Verification -> [ Jarvis.Contracts, Jarvis.Infrastructure ]
Jarvis.Infrastructure -> [ Jarvis.Contracts ]
Jarvis.Cli -> [ Jarvis.Contracts, Jarvis.Core, Jarvis.Infrastructure, Jarvis.Safety, Jarvis.Tools ]
```

The measured transitive assembly reference closure for `Jarvis.Verification` contains **112 assemblies**:

```text
Jarvis.Contracts
Jarvis.Infrastructure
Jarvis.Verification
Microsoft.CSharp
Microsoft.Data.Sqlite
Microsoft.Win32.Primitives
Microsoft.Win32.Registry
netstandard
Serilog
Serilog.Formatting.Compact
Serilog.Sinks.File
SQLitePCLRaw.core
System.Collections
System.Collections.Concurrent
System.Collections.Immutable
System.Collections.NonGeneric
System.Collections.Specialized
System.ComponentModel
System.ComponentModel.EventBasedAsync
System.ComponentModel.Primitives
System.ComponentModel.TypeConverter
System.Console
System.Data.Common
System.Diagnostics.Contracts
System.Diagnostics.DiagnosticSource
System.Diagnostics.FileVersionInfo
System.Diagnostics.Process
System.Diagnostics.StackTrace
System.Diagnostics.TextWriterTraceListener
System.Diagnostics.TraceSource
System.Diagnostics.Tracing
System.Drawing.Primitives
System.Formats.Asn1
System.IO.Compression
System.IO.Compression.Brotli
System.IO.Compression.ZipFile
System.IO.FileSystem
System.IO.FileSystem.AccessControl
System.IO.FileSystem.DriveInfo
System.IO.FileSystem.Watcher
System.IO.IsolatedStorage
System.IO.MemoryMappedFiles
System.IO.Pipelines
System.IO.Pipes
System.Linq
System.Linq.Expressions
System.Linq.Parallel
System.Linq.Queryable
System.Memory
System.Net.Http
System.Net.Http.Json
System.Net.HttpListener
System.Net.Mail
System.Net.NameResolution
System.Net.NetworkInformation
System.Net.Ping
System.Net.Primitives
System.Net.Quic
System.Net.Requests
System.Net.Security
System.Net.Sockets
System.Net.WebClient
System.Net.WebHeaderCollection
System.Net.WebProxy
System.Net.WebSockets
System.Net.WebSockets.Client
System.Numerics.Vectors
System.ObjectModel
System.Private.CoreLib
System.Private.DataContractSerialization
System.Private.Uri
System.Private.Xml
System.Private.Xml.Linq
System.Reflection.DispatchProxy
System.Reflection.Emit
System.Reflection.Emit.ILGeneration
System.Reflection.Emit.Lightweight
System.Reflection.Metadata
System.Reflection.Primitives
System.Resources.Writer
System.Runtime
System.Runtime.CompilerServices.VisualC
System.Runtime.InteropServices
System.Runtime.Intrinsics
System.Runtime.Loader
System.Runtime.Numerics
System.Runtime.Serialization.Formatters
System.Runtime.Serialization.Json
System.Runtime.Serialization.Primitives
System.Runtime.Serialization.Xml
System.Security.AccessControl
System.Security.Claims
System.Security.Cryptography
System.Security.Cryptography.ProtectedData
System.Security.Principal.Windows
System.Text.Encoding.Extensions
System.Text.Encodings.Web
System.Text.Json
System.Text.RegularExpressions
System.Threading
System.Threading.Channels
System.Threading.Overlapped
System.Threading.Tasks.Parallel
System.Threading.Thread
System.Threading.ThreadPool
System.Transactions.Local
System.Web.HttpUtility
System.Xml.ReaderWriter
System.Xml.XDocument
System.Xml.XmlSerializer
System.Xml.XPath
System.Xml.XPath.XDocument
```

`Jarvis.Tools`, `Jarvis.Safety`, and `Jarvis.Core` are not reachable. The architecture test asserts both direct and transitive constraints.

## Decisions, deviations, and limitations

- The execution-side Task Scheduler adapter uses the `TaskScheduler` NuGet package, only from `Jarvis.Tools`. Verification independently inspects OS task state through its own COM/XML code; this deliberate duplication avoids a shared implementation or trust path.
- Task Scheduler trigger XML has whole-second precision, so the reminder executor rejects subsecond times instead of silently rounding.
- Reminder title/message are stored out-of-band in an exclusive-created, task-ID-keyed JSON file; the signed Task Scheduler action contains only the fixed host path and task ID switch. The registry, Python schema consumer, and `ToolPolicy` enforce the 200-character limit and reject control characters while allowing Unicode.
- The independent verifier sources the expected action and data path from signed authorization fields, and the expected text from canonical arguments. It rejects a changed executable, arguments, missing data, or changed data. Its only argument normalization is line-ending normalization.
- Reminder notifications use a `NotifyIcon` balloon rather than Windows app-identity Toast notifications.
- Scheduled-task evidence is COM-only. The collector reports `scheduler_method: COM` only for COM-obtained XML; a COM access failure produces insufficient evidence and therefore `ESCALATE`. There is deliberately no `schtasks /Query /XML` fallback in the verifier.
- Reminder registration refuses a `dotnet.exe` host (including `dotnet run` and `dotnet Jarvis.Cli.dll`), because the required action arguments intentionally contain no DLL path. Use the published `Jarvis.Cli.exe`; host-path injection is used only by tests.
- Search verification reopens and checks each receipt-reported path as an untrusted claim (root containment, file attributes, reparse status, and pattern); it does not establish that the search result set is complete. Receipt success fields are not evidence.
- App launch verification uses OS process state, image path, direct parent, start time, and token elevation. A positive result is not expected when the host/process is elevated.
- The snapshot scope is non-recursive and limited to the signed watch scope. It cannot detect side effects elsewhere; metadata-first comparison can miss a same-size edit whose modification time is restored. The 100 ms timestamp tolerance and these limitations are recorded in `docs/threat-model.md`.
- CI configuration triggers pushes to all branches and pull requests. This Phase 3b close-out change is not complete until its commit is pushed and the single requested CI status check is recorded.

## Files added or changed

Added independent evidence collectors and tests: `ProcessEvidenceCollector.cs`, `SearchEvidenceCollector.cs`, `ScheduledTaskEvidenceCollector.cs`, `IndependentEvidenceObservation.cs`, and `Phase3bVerificationTests.cs`. Added the reminder scheduler interface/implementation and executor under `Jarvis.Tools`, plus ADR-0005.

Updated `VerificationEngine.cs`, `FileEvidenceCollector.cs`, `AuthorizationService.cs`, `ToolPolicy.cs`, `AppLaunchExecutor.cs`, CLI registration and authorization signing, project/test target frameworks, `tools.registry.json`, `ci.yml`, the Python project pins and lock, `docs/threat-model.md`, and `docs/phases/PHASE-3-progress.md`.

## Reproduction

```powershell
.\scripts\check-all.ps1
dotnet test .\windows-host\tests\Jarvis.Verification.Tests\Jarvis.Verification.Tests.csproj
dotnet test .\windows-host\tests\Jarvis.E2E.Tests\Jarvis.E2E.Tests.csproj
dotnet test .\windows-host\tests\Jarvis.Architecture.Tests\Jarvis.Architecture.Tests.csproj --logger "console;verbosity=detailed"
.\ai-server\.venv\Scripts\python.exe -m pip show ollama httpx
.\ai-server\.venv\Scripts\python.exe .\scripts\eval_llm_json.py
```

## Reminder hardening follow-up

The current phase-3b reminder contract signs the task name `reminder-<task-id>`, a trigger end boundary ten minutes after the start, and one-hour expiry cleanup. The dispatcher validates the task ID, displays and consumes the payload, then best-effort unregisters its own task. Unregister failures are reported and do not make a successfully displayed reminder fail.

The verifier compares each expected setting against the signed authorization record and the independently observed Task Scheduler XML. It does not derive expected values from executor receipts.

### Scheduler fault table

| Fault | Expected status | Test name |
|---|---|---|
| Task name differs from signed name | FAILED | `Changed_Signed_Scheduler_Setting_Is_Failed` (`task_name`) |
| Trigger time differs from signed time | FAILED | `Changed_Signed_Scheduler_Setting_Is_Failed` (`start_boundary`) |
| Action executable differs from signed path | FAILED | `Changed_Signed_Scheduler_Setting_Is_Failed` (`command`) |
| Action arguments differ from signed arguments | FAILED | `Changed_Signed_Scheduler_Setting_Is_Failed` (`arguments`) |
| Disallow-start-on-battery differs | FAILED | `Changed_Signed_Scheduler_Setting_Is_Failed` (`disallow_start_if_on_batteries`) |
| Stop-on-battery differs | FAILED | `Changed_Signed_Scheduler_Setting_Is_Failed` (`stop_if_going_on_batteries`) |
| Execution time limit differs | FAILED | `Changed_Signed_Scheduler_Setting_Is_Failed` (`execution_time_limit`) |
| StartWhenAvailable differs | FAILED | `Changed_Signed_Scheduler_Setting_Is_Failed` (`start_when_available`) |
| Multiple-instances policy differs from IgnoreNew | FAILED | `Changed_Signed_Scheduler_Setting_Is_Failed` (`multiple_instances`) |
| Logon type differs from InteractiveToken | FAILED | `Changed_Signed_Scheduler_Setting_Is_Failed` (`logon_type`) |
| Run level differs from LeastPrivilege | FAILED | `Changed_Signed_Scheduler_Setting_Is_Failed` (`run_level`) |
| EndBoundary differs from signed start plus offset | FAILED | `Changed_Signed_Scheduler_Setting_Is_Failed` (`end_boundary`) |
| DeleteExpiredTaskAfter differs from signed duration | FAILED | `Changed_Signed_Scheduler_Setting_Is_Failed` (`delete_expired_task_after`) |
| Task has fired and was removed before verification | FAILED with explicit missing-task reason | `Already_Fired_And_Removed_Task_Is_Failed_With_Explicit_Reason` |
| No scheduler setting fault | VERIFIED | `No_Scheduler_Setting_Fault_Aggregates_To_Verified` |

The 13 setting-fault rows are cases in one theory. The aggregation check confirms that a definite `FAILED` finding takes precedence over an independent `ESCALATE` result.

### Test categories and real-state boundary

- `AppLaunchRepeat`: 50 Notepad launches, skipped on elevated runners, each test-launched PID is tracked and stopped by PID.
- `ReminderRepeat`: 50 executor/dispatcher cycles against a fake scheduler and a temporary payload directory.
- `RealScheduler`: gated by `JARVIS_REAL_SCHEDULER=1`; the real-scheduler fact verifies task XML and unregisters the exact task in `finally`. The regular test suite leaves this unset and does not access the real Task Scheduler.
- The shared `ReminderDataPayload.valid1.json` contract fixture is schema-validated by the Python and C# contract suites.

`scripts/smoke-reminder.ps1` checks the task name and ID relationship, action, one-time trigger, both trigger boundaries, battery behavior, execution limit, start behavior, instance policy, principal, and expiry cleanup. It is a manual real-state smoke test and is not run by automated validation.
