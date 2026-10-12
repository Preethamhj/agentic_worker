# Phase 2a Report: Host Core (Contracts, State Machine, Safety, Authorization)

## 1. Summary
Phase 2a implements the foundational core of the C# Windows host (`Jarvis`), adhering strictly to TDD, zero-trust towards external AI server inputs, and independent host-side re-validation:
- **Prerequisite Resolution:** Formulated and committed `docs/architecture/adr/ADR-0004-solution-format.md`, providing dual `.sln` and `.slnx` compatibility and configuring `scripts/check-all.ps1` to target `Jarvis.sln` explicitly.
- **Strict Contracts (`Jarvis.Contracts`):** Added strict C# DTOs rejecting unmapped properties via `JsonUnmappedMemberHandling.Disallow` across all schema models, with deadline fields `execute_before` and `verify_before`.
- **State Machine (`Jarvis.Core`):** Implemented `IClock`, `SystemClock`, `TaskState`, `IllegalStateTransitionException`, and `StateMachine` enforcing the legal transition table from DESIGN.md section 8. Terminal states (`Completed`, `Failed`, `Cancelled`, `Timeout`) have zero exits; `Completed` is reachable only from `Verifying` with status `VERIFIED`; every illegal transition throws.
- **Path Security (`Jarvis.Safety.PathPolicy`):** Enforced Unicode NFC normalization, environment variable expansion, known-folder allowed roots, and rejection of traversal (`..`), UNC paths, device namespaces (`\\.\`, `\\?\`, `\??\`), reserved DOS device names (`CON`, `PRN`, `AUX`, `NUL`, `COM1-9`, `LPT1-9`), alternate data streams (`:stream`, `::$DATA`), trailing dots/spaces, paths exceeding 260 characters, mixed separators, and reparse points (junctions/symlinks). Tested against 29 blocked paths and 10 accepted paths.
- **Tool Policy (`Jarvis.Safety.ToolPolicy`):** Independently loads and parses `config/tools.registry.json` and `config/apps.allowlist.json`, evaluates argument JSON schemas via `JsonSchema.Net`, enforces app allowlists, and computes authoritative policy risk.
- **Authorization Engine (`Jarvis.Safety.AuthorizationService`):** Deterministic canonical arguments serialization, HMAC-SHA256 signing with Windows DPAPI key (`CurrentUser` scope) generated on first run, deadline enforcement (`execute_before`, `verify_before`), and single-use lifecycle tracking (`Issued` -> `Consumed` -> `Verified`). Enforced invariant: unconsumed records cannot verify.
- **Type-Safe Authorization (`VerifiedAuthorization`):** Record wrapper with strictly internal constructor accessible only to `AuthorizationService`. Architecture test verifies `VerifiedAuthorization` cannot be constructed outside `Jarvis.Safety`.
- **Safety Gate (`Jarvis.Safety.SafetyGate`):** Computes authoritative `finalRisk = max(advisoryRisk, policyRisk)`, enforces auto-approval for low risk, requests human approval via `IApprovalProvider` for medium/high risk, and delegates signing to `AuthorizationService`.
- **Audit Logging (`Jarvis.Infrastructure.IAuditStore`):** Defined audit storage abstractions with `InMemoryAuditStore` test double, recording every state machine transition and safety gate decision.
- **Clean Tool Interface (`Jarvis.Tools.IToolExecutor`):** Replaced placeholder in `Jarvis.Tools` with `IToolExecutor` contract enforcing `VerifiedAuthorization` parameter signature.

## 2. Files Added or Changed
- `docs/architecture/adr/ADR-0004-solution-format.md`: Solution compatibility decision (.sln & .slnx).
- `contracts/schemas/AuthorizationRecord.schema.json`: Added `execute_before` and `verify_before` deadline fields.
- `contracts/examples/AuthorizationRecord.valid1.json`: Updated example fixture with deadlines.
- `scripts/check-all.ps1`: Configured `Jarvis.sln` target across build, test, and format steps.
- `windows-host/Jarvis.sln`: Generated Visual Studio standard solution referencing all 16 projects.
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
  - `IAuditStore.cs`: Audit store interface and record models.
  - `InMemoryAuditStore.cs`: In-memory test double for audit logging.
  - `IDpapiKeyStore.cs` & `DpapiKeyStore.cs`: DPAPI-backed key store with `InMemoryDpapiKeyStore`.
  - Removed `InfrastructurePlaceholder.cs`.
- `windows-host/src/Jarvis.Safety/`:
  - `PathPolicy.cs`: Complete path validation and reparse point checks.
  - `ToolPolicy.cs`: Registry & allowlist loader with schema evaluation.
  - `VerifiedAuthorization.cs`: Type-safe token with internal constructor.
  - `AuthorizationSecurityException.cs`: Typed exception for authorization failures.
  - `AuthorizationService.cs`: HMAC signing, DPAPI key integration, and lifecycle tracking.
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
  - `PathPolicyTests.cs`: 29 blocked path inputs and 10 accepted path inputs.
  - `ToolPolicyTests.cs`: Tool registry, args schema, app allowlist, and policy risk tests.
  - `AuthorizationTests.cs`: Flipped byte, expired deadlines, reused nonce, wrong step, unconsumed record, tampered args, and key regeneration tests.
  - `SafetyGateTests.cs`: Risk escalation, user approval flow, and audit logging tests.
  - `SafetyTests.cs`: DPAPI key persistence and encryption tests.
- `windows-host/tests/Jarvis.Tools.Tests/`:
  - `ToolsTests.cs`: VerifiedAuthorization parameter requirement and contract tests.
- `windows-host/tests/Jarvis.Architecture.Tests/`:
  - `DependencyRulesTests.cs`: NetArchTest dependency checks and VerifiedAuthorization internal constructor reflection assertion.

## 3. Test Results

### C# xUnit Test Suite (183 Tests Total, 0 Failed, 0 Skipped)
- `Jarvis.Contracts.Tests`: 46 Passed
  - 36 contract schema evaluation tests across JSON fixtures.
  - 10 strict DTO deserialization and unknown property rejection tests.
- `Jarvis.Core.Tests`: 47 Passed
  - Initial state creation (`Created`).
  - Full happy path progression (`Created` -> `Completed`).
  - `Completed_Throws_If_Verification_Not_VERIFIED` (6 theory cases: FAILED, ESCALATE, PARTIAL, UNKNOWN, empty, null).
  - `Completed_Cannot_Be_Reached_From_States_Other_Than_Verifying` (9 theory cases).
  - `NonTerminal_States_Can_Exit_To_Failed_Cancelled_Timeout` (10 theory cases).
  - `Terminal_States_Have_Zero_Exits` (4 terminal states x 14 target states = 56 transition rejections).
  - `Every_Illegal_Transition_Throws`: exhaustive permutation test across all non-permitted state pairs.
  - `SystemClock_Returns_Recent_Utc_Time` and wire string mapping tests.
- `Jarvis.Safety.Tests`: 82 Passed
  - `PathPolicyTests`:
    - 29 blocked path inputs (directory traversal, UNC paths, device namespaces, DOS reserved names, alternate data streams, trailing dots/spaces, over-long paths, mixed separators, out-of-root paths, null byte injections).
    - 10 accepted path inputs (Unicode Latin, German, Japanese, Kannada names, nested folders, spaces, hyphens).
    - Reparse point junction / symlink validation.
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
  - `SafetyGateTests`:
    - `MaxRisk` authoritative calculation (8 theory cases).
    - Low-risk tool auto-approved without provider prompt.
    - Medium-risk tool approved by provider returns signed authorization.
    - Medium-risk tool denied by provider rejects execution.
    - Invalid tool fails immediately without approval request.
  - `SafetyTests`:
    - In-memory key store generation and regeneration.
    - DPAPI persistent encryption and decryption under Windows DPAPI `CurrentUser` scope.
- `Jarvis.Tools.Tests`: 2 Passed
  - `IToolExecutor_ExecuteAsync_Requires_VerifiedAuthorization_Parameter`.
  - `FakeToolExecutor_Implements_IToolExecutor_Contract`.
- `Jarvis.Architecture.Tests`: 4 Passed
  - `Verification_Must_Not_Reference_Tools`.
  - `Contracts_Must_Not_Reference_Other_Jarvis_Projects`.
  - `Tools_Must_Reference_Safety`.
  - `VerifiedAuthorization_Cannot_Be_Constructed_Outside_Jarvis_Safety`.
- `Jarvis.Verification.Tests`: 1 Passed (Placeholder test; full implementation scheduled for Phase 3).
- `Jarvis.E2E.Tests`: 1 Passed (Placeholder test; scheduled for Phase 4).

### Python Pytest Suite (98 Tests Total, 0 Failed)
- All 98 AI server tests pass cleanly.
- `ruff check`: Clean (0 errors across 29 source files).
- `mypy src`: Clean (0 errors across 29 source files).

### C# Formatting Verification
- `dotnet format Jarvis.sln --verify-no-changes`: Clean (0 formatting discrepancies).

## 4. Decisions & Deviations from DESIGN.md
1. **Solution Format (.sln and .slnx):** Documented in `ADR-0004`. Maintained dual solution format to support both modern Visual Studio 2022 v17.10+ XML solutions and CLI `dotnet` tooling.
2. **Authorization Deadlines:** Added `execute_before` and `verify_before` ISO-8601 timestamps to `AuthorizationRecord.schema.json` and contracts to provide bounded windows for tool execution and independent verification.
3. **Type-Safe Authorization Pattern:** Implemented `VerifiedAuthorization` with an internal constructor. This guarantees at compile-time and runtime that only `Jarvis.Safety.AuthorizationService` can instantiate execution tokens.
4. **Platform Targeting for DPAPI:** Added `[SupportedOSPlatform("windows")]` to `DpapiKeyStore` to resolve analyzer rule CA1416 cleanly under strict nullable and warnings-as-errors compiler settings.

## 5. Remaining Placeholders
- `src/Jarvis.Verification/VerificationPlaceholder.cs` and `tests/Jarvis.Verification.Tests/VerificationTests.cs` (Verification engine scheduled for Phase 3).
- `tests/Jarvis.E2E.Tests/E2ETests.cs` (End-to-end integration scheduled for Phase 4).
- All placeholders in `Core`, `Safety`, `Tools`, `Contracts`, and `Infrastructure` have been completely removed and replaced with real production implementations and tests.

## 6. Gate Checklist
- [x] SDK decision recorded and CI config consistent (`ADR-0004`, `check-all.ps1`).
- [x] State machine and authorization tests pass (47 state machine tests, 82 safety & authorization tests).
- [x] 25 blocked and 10 accepted paths (29 blocked paths, 10 accepted paths tested in `PathPolicyTests`).
- [x] `VerifiedAuthorization` cannot be constructed outside `Jarvis.Safety` (Verified by `DependencyRulesTests.VerifiedAuthorization_Cannot_Be_Constructed_Outside_Jarvis_Safety`).
- [x] `check-all.ps1` passes cleanly (C# Release build, 183 C# tests, 98 Python tests, ruff, mypy, dotnet format).
- [x] `docs/phases/PHASE-2a.md` written.
- [x] New git tag `phase-2a-done` created.

## 7. Exact Commands to Reproduce
```powershell
# 1. Run all tests and checks across C# and Python
.\scripts\check-all.ps1

# 2. Run C# unit tests specifically
dotnet test windows-host\Jarvis.sln --configuration Release

# 3. Verify C# formatting
dotnet format windows-host\Jarvis.sln --verify-no-changes
```
