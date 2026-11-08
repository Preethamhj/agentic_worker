# ADR-0009: Google account sign-in proposal

Status: owner-requested future direction, 2026-11-08. No external network
access is authorized by this ADR. The current loopback-only operating
restriction remains in force.

## Context

The owner selected Google account sign-in as the first collaboration
milestone, ahead of the previously proposed Outlook Calendar feature. The
immediate goal is one optional Google account connection for Jarvis. This
changes the proposed roadmap, but not the active local-first network boundary.
The current change adds only local DPAPI credential-storage scaffolding;
browser OAuth and provider API calls are not implemented.

## Proposed G1 boundary

- Sign-in must use the user's system browser; Jarvis must not collect a Google
  password. The provider account and Jarvis connection are distinct.
- G1 requests identity scopes only (`openid`, `email`, and `profile` if the
  display name is needed). Calendar, Gmail, Drive, and Meet scopes are not part
  of G1 and require separate feature decisions.
- Keep the client JSON outside the repository. Protect persisted tokens and
  account metadata with Windows DPAPI `CurrentUser`; never send them to Python
  or Ollama or include them in logs.
- Disconnecting removes Jarvis's local credential files. It does not by
  itself revoke the grant at Google; revocation must be designed and tested
  separately before claiming that disconnect revokes access.
- Do not report a future provider-side write as successful from its response
  alone. Preserve the existing authorization and independent-verification
  requirements for any later action tools.
- Use fakes for development tests. Any live check requires explicit approval
  and an isolated test account.

## User-provided setup placeholders

No values should be sent in chat or committed. Before any future OAuth
implementation, the owner must supply these locally:

| Placeholder | What to provide / configure |
|---|---|
| `<GOOGLE_CLOUD_PROJECT_ID>` | The Google Cloud project used for Jarvis's OAuth consent configuration. |
| `<GOOGLE_OAUTH_CLIENT_ID>` | Create an OAuth client of the installed/desktop application type in that project. The client ID is in the downloaded client JSON. |
| `<GOOGLE_OAUTH_CLIENT_SECRET>` | Keep it only in the downloaded client JSON at `%LOCALAPPDATA%\Jarvis\Google\oauth-client.json`; do not paste it into chat, source, checked-in config, or logs. |
| `<GOOGLE_TEST_ACCOUNT_EMAIL>` | Add the account to the consent screen's test-user list when the app is in testing mode. Do not share its password. |
| `<GOOGLE_REDIRECT_HANDLING>` | Use the selected OAuth library's supported loopback callback; implementation must document the exact binding, ephemeral/fixed port choice, state validation, and PKCE behavior before enabling it. |
| `<GOOGLE_ALLOWED_ENDPOINTS>` | Enumerate and approve the minimum Google OAuth endpoints needed. The current network policy allows none of them. |

The local credential directory is
`%LOCALAPPDATA%\Jarvis\Google\credentials`. The client JSON is user-managed
setup, not a repository file. The OAuth package references currently in the
project are floating and have not been restored or validated; pin compatible
versions before a reproducible OAuth build.

## Blocking requirements before implementation

The active operating network restriction and design must be formally changed
in the environment where OAuth will run. A complete design must approve exact
endpoints, redirect handling, credential lifecycle and revocation behavior,
scope consent, failure/partial states, and a test strategy. It must also
identify the one Jarvis host service that owns connection state and how the
user explicitly connects/disconnects the account. Until those prerequisites
are met, no browser flow, Google call, or Jarvis DI/UI integration may be
enabled. No OAuth credentials have been supplied or accessed, and no Google
account is connected.
