# Threat model notes

## Filesystem creation timestamps

Windows filesystem creation timestamps may be rounded or have limited resolution. Stage 5 therefore tolerates a target creation time up to 100 ms before the host-provided `ExecutionObservation.StartedAtUtc`. This tolerance is not proof that the authorized step created the target: Stage 6 also requires the target to be absent from the before-snapshot and present in the after-snapshot. A pre-existing file with identical content still fails, even if its creation timestamp falls inside the 100 ms tolerance.

## Filesystem snapshot coverage

Create-tool watch scopes default to the target's parent folder and are enumerated non-recursively. Changes outside that immediate folder are not detected by those snapshots. Metadata-first comparison hashes only entries with changed metadata (plus the authorized target); a same-size edit whose last-write time is restored can therefore escape detection for an unrelated watched file. This is an explicit performance tradeoff, not a claim of complete filesystem monitoring.

## Reminder task and message integrity

Scheduled reminder actions run the absolute executable path signed by `AuthorizationService`, with exactly `--jarvis-reminder <32-hex-task-id>` as arguments. The title and message are stored separately in `%LOCALAPPDATA%\Jarvis\reminders\<task-id>.json`; the executor uses exclusive file creation before registering the task. The dispatcher validates the ID again, reads only that ID-derived file, displays the payload, and deletes it. Message text is limited to 200 Unicode characters and rejects control characters; the registry schema and host policy enforce the same rule.

The independent verifier compares the registered Task Scheduler command and arguments with signed expected fields, and compares the data file's title/message with the authorization's canonical arguments. Missing or changed data therefore fails verification. It normalizes only CRLF and CR line endings to LF when comparing arguments, because XML/platform serialization may normalize line endings; it does not normalize whitespace, quoting, or other characters.

This check is a point-in-time integrity check, not protection from a process running as the same Windows user changing the task or data file after verification and before its trigger. The dispatcher validates the payload's structure and character limits but does not possess the authorization record and cannot re-check the authorized message. A same-user process can also read the per-user data file before dispatch. Protecting against those same-user races would require a stronger authenticated payload/dispatch contract and remains a known limitation.
