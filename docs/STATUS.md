# Jarvis status

Updated 2026-11-07. Default branch: `master`; active branch: `live-ops`, based on `release-prep` head `c79a78678688fdb4afd45d248c8f689c9fa4d4f3`, the latest successful CI head checked for this work (run 126). The newer `test-hardening` head `bb741ae9add66be30637def14197e509c096eb9f` has failed CI run 134; its failing CLI app-launch test is documented in `docs/PROGRESS-live-ops.md`.
The `eval-paper-v1` tag remains frozen at `7b5b7e88de868e22c1e8f04d4f5de178bb807c66`. No tags were created, moved, or deleted.

## Latest verified gate

`scripts/check-all.ps1`: **passed**, ending with `ALL CHECKS PASSED SUCCESSFULLY!`; .NET SDK reported by the gate: `10.0.300`.
Release build: 0 warnings, 0 errors.
Non-RealScheduler Release solution tests: Architecture 10, Contracts 47, Core 61, E2E 134, Safety 118, Tools 11, Verification 85, UI 23; **489 passed, 0 failed, 0 skipped**.
RealScheduler category: Verification **13 passed, 0 failed, 0 skipped**. Combined C# total: **502 passed, 0 failed, 0 skipped**.
AI-server Python: **121 passed**, 4 dependency deprecation warnings. Evaluation Python: **12 passed**. Ruff and mypy passed for both areas; .NET format verification passed.
Full gate log: `%TEMP%\jarvis-release-prep-check-all.log`. CI run 126 passed for commit `c79a78678688fdb4afd45d248c8f689c9fa4d4f3`: https://github.com/Preethamhj/agentic_worker/actions/runs/42085040711.

## Release preparation completed

H0–H4 are committed and pushed on `release-prep`: updated README, MIT license and SECURITY policy, tracked-file and ignore-rule hygiene changes, reproducibility guide with raw metadata disclosure, and merge/PR/tag preparation in `docs/RELEASE-PREP.md`.
H2 found no credential-like literals, private key markers, GitHub token patterns, or AWS access-key patterns. Machine-specific `ASUS` path/user references were removed from tracked examples, fixture generation, and documentation; six remaining `C:\Users\test\...` matches are generic test inputs. `.gitignore` covers reports, logs, eval-sandbox, venvs, egg-info, bin, and obj.
The latest raw evaluation metadata records Windows `10.0.26300.0` and `dotnet_version` `10.0.9`; it does not record Python or Ollama model. Current config selects `qwen2.5:7b-instruct`, but that is not proof of the model loaded for the recorded run.

## Findings and open work

Known Phase 5 finding remains: verifier D returned VERIFIED for all 6 BAD F11 paper cases (extra child with another image); do not tune without an amendment. Known Q2 deviations remain recorded in `docs/eval/expected-deviations.json`.
The owner’s Phase 4 interactive checklist remains pending in `docs/phases/PHASE-4-manual-test.md`.
`master` at `b83d305` is not an ancestor of `release-prep`; merge requires a merge commit and is not fast-forwardable. No merge, pull request, or completion tag was created. The owner-managed requested tags are `phase-4-done` and `phase-5-done`; see `docs/RELEASE-PREP.md` for proposed targets and merge commands.

Ignore everything under `reports/`.

## Live Ops

Part B0 implementation is committed and pushed as `7c15fa2`, with fixes `4594046` and `94fc509`. The owner Release solution build passed with 0 warnings and 0 errors. The two new CLI/UI planner tests pass individually (1/1 each). On 2026-11-07, `scripts/live-check.ps1` reported all checks passed, including configured model, Python health, doctor, workspace, DPAPI, scheduler task creation/deletion, and notifier source detection. The script may reuse an already-running server, so its output does not establish that it started a new server; notifier source detection is not a visible-notification test. Full gate and CI for this branch are not verified.

A separate owner-submitted gate excerpt reported `Jarvis.E2E.Tests.EvalTaskCatalogTests.Checked_In_Catalog_Is_Deterministic_And_Valid_Against_Its_Schema` failing on a generated-versus-checked-in catalog mismatch in solution test run 3. A focused Release rerun with a build passed on the current branch (1 passed, 0 failed, 0 skipped); the full gate has not been rerun.

The current planner is **rule-based fast path first**, with the configured Ollama model used as fallback for requests not handled by rules. CLI and UI now display that strategy and configured model. B1's 30-case opt-in live planner suite is implemented; its default run passes the prompt matrix check and skips 30 model tests. B2's opt-in live search-to-report E2E is implemented and default-skips; it uses actual signed authorization, execution, verification, and API reporting with test-local approval/keying and state. B1 and B2 real-model outcomes remain unverified pending owner execution. B3–B9 have not started.
