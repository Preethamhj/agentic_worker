# Phase 2a Report: Host Core (Contracts, State Machine, Safety, Authorization)

## 1. Summary
Phase 2a implements the foundational core of the C# Windows host (`Jarvis`), adhering strictly to TDD, zero-trust towards external AI server inputs, and independent host-side re-validation:
- **Solution Standardization:** Standardized strictly on a single solution format `Jarvis.sln` in `windows-host/`. Removed `Jarvis.slnx` to eliminate MSBuild ambiguity (`MSB1011`). Aligned `global.json`, `scripts/check-all.ps1`, `.github/workflows/ci.yml`, and `ADR-0004`.
- **Strict Contracts (`Jarvis.Contracts`):** Added strict C# DTOs rejecting unmapped properties via `JsonUnmappedMemberHandling.Disallow` across all schema models, with deadline fields `execute_before` and `verify_before`.
- **State Machine (`Jarvis.Core`):** Implemented `IClock`, `SystemClock`, `TaskState`, `IllegalStateTransitionException`, and `StateMachine` enforcing the legal transition table from DESIGN.md section 8. Terminal states (`Completed`, `Failed`, `Cancelled`, `Timeout`) have zero exits; `Completed` is reachable only from `Verifying` with status `VERIFIED`; every illegal transition throws.
- **Path Security (`Jarvis.Safety.PathPolicy`):** Enforces Unicode NFC normalization, environment variable expansion, known-folder allowed roots, and rejection of traversal (`..`), UNC paths, device namespaces (`\\.\`, `\\?\`, `\??\`), reserved DOS device names (`CON`, `PRN`, `AUX`, `NUL`, `COM1-9`, `LPT1-9`), alternate data streams (`:stream`, `::$DATA`), trailing dots/spaces, paths exceeding 260 characters, mixed separators, and reparse points (junctions/symlinks). Tested against 29 blocked paths, 10 accepted paths, root-prefix confusion, real NTFS junctions via `mklink /J`, pure forward-slash input, and case variations.
- **Tool Policy (`Jarvis.Safety.ToolPolicy`):** Independently loads and validates `config/tools.registry.json` and `config/apps.allowlist.json`, evaluates argument JSON schemas via `JsonSchema.Net`, enforces app allowlists, and computes authoritative policy risk.
- **Authorization Engine (`Jarvis.Safety.AuthorizationService`):** Deterministic canonical arguments serialization, HMAC-SHA256 signing with Windows DPAPI key (`CurrentUser` scope) generated on first run, deadline enforcement (`execute_before`, `verify_before`), and thread-safe atomic single-use lifecycle tracking (`Issued` -> `Consumed` -> `Verified`). Enforced invariant: unconsumed records cannot verify.
- **Type-Safe Authorization & Immutability (`VerifiedAuthorization`):** Made `VerifiedAuthorization` a `sealed class` with get-only properties and NO copy path or `with` expression support (no compiler-generated `<Clone>$` method). Internal constructor guarantees only `AuthorizationService` can instantiate it. `AuthorizationService.Consume(auth)` re-verifies HMAC over the actual field values, refusing forged copies.
- **Safety Gate (`Jarvis.Safety.SafetyGate`):** Computes authoritative `finalRisk = max(advisoryRisk, policyRisk)`, auto-approves low risk, requests human approval via `IApprovalProvider` for medium/high risk, and delegates signing to `AuthorizationService`.
- **Audit Logging (`Jarvis.Infrastructure.IAuditStore`):** Defined audit storage abstractions with `InMemoryAuditStore` test double, recording state transitions, safety decisions, and authorization lifecycle transitions (`Issued`, `Consumed`, `Verified`).
- **Tool Executor Interface (`Jarvis.Tools.IToolExecutor`):** Replaced placeholder in `Jarvis.Tools` with `IToolExecutor` contract enforcing `VerifiedAuthorization` parameter signature.

## 2. Files Added or Changed
- `docs/architecture/adr/ADR-0004-solution-format.md`: Single standard solution file format decision (`Jarvis.sln`).
- `contracts/schemas/AuthorizationRecord.schema.json`: Added `execute_before` and `verify_before` deadline fields.
- `contracts/examples/AuthorizationRecord.valid1.json`: Updated example fixture with deadlines.
- `scripts/check-all.ps1`: Configured `Jarvis.sln` target across build, test, and format steps.
- `windows-host/Jarvis.sln`: Canonical Visual Studio solution referencing all 16 projects. Removed `Jarvis.slnx`.
- `windows-host/src/Jarvis.Contracts/`:
  - `ContractModels.cs`: Strict record DTOs with `[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]`.
  - Removed `ContractPlaceholder.cs`.
- `windows-host/src/Jarvis.Core/`:
  - `IClock.cs`: Interface and `SystemClock` implementation.
  - `TaskState.cs`: Enum of 14 states and wire string mappings.
  - `IllegalStateTransitionException.cs`: Typed exception for invalid transitions.
  - `StateMachine.cs`: Thread-safe state machine enforcing legal transition table and audit events.
  - Removed `CorePlaceholder.cs`.
- `windows-host/src/Jarvis.Infrastructure/`:
  - `IAuditStore.cs`: Audit store interface for state transitions, safety decisions, and authorization lifecycle.
  - `InMemoryAuditStore.cs`: In-memory test double for audit logging.
  - `IDpapiKeyStore.cs` & `DpapiKeyStore.cs`: DPAPI-backed key store with `InMemoryDpapiKeyStore`.
  - Removed `InfrastructurePlaceholder.cs`.
- `windows-host/src/Jarvis.Safety/`:
  - `PathPolicy.cs`: Path validation and reparse point checks.
  - `ToolPolicy.cs`: Registry & allowlist loader with schema evaluation.
  - `VerifiedAuthorization.cs`: Sealed class with get-only properties, no copy path, and internal constructor.
  - `AuthorizationSecurityException.cs`: Typed exception for authorization failures.
  - `AuthorizationService.cs`: HMAC signing, DPAPI key integration, atomic `Consume`, lifecycle persistence, and `VerifiedAuthorization` re-verification.
  - `IApprovalProvider.cs`: Interface for human approval.
  - `SafetyGate.cs`: Authoritative gate evaluating max risk and issuing authorizations.
  - Removed `SafetyPlaceholder.cs`.
- `windows-host/src/Jarvis.Tools/`:
  - `IToolExecutor.cs`: Tool executor interface accepting `VerifiedAuthorization`.
  - Removed `ToolsPlaceholder.cs`.
- `windows-host/tests/Jarvis.Contracts.Tests/`:
  - `ContractDtoTests.cs`: DTO deserialization and unknown field rejection tests.
- `windows-host/tests/Jarvis.Core.Tests/`:
  - `StateMachineTests.cs`: Full happy path, illegal transitions, terminal states, and VERIFIED requirement tests.
  - `CoreTests.cs`: Clock and wire string tests.
- `windows-host/tests/Jarvis.Safety.Tests/`:
  - `PathPolicyTests.cs`: 29 blocked path inputs, 10 accepted path inputs, root-prefix confusion tests, real `mklink /J` junction test, pure forward-slash input, and case differences tests.
  - `ToolPolicyTests.cs`: Tool registry, args schema, app allowlist, and policy risk tests.
  - `AuthorizationTests.cs`: Flipped byte, expired deadlines, reused nonce, wrong step, unconsumed record, tampered args, key regeneration, atomic 50 concurrent calls test, and forged copy rejection tests.
  - `SafetyGateTests.cs`: Risk escalation, user approval flow, and audit logging tests.
  - `SafetyTests.cs`: DPAPI key persistence and encryption tests.
- `windows-host/tests/Jarvis.Tools.Tests/`:
  - `ToolsTests.cs`: VerifiedAuthorization parameter requirement, contract tests, and test proving VerifiedAuthorization has no copy path or `with` expression support.
- `windows-host/tests/Jarvis.Architecture.Tests/`:
  - `DependencyRulesTests.cs`: NetArchTest dependency checks and VerifiedAuthorization internal constructor reflection assertion.

## 3. Test Results & Metrics

### C# xUnit Test Suite (192 Tests Total, 0 Failed, 0 Skipped)
- **`Jarvis.Contracts.Tests`:** 46 Passed, 0 Skipped
  - 36 contract schema evaluation tests across JSON fixtures.
  - 10 strict DTO deserialization and unknown property rejection tests.
- **`Jarvis.Core.Tests`:** 47 Passed, 0 Skipped
  - Full happy path progression (`Created` -> `Completed`).
  - `Completed_Throws_If_Verification_Not_VERIFIED` (6 theory cases: FAILED, ESCALATE, PARTIAL, UNKNOWN, empty, null).
  - `Completed_Cannot_Be_Reached_From_States_Other_Than_Verifying` (9 theory cases).
  - `NonTerminal_States_Can_Exit_To_Failed_Cancelled_Timeout` (10 theory cases).
  - `Terminal_States_Have_Zero_Exits` (4 terminal states x 14 target states = 56 transition rejections).
  - `Every_Illegal_Transition_Throws`: exhaustive permutation test across all non-permitted state pairs.
  - `SystemClock_Returns_Recent_Utc_Time` and wire string mapping tests.
- **`Jarvis.Safety.Tests`:** 90 Passed, 0 Skipped
  - `PathPolicyTests`:
    - 29 blocked path inputs (traversal, UNC, device namespaces, DOS reserved names, alternate data streams, trailing dots/spaces, over-long paths, mixed separators, null byte injections).
    - 10 accepted path inputs (Unicode Latin, German, Japanese, Kannada names, nested folders, spaces, hyphens).
    - `Root_Prefix_Confusion_Is_Blocked` (3 theory cases: `Evil`, `_malicious`, `-bad`).
    - `Real_Junction_Via_Mklink_Is_Blocked` (verifies actual NTFS junction created via `cmd.exe /c mklink /J` is detected as a reparse point and blocked).
    - `Forward_Slash_Input_Is_Accepted_And_Normalized` (pure forward-slash input resolves to normalized Windows path inside allowed root).
    - `Case_Differences_Are_Accepted_And_Match_Root` (case-insensitive matching within allowed root).
  - `ToolPolicyTests`:
    - Enabled tool validation, disabled tool rejection (`shell.run_powershell`), unknown tool rejection, schema validation (missing required arg, wrong arg type, extra disallowed arg), app allowlist (`notepad`, `calc`, `mspaint` accepted; `cmd`, `powershell`, `malware` rejected), policy risk retrieval.
  - `AuthorizationTests`:
    - Full happy path (`Issue` -> `Consume` -> `Verify`).
    - Flip one byte in HMAC signature fails.
    - Expired execute deadline fails consume.
    - Expired verify deadline fails verify.
    - Reused nonce / replay attack fails second consume.
    - Wrong step ID fails consume.
    - Never-consumed record fails verification.
    - Tampered canonical args fails.
    - DPAPI key regeneration invalidates existing authorizations.
    - `Atomic_Consume_With_50_Concurrent_Calls_Gives_Exactly_One_Success`: 50 concurrent tasks invoke `Consume`; exactly 1 succeeds and 49 fail with `AuthorizationSecurityException`, and `IAuditStore` records `Issued` and `Consumed` lifecycle events.
    - `Forged_Copy_Of_VerifiedAuthorization_Is_Refused`: Executor calls `_authService.Consume(forgedAuth)`; HMAC re-verification over actual fields catches tampering and throws `AuthorizationSecurityException`.
  - `SafetyGateTests`:
    - `MaxRisk` authoritative calculation (8 theory cases).
    - Low-risk tool auto-approved without provider prompt.
    - Medium-risk tool approved by provider returns signed authorization.
    - Medium-risk tool denied by provider rejects execution.
    - Invalid tool fails immediately without approval request.
  - `SafetyTests`:
    - In-memory key store generation and regeneration.
    - DPAPI persistent encryption and decryption under Windows DPAPI `CurrentUser` scope.
- **`Jarvis.Tools.Tests`:** 3 Passed, 0 Skipped
  - `IToolExecutor_ExecuteAsync_Requires_VerifiedAuthorization_Parameter`.
  - `FakeToolExecutor_Implements_IToolExecutor_Contract`.
  - `VerifiedAuthorization_Has_No_Copy_Path_And_No_With_Expression_Support`: proves `VerifiedAuthorization` is a sealed class, not a record, has no `<Clone>$` method, and all properties are get-only without setters/init accessors.
- **`Jarvis.Architecture.Tests`:** 4 Passed, 0 Skipped
  - `Verification_Must_Not_Reference_Tools`.
  - `Contracts_Must_Not_Reference_Other_Jarvis_Projects`.
  - `Tools_Must_Reference_Safety`.
  - `VerifiedAuthorization_Cannot_Be_Constructed_Outside_Jarvis_Safety`.
- **`Jarvis.Verification.Tests`:** 1 Passed, 0 Skipped (Placeholder test; full implementation scheduled for Phase 3).
- **`Jarvis.E2E.Tests`:** 1 Passed, 0 Skipped (Placeholder test; scheduled for Phase 4).

**Skipped Tests Count:** **0 skipped tests** across the entire test suite.

### Python Pytest Suite (98 Tests Total, 0 Failed, 0 Skipped)
- All 98 AI server tests pass cleanly.
- `ruff check`: Clean (0 errors across 29 source files).
- `mypy src`: Clean (0 errors across 29 source files).

### C# Formatting Verification
- Exact command: `dotnet format Jarvis.sln --verify-no-changes` (executed inside `windows-host/`).
- Verified to return exit code 2 (failure) on formatting differences, and exit code 0 when clean.
- `git ls-files "*bin/*" "*obj/*"` returned empty (0 tracked build artifacts).

## 4. SDK and Solution Environment Verification
- `dotnet --list-sdks`:
  ```text
  10.0.201 [C:\Program Files\dotnet\sdk]
  10.0.300 [C:\Program Files\dotnet\sdk]
  ```
- CI Setup (.NET 8):
  ```yaml
  - name: Setup .NET 8
    uses: actions/setup-dotnet@v4
    with:
      dotnet-version: '8.0.x'
  ```
- `global.json`:
  ```json
  {
    "sdk": {
      "version": "8.0.100",
      "rollForward": "latestMajor"
    }
  }
  ```
- Single solution `windows-host/Jarvis.sln` selected and verified across CI and local environments.

## 5. Gate Checklist
- [x] SDK decision recorded and CI config consistent (`ADR-0004`, `check-all.ps1`, `ci.yml`, `global.json`).
- [x] State machine and authorization tests pass (47 state machine tests, 90 safety & authorization tests).
- [x] 25 blocked and 10 accepted paths (29 blocked paths, 10 accepted paths, root-prefix confusion, mklink real junction, forward-slash input, and case differences tested in `PathPolicyTests`).
- [x] `VerifiedAuthorization` cannot be constructed outside `Jarvis.Safety` (Verified by `DependencyRulesTests.VerifiedAuthorization_Cannot_Be_Constructed_Outside_Jarvis_Safety`, sealed class with no copy path in `ToolsTests`).
- [x] `check-all.ps1` passes cleanly (C# Release build, 192 C# tests, 98 Python tests, ruff clean, mypy 29 files clean, dotnet format clean).
- [x] `docs/phases/PHASE-2a.md` written and updated.
- [x] New git tag `phase-2a-fixed` created.

## 6. Exact Commands to Reproduce
```powershell
# 1. Run all tests and checks across C# and Python
.\scripts\check-all.ps1

# 2. Run C# unit tests specifically
dotnet test windows-host\Jarvis.sln --configuration Release

# 3. Verify C# formatting
dotnet format windows-host\Jarvis.sln --verify-no-changes
```
