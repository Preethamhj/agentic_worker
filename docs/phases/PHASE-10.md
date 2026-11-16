# Phase 10: Browser observation foundation (incomplete)

This report concerns Phase 4 of the user-provided browser-agent specification.
Repository phase numbering is separate from the specification's phase numbers.

## Exact link-label planning follow-up (2026-11-11)

The owner supplied a transcript in which the first Mobiles request was
cancelled, an External HDD request produced a generic clarification, and an
approved `main content` link opened in a new tab and was verified. This shows
the link-action path worked for that action but does not establish why the
model clarified the other request or whether the cancelled request would have
worked. The page projection contained the requested visible label; link URLs
intentionally stay in the host and are not given to the model.

The local planner now handles the narrow `open <label> link` form by exact,
case-insensitive, whitespace-normalized matching against the approved page
projection. A unique match returns only its temporary ID; missing or duplicate
labels clarify without guessing. These requests no longer wait for Ollama;
other requests still use the schema-validated planner. Separate disclosure,
action approval, signed authorization, source revalidation and independent
verification remain unchanged.

Browser planner tests: **20 passed, 0 failed** with two dependency deprecation
warnings. Ruff and mypy passed. A standalone call using synthetic labels
returned the expected typed link ID; it did not test the UI or browser action.
Before the fast path, a synthetic request involving the configured local model
did not finish within the 30-second test limit; this does not establish the
cause of the user's cancellation. No real-site end-to-end rerun, full gate, or
CI check was performed. Restart the active AI-server process as well as Jarvis
to load the Python change.

```powershell
ai-server\.venv\Scripts\python.exe -m pytest ai-server\tests\unit\test_browser_routes.py -q
ai-server\.venv\Scripts\ruff.exe check ai-server\src\jarvis_ai\api\routes_browser.py ai-server\tests\unit\test_browser_routes.py
ai-server\.venv\Scripts\mypy.exe ai-server\src
```

## YouTube layout and dynamic-page follow-up (2026-11-11)

The user transcript showed a search failing because the CDP layout contained
multiple entries for one node. Layout fragments are now combined: all must
be visible, and at least one must have positive size. A separate Amazon
request failed link binding because its observation did not remain consistent
during capture; the precise failed comparison is not recorded. Binding now
retries transient consistency failures up to three times, with cancellation,
the original navigation generation and the original per-attempt safety checks.
Persistently changing pages can still fail closed.

Changed `CdpLayoutVisibility`, `CdpLinkObserver`, `BrowserWorkspace` and their
UI tests. Added `windows-host/tests/Jarvis.UI.Tests/TestPages/index.html` and
`mobiles.html` for a controlled local link test. UI suite: **137 passed, 0
failed, 0 skipped**; isolated Release build: **0 warnings, 0 errors**. A
loopback HTTP check confirmed the test page and destination were served. It
did not exercise WebView2, model planning, approval, or verification for this
fixture. The real-site failures were not reproduced after this change; the
full gate and CI were not run.

From the repository root, start the local test server in a separate terminal:

```powershell
ai-server\.venv\Scripts\python.exe -m http.server 8765 --bind 127.0.0.1 --directory windows-host\tests\Jarvis.UI.Tests\TestPages
```

Restart Jarvis, open `http://127.0.0.1:8765/` in its browser, and ask it to
open the **Mobiles** link. Approve both the disclosure and the separate
new-tab action. The expected destination displays **Mobiles test
destination**; inspect both the tab and the verification message. Stop the
server with Ctrl+C. This is a manual test procedure, not a reported pass of
the end-to-end workflow.

```powershell
dotnet test windows-host\tests\Jarvis.UI.Tests\Jarvis.UI.Tests.csproj --no-restore --disable-build-servers -m:1
dotnet build windows-host\src\Jarvis.UI\Jarvis.UI.csproj --configuration Release --no-restore --disable-build-servers -m:1 -p:OutDir="$env:TEMP\jarvis-retry-release\"
```

## Stable link and new-tab follow-up (2026-11-11)

The browser chat now captures a local, bounded CDP binding between disclosed
main-frame elements and backend node identities before planning. After the
separate link approval it creates a new tab, revalidates the original node,
document, label, destination, visibility and enabled state, consumes the
signed new-tab authorization, and navigates the destination. A tab switch,
navigation, cancellation, changed node or inconsistent capture rejects the
action. The raw CDP document stays local. If link binding cannot be completed,
the chat reports that link actions are unavailable for that snapshot instead
of suppressing the search path.

