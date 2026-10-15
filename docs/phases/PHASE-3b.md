# Phase 3b - Process, Search, and Scheduler Verification

## Summary

Implemented independent OS-evidence verification for `fs.search_files`, `app.launch`, and `task.schedule_reminder`, plus the real reminder executor and Windows Task Scheduler adapter. `Jarvis.Verification` still references only `Jarvis.Contracts` and `Jarvis.Infrastructure`; its own process enumeration and Task Scheduler COM/XML inspection are intentionally separate from the execution-side implementation in `Jarvis.Tools`.

The CLI now signs the allowlisted executable path for launches and registers the reminder executor. Reminder tasks are scheduled under the current user's `\Jarvis\<authorization-id>` task path and run the host in interactive, non-elevated mode. The notification host uses a `NotifyIcon` balloon. Verification stages 7 and 8 remain `NOT_RUN`; stages 1-6, 9, and 10 are applied as appropriate.

## Validation results

Phase 3b close-out validation on 2026-10-15 passed:

| Gate | Result |
|---|---:|
| C# build | Passed, 0 warnings, 0 errors |
| C# tests | 283 passed, 0 failed, 0 skipped |
| Python tests | 103 passed, 0 failed |
| mypy | No issues in 29 source files |
| C# formatting | Passed |
| Python warnings | 4 deprecation warnings from the existing Starlette/HTTPX TestClient stack |

The focused verifier suite passed 46/46 tests. The Phase 3b verification class passed 17/17, including its search evidence and process-launch cases. The E2E suite passed 26/26 and the architecture suite passed 6/6.

### Phase 3b verifier tests

All 17 tests in `Phase3bVerificationTests` passed locally:

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
- `ScheduleReminder_Executor_Creates_A_Task_That_Independent_Xml_Verification_Accepts`

Search receipts are intentionally untrusted. The engine passes the receipt's reported path list to `SearchEvidenceCollector`, which compares the claimed path set against an independent `FindFirstFileEx` enumeration of the signed root. Under-reporting and over-reporting fail; reaching the signed result cap escalates; exact claims, including nested Kannada paths, verify. Fifty consecutive clean runs produced no false failures.

`AppLaunch_Verifies_Live_NonElevated_Child_By_OS_Image_And_Parent` uses `[NonElevatedFact]`; it ran locally with no skip. On an elevated Windows CI runner, this one fact is skipped because that test must verify a normal-integrity child. No test requires an elevated host, so `[ElevatedFact]` is not applied. The tests have no early returns.

The complete 46-test Verification suite also includes the prior file/folder fault fixtures, real executor round trips, snapshot tests, persistence checks, and clean-run latency case. The E2E suite covers CLI registration and resolved-path authorization; architecture tests guard both direct and transitive project dependencies.

### Close-out reproduction and cleanup

```powershell
dotnet test windows-host\tests\Jarvis.Verification.Tests\Jarvis.Verification.Tests.csproj --filter FullyQualifiedName~Phase3bVerificationTests --no-restore
.\scripts\check-all.ps1
schtasks.exe /Query /FO LIST | Select-String -Pattern 'Jarvis'
Get-Process -Name notepad,calc,CalculatorApp,mspaint -ErrorAction SilentlyContinue
```

The task and process checks returned no matches after verification tests completed. The test suite had launched two Calculator processes; both were closed by their specific process IDs before the final empty process check.

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
- Reminder notifications use a `NotifyIcon` balloon rather than Windows app-identity Toast notifications.
- Search verification reopens and checks each receipt-reported path as an untrusted claim (root containment, file attributes, reparse status, and pattern); it does not establish that the search result set is complete. Receipt success fields are not evidence.
- App launch verification uses OS process state, image path, direct parent, start time, and token elevation. A positive result is not expected when the host/process is elevated.
- The snapshot scope is non-recursive and limited to the signed watch scope. It cannot detect side effects elsewhere; metadata-first comparison can miss a same-size edit whose modification time is restored. The 100 ms timestamp tolerance and these limitations are recorded in `docs/threat-model.md`.
- CI configuration triggers pushes to all branches and pull requests. The previously checked Actions run **38611609458** succeeded, but predates this unpushed Phase 3b working tree; it is not evidence that these changes passed CI. No new run was started.

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
