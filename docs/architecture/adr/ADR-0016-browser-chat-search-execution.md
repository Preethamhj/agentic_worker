# ADR-0016: Browser chat search execution and verification

Status: Accepted (owner approved the Jarvis.Browser.Core reference in-session, 2026-11-11)

## Context
The browser chat lets the user type an instruction; local Ollama plans it; C# executes it in WebView2 via CDP.

## Decisions
1. **Architecture rule amendment.** `Jarvis.UI` may reference `Jarvis.Browser.Core` (browser runtime abstraction, no tool/safety/verification logic) in addition to Composition, Contracts, Core and Infrastructure. `Jarvis.UI` still must not depend on Tools, Verification or Safety; Verification types are hidden behind the opaque `BrowserBaseline` in Composition.
2. **Plan.** `POST /v1/browser/plan` receives only the user's typed text (never page content) and returns a validated `search` or `clarify` plan. The LLM supplies no commands.
3. **Authorization.** `browser.search` is registered in `config/tools.registry.json`; every search passes ToolPolicy, the approval dialog and a single-use signed AuthorizationRecord (`BrowserActionAuthority`).
4. **Executor.** Acts only on the consumed signed query, needs exactly one visible search field in the main frame, revalidates before acting.
5. **Verification.** `BrowserSearchVerifier` uses only the browser engine's address and loading state. VERIFIED requires a same-host change carrying the signed query; unchanged is FAILED; the rest ESCALATE.
6. **Page-aware extension.** ADR-0017 permits only the explicitly reviewed, URL-free page projection and a separate typed `browser.open_link` action. It does not extend the search action or authorize form interaction.

## Limitations
Search remains main-frame only and a page-controlled URL can imitate a search result. Page-aware planning uses the separate ADR-0017 disclosure preview and confirmation; opening a link requires its own signed approval, live-target revalidation, and independent browser-address verification. The page observer is not a trusted security principal, and a narrow check-to-action race remains.