The approval includes `mode: new_tab`; verification must use the destination
browser state and pre-action tab identities rather than an execution receipt.
The implementation does not add general form filling, arbitrary button
clicks, sign-in or transaction support. The remaining check-to-navigation
race, dynamic pages that cannot be sampled consistently, cross-frame links,
large CDP documents and redirects remain limitations. A failed operation may
leave an empty tab. Browser page scripts can interfere with the observation,
so it is not itself independent verification.

The full gate is not run because it includes owner-operated real-scheduler
tests; CI is not checked. For focused validation, run:

```powershell
dotnet test windows-host\tests\Jarvis.UI.Tests\Jarvis.UI.Tests.csproj --no-restore --disable-build-servers -m:1
dotnet test windows-host\tests\Jarvis.Safety.Tests\Jarvis.Safety.Tests.csproj --no-restore --disable-build-servers -m:1
dotnet test windows-host\tests\Jarvis.Verification.Tests\Jarvis.Verification.Tests.csproj --no-restore --disable-build-servers -m:1
dotnet build windows-host\src\Jarvis.UI\Jarvis.UI.csproj --configuration Release --no-restore --disable-build-servers -m:1 -p:OutDir="$env:TEMP\jarvis-link-release\"
```

UI tests: **132 passed, 0 failed, 0 skipped**; isolated Release build: **0
warnings, 0 errors**. The agent reported Safety **129 passed, 0 failed, 0
skipped** and Verification **133 passed, 0 failed, 2 skipped**. An isolated
loopback WebView2 smoke test passed for backend-node capture, an unrelated
mutation, node replacement, cancellation and a distinct new tab's destination
evidence. It did not exercise the full signed approval and local-Ollama
workflow, a real website, or user tab switching. To manually exercise the
complete workflow, restart Jarvis, load a page, ask it to open a visible link,
approve the exact page disclosure and then the separate new-tab action, and
check both the new tab and the reported verification outcome.

Changed or added components for this follow-up include `BrowserPageDisclosure`,
`CdpLinkNodeBinding`, `CdpLinkObserver`, `BrowserObservationScripts`,
`WebView2BrowserRuntime`, `BrowserWorkspace`, the browser evidence contracts,
`BrowserActionAuthority`, `AuthorizationService`, `BrowserOpenLinkVerifier`,
the tool registry, and their UI, Safety and Verification tests.

## Summary and decisions

The user approved local browser-page observation and chose explicit
confirmation before page content is disclosed to local Ollama. The decision
is recorded in [ADR-0015](../architecture/adr/ADR-0015-browser-observation-privacy.md).
The Python network boundary and existing Google HTTP transport are unchanged.

The browser core now has bounded page, frame, and element snapshot models.
The WebView2 adapter has an experimental local DOM capture path. The browser
workspace now exposes an **Inspect page** button and displays the captured
text, frames, interactive elements and known partial-capture warnings. Clear,
tab changes, navigation and leaving the workspace clear the displayed
snapshot; inspection is time-bounded and cancelled when it becomes obsolete.
It is not connected to the planner, browser tools, or Ollama. No page content
is transmitted or persisted by this work.

## Files changed or created

- Created `windows-host/src/Jarvis.Browser.Core/PageObservation.cs`.
- Created `windows-host/src/Jarvis.UI/Browser/BrowserObservationScripts.cs`.
- Created `windows-host/tests/Jarvis.UI.Tests/BrowserPageSnapshotTests.cs`.
- Changed `windows-host/src/Jarvis.Browser.Core/IBrowserTab.cs` and
  `windows-host/src/Jarvis.UI/Browser/WebView2BrowserRuntime.cs`.
- Changed `windows-host/src/Jarvis.UI/BrowserWorkspace.xaml` and
  `windows-host/src/Jarvis.UI/BrowserWorkspace.xaml.cs`; created
  `windows-host/src/Jarvis.UI/Browser/BrowserSnapshotFormatter.cs` and
  `windows-host/tests/Jarvis.UI.Tests/BrowserSnapshotFormatterTests.cs`.
- Updated `AGENTS.md`, `docs/DESIGN.md`, `docs/threat-model.md`,
  `docs/STATUS.md`, and ADR-0013; created ADR-0015 and this report.

## Validation

- WPF UI test project: **73 passed, 0 failed, 0 skipped**. This includes five
  `BrowserPageSnapshotTests` for scoped identifiers, distinct observation IDs,
  main-frame validation, bounds, and defensive copying.
