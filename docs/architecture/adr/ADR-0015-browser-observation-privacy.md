# ADR-0015: Browser observation and disclosure boundary

Status: Accepted (2026-11-10)

## Context

The requested browser agent needs to understand the page that the user is
working with. ADR-0013 and ADR-0014 initially limited browsing to
user-operated navigation without page inspection. Page text is untrusted and
can include private information, sensitive values, or instructions planted by
a website. Sending it to the local model is a separate disclosure decision.

## Decision

- Permit local observation of a page in the dedicated WebView2 profile as a
  prerequisite for the browser agent. Do not fetch the page through Python or
  a separate HTTP client.
- Ask for explicit user confirmation for the particular disclosure before
  sending any page content to local Ollama. No approval or disclosure is
  implemented in the observation phase; page content stays in host memory.
- Never send passwords, cookies, or authentication tokens to Ollama. A
  future disclosure feature must demonstrate that it enforces this rule; a
  general warning or assumed redaction is insufficient.
- Treat all observed content as untrusted data, not instructions. Require
  typed registered operations, signed authorization and the applicable
  approval and verification controls before enabling browser actions.
- Retain the Python loopback-only rule, controlled Google API transport and
  dedicated WebView2 profile. Do not launch native applications from a page.

## Consequences

- Observation does not itself constitute independent verification and cannot
  justify a `VERIFIED` verdict.
- Page observations can still contain private text or sensitive URL
  components. They must not be logged, persisted, or shared as a side effect
  of observation.
- Confirmation, safe disclosure, automation and human-intervention flows are
  separate follow-up work; this ADR does not claim they are implemented.

## Amendment (2026-11-11): search metadata foundation

For the first website-search slice, the owner chose confirmation for each
disclosure, approval for each browser action, and independent verification
before reporting a search complete. A local projection can identify up to
20 possible main-frame search fields using only snapshot-local numeric
indices. Its output contains no page text, attributes, URLs, input values or
credentials. This minimizes what a future model request would expose; it is
not itself consent and does not establish that the proposed field is safe or
still exists. Page-supplied labels can be misleading. A future executor must
resolve and validate a fresh live target, obtain signed authorization and
approval, and use independent verification. The projection is not yet wired to
Ollama, the user interface or any execution path.

## Amendment (2026-11-11): CDP search observation

The WebView2 adapter now has an internal, observation-only CDP path that
enumerates possible main-frame input fields and retains CDP backend node IDs
locally. Its outward result contains only an observation ID, numeric candidate
indices and a truncation flag; input attributes are not returned or sent to
Ollama. A fresh CDP enumeration checks that a candidate still refers to the
same node and still matches the search-field heuristic. Navigation, disposal,
replacement and new captures invalidate earlier observations.

This is not a browser action, signed authorization or independent evidence.
Page-controlled metadata can misclassify fields; visibility, shadow roots,
child frames and changes between revalidation and execution remain unresolved.
The existing JavaScript snapshot's references are not mapped to these CDP
candidates. Confirmation, a bounded typed execution path and independent
verification remain prerequisites for browser search.

## Amendment (2026-11-11): observation invalidation

CDP search observations now carry a local invalidation revision. An
invalidation during capture or revalidation prevents the in-flight operation
from returning a current result, and a failed or cancelled revalidation
invalidates the observation. Switching browser tabs or unloading the browser
workspace also invalidates the affected tab's observations. Inputs explicitly
marked `hidden` or `aria-hidden="true"` are excluded. This does not establish
CSS visibility, prevent changes after revalidation, or authorize an action.

## Amendment (2026-11-11): layout filtering

The internal CDP search observer now requests a local DOM layout snapshot and
requires a positive-size layout box, computed visible style and non-zero
opacity for a candidate. It rejects transparent or unmeasurable element
ancestors. The raw CDP snapshot may contain private page data; it stays
transient in host memory and is neither logged nor sent to Ollama. A
12-million-character parsing limit rejects larger responses after receipt.
The search metadata remains observation-only. Viewport visibility, clipping,
occlusion, frame and shadow-root coverage, and the check-to-action race are
not established by this filter.
