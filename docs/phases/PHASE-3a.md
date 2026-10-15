# Phase 3a - Verification Foundation and File/Folder Verification

## Summary

Implemented independent operating-system verification for `fs.create_file` and
`fs.create_folder`, and the verification-engine foundation that later phases
can extend. `StubVerifier` remains wired in the host. The engine runs stages 1
through 6, 9, and 10; stages 7 and 8 are explicitly `NOT_RUN`.

Evidence is collected independently of executor receipt outcomes, stdout,
stderr, exit codes, and receipt timestamps. The host supplies an
`ExecutionObservation` measured with `IClock`; evidence freshness is checked
against that observation. Signed authorization validation is performed through
`IAuthorizationVerifier`, and its consumed-to-verified lifecycle transition is
audited before verification continues.

## Implementation

- Added `StepToken`, `ExecutionObservation`, `IAuthorizationVerifier`, and the
  begin/verify calls on `IVerifier`.
- Added `VerificationEngine`, including evidence nonce binding, canonical
  SHA-256 validation, replay rejection, stage aggregation, and audit reporting.
- Added `FileEvidenceCollector` with an independent Win32 P/Invoke layer for
  handle-based existence/type/reparse/final-path checks, file size, SHA-256,
  creation time, and last-write time.
- Added `SnapshotService` for before/after watch-scope snapshots, capped at
  10,000 entries by default. It records file size, mtime, and hashes for files
  smaller than 1 MiB; reports configured noise exclusions; and does not follow
  reparse points. Changes outside the signed `allowed_effects` fail
  verification. Only the signed watched scopes are covered.
- Persisted complete immutable evidence JSON, verification results, and the
  `Verified` authorization lifecycle audit in SQLite.
- Added dependency architecture checks that print and validate the transitive
  assembly-reference closure.

The Win32 P/Invoke code in Verification intentionally duplicates the relevant
OS API boundary rather than sharing or calling `Jarvis.Tools` code (including
`Win32PathResolver`). This is deliberate: independent verification must not
share the executor's implementation or state.

## Fault and regression tests

`Jarvis.Verification.Tests.FileVerificationFaultTests`:

- `File_Absent_Is_Failed_Despite_Lying_Success_Receipt`
- `File_With_Wrong_Content_Is_Failed`
- `Truncated_File_Is_Failed`
- `File_Created_At_Wrong_Location_Is_Failed`
- `Extra_Unauthorized_File_Is_Failed`
- `Modified_Unauthorized_File_Is_Failed`
- `Deleted_Unauthorized_File_Is_Failed`
- `Folder_Replaced_By_Junction_Is_Failed`
- `Expired_Authorization_Is_Failed`
- `Unconsumed_Authorization_Is_Failed`
- `Replayed_Nonce_Is_Failed`
- `Stale_Evidence_Is_Failed`
- `Tampered_Receipt_Claims_Are_Failed`
- `Exclusively_Locked_File_Escalates`
- `Snapshot_Over_Entry_Cap_Escalates`
- `Clean_File_And_Folder_Creation_Have_No_False_Failures_And_Report_Latency`
- `Verification_Evidence_And_Result_Are_Persisted`
- `SqliteAuditStore_Persists_Immutable_Evidence_Record_And_Verification`

The clean test performs 50 file and 50 folder creations: **100/100 VERIFIED,
zero false failures**. The lying-success receipt fixture confirms filesystem
state, not executor claims, determines the result.

`Jarvis.Architecture.Tests.DependencyRulesTests`:

- `Verification_Must_Not_Reference_Tools`
- `Transitive_Closure_Of_Verification_Must_Not_Reach_Tools`
- `Print_Project_Reference_Graph`

The architecture checks also assert that Safety and Core are not in the
Verification transitive closure and that the only direct Jarvis references are
Contracts and Infrastructure.

## Transitive assembly-reference closure

The runtime architecture test printed a closure of 111 assemblies for
`Jarvis.Verification`:

```text
Jarvis.Contracts
Jarvis.Infrastructure
Jarvis.Verification
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

`Jarvis.Tools`, `Jarvis.Safety`, and `Jarvis.Core` are absent from that closure.

## Latency

Measured by the clean file/folder test on this Windows host, including
verification work:

| Tool | Runs | p50 | p95 | Target |
|---|---:|---:|---:|---:|
| `fs.create_file` | 50 | 15.08 ms | 41.41 ms | p95 < 500 ms |
| `fs.create_folder` | 50 | 17.77 ms | 35.39 ms | p95 < 500 ms |

Both targets pass.

## Validation results

`scripts\check-all.ps1`: **PASS**

- C# build: 0 warnings, 0 errors.
- C# tests: 254 passed, 0 failed.
  - Contracts: 46
  - Core: 56
  - Safety: 92
  - Tools: 10
  - Verification: 19
  - Architecture: 6
  - E2E: 25
- Python tests: 103 passed, 1 existing Starlette/httpx deprecation warning.
- Python lint/type checks: passed; no issues in 29 source files.
- C# formatting verification: passed.

## Decisions, deviations, and limitations

- P/Invoke is intentionally implemented independently inside Verification.
  This duplicates an OS API boundary by design and avoids any dependency on
  executor code.
- Directory metadata changes for the immediate parent of an authorized new
  entry are allowed only when that exact sibling creation is itself authorized;
  all other changed/deleted/created entries remain unauthorized side effects.
- Snapshot verification covers only declared watch scopes. Files above 1 MiB
  are represented by size and mtime in snapshots and are not snapshot-hashed;
  the target file's independent evidence is SHA-256 hashed through an OS handle.
- Snapshot-cap overflow and evidence collection failures escalate rather than
  pass. Locked target files are escalated. Failed evidence or unauthorized
  effects produce `FAILED`.
- Stages 7 and 8 intentionally remain not implemented in Phase 3a.
- The host continues to use `StubVerifier`; the new engine is not activated in
  the host until later phase work.

## Reproduction commands

Run from the repository root in PowerShell:

```powershell
dotnet test windows-host\tests\Jarvis.Verification.Tests\Jarvis.Verification.Tests.csproj --configuration Release --no-restore
dotnet test windows-host\tests\Jarvis.Verification.Tests\Jarvis.Verification.Tests.csproj --configuration Release --no-build --filter FullyQualifiedName~Clean_File_And_Folder_Creation_Have_No_False_Failures_And_Report_Latency --logger "console;verbosity=detailed"
dotnet test windows-host\tests\Jarvis.Architecture.Tests\Jarvis.Architecture.Tests.csproj --configuration Release --no-build --filter FullyQualifiedName~Transitive_Closure_Of_Verification_Must_Not_Reach_Tools --logger "console;verbosity=detailed"
.\scripts\check-all.ps1
```
