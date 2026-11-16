# ADR-0013: User-operated WebView2 browser network boundary

Status: Accepted (2026-11-10)

## Context

The existing network policy restricts Python to loopback and local Ollama,
and restricts C# API clients to the exact enabled Google endpoint policy.
The browser requirement is for ordinary user-driven access to arbitrary
websites, which cannot work under the Google-only API allowlist.

## Decision

- Add a WPF WebView2 workspace as a separate user-operated browsing surface.
- Permit that WebView2 instance to load user-selected HTTP and HTTPS sites,
  including page subresources. This is the only broad browsing exception.
- Use a Jarvis-owned WebView2 user-data directory under
  `%LOCALAPPDATA%\Jarvis\Browser\Profile`; do not attach to the user's
  personal Chrome profile.
- Keep Python loopback/Ollama-only and keep all C# API clients under their
  existing explicit policies. This ADR does not authorize arbitrary
  `HttpClient`, socket, or model-directed network access.
- The initial workspace starts blank, permits manual navigation only,
  rejects non-HTTP(S) top-level navigations, and cancels downloads and site
  permission requests. The browser does not send page content to Ollama.
- Browser automation and page-directed natural-language actions are deferred.
  The narrow user-directed URL resolution and Google search navigation flows
  are separately specified in ADR-0014; they do not inspect or interact with
  page content.
  Broader browser actions must use typed registered operations, current
  observations, authorization appropriate to the action, and explicit
  confirmation for sensitive external effects. Page content remains untrusted.

## Consequences

- The user can browse public and authenticated sites in a profile separate
  from personal Chrome. Sites can still receive normal browser requests and
  may set cookies within the Jarvis profile.
- WebView2 page and subresource traffic is outside the Google host/path
  enforcement and is not constrained by `GoogleApiClientFactory`.
- Download saving, site permissions, browser automation, DOM inspection,
  chat context, and browser persistence need separate implementations and
  validation before being enabled.
- The installed WebView2 runtime is environment-dependent; build-time SDK
  packaging does not itself install the Evergreen Runtime.

## Amendment (2026-11-10): local observation

[ADR-0015](ADR-0015-browser-observation-privacy.md) permits local page
observation for the future browser agent and requires explicit confirmation
before disclosure of page content to Ollama. It does not expand the network
boundary or authorize model-directed actions.
