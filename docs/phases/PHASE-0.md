# Phase 0 Report: Foundation

## 1. Summary
Phase 0 establishes the clean, runnable skeleton, repository layout, build scripts, contract JSON schemas, shared test fixtures, Python package (`jarvis_ai`), .NET solution (`Jarvis.sln`), and architectural guardrails (`Jarvis.Architecture.Tests`).

## 2. Files Added or Changed
- `AGENTS.md`: Master coding agent prompt & rules.
- `docs/DESIGN.md`: Design specification.
- `docs/architecture/adr/ADR-0001-decisions.md`: Architectural decisions summary.
- `README.md`: Quickstart & repository documentation.
- `scripts/setup-windows.ps1`: Automated environment setup script.
- `scripts/check-all.ps1`: Master CI validation script.
- `.github/workflows/ci.yml`: GitHub Actions CI pipeline.
- `config/`: `settings.json`, `tools.registry.json`, `safety.policy.json`, `apps.allowlist.json`.
- `contracts/schemas/`: 12 JSON schemas (`PlanRequest`, `PlanResponse`, `Intent`, `PlanStep`, `ToolCall`, `VerificationPlan`, `AuthorizationRecord`, `ExecutionReceipt`, `Evidence`, `VerificationResult`, `ReportRequest`, `ReportResponse`).
- `contracts/examples/`: 36 valid and invalid contract fixtures.
- `ai-server/`: FastAPI skeleton with `/health` endpoint, pytest, ruff, mypy configuration.
- `windows-host/`: .NET 8 solution with 9 source projects and 6 test projects.

## 3. Test Results

### C# xUnit Test Suite (44 Tests Total)
- `Jarvis.Core.Tests`: 1 Passed (Placeholder check)
- `Jarvis.Safety.Tests`: 1 Passed (Placeholder check)
- `Jarvis.Tools.Tests`: 1 Passed (Placeholder check)
- `Jarvis.Verification.Tests`: 37 Passed (Validates all 36 contract fixtures + schema resolution)
- `Jarvis.Architecture.Tests`: 3 Passed
  - `Verification_Must_Not_Reference_Tools` (PASS)
  - `Contracts_Must_Not_Reference_Other_Jarvis_Projects` (PASS)
  - `Tools_Must_Reference_Safety` (PASS)
- `Jarvis.E2E.Tests`: 1 Passed (Placeholder check)

### Python Pytest & Lint Suite (37 Tests Total)
- `tests/unit/test_health.py`: 1 Passed (`GET /health` status ok)
- `tests/contract/test_contracts.py`: 36 Passed (Validates all fixtures against JSON Schemas)
- `ruff check`: Clean (0 errors)
- `mypy src`: Clean (0 errors)

## 4. Decisions & Deviations from DESIGN.md
- **WPF Target Platform:** Configured `Jarvis.UI.csproj` with `<TargetFramework>net8.0-windows</TargetFramework>` to satisfy .NET SDK requirements for WPF UI rendering.
- No other deviations.

## 5. Known Limitations
- No product features built yet (per Phase 0 scope).
- AI Server currently only serves `/health`.

## 6. Exact Commands to Reproduce
```powershell
# 1. Environment Setup
powershell -ExecutionPolicy Bypass -File scripts\setup-windows.ps1 -SkipOllamaPull

# 2. Master Verification Check
powershell -ExecutionPolicy Bypass -File scripts\check-all.ps1
```

## 7. Gate Checklist
- [x] `scripts/check-all.ps1` passes from a clean clone
- [x] All fixtures validate identically in Python and C#
- [x] Architecture tests present and passing
- [x] CI workflow green (`ci.yml`)
- [x] Setup script is idempotent
