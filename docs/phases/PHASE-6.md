# Phase 6: WPF/WebView2 Browser Workspace

## Summary

Added a user-operated browser workspace to the existing WPF application. It supports manually entered HTTP/HTTPS addresses, multiple tabs, back/forward/refresh, and popups opened as new Jarvis tabs. The browser uses a dedicated `%LOCALAPPDATA%\Jarvis\Browser\Profile` WebView2 profile. Address entry rejects unsupported schemes and embedded credentials. Downloads and site permission requests are denied until separately implemented.

The network boundary is documented in [ADR-0013](../architecture/adr/ADR-0013-webview-browser-network-boundary.md): broad web browsing is an explicit exception for this dedicated, user-operated WebView2 surface only. Python remains loopback/Ollama-only, and C# API clients remain under the existing controlled Google transport. WebView2's page and subresource traffic is not restricted by `GoogleApiClientFactory`.

## Files changed or created

- Updated `AGENTS.md`, `docs/DESIGN.md`, and `docs/threat-model.md` with the approved browser network boundary.
- Updated `docs/STATUS.md` with phase completion and verification results.
- Added `docs/architecture/adr/ADR-0013-webview-browser-network-boundary.md`.
- Added `windows-host/src/Jarvis.UI/BrowserAddressPolicy.cs`.
- Added `windows-host/src/Jarvis.UI/BrowserWorkspace.xaml` and `BrowserWorkspace.xaml.cs`.
- Updated `windows-host/src/Jarvis.UI/Jarvis.UI.csproj` to reference the pinned WebView2 SDK and `windows-host/src/Jarvis.UI/MainWindow.xaml` to expose the workspace.
- Added `windows-host/tests/Jarvis.UI.Tests/BrowserAddressPolicyTests.cs`.

## Validation

- `Jarvis.UI.Tests`: **45 passed, 0 failed, 0 skipped**.
- Release build of `Jarvis.UI`: succeeded with **0 warnings and 0 errors**.
- `git diff --check`: passed.
- The new address-policy test methods are `TryNormalize_Accepts_Web_Addresses` and `TryNormalize_Rejects_Non_Web_Or_Credentialed_Addresses` (nine data cases total).
- `scripts/check-all.ps1` and CI were not run. No live website was opened; actual WebView2 navigation and profile behavior remain unverified.

Reproduction commands from the repository root:

```powershell
$out = Join-Path $env:TEMP 'jarvis-browser-validation'
dotnet test windows-host\tests\Jarvis.UI.Tests\Jarvis.UI.Tests.csproj --configuration Release --no-restore --disable-build-servers -m:1 -p:OutDir=$out
dotnet build windows-host\src\Jarvis.UI\Jarvis.UI.csproj --configuration Release --no-restore --disable-build-servers -m:1 -p:OutDir=$out
git diff --check
```

## Decisions and deviations

- The user explicitly approved arbitrary HTTP/HTTPS browsing inside the dedicated WebView2 profile. This is intentionally separate from C# API clients and Python network access; it does not add Google hosts to the Python network guard or broaden the controlled Google HTTP transport.
- Navigation accepts only HTTP and HTTPS and rejects embedded credentials. Downloads and site permission requests default to denial. New-window requests are handled inside Jarvis tabs.
- The phase implements the browser workspace only. Browser Chat, DOM/accessibility observation, automation, dynamic tools, context persistence, voice, managed downloads/permissions, and an agent pipeline are outside this phase.
- The full check script was not run; its real-scheduler behavior is outside the browser phase's focused validation. No verification architecture, frozen evaluation protocol, or signed-authorization behavior was changed.

## Known limitations and prerequisites for the next phase

- No live browser navigation was performed. The locally found WebView2 Evergreen Runtime does not by itself verify that the WPF browser starts or that a profile persists across runs.
- The browser is manual-only; no page content is read by Jarvis and no browser actions are performed by an LLM.
- Before adding automation or page inspection, define and validate the separate authorization, verification, origin, and content-isolation controls required for that phase.