- After connecting the UI: **75 passed, 0 failed, 0 skipped** in the WPF UI
  test project. The focused snapshot model and formatter tests subsequently
  passed **7, failed 0, skipped 0**.
- WPF UI Release build: **0 warnings, 0 errors**.
- `git diff --check` passed for tracked changes.
- No live WebView2 observation, CDP capability, page navigation, or Ollama
  disclosure was tested. The tests cover the snapshot mapper, not the
  browser adapter or script.
- `scripts/check-all.ps1` was not run: it runs real scheduler tests reserved
  for the owner. CI: not checked.

To try the snapshot manually, start Jarvis, select **Browser**, open an
HTTP/HTTPS page, wait for it to load, and select **Inspect page** in the
right-hand panel. The panel displays locally captured content, which can
include private information. **Clear** removes it from the view. A snapshot is
point-in-time and can become stale as the page changes; use **Inspect page**
again. This is not a browser chat and does not send content to Ollama.

Reproduce from the repository root:

```powershell
dotnet test windows-host\tests\Jarvis.UI.Tests\Jarvis.UI.Tests.csproj --no-restore --disable-build-servers -m:1
dotnet build windows-host\src\Jarvis.UI\Jarvis.UI.csproj --configuration Release --no-restore --disable-build-servers -m:1
git diff --check
.\scripts\check-all.ps1
```

## Known limitations and remaining work

- This is **not a completed observation phase**. Element IDs describe snapshot
  entries only: live element resolution and reliable stale-reference rejection
  have not been implemented. They must not be used for browser actions.
- The capture script executes in the page's JavaScript context; a hostile page
  can interfere with its data and document-generation marker. DOM changes,
  navigation races, frame coverage, accessibility names and cancellation need
  real-runtime testing and hardening. The observer is not independent
  verification.
- Frame inspection can be partial; cross-origin and nested-frame coverage has
  not been established. A bounded snapshot may omit content and reports
  truncation or known frame failures.
- Snapshot data can contain private text and URL components; it is not safe to
  send to Ollama. The per-disclosure approval UI, secret-exclusion controls,
  browser tools, authorization, Browser Agent and Browser Chat do not exist.
- The browser-agent specification's later phases must not begin until the
  observation and safe element-resolution gaps are addressed. No exception
  from the existing authorization or independent-verification requirements
  was made.

## Browser routing follow-up (2026-11-11)

The owner reported a three-minute wait opening `amazon.in`. The explicit URL
or hostname is now normalized by the existing address policy and opened
directly without an AI-server session; an invalid explicit address is
rejected. Ambiguous names still use the existing Ollama resolver and may
remain slow. Browser search does not replace a tab-creation failure with a
success status. See the amendment in [ADR-0014](../architecture/adr/ADR-0014-prompt-bar-browser-navigation.md).

The WPF UI test project passed **80 tests, 0 failed, 0 skipped**. A Release
build to an isolated output directory passed with **0 warnings and 0 errors**.
The normal Release output was locked by an already-running Jarvis.UI process;
it was not stopped. A separate WebView2 initialization diagnostic did not
complete in 30 seconds; actual WebView2 navigation remains unverified. No
live Amazon request was made. Full gate and CI were not run.

## Browser initialization follow-up

A controlled diagnostic reproduced the startup issue: WebView2 initialization
did not complete within eight seconds while its WPF control was unattached,
but completed after the same control was attached to a window. The production
runtime had the same ordering. It now attaches the control before waiting for
initialization and removes failed placeholders. The workspace serializes
creation, prevents closing an initializing tab, applies a 30-second creation
timeout, and avoids a late initial blank tab replacing a requested tab.

An isolated production-runtime diagnostic created an attached tab, loaded a
loopback page and captured its DOM. A second diagnostic using the actual
workspace loaded the loopback page and displayed the expected text and button
in the Inspect page panel. These are local-only checks, not Amazon or
cross-origin/frame coverage tests. The original reported session was not
traced, so the exact cause of that particular stall remains unconfirmed. A
point-in-time machine sample showed 778,556 KB free physical memory out of
16,067,364 KB and 91% CPU load; this may affect responsiveness but does not
prove the cause.

