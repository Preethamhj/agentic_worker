# ADR-0007: Daily reminder delivery contract

Status: selected by owner on 2026-11-08; not implemented.

Daily reminders should use the current PC local wall-clock time, including after
a time-zone change. If multiple occurrences are missed, show one catch-up
notification. On a daylight-saving gap, use the next valid local time; on an
ambiguous time, show once at the first occurrence. When the machine starts while
the user is signed out, catch-up may occur after sign-in.

This decision does not establish that Task Scheduler provides these guarantees.
The implementation must preserve the signed authorization and independent OS
verification boundaries, retain recurring data across dispatch, and prevent
duplicate catch-up notifications. It must not change the existing one-time
lifecycle. Owner-run wake, restart, sign-in, time-zone and daylight-saving
checks remain necessary before claiming delivery behavior works.

For the existing one-time fast path, an absolute time without a date selects
the next occurrence; an explicitly past time today, an invalid time or an
ambiguous or nonexistent local time requires clarification. This one-time
rule is separate from the selected daily daylight-saving behavior.
