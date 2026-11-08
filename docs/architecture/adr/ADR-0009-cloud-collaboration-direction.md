# ADR-0009: Requested cloud collaboration direction

Status: owner-requested future direction, 2026-11-08. Not implemented or
authorized as a change to the current operational network boundary.

## Context

The owner wants Jarvis to connect to internet-based services, beginning with
Outlook Calendar, and later potentially Google Calendar, mail and Drive.
This changes the original local-only product direction. The current design
and operating constraints allow loopback-only connections and defer Outlook
integration; they remain in force. This document does not authorize network
access or a production integration.

## Target design

- Connections are optional and independently enabled by the user. Sign-in
  should use the provider's browser-based OAuth flow, not a password entered
  into Jarvis. The provider account and Jarvis connection are distinct: a
  normal Outlook sign-in does not connect the account to Jarvis.
- Request the least permissions required for the selected feature. Support
  disconnecting an account and revoking or removing stored credentials. Never
  place tokens, passwords or client secrets in code, configuration or logs.
- Preserve per-action policy checks, user approval for writes, signed
  authorization and an independently collected read-back before reporting
  success. A provider response from the executor is not verification evidence.
- The first proposed feature is a single approved Outlook Calendar event and
  independent read-back. Existing local reminder registration is a separate
  effect and must not be reported as successful merely because the calendar
  event succeeded. Mail, Drive and other providers are subsequent work.
- Use fakes for development tests and explicitly approved, isolated test
  accounts and events for any future real-provider checks.

## Before implementation

The operating network restrictions and active design must be formally changed
in the environment where cloud integration is to run. The design must specify
allowed provider endpoints, OAuth client registration and redirect handling,
credential storage, permission and revocation behavior, independent evidence,
event identity, error handling and partial failures. Provider-specific setup
and end-to-end testing remain outstanding. Nothing in this record implements
OAuth, accesses an account, or establishes that Outlook is connected.