After the final change, the WPF UI suite passed **80, failed 0, skipped 0**;
Debug and isolated-output Release builds succeeded with **0 warnings and 0
errors**. `git diff --check` passed for tracked changes. The diagnostics were
temporary and used a separate profile. No live Amazon navigation was tested.
`scripts/check-all.ps1` was not run because it includes owner-operated real
scheduler tests. CI: not checked.

## Website-search preparation (incomplete)

The owner selected a first scope of website search with confirmation before
each page-content disclosure, approval for every browser action, and
independent verification before reporting completion. The first privacy
foundation is `BrowserSearchContext`: it projects an active, loaded page
snapshot into at most 20 main-frame search-field indices without copying
page-controlled strings, URLs or values. Search-field classification is
heuristic and can be fooled; an index is neither a live target nor authority
to act. No model request, disclosure confirmation, browser chat, action
executor, authorization or browser verifier is wired to this projection.
The existing Inspect page panel is unchanged.

Created `windows-host/src/Jarvis.Browser.Core/BrowserSearchContext.cs` and
`windows-host/tests/Jarvis.UI.Tests/BrowserSearchContextTests.cs`; updated
ADR-0015 and `docs/STATUS.md`. The focused tests passed **4, failed 0,
skipped 0**; the complete WPF UI test project passed **84, failed 0, skipped
0**. The isolated Release build succeeded with **0 warnings and 0 errors**.
A normal Debug test build could not copy the browser core DLL because the
running Jarvis UI held it open; it was not stopped. The commands below use
an isolated output directory instead:

```powershell
dotnet test windows-host\tests\Jarvis.UI.Tests\Jarvis.UI.Tests.csproj --no-restore --disable-build-servers -m:1 -p:OutDir="$env:TEMP\jarvis-browser-context-tests\"
dotnet build windows-host\src\Jarvis.UI\Jarvis.UI.csproj --configuration Release --no-restore --disable-build-servers -m:1 -p:OutDir="$env:TEMP\jarvis-browser-context-release\"
```

`scripts/check-all.ps1` was not run because it includes owner-operated real
scheduler tests. CI: not checked. Website search automation remains blocked
on safe live element resolution, confirmation and signed authorization,
independent browser evidence, model integration and the browser chat UI.

## CDP search observation follow-up (incomplete)

Added an internal read-only WebView2 CDP search observer. It enumerates up to
200 main-frame inputs and returns up to 20 candidate indices plus a truncation
indicator; CDP backend node IDs and attributes remain local. Fresh enumeration
checks the candidate's backend node identity and search-field classification.
Navigation or disposal invalidates an observation. It is not connected to
chat, Ollama or a browser action, and a matching observation is not permission
to act. It is not independent verification.

Changed `windows-host/src/Jarvis.UI/Browser/WebView2BrowserRuntime.cs` and
`windows-host/src/Jarvis.UI/Jarvis.UI.csproj`; created
`windows-host/src/Jarvis.UI/Browser/CdpSearchObserver.cs` and
`windows-host/tests/Jarvis.UI.Tests/CdpSearchObserverTests.cs`; updated
ADR-0015 and `docs/STATUS.md`.

The WPF UI test suite passed **92, failed 0, skipped 0**, including eight
mocked CDP observer tests for navigation, replacement, removal, invalidation,
disabled fields, cancellation and output privacy. A separate actual WebView2
diagnostic loaded a local loopback page, found and revalidated its search
input, and rejected the same observation after replacing the input. The
isolated Release build passed with **0 warnings and 0 errors**. No live YouTube
or other external website was tested. `scripts/check-all.ps1` was not run
because it includes owner-operated real scheduler tests; CI: not checked.

This does not complete the task. The heuristic is page-controlled, cannot
guarantee the input is visible or safe, and does not cover child frames or
shadow roots. A target can change between the check and execution. There is
no binding from the existing snapshot to CDP candidates, no action executor,
signed browser authorization, independent browser verifier, disclosure
approval, model integration or browser chat. The Inspect page UI remains
unchanged. Before implementing browser actions, these gaps and the
observation-to-action race must be addressed without treating tool output or
browser observations as verification evidence.

## Observation invalidation hardening (incomplete)

Tests reproduced three bugs: explicit invalidation during a CDP capture could
be overwritten by the capture, invalidation during revalidation could still
produce a current result, and a CDP failure during revalidation left the old
observation usable. The observer now synchronizes state changes and checks a
revision before returning an in-flight result; an attempted revalidation that
fails or is cancelled invalidates its observation. Selecting another browser
tab or unloading the workspace invalidates affected observations. Explicit
`hidden` and `aria-hidden="true"` input attributes are excluded. CSS-hidden
inputs, hidden ancestors, occlusion and the check-to-action race are still not
resolved.

