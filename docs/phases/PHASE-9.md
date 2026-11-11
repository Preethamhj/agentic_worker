# Phase 9: Browser runtime abstraction

## Summary

Implemented the browser-runtime abstraction and connected the existing WPF
browser workspace to its WebView2 adapter. The engine-neutral core owns tab
identity and observable navigation state; WPF still applies the existing
user-operated navigation, popup, download, and permission policies. This phase
does not add page observation, model-directed browser actions, or automation.

## Files changed or created

Created:

- `windows-host/src/Jarvis.Browser.Core/Jarvis.Browser.Core.csproj`
- `windows-host/src/Jarvis.Browser.Core/BrowserTabId.cs`
- `windows-host/src/Jarvis.Browser.Core/BrowserTabState.cs`
- `windows-host/src/Jarvis.Browser.Core/BrowserTabStateTracker.cs`
- `windows-host/src/Jarvis.Browser.Core/IBrowserRuntime.cs`
- `windows-host/src/Jarvis.Browser.Core/IBrowserTab.cs`
- `windows-host/src/Jarvis.UI/Browser/WebView2BrowserRuntime.cs`
- `windows-host/tests/Jarvis.UI.Tests/BrowserTabStateTrackerTests.cs`

Changed:

- `windows-host/Jarvis.sln`
- `windows-host/src/Jarvis.UI/Jarvis.UI.csproj`
- `windows-host/src/Jarvis.UI/BrowserWorkspace.xaml.cs`
- `windows-host/tests/Jarvis.UI.Tests/Jarvis.UI.Tests.csproj`
- `docs/STATUS.md`

## Validation

- Focused browser state tests: **3 passed, 0 failed, 0 skipped**.
- Complete WPF UI test project: **68 passed, 0 failed, 0 skipped**.
- Release WPF UI build: **0 warnings, 0 errors**.
- No live WebView2 navigation was performed; native runtime behavior remains
  unverified.
- `scripts/check-all.ps1` was not run because it includes real scheduler tests,
  which are reserved for the owner's desktop session.

Reproduction commands from the repository root:

```powershell
dotnet test windows-host\tests\Jarvis.UI.Tests\Jarvis.UI.Tests.csproj --no-restore --disable-build-servers -m:1 --filter FullyQualifiedName~BrowserTabStateTrackerTests
dotnet test windows-host\tests\Jarvis.UI.Tests\Jarvis.UI.Tests.csproj --no-restore --disable-build-servers -m:1
dotnet build windows-host\src\Jarvis.UI\Jarvis.UI.csproj --configuration Release --no-restore --disable-build-servers -m:1
```

## Decisions and limitations

- `Jarvis.Browser.Core` exposes engine-neutral runtime and tab contracts;
  WebView2 types are confined to the WPF adapter.
- The workspace uses the runtime for tab creation, activation, navigation,
  history, refresh, and disposal while retaining its existing WebView2 security
  event handling.
- Tab state is sourced from WebView2 navigation, title, and history events.
- DOM observation, browser tools, model-driven navigation/actions, Browser Chat,
  persistence, and agent safety are not implemented in this phase.
- No deviations from the phase scope were needed.
