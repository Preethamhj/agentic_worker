# Evaluation summary

Distinct verifier/case rows: 384

## Control verdicts per verifier

| Verifier | VERIFIED | ESCALATE | FAILED | ERROR |
|---|---:|---:|---:|---:|
| A | 12 | 0 | 0 | 0 |
| B | 12 | 0 | 0 | 0 |
| C | 12 | 0 | 0 | 0 |
| D | 12 | 0 | 0 | 0 |

## Detection by verifier and fault

| Verifier | Fault | Cases | Detected | Wilson 95% | Instability |
|---|---:|---:|---:|---:|---:|
| A | F0 | 12 | 0 | [0.000, 0.242] | 0 |
| A | F1 | 12 | 0 | [0.000, 0.242] | 0 |
| A | F18 | 12 | 0 | [0.000, 0.242] | 0 |
| A | F2 | 12 | 0 | [0.000, 0.242] | 0 |
| A | F3 | 12 | 0 | [0.000, 0.242] | 0 |
| A | F4 | 12 | 0 | [0.000, 0.242] | 0 |
| A | F5 | 12 | 0 | [0.000, 0.242] | 0 |
| A | F6 | 12 | 0 | [0.000, 0.242] | 0 |
| B | F0 | 12 | 0 | [0.000, 0.242] | 0 |
| B | F1 | 12 | 12 | [0.758, 1.000] | 0 |
| B | F18 | 12 | 0 | [0.000, 0.242] | 0 |
| B | F2 | 12 | 12 | [0.758, 1.000] | 0 |
| B | F3 | 12 | 12 | [0.758, 1.000] | 0 |
| B | F4 | 12 | 12 | [0.758, 1.000] | 0 |
| B | F5 | 12 | 0 | [0.000, 0.242] | 0 |
| B | F6 | 12 | 12 | [0.758, 1.000] | 0 |
| C | F0 | 12 | 0 | [0.000, 0.242] | 0 |
| C | F1 | 12 | 12 | [0.758, 1.000] | 0 |
| C | F18 | 12 | 0 | [0.000, 0.242] | 0 |
| C | F2 | 12 | 12 | [0.758, 1.000] | 0 |
| C | F3 | 12 | 12 | [0.758, 1.000] | 0 |
| C | F4 | 12 | 12 | [0.758, 1.000] | 0 |
| C | F5 | 12 | 0 | [0.000, 0.242] | 0 |
| C | F6 | 12 | 12 | [0.758, 1.000] | 0 |
| D | F0 | 12 | 0 | [0.000, 0.242] | 0 |
| D | F1 | 12 | 12 | [0.758, 1.000] | 0 |
| D | F18 | 12 | 12 | [0.758, 1.000] | 0 |
| D | F2 | 12 | 12 | [0.758, 1.000] | 0 |
| D | F3 | 12 | 12 | [0.758, 1.000] | 0 |
| D | F4 | 12 | 12 | [0.758, 1.000] | 0 |
| D | F5 | 12 | 12 | [0.758, 1.000] | 0 |
| D | F6 | 12 | 12 | [0.758, 1.000] | 0 |

## C scope

Inside-scope faults are listed before outside-scope faults: F5, F11, F15, F17a, and F17b are outside C's predicate scope.

## Expected-verdict deviations

No expectation table was embedded in raw.jsonl; deviations require the pre-registered table in PROTOCOL.md and are therefore reported as manual review items.

## D not better

Per-case D-versus-peer comparisons are intentionally emitted only when raw rows contain all four verifier statuses; review raw.jsonl for those cases.
