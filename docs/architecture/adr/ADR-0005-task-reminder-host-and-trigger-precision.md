# ADR-0005: Task Reminder Host and Trigger Precision

* **Status:** Accepted
* **Date:** 2026-10-15
* **Authors:** Jarvis Architecture Team

## Context

`task.schedule_reminder` needs to create a per-user Task Scheduler entry that visibly presents the signed reminder data without running a shell or trusting executor output. Task Scheduler XML stores one-time trigger boundaries at whole-second precision, while reminder text is data supplied in the signed canonical arguments.

## Decision

1. Register one task per authorization under `\Jarvis`, with the authorization ID as the task name. Run it as the current user with an interactive token and LUA run level; do not request elevation.
2. Launch the current Jarvis CLI executable directly with the fixed `--jarvis-reminder` switch and properly quoted title/message arguments. No `cmd.exe`, PowerShell, or generated script is involved.
3. The CLI hidden reminder entry point displays a Windows notification-area balloon using `NotifyIcon`; on Windows 10/11 this is surfaced through the OS notification UI. Keep the title to 63 characters and the message to 255 characters.
4. Reject timestamps containing subsecond precision rather than silently rounding them to the Scheduler's whole-second boundary.
5. `Jarvis.Verification` independently reads the task via the Task Scheduler COM API and validates the task path, XML, trigger, action, and enabled state. It does not reference the Task Scheduler package or `Jarvis.Tools`.

## Consequences

- Reminders are per-user and require that user's interactive session for a visible notification.
- A task can be created and verified even when the UI session is unavailable, but the notification cannot be displayed until an interactive session is available.
- Subsecond reminder times are rejected explicitly; callers must supply a whole-second ISO-8601 time.
- The notification is a Windows notification-area balloon rather than an app-identity-based toast API. The OS controls how it is surfaced in the notification UI.