Changed `windows-host/src/Jarvis.UI/Browser/CdpSearchObserver.cs`,
`windows-host/src/Jarvis.UI/Browser/WebView2BrowserRuntime.cs`,
`windows-host/src/Jarvis.UI/BrowserWorkspace.xaml.cs`, and
`windows-host/tests/Jarvis.UI.Tests/CdpSearchObserverTests.cs`; updated
ADR-0015 and `docs/STATUS.md`. The new race tests failed before the fix and
passed afterwards. The WPF UI test suite passed **96, failed 0, skipped 0**;
an isolated Release build passed with **0 warnings and 0 errors**. The new
race and attribute checks used mocked CDP; tab-switch invalidation has not
been verified in a live WebView2 workspace. No external website was tested.
`scripts/check-all.ps1` was not run because it includes owner-operated real
scheduler tests; CI: not checked.

Reproduce without affecting a running UI:

```powershell
dotnet test windows-host\tests\Jarvis.UI.Tests\Jarvis.UI.Tests.csproj --no-restore --disable-build-servers -m:1 -p:OutDir="$env:TEMP\jarvis-observation-tests\"
dotnet build windows-host\src\Jarvis.UI\Jarvis.UI.csproj --configuration Release --no-restore --disable-build-servers -m:1 -p:OutDir="$env:TEMP\jarvis-observation-release\"
```

Browser chat, model disclosure, registered signed actions and independent
verification remain unimplemented; these changes do not make automated
website search available.

## CDP layout filtering (incomplete)

The internal search observer now uses CDP `DOMSnapshot.captureSnapshot` to
filter candidate inputs by computed visibility, non-zero opacity and
positive-size layout bounds; it also rejects element ancestors that are
transparent or lack layout data. The raw snapshot can contain private page
data, is used only locally, is not logged or sent to the model, and is
rejected for parsing if its serialized size exceeds 12 million characters
(the WebView2 call has already received the response at that point).

Created `windows-host/src/Jarvis.UI/Browser/CdpLayoutVisibility.cs` and
`windows-host/tests/Jarvis.UI.Tests/CdpLayoutVisibilityTests.cs`; updated
`windows-host/src/Jarvis.UI/Browser/CdpSearchObserver.cs`,
`windows-host/tests/Jarvis.UI.Tests/CdpSearchObserverTests.cs`, ADR-0015 and
`docs/STATUS.md`. The visibility tests initially failed for six hidden/empty
layout cases and a transparent ancestor; they passed after implementation.
The full UI test suite passed **105, failed 0, skipped 0**. An isolated Release
UI build succeeded with **0 warnings and 0 errors**. A real WebView2 test
using a loopback-only synthetic page found the visible field, excluded
`display:none` and a transparent ancestor, and rejected the field after it
became hidden. No external website was tested. Full `scripts/check-all.ps1`
was not run because it includes owner-operated real scheduler tests. CI: not
checked.

This filter is conservative but is not a guarantee of actionable visibility:
viewport intersection, clipping and occlusion are not checked. Shadow roots
and child frames are not supported by the search enumeration. CDP snapshots
can change during collection, and a field may change after revalidation.
Browser chat, model disclosure approval, signed browser execution and
independent verification remain unimplemented. These limitations keep the
requested eight-step browser-chat task incomplete.

## Safe page disclosure and open-link execution (runtime verification incomplete)

This follow-up supersedes earlier statements in this report that disclosure,
browser chat, signed link execution, and independent link verification were
unimplemented. [ADR-0017](../architecture/adr/ADR-0017-safe-page-disclosure-and-link-actions.md)
records the safety-policy change made before implementation. The scope is
limited to opening links; forms, purchases, bookings, downloads, and other
page actions remain prohibited.

The browser workspace now creates a bounded, filtered page projection with
temporary link IDs, displays the exact projection for explicit disclosure
approval, and sends only the approved typed page context and user request to
the local planner. The planner can return a validated `open_link` proposal or
clarification; malformed or unknown link IDs are rejected. The `browser.open_link`
registered operation requires a separate approval and single-use signed
authorization bound to the selected tab, observation, temporary link ID, host,
and one-way target fingerprint. The URL is not included in the planner request
or authorization arguments. Immediately before navigation, the browser
adapter revalidates the same page and target. A verifier in
`Jarvis.Verification` checks the resulting browser-engine address against the
authorized target; UI/composition code handles verifier results opaquely.
Disclosure cancellation is linked to the active chat action. Search is also
bound to the page whose snapshot the user approved.

