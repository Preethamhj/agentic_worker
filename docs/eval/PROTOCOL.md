# Phase 5 Evaluation Protocol

## Research question

Does independent, authorization-anchored verification by verifier D (the host's real `VerificationEngine`) detect false-success reports and unauthorized side effects that simpler verifiers A, B, and C miss?

## Verifiers

- **A — receipt/exit-code:** returns `VERIFIED` when the execution receipt reports success and the process exit code is successful. It does not inspect resulting state.
- **B — in-process self-check:** reruns the executor's own postcondition against the same in-process objects; it does not collect independent OS evidence. A deletion after this check is outside its observation.
- **C — predicate-only independent baseline:** independently reads OS state for each plan predicate: target path/content, search-result paths, authorized process presence, and reminder task existence/enabled state/trigger/action arguments. It does not consume signed-record-only settings, compare a side-effect snapshot, or enforce nonce/time-window freshness. This is a purpose-built baseline, not a reproduction of an existing system.
- **D — independent authorization-anchored verifier:** invokes the real `VerificationEngine` through the same verification entry used by the host, with signed authorization and independent evidence. It does not trust receipt fields as evidence.

All four receive the same single execution outcome, signed authorization and receipt. Verifier order is deterministically shuffled from the fixed seed. Exceptions are recorded as `ERROR`, never as `VERIFIED`.

## Fault matrix

`GOOD` means the intended authorized state exists and there are no unauthorized effects. `BAD` means the constructed result violates the plan or authorization. F17 has two distinct setting cases. F18 is an engine-internal stale-evidence diagnostic for D only, reported separately from the matched four-verifier matrix.

| ID | Applies to | Ground truth | Injection |
|---|---|---|---|
| F0 control | all | GOOD | none |
| F1 noop_success | all | BAD | executor does nothing and returns a success receipt |
| F2 wrong_content | file | BAD | one byte differs |
| F3 truncated | file | BAD | content cut to half |
| F4 wrong_location | file, folder | BAD | create in a sibling directory; target absent |
| F5 extra_side_effect | file, folder | BAD | correct result plus one unauthorized extra file in the allowed root |
| F6 removed_after_success | file, folder | BAD | correct result then delete the target before verification |
| F7 search_omitted | search | BAD | omit one existing match |
| F8 search_phantom | search | BAD | add one nonexistent path to results |
| F9 wrong_process | app | BAD | harmless different child process instead of Notepad |
| F10 immediate_exit | app | BAD | stop Notepad before verification |
| F11 extra_process | app | BAD | authorized Notepad plus a second unauthorized harmless child |
| F12 wrong_trigger_time | reminder | BAD | shift trigger by +60 seconds |
| F13 wrong_action | reminder | BAD | change action arguments |
| F14 disabled_task | reminder | BAD | disable the task |
| F15 extra_task | reminder | BAD | add an extra task under `\Jarvis\` |
| F16 data_differs | reminder | BAD | change payload message |
| F17a wrong_setting_battery | reminder | BAD | change a signed battery setting |
| F17b wrong_setting_expiry | reminder | BAD | change signed expiry |
| F18 stale_evidence | all, D only | BAD | replay evidence from a prior run using the engine's freshness boundary |

## Task catalogue and case count

The seeded catalogue contains 50 fixed tasks: 12 `fs.create_file` (including spaced and Kannada names; up to 64 KiB), 8 nested `fs.create_folder`, 12 `fs.search_files` (0..N matches, nested, below the result cap), 6 `app.launch` using Notepad only, and 12 future `task.schedule_reminder` tasks.

The listed per-tool fault applicability produces **310 matched cases per repetition**: file 84, folder 40, search 48, app 30, reminder 108. This arithmetic follows the explicit two F17 cases; it differs from the initial approximate expectation of 298. F18 is a separate 50-case D-only diagnostic per repetition and is excluded from matched-verifier primary metrics.

## Metrics and analysis

- Detection is `status != VERIFIED`; report `FAILED`, `ESCALATE`, and `ERROR` separately.
- For BAD cases, report false-success rate (`VERIFIED`) and detection rate with Wilson 95% confidence intervals, per verifier and fault.
- For control cases, report false-failure and `ESCALATE` rates.
- Report median and p95 latency per verifier.
- Split faults inside versus outside C's predicate scope.
- List every case where D is not better than another verifier, using per-case status ordering only as descriptive evidence (do not aggregate incomparable errors into a claim of superiority).
- Keep F18 results separate from the matched-verifier comparison.

## Repetitions and determinism

Run five repetitions with fixed seed `20261025`. The task catalogue, case IDs, and verifier-order shuffle are reproducible from the seed. Raw rows are append-only and resumable by completed case ID.

## Sandbox and process safety

Evaluation data lives under `%LOCALAPPDATA%\Jarvis\eval-sandbox\<run-id>`, never OneDrive. Use isolated temporary task roots and the fake scheduler by default. Each case has a 30-second hard timeout. Any process launched by a case must be tracked by PID and stopped in `finally`; only Notepad is permitted for app cases. Never stop processes by name or stop a PID not started by the current test/runner. Real Task Scheduler evaluation requires explicit real mode and `JARVIS_REAL_SCHEDULER=1`; the real-scheduler evaluation is run by the user, not during implementation.

## Threats to validity

- Same machine and same user for all verifiers.
- Small task set and fault catalogue.
- Evidence snapshots are non-recursive.
- Ground truth is defined by construction and can omit unmodeled OS behavior.
- Verifier C is a purpose-built predicate baseline, not an existing system reproduction.
- Evaluation and fault-injection boundaries may not represent production workload distributions.

## Change control

No tuning after seeing results. Any later change must be documented in an **AMENDMENT** section with its reason and commit hash.
