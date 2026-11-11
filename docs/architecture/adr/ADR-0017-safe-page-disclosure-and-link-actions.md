# ADR-0017: Safe page disclosure and link actions

Status: Accepted (2026-11-11)

## Context

The browser chat currently sends only the user's instruction to the local
planner and supports an approved search action. Page-aware planning and opening
a link require a distinct disclosure decision and a narrowly scoped action
contract. Website content is untrusted and may contain private data or
instructions intended to manipulate the model.

## Decision

- Page inspection remains local to the dedicated WebView2 profile. Never send
  raw DOM, scripts, form values, cookies, or page URLs to Ollama.
- Before every page-aware planner request, show the user the exact bounded
  projection that would be sent and require explicit confirmation. The
  projection contains visible page text and temporary IDs with visible link
  labels only; the ID-to-target mapping remains in host memory.
- Omit hidden or disabled controls, input values, link destinations, query
  strings, fragments, and arbitrary element attributes. Redact recognizable
  credential and token patterns. If credential-like content cannot be safely
  filtered, do not disclose the snapshot and ask the user to inspect it.
- Treat the snapshot as untrusted data, never as instructions. The planner
  returns a validated `open_link` proposal containing only a temporary link
  ID, or a clarification. Unknown IDs, malformed proposals, and ambiguous
  targets are rejected.
- Disclosure approval does not approve an action. Opening a link is a separate
  registered typed operation requiring user approval and a single-use signed
  authorization bound to the selected tab, observation, link ID, and a
  one-way target fingerprint. The URL itself is not sent to Ollama or included
  in authorization arguments or logs.
- Immediately before action, revalidate the observation and target against the
  live page through the browser adapter. Reject stale, hidden, disabled,
  ambiguous, non-HTTP(S), or credential-bearing targets. Never execute
  page-provided scripts or fill forms.
- Verify the resulting browser state independently. Report success only when
  browser-engine evidence supports the authorized navigation; otherwise report
  failure or that the outcome could not be confirmed.
- Form filling, purchases, bookings, downloads, and any action beyond opening
  a link remain out of scope and require separate policy decisions.

## Consequences

- Page-aware planning cannot occur when disclosure is declined or filtering
  cannot safely produce a preview.
- The temporary ID mapping is invalidated on navigation, tab changes, new
  observations, cancellation, and workspace unload.
- The destination remains private to the host and the browser. The approval
  UI may identify its host so the user can make an informed action decision,
  but must not place the full URL in planner content or logs.
- Snapshot text and link labels may still contain prompt injection or
  sensitive information that cannot be identified mechanically. The preview
  is therefore mandatory, and suspicious content must fail closed rather than
  being treated as a successful redaction.

## Amendment (2026-11-11): disclose bounded partial observations

Oversized pages and unavailable child frames are expected in ordinary sites;
rejecting every truncated or partial observation prevented any page context
from reaching the planner on those pages. An active, fully loaded main-frame
observation may now be disclosed even when the bounded snapshot is truncated
or one or more child frames could not be captured. The exact preview must
prominently state that the snapshot is truncated or that frames were omitted.
Only the content shown in the preview is sent, and the same explicit
per-request disclosure confirmation remains mandatory. Credential-like
content, inactive observations, and pages still loading remain blocked.

Truncation and child-frame capture issues do not by themselves mean the main
document is stale. Freshness checks still require the same active tab, main
frame, document token, and DOM version. A proposed link must independently
resolve to the same visible, enabled main-frame target immediately before
execution; partial capture does not authorize an omitted or stale element.

## Proposed follow-up scope (2026-11-11): targeted links and browser forms

The owner requested that unrelated page mutations no longer block a link when
the same browser-engine node, document, destination, label, and visibility can
be revalidated. The owner also requested form filling and button clicks, with
separate approval for non-transactional submissions. Passwords, one-time
codes, payment details, and authentication secrets are excluded from automated
filling. Sign-ins, purchases, payments, bookings, and destructive actions are
excluded from the requested submission scope.

This is a requested follow-up, not permission to bypass the current controls.
The existing page-wide freshness rule and prohibition on general form filling
and button clicks remain active until separate typed registered tools,
authorization, independent verification, and live-target validation are
implemented and tested. A page-provided label alone cannot establish that a
button or submission is non-transactional; unknown or ambiguous effects must
be refused. The existing disclosure preview and separate signed approval
remain mandatory. Passwords, cookies, authentication tokens, and other secrets
must never be sent to Ollama. This follow-up does not authorize arbitrary
scripts or model-provided commands.

## Proposed follow-up (2026-11-11): new-tab navigation

The owner requested that approved browser-agent link actions open in a new tab
by default. This is not implemented yet. It does not replace source-page and
target revalidation: the authorized source tab and link must remain current
before navigation. The approval and signed operation must identify the
new-tab behavior, and independent verification must inspect the destination
tab rather than the original tab. Tab creation, cancellation, navigation
failure, and disposal require explicit tests before the default changes.

## Approved direction (2026-11-11): stable target and new tab

The owner approved new-tab navigation by default and target-specific
freshness checks. The replacement design binds each proposed link to its
browser-engine node before model planning, while the initial observation is
still current. Before execution, the host must confirm the same live main
document and browser node, recheck the link's label, destination, visibility
and enabled state, and reject removal, replacement, navigation, cancellation,
or ambiguous binding. Unrelated document mutations need not invalidate an
unchanged link. Node binding and revalidation are host-only and cannot add
URLs to model context. The new-tab operation must be reflected in the signed
authorization and approval, and verification must inspect the destination tab.
Until these controls are implemented and tested, the previous page-wide rule
remains in force.

## Implementation update (2026-11-11): bound link and destination tab

The host now binds the main-frame interactive-element order to CDP backend
node identities during a consistent local observation. It re-enumerates the
nodes after approval and checks the same document, node, label, destination,
visibility and enabled state. Unrelated mutations between the two observations
do not invalidate that link; mutations during either binding attempt still
reject it. The DOM response stays local and is bounded for parsing.

The registered link operation signs `mode: new_tab`. The workspace creates a
destination tab after approval, checks that the user has not selected another
tab or navigated the destination, consumes the authorization after source
revalidation, and verifies using destination-browser evidence. A failed
revalidation can leave an empty destination tab; it does not navigate the
link. A user tab switch or cancellation must abort the action. Exact URL
matching may escalate on legitimate redirects. The observation script and
the remaining check-to-navigation interval are not independent evidence and
still need real-site and adversarial testing. No new form, button, sign-in or
transaction capability is granted by this update.

## Follow-up (2026-11-11): transient observation changes

The host may retry a link binding up to three times when a capture reports a
page change. Each attempt still requires the same document, stable ordered
node identities and unchanged observation version throughout that attempt.
Retries never cross a navigation and remain cancellable and time-bounded.
Continuously changing pages may still fail closed. This does not authorize an
unbound link or relax action approval or independent verification.

## Follow-up (2026-11-11): exact-label planning

After disclosure approval, an instruction in the narrow form `open <label>
link` may be resolved deterministically against the approved link labels
before calling Ollama. Matching ignores case and repeated whitespace only.
Exactly one match yields its temporary ID; no match or multiple matches
requests clarification. Other instructions continue through the validated
model planner. This produces a proposal only: separate action approval,
signed authorization, stable-node revalidation and independent verification
are unchanged. For exact-label requests, the approved projection is processed
by the local AI server but is not sent to Ollama.