Files added or changed for this follow-up include:

- `docs/architecture/adr/ADR-0017-safe-page-disclosure-and-link-actions.md`,
  `docs/DESIGN.md`, and `docs/threat-model.md`.
- `windows-host/src/Jarvis.Browser.Core/BrowserPageDisclosure.cs`,
  `windows-host/src/Jarvis.Verification/BrowserOpenLinkVerifier.cs`,
  `windows-host/src/Jarvis.Composition/BrowserActionAuthority.cs`, and the
  corresponding contracts, API, composition, safety, and UI integration.
- `windows-host/src/Jarvis.UI/BrowserWorkspace.xaml`,
  `windows-host/src/Jarvis.UI/BrowserWorkspace.xaml.cs`, and the browser CDP
  adapter and snapshot projection/formatting code.
- `ai-server/src/jarvis_ai/schemas/models.py`,
  `ai-server/src/jarvis_ai/api/routes_browser.py`, and
  `config/tools.registry.json`.
- Focused UI, safety, API-client, and verification tests.

Validation on 2026-11-11:

- `Jarvis.UI.Tests`: **111 passed, 0 failed, 0 skipped**.
- Release build of `Jarvis.UI`: **0 warnings, 0 errors**.
- `Jarvis.Architecture.Tests`: **10 passed, 0 failed, 0 skipped**.
- Filtered `Jarvis.Safety.Tests` (`SafetyGateTests`): **22 passed, 0 failed,
  0 skipped**.
- Filtered `Jarvis.Verification.Tests` (`BrowserOpenLinkVerifierTests`):
  **4 passed, 0 failed, 0 skipped**.
- Filtered `Jarvis.E2E.Tests` (`ApiClientTests`): **13 passed, 0 failed,
  0 skipped**.
- `ai-server/tests/unit/test_browser_routes.py`: **13 passed**; Ruff and mypy
  passed for the changed planner files.
- `git diff --check` passed.

Reproduce the focused validation from the repository root:

```powershell
dotnet test windows-host\tests\Jarvis.UI.Tests\Jarvis.UI.Tests.csproj --no-restore --disable-build-servers -m:1
dotnet build windows-host\src\Jarvis.UI\Jarvis.UI.csproj --configuration Release --no-restore --disable-build-servers -m:1
dotnet test windows-host\tests\Jarvis.Architecture.Tests\Jarvis.Architecture.Tests.csproj --no-restore --disable-build-servers -m:1
dotnet test windows-host\tests\Jarvis.Safety.Tests\Jarvis.Safety.Tests.csproj --no-restore --disable-build-servers -m:1 --filter "FullyQualifiedName~SafetyGateTests"
dotnet test windows-host\tests\Jarvis.Verification.Tests\Jarvis.Verification.Tests.csproj --no-restore --disable-build-servers -m:1 --filter "FullyQualifiedName~BrowserOpenLinkVerifierTests"
dotnet test windows-host\tests\Jarvis.E2E.Tests\Jarvis.E2E.Tests.csproj --no-restore --disable-build-servers -m:1 --filter "FullyQualifiedName~ApiClientTests"
ai-server\.venv\Scripts\python.exe -m pytest ai-server\tests\unit\test_browser_routes.py -q
ai-server\.venv\Scripts\ruff.exe check ai-server\src\jarvis_ai\api\routes_browser.py ai-server\src\jarvis_ai\schemas\models.py ai-server\tests\unit\test_browser_routes.py
ai-server\.venv\Scripts\mypy.exe ai-server\src\jarvis_ai\api\routes_browser.py ai-server\src\jarvis_ai\schemas\models.py
git diff --check
```

The full `scripts/check-all.ps1` gate was not run; its real-scheduler tests
are reserved for the owner's session and the repository workflow limits
commands to five minutes. CI was not checked. The link opening, signed
approval, and post-action verification path has not been exercised in a live
WebView2 session against a controlled local page or an external site. The
page-text and credential filtering is conservative and heuristic; the
mandatory exact-preview consent remains necessary, and unusual secrets in
visible content may not be detected. The earlier search-input visibility,
frame, and check-to-action limitations in this report still apply to search.

## Partial-observation disclosure follow-up

