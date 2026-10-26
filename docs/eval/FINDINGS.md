# Evaluation findings

## W1 smoke — D false negative on GOOD controls

- Case IDs: `594b5994f68f93ac1ff88e89cc566eac-r1-F0`, `51d91a1c0f04b1184c753a0007643a06-r1-F0`
- Fault: F0 control (`GOOD`)
- Fault: verifier D returned `ESCALATE`, not `VERIFIED`.
- Evidence: raw smoke rows report `Snapshot collection failed: Snapshot I/O failed: The process cannot ...` during independent verification.
- Run: W1 smoke, 2 file tasks, 1 repetition, 64 verdict rows plus metadata.
- Action: recorded only; production verifier, executor, safety, and host code were not changed.
