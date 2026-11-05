# Jarvis status

Updated 2026-11-05 from Phase 4 Release and lint outputs. Default branch: `master`; working branch: `phase-4`; close-out baseline: Phase 5 head `b6de340`.
Tags were not changed. The frozen `eval-paper-v1` tag remains at `7b5b7e88de868e22c1e8f04d4f5de178bb807c66`.

## Latest verified Phase 4 checks

Release solution build: 0 warnings, 0 errors.
Release solution tests excluding RealScheduler: Architecture 10, Contracts 47, Core 61, E2E 134, Safety 118, Tools 11, Verification 85, UI 23; **489 passed, 0 failed, 0 skipped**.
AI-server Python: 121 passed, 4 dependency deprecation warnings. Evaluation Python: 12 passed. Ruff and mypy passed for both areas. .NET format verification passed.
CLI compatibility selection (`CliEndToEndTests` and `CliOutputTests`): 9 passed, 0 failed, 0 skipped.
`scripts/check-all.ps1`: not run because it unconditionally runs RealScheduler tests after setting `JARVIS_REAL_SCHEDULER=1`; those tests are excluded from this Phase 4 close-out. Its equivalent non-real build, tests, Python, lint/type checks, and format checks were run separately and passed. RealScheduler on this head: not checked. CI: not checked.

## Done

Phases 3b and 3c remain complete. Phase 5 evaluation and its frozen evaluation tag were not modified.
Phase 4 U0-U4 are implemented and pushed on `phase-4`: ADR-0006, headless view models and decision handlers, shared CLI/WPF host composition, WPF main task/evidence/history/settings views, and UI/Eval architecture rules. Debug-only low-risk auto-approval matches the existing console behavior; Release has no auto-approval.

## Findings and open work

Known Phase 5 finding remains: D returned VERIFIED for all 6 BAD F11 paper cases (extra child with another image); retain as a finding and do not tune without an amendment. Known Q2 deviations remain recorded in `docs/eval/expected-deviations.json`.
Owner interactive validation remains pending; see `docs/phases/PHASE-4-manual-test.md`. RealScheduler and CI were not checked for the Phase 4 head.

Ignore everything under `reports/`.