The user reported that page-aware requests were blocked with “The page
observation is incomplete or no longer active.” The guard conflated bounded
snapshots and unavailable child frames with an inactive/stale main page, so
ordinary large pages could not reach the user-approved planner flow. The
disclosure path now permits active, loaded partial observations while adding
explicit preview warnings for truncation and unavailable frames. Inactive and
loading pages still fail closed, and suspicious credential-like content is
still blocked. Freshness checks now distinguish partial coverage from stale
main-document state; link execution still requires the exact visible,
enabled target to be revalidated in the main frame before navigation.
ADR-0017 records this clarification.

Added regression coverage for truncated snapshots, unavailable child frames,
inactive/loading pages, and current-link validation on a partial capture.
`Jarvis.UI.Tests`: **115 passed, 0 failed, 0 skipped** using an isolated output
directory; isolated Release UI build: **0 warnings, 0 errors**. The first
normal-output test attempt could not copy a DLL because the running Jarvis.UI
process held it open; that process was not stopped. The new build has not been
launched into the existing WebView2 profile, and the reported Amazon flow has
not yet been repeated. Full gate and CI: not checked.

Reproduce without overwriting binaries used by a running UI:

```powershell
dotnet test windows-host\tests\Jarvis.UI.Tests\Jarvis.UI.Tests.csproj --no-restore --disable-build-servers -m:1 -p:OutDir="$env:TEMP\jarvis-partial-disclosure-tests\"
dotnet build windows-host\src\Jarvis.UI\Jarvis.UI.csproj --configuration Release --no-restore --disable-build-servers -m:1 -p:OutDir="$env:TEMP\jarvis-partial-disclosure-release\"
```

## Empty link label planner rejection follow-up

The user supplied a browser-chat transcript in which the planner endpoint
returned HTTP 422 before any action could be proposed. The captured projection
included several visible links with empty labels, but the AI server requires
each link label to contain at least one character. The host now omits empty
and whitespace-only link labels from both the preview and actionable link
list. It also reapplies the 500-character title and label limits after
redaction, since replacing a short URL with a redaction marker can otherwise
exceed the API limit. Link revalidation compares both the sanitized label and
a host-only fingerprint of the original label, so changes masked by redaction
are still rejected. Existing privacy filtering, consent, target
validation, signed authorization and independent verification remain in
place. Unlabelled links cannot be selected by the planner.

The new tests reproduced the empty-label and redaction-length problems and
the need to reject raw-label changes hidden by redaction. After the fix
`Jarvis.UI.Tests` passed **118, failed 0,
skipped 0** using an isolated output directory; the isolated Release UI build
passed with **0 warnings and 0 errors** on a sequential rerun after concurrent
builds interfered with a WPF intermediate file. The browser planner Python tests
passed **13**, with two dependency deprecation warnings. Neither the
reported Amazon scenario nor a live browser action was retested. The full
gate was not run and CI was not checked.

```powershell
dotnet test windows-host\tests\Jarvis.UI.Tests\Jarvis.UI.Tests.csproj --no-restore --disable-build-servers -m:1 -p:OutDir="$env:TEMP\jarvis-label-fix-tests\"
dotnet build windows-host\src\Jarvis.UI\Jarvis.UI.csproj --configuration Release --no-restore --disable-build-servers -m:1 -p:OutDir="$env:TEMP\jarvis-label-fix-release\"
ai-server\.venv\Scripts\python.exe -m pytest ai-server\tests\unit\test_browser_routes.py -q
```

## Link freshness diagnosis follow-up

The owner supplied a transcript in which the planner proposed the requested
Mobiles link once and unrelated links for two other requests. After separate
approval, all three actions failed at the generic link freshness check. The
transcript does not establish whether the document version, selected tab,
element presence, visibility, label or target caused those failures. The
planner receives the approved title, visible text, and temporary link IDs and
labels, not link destination URLs. Membership validation does not establish
that a model-selected link matches the user's intent.

The existing JavaScript observer increments a page-wide version on DOM
mutations, and the link freshness check rejects any version change. Unrelated
updates on a dynamic site are therefore a plausible cause of rejection, not a
proven diagnosis for the supplied session. The owner approved investigating a
target-specific freshness policy with the same live link identity, document,
destination, label and visibility revalidated. That policy is **not
implemented**: the snapshot's element IDs are list positions, not stable
browser-engine node identities. Ignoring the DOM version and comparing those
positions could authorize a replaced or shifted link. A safe implementation
needs a browser-engine identity binding captured with the approved snapshot,
race and replacement tests, and controlled live WebView2 validation.

