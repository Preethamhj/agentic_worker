# Jarvis status

Updated 2026-11-07 from the test-hardening branch (`test-hardening`), based on release-prep HEAD `c79a78678688fdb4afd45d248c8f689c9fa4d4f3`. The test-hardening changes through A6 are pushed; A7 close-out is recorded in `docs/PROGRESS-test-hardening.md`. The frozen `eval-paper-v1` tag remains unchanged; no tags were created, moved, or deleted.

## Latest verified test-hardening results

Release solution build: **passed**, 0 warnings, 0 errors.
Three sequential Release solution runs with `Category!=RealScheduler`: Architecture 11, Contracts 47, Core 61, E2E 138, Safety 118, Tools 11, Verification 85, UI 23; **494 passed, 0 failed, 0 skipped per run**. Per-project totals matched. No Notepad processes existed before or after the runs, and no Jarvis.TestApp process remained.
`scripts/check-all.ps1 -SkipBuild -SkipRealScheduler`: **passed**, ending with `ALL CHECKS PASSED SUCCESSFULLY!`; C# 494 passed, AI-server Python 121 passed (4 dependency deprecation warnings), evaluation Python 12 passed. Ruff, mypy, and .NET formatting passed. The separate `dotnet format --verify-no-changes` check passed. The architecture project contributed 11 passing tests.

The monolithic `scripts/gate.ps1` wrapper was **not run**: it combines a clean rebuild, three solution test runs and check-all in a single command that may exceed the repository's five-minute command limit. Its rebuild, three non-real test runs and check-all validations were run separately. `check-all` was invoked with `-SkipBuild` because the clean Release rebuild had already passed, and `-SkipRealScheduler` to avoid touching real Task Scheduler state. RealScheduler tests remain unrun and opt-in. CI: not checked.

## Test-hardening result

The owner-machine Notepad failure was classified as Windows 11 launcher hand-off preventing reliable PID attribution, not an overly strict package resolver. The default-gate app tests and Eval now use the no-window Jarvis.TestApp helper with PID/start-time-verified teardown; real Notepad remains owner-run via `scripts/live-app-check.ps1`. C# source audits found no default-gate app-launch or executor test using Notepad, calc, or mspaint, and no direct process launch of those executables. Resolver fixtures and UI display-model values still mention Notepad without launching it.

The Eval app substitution and dated protocol amendment are complete. The frozen expected-verdict table remains unchanged; the known app-launch F11 false positive remains documented in `docs/eval/expected-deviations.json`. The live desktop Notepad check and RealScheduler tests were not run.

## Prior release-preparation state

Release-prep H0–H4 remain documented and committed on `release-prep`: README, MIT license and SECURITY policy, hygiene audit, reproduction guide with raw metadata disclosure, and merge/PR/tag preparation in `docs/RELEASE-PREP.md`. The latest raw evaluation metadata records Windows `10.0.26300.0` and .NET `10.0.9`; it does not record Python or the Ollama model loaded for that run. Current config selects `qwen2.5:7b-instruct`, but that is not proof of the model used in the recorded run.

The `eval-paper-v1` tag remains frozen at `7b5b7e88de868e22c1e8f04d4f5de178bb807c66`. No merge, pull request, or completion tag was created. The owner’s Phase 4 interactive checklist remains pending in `docs/phases/PHASE-4-manual-test.md`. Ignore everything under `reports/`.
