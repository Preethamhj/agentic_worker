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
| `fs.create_file` | 50 | 31.95 ms | 61.11 ms | p95 < 500 ms |
| `fs.create_folder` | 50 | 30.54 ms | 52.99 ms | p95 < 500 ms |

Both targets pass.

## Validation results

`scripts\check-all.ps1`: **PASS**

- C# build: 0 warnings, 0 errors.
- C# tests: 261 passed, 0 failed.
  - Contracts: 46
  - Core: 56
  - Safety: 92
  - Tools: 10
  - Verification: 26
  - Architecture: 6
  - E2E: 25
- Python tests: 103 passed, 1 existing Starlette/httpx deprecation warning.
- Python lint/type checks: passed; no issues in 29 source files.
- C# formatting verification: passed.

## Phase 3a close-out round trips and scope measurement

The Verification.Tests project references `Jarvis.Tools` for integration tests;
the `Jarvis.Verification` production project still references only Contracts
and Infrastructure. The added tests use a real `AuthorizationService`, the
real `CreateFileExecutor` or `CreateFolderExecutor`, host timestamps from
`SystemClock`, and the real `VerificationEngine`.

`Jarvis.Verification.Tests.RealExecutorRoundTripTests`:

- `Real_CreateFile_Executor_RoundTrips_And_Verifies_Exact_Utf8_Bytes` (five
  data cases: Kannada text, CRLF, trailing CRLF, empty content, and 900 KiB).
- `Real_CreateFolder_Executor_RoundTrips_And_Verifies`
- `Default_WatchScope_Snapshot_Of_5000_Files_Reports_Time_And_Cap`

All seven executions passed with `VERIFIED`. The first run exposed that
`Encoding.UTF8` caused a UTF-8 BOM to be written; the file executor now writes
UTF-8 without a BOM, and authorization derives the expected size and SHA-256
from exactly `UTF8Encoding(false).GetBytes(content)`. Round-trip tests assert
byte-for-byte file contents and the signed expected size/hash.

For the real file and folder round trips, default `watch_scopes` is the
immediate containing directory of the authorized target. Each run printed its
temporary root; the test asserts exactly one scope and that it equals this
parent directory. This scope is recursive for snapshot collection.

On this Windows host, the 5,000-file scope produced 5,000 snapshot entries in
**2,217.03 ms**; the default 10,000-entry cap **did not trigger**. The chosen
large-folder behavior is conservative: keep the fixed cap, never truncate and
pass; exceeding 10,000 entries escalates verification. A user working in a
large folder must choose a narrower authorized folder/scope for a verified
result. A 5,000-entry scope remains under the cap but incurs substantial
latency (above the per-file-step 500 ms target); this measured performance
limitation is recorded rather than hidden or bypassed.

The focused Verification.Tests run passed **26/26**. The measured clean
verification p95 after adding real round trips was 61.11 ms for file creation
and 52.99 ms for folder creation (50 runs each).

### Manual check output

```text
Real file round trips: 5/5 VERIFIED (Kannada, CRLF, trailing CRLF, empty, 900 KiB)
Real folder round trip: VERIFIED
5,000-entry default watch scope: entries=5000; elapsed=2217.03ms; cap=10000; cap_triggered=False
scripts\check-all.ps1: 261 C# passed; 103 Python passed; formatting passed; ALL CHECKS PASSED
```

## CI close-out

`.github/workflows/ci.yml` now runs on pushes to every branch and pull requests
targeting every branch. Push commit and GitHub Actions run ID/result are
recorded here after the remote workflow completes.

Close-out commands:

```powershell
dotnet test windows-host\tests\Jarvis.Verification.Tests\Jarvis.Verification.Tests.csproj --configuration Release --no-build --logger "console;verbosity=detailed"
.\scripts\check-all.ps1
```

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
