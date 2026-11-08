# ADR-0008: Proposed classic Outlook calendar integration

Status: proposal requested by owner on 2026-11-08; not approved for
implementation. This does not amend the active design or relax its boundaries.

## Context

The owner wants Jarvis to add an event directly to a configured classic
Outlook calendar and separately schedule a Jarvis reminder. A Task Scheduler
reminder alone is not a calendar event. The owner reports that classic Outlook
opens with a configured calendar. A read-only local probe did not find
`outlook.exe` on PATH or `Outlook.Application` registration; the difference
has not been explained. No Outlook profile or calendar was accessed by Jarvis.

The design currently defers Outlook and Calendar COM. A feasible integration
must be established without bypassing the existing restrictions on network
access, COM placement, signed authorization, and independent verification.
Outlook may synchronize a calendar through its own account; its actual behavior
and compatibility with the network policy have not been established.

## Proposed requirements

1. Use a typed allowlisted calendar operation with schema-validated arguments.
   The approved, signed record must bind the target calendar, event identity,
   subject, start and end, time zone, recurrence and reminder settings as
   applicable. Reject missing or ambiguous fields rather than inventing them.
2. Obtain explicit approval before any calendar write. Never generate commands
   from model output or treat executor output as evidence.
3. Independently inspect the target calendar after the write and compare the
   observed event with signed expectations. Missing, ambiguous or unreadable
   evidence must not produce VERIFIED. Do not introduce forbidden dependencies
   into the verification assembly.
4. Treat calendar creation and Jarvis reminder registration as separately
   authorized and verified effects. If either fails, report precisely which
   effect was observed; do not report the combined request as successful.
   Define partial-failure and recovery behavior before implementation.
5. Preserve one-time reminder behavior. Daily recurrence must follow the
   selected contract in ADR-0007 and must not be presented as complete until
   both scheduling and calendar recurrence are independently verified.
6. Develop with fakes and isolated fixtures. Owner-run real checks must use a
   specifically approved test event and verify cleanup, without inspecting
   unrelated calendar data.

## Open blockers

- Identify the installed Outlook variant, architecture, registration and
  supported local integration mechanism; resolve the missing registration
  without assuming Outlook is absent.
- Establish an integration and independent evidence mechanism that meets the
  existing COM and network restrictions. No exception is approved by this
  proposal.
- Specify calendar selection, event duration when omitted, recurrence and
  daylight-saving representation, duplicate prevention, and partial-failure
  recovery. Confirm choices affecting the user's calendar before writing.
- Test actual Outlook event creation, readback, account sync and reminder
  delivery with the owner. None has been implemented or verified.
