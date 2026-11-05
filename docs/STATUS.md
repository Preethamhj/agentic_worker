# Jarvis status

Updated 2026-11-05 from the full Release-prep gate. Default branch: `master`; current branch: `release-prep`, based on Phase 4 head `d5b50551ff037353a194bb3324e4014276f1d8f1`. The full gate was run on `77c6f6500db30f61fa143afa83d0644c9753c8cc`; this status update is a documentation-only follow-up.
The `eval-paper-v1` tag remains frozen at `7b5b7e88de868e22c1e8f04d4f5de178bb807c66`. No tags were created, moved, or deleted.

## Latest verified gate

`scripts/check-all.ps1`: **passed**, ending with `ALL CHECKS PASSED SUCCESSFULLY!`; .NET SDK reported by the gate: `10.0.300`.
Release build: 0 warnings, 0 errors.
Non-RealScheduler Release solution tests: Architecture 10, Contracts 47, Core 61, E2E 134, Safety 118, Tools 11, Verification 85, UI 23; **489 passed, 0 failed, 0 skipped**.
RealScheduler category: Verification **13 passed, 0 failed, 0 skipped**. Combined C# total: **502 passed, 0 failed, 0 skipped**.
AI-server Python: **121 passed**, 4 dependency deprecation warnings. Evaluation Python: **12 passed**. Ruff and mypy passed for both areas; .NET format verification passed.
Full gate log: `%TEMP%\jarvis-release-prep-check-all.log`. CI: not checked.

## Release preparation completed

H0–H4 are committed and pushed on `release-prep`: updated README, MIT license and SECURITY policy, tracked-file and ignore-rule hygiene changes, reproducibility guide with raw metadata disclosure, and merge/PR/tag preparation in `docs/RELEASE-PREP.md`.
H2 found no credential-like literals, private key markers, GitHub token patterns, or AWS access-key patterns. Machine-specific `ASUS` path/user references were removed from tracked examples, fixture generation, and documentation; six remaining `C:\Users\test\...` matches are generic test inputs. `.gitignore` covers reports, logs, eval-sandbox, venvs, egg-info, bin, and obj.
The latest raw evaluation metadata records Windows `10.0.26300.0` and `dotnet_version` `10.0.9`; it does not record Python or Ollama model. Current config selects `qwen2.5:7b-instruct`, but that is not proof of the model loaded for the recorded run.

## Findings and open work

Known Phase 5 finding remains: verifier D returned VERIFIED for all 6 BAD F11 paper cases (extra child with another image); do not tune without an amendment. Known Q2 deviations remain recorded in `docs/eval/expected-deviations.json`.
The owner’s Phase 4 interactive checklist remains pending in `docs/phases/PHASE-4-manual-test.md`.
`master` at `b83d305` is not an ancestor of `release-prep`; merge requires a merge commit and is not fast-forwardable. No merge, pull request, or completion tag was created. The owner-managed requested tags are `phase-4-done` and `phase-5-done`; see `docs/RELEASE-PREP.md` for proposed targets and merge commands.

Ignore everything under `reports/`.
