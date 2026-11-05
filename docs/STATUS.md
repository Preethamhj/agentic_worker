# Jarvis status

Updated 2026-11-05 from corrected owner-gate output. Default branch: `master`; working branch: `phase-5`.
Tags: `phase-3b-done` = `878635d`; `phase-3c-done` = `5b68602`; local `eval-paper-v1` resolves to `7b5b7e88de868e22c1e8f04d4f5de178bb807c66`; remote tag ref exists. No paper run was executed.

## Latest verified Release totals

Non-RealScheduler solution tests: Architecture 7, Contracts 47, Core 61, E2E 134, Safety 118, Tools 11, Verification 85; 463 passed, 0 failed, 0 skipped.
RealScheduler category: Verification 13 passed, 0 failed, 0 skipped (run explicitly by `check-all.ps1`).
Combined C# total: 476 passed, 0 failed, 0 skipped. Release solution build: 0 warnings, 0 errors.
Before this turn (the prior STATUS snapshot at 6321285): Architecture 7, Contracts 47, Core 61, E2E 132, Safety 118, Tools 11, Verification 96 passed and 2 skipped.
Python: ai-server 121 passed (4 dependency deprecation warnings); eval 12 passed. Ruff and mypy passed for both; .NET format verification passed.
`scripts/check-all.ps1`: corrected `scripts/gate.ps1` captured exit 0 twice; all checks passed. Three consecutive non-real Release solution runs had identical per-project totals (463 passed each); explicit RealScheduler had 13 passed. `CI: not checked` (no actionable log or run URL supplied).

## Done

Phase 3b and 3c completion tags exist. The real VerificationEngine is wired into the CLI; completion requires VERIFIED. Signed reminder policy, task identity/expiry, dispatcher cleanup, audit attribution/migration, and Debug-only executor-lie coverage are implemented.
Phase 5 includes the seeded catalogue, fake-scheduler runner, owner scripts, multi-input analysis, Q2 deviation gate, and development smoke results. R0-R5 updates are committed on `phase-5`.
The required production diff from `5b68602` is only `[assembly: InternalsVisibleTo("Jarvis.Eval")]` in `Jarvis.Verification/AssemblyInfo.cs`.

## Findings and open work

Known finding: D returns VERIFIED for BAD F11 (extra child with another image); this is an established scope limitation and must not be tuned before the paper run.
Known Q2 deviations are enumerated in `docs/eval/expected-deviations.json`; the dev-run-4 check reported no unlisted or missing deviations. B/F6 and C/F16 deviations remain documented.
Open: CI has not been checked; the five-repetition paper run is pending. The premature `eval-paper-v1` tag currently points to the phase-5 close-out commit; do not move or recreate it without owner direction. Phase 4 UI and Phase 6 are not started.
Real-scheduler tests passed through the explicitly gated check-all category; no real-scheduler evaluation was run. Cleanup query found no Jarvis tasks or app processes; the eval-sandbox and reminders directories existed but were empty.

Ignore everything under `reports/`.
