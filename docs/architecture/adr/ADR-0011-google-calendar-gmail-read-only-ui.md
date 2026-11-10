# ADR-0011: Read-only Google Calendar and Gmail UI

Status: accepted, 2026-11-09.

## Context

Google account sign-in and the exact-host C# transport are implemented under
[ADR-0010](ADR-0010-google-external-network-access.md). The first provider
API increment should display Calendar and Gmail data without extending
Jarvis's LLM tool registry, execution path, or verification architecture.

## Decision

- Implement only read-only Calendar and Gmail access in this milestone.
- A dedicated WPF tab presents the primary calendar's next ten events and
  the inbox's ten most recent messages after the user explicitly selects
  Refresh. No automatic data load occurs on application startup.
- Gmail uses the list endpoint and requests metadata-only message details.
  The UI displays headers and the API-provided snippet; it does not fetch
  message bodies or attachments.
- Both generated Google API services are created through
  `GoogleApiClientFactory`; their requests remain limited by the exact
  host/path/method, DNS, TLS, timeout, and redirect policy in ADR-0010.
- These explicit read-only UI actions are not tool executions. They do not
  use LLM plans, issue `AuthorizationRecord`s, or bypass a tool execution
  requirement; they are direct user-requested presentation reads.
- The stored Google scopes for these services remain read-only. Drive and
  Meet service flags are disabled in `config/settings.json` and their
  implementations are deferred.

## Limitations

- The user must configure a Google Desktop OAuth client and grant consent.
- API enablement, OAuth consent-screen configuration, account policy, and
  network availability can still prevent live reads.
- Calendar event `htmlLink` values are returned by the reader but are not
  automatically opened.
- Unit tests use mocked HTTP transports. They do not access a live Google
  account or prove credentials/API setup in the user's environment.
