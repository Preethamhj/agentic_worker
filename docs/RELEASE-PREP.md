# Release preparation

## Merge plan

At the time of this preparation, `origin/master` is `b83d305` and `origin/release-prep` is at `25adbdc`. Their merge base is `b6de340`. `master` is **not** an ancestor of `release-prep`, so this merge cannot fast-forward; merge commit required.

After review, a clean working tree, and a passing gate/CI for the exact release-prep head, the owner can merge with:

```powershell
git fetch origin
git switch master
git pull --ff-only origin master
git merge --no-ff --no-edit origin/release-prep
git push origin master
```

Resolve any merge conflicts deliberately and rerun the gate before pushing. These commands are preparation only; no merge or tag action was performed.

## Pull request draft

**Title:** `Prepare Phase 4 release documentation and hygiene`

**Body:**

```markdown
## Summary
- Document Jarvis architecture, safety/verification behavior, quick start, commands, limitations, and evaluation reproduction.
- Add the MIT license and a private vulnerability reporting policy.
- Remove tracked machine-specific username/path references and make fixture generation repository-relative.
- Ignore release/evaluation output directories and add the Phase 4 UI host documentation.

## Validation
- `scripts/check-all.ps1`: passed on `77c6f6500db30f61fa143afa83d0644c9753c8cc`; 489 non-RealScheduler and 13 RealScheduler tests passed, with 0 failures/skips. Release build had 0 warnings/errors; Python tests, Ruff, mypy, and format verification passed.
- CI: [add run URL and status after checking this exact head.]

## Release notes
- `master` requires a merge commit; this branch cannot fast-forward from the current master head.
- Do not move or recreate `eval-paper-v1`.
- Owner creates the phase completion tags only after merge, gate, and CI are confirmed.
```

## Owner-managed tags

No tags were created or changed. After merge and successful gate/CI, the requested tag names and proposed targets are:

| Tag | Proposed target |
|---|---|
| `phase-4-done` | The merge commit containing this release-prep branch |
| `phase-5-done` | Phase 5 completion head `b6de3409ac54234775045b2e65aab4a834829085` |

The frozen `eval-paper-v1` tag remains unchanged and is not one of the proposed completion tags.

## H2 tracked-file audit

The tracked-file scan initially found one machine-specific `C:\Users\ASUS` path in the fixture generator, one in a generated contract example, one in a phase document, and two literal `ASUS` approver/example values. These were replaced with repository-relative fixture generation and generic test/documentation values. Six remaining absolute `C:\Users\test\...` matches are generic path-policy test inputs, not the current user's home path.

The scan found no credential-like literals, private-key markers, GitHub personal access token patterns, or AWS access-key patterns. No generated build/cache files were tracked. `.gitignore` now covers `reports/`, `logs/`, `eval-sandbox/`, `.venv/`, `*.egg-info/`, `bin/`, and `obj/`; each was verified with `git check-ignore`.
