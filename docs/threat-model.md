# Threat model notes

## Filesystem creation timestamps

Windows filesystem creation timestamps may be rounded or have limited resolution. Stage 5 therefore tolerates a target creation time up to 100 ms before the host-provided `ExecutionObservation.StartedAtUtc`. This tolerance is not proof that the authorized step created the target: Stage 6 also requires the target to be absent from the before-snapshot and present in the after-snapshot. A pre-existing file with identical content still fails, even if its creation timestamp falls inside the 100 ms tolerance.
