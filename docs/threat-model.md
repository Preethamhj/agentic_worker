# Threat model notes

## Filesystem creation timestamps

Windows filesystem creation timestamps may be rounded or have limited resolution. Stage 5 therefore tolerates a target creation time up to 100 ms before the host-provided `ExecutionObservation.StartedAtUtc`. This tolerance is not proof that the authorized step created the target: Stage 6 also requires the target to be absent from the before-snapshot and present in the after-snapshot. A pre-existing file with identical content still fails, even if its creation timestamp falls inside the 100 ms tolerance.

## Filesystem snapshot coverage

Create-tool watch scopes default to the target's parent folder and are enumerated non-recursively. Changes outside that immediate folder are not detected by those snapshots. Metadata-first comparison hashes only entries with changed metadata (plus the authorized target); a same-size edit whose last-write time is restored can therefore escape detection for an unrelated watched file. This is an explicit performance tradeoff, not a claim of complete filesystem monitoring.