Added privacy-preserving freshness failure categories and user-facing
messages distinguishing page changes, unknown links, missing elements,
unavailable elements and changed target/label. No freshness requirement was
relaxed. Focused new tests first failed to build because the categories did not
exist; the full UI suite then passed **120, failed 0, skipped 0**. An initial
test build failed due to a missing generated WPF intermediate file; its
immediate rerun succeeded. An isolated Release build succeeded with
**0 warnings, 0 errors**. The reported Amazon flow was not rerun and the
unrelated planner proposals are not fixed. Full gate and CI: not checked.

```powershell
dotnet test windows-host\tests\Jarvis.UI.Tests\Jarvis.UI.Tests.csproj --no-restore --disable-build-servers -m:1 -p:OutDir="$env:TEMP\jarvis-freshness-tests\"
dotnet build windows-host\src\Jarvis.UI\Jarvis.UI.csproj --configuration Release --no-restore --disable-build-servers -m:1 -p:OutDir="$env:TEMP\jarvis-freshness-release\"
```

## Browser automation scope and clarification follow-up

The owner's new transcript shows that the planner selected “Mobiles” and
the signed action was rejected as “The page or its document changed while
awaiting approval.” This identifies the failing validation category, but
does not distinguish DOM mutations from other document/tab/loading changes.
The same transcript shows the generic capability response for unrelated
clarification requests. The planner endpoint replaced every validated model
clarification with that generic message; it now includes the bounded model
clarification as well as the unchanged capability notice. A regression test
reproduced the omission and passes after the fix. This does not make
unregistered actions available.

The owner requested targeted node revalidation, general non-secret form
filling, button clicks, and separately approved non-transactional submissions.
The agreed exclusions are passwords, one-time codes, payment details,
authentication secrets, sign-ins, purchases, payments, bookings, and
destructive actions. ADR-0017 records that requested scope. The current
page-wide freshness rule and form/button restrictions remain in force:
implementation of browser-engine node identity binding, safe effect
classification, registered operations, authorization and independent
verification is still required. Page-provided labels cannot reliably prove
that a submission is non-transactional, so unknown effects must fail closed.

The browser planner unit tests passed **15**, with two dependency
deprecation warnings; Ruff, mypy, and Pylance syntax checks passed for the
changed planner file. No live model or WebView2 action was tested. Full
`scripts/check-all.ps1` and CI: not checked.

```powershell
ai-server\.venv\Scripts\python.exe -m pytest ai-server\tests\unit\test_browser_routes.py -q
ai-server\.venv\Scripts\ruff.exe check ai-server\src\jarvis_ai\api\routes_browser.py ai-server\tests\unit\test_browser_routes.py
ai-server\.venv\Scripts\mypy.exe ai-server\src\jarvis_ai\api\routes_browser.py
```

## Stable-node binding groundwork (incomplete)

The owner requested completion of stable link revalidation and opening
approved links in a new tab by default. ADR-0017 records that direction.
Added `CdpLinkNodeBinding`, a host-only binding primitive that requires two
identical node lists and matching document versions at capture and resolves
the bound node in a later list. It rejects missing, duplicated, replaced and
cross-document node identities. Its tests use supplied integer node IDs;
they do not exercise CDP or prove a real snapshot-to-node association.

The primitive is **not wired into the browser**. The production observer still
uses the page-wide mutation check and the action still navigates the original
tab. To complete the requested behavior, the adapter must collect actual CDP
backend-node identities while proving their correspondence to the approved
snapshot, revalidate the same live target, implement a signed new-tab action
without cancelling itself, verify the destination tab, and test cancellation,
replacement and navigation races in WebView2. No security check was removed.

The full UI test suite passed **124, failed 0, skipped 0** using isolated
output; an isolated Release UI build passed with **0 warnings and 0 errors**.
No live browser action, full gate, or CI check was performed.

```powershell
dotnet test windows-host\tests\Jarvis.UI.Tests\Jarvis.UI.Tests.csproj --no-restore --disable-build-servers -m:1 -p:OutDir="$env:TEMP\jarvis-node-binding-tests\"
dotnet build windows-host\src\Jarvis.UI\Jarvis.UI.csproj --configuration Release --no-restore --disable-build-servers -m:1 -p:OutDir="$env:TEMP\jarvis-node-binding-release\"
```
