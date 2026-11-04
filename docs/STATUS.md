# STATUS (single source of truth). Updated 2026-10-26 by the project owner.

Default branch: master. Working branch: phase-5. Tags: phase-3b-done = 878635d, phase-3c-done = 5b68602. The tag eval-paper-v1 does NOT exist yet.

## Verified test totals (Release default solution run at 6321285, owner's own output)
Contracts 47, Core 61, Safety 118, Tools 11, Architecture 7, E2E 132, Verification 96 passed plus 2 skipped (they need JARVIS_REAL_SCHEDULER=1). RealScheduler category: 13 passed (owner-verified). Python: ai-server 121 passed; eval pytest 9 at last check.

## Done (do NOT reimplement)
- The real VerificationEngine is wired into the CLI host. StubVerifier exists only in tests. A task completes only on VERIFIED. ESCALATE handler exists. The true verification_status is reported to /v1/report.
- Signed reminder scheduler policy (HMAC-covered settings), task name reminder-<id>, EndBoundary, DeleteExpiredTaskAfter, dispatcher self-unregister, verifier checks every signed setting.
- Audit: separate policy-validation and approval events, approver names, SQLite migration of old rows.
- Debug-only executor-lie switch compiled out of Release. Packaged-Notepad resolution hardened (ADR-0005).
- Phase 5 harness: frozen protocol with dated amendments, runner for file, folder, search, app and reminder (fake scheduler; --scheduler real is a guarded subset the USER runs), preflight for pre-existing Notepad processes, baseline predicates aligned with the protocol (2095b3b), deviations classified (440b3e1), analysis in eval/analyze.py, scripts/run-eval.ps1.

## Open
- CI has been red since run #66 (0e4a7d0). The failing step is scripts/check-all.ps1. Root cause NOT confirmed. Latest run status not checked.
- The owner has not yet reviewed the Q2 baseline fixes and deviation classification.
- scripts/run-eval.ps1 has not been run by the owner. The paper run has not been executed. The tag eval-paper-v1 is pending.
- Known finding: verifier D returns VERIFIED on F11 (extra process of a different image). Scope limit, recorded in an amendment. D must NOT be changed before the paper run.
- Not started: Phase 4 UI, Phase 6.

## Limits
Real Task Scheduler runs are opt-in (JARVIS_REAL_SCHEDULER=1). No real-scheduler evaluation has been run. Ignore everything under reports/.