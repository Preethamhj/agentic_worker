# Phase 7: Prompt-bar browser navigation

## Summary

Connected the main prompt box to the Browser tab for explicit `in browser
open <site>` or `open <site> in browser` requests. The target name is sent
through the authenticated loopback AI API to the configured Ollama model.
The model must return a schema-validated clarification or a credential-free
HTTP/HTTPS URL. The WPF address policy validates the URL again before opening
it in a new WebView2 tab. Existing mail and Jarvis task prompts retain their
previous routes.

This feature only opens a destination. It does not launch native applications,
inspect page content, click controls, run scripts, or add browser-agent
behavior. See [ADR-0014](../architecture/adr/ADR-0014-prompt-bar-browser-navigation.md).

## Files changed or created

- Updated `AGENTS.md`, `docs/DESIGN.md`, and `docs/threat-model.md` with the
  narrow prompt-directed navigation boundary.
- Updated `docs/architecture/adr/ADR-0013-webview-browser-network-boundary.md`
  to distinguish this narrow navigation from browser automation.
- Added ADR-0014 and this phase report.
- Added `ai-server/src/jarvis_ai/api/routes_browser.py`; registered the
  authenticated route in `ai-server/src/jarvis_ai/api/main.py` and added
  request/response validation in `schemas/models.py`.
- Added browser-resolution DTOs and a local authenticated request in
  `windows-host/src/Jarvis.Contracts/ContractModels.cs` and
  `windows-host/src/Jarvis.Infrastructure/ApiClient.cs`.
- Extended the session contract/composition to use the same local AI server.
- Added explicit prompt parsing, WPF view-model resolution, and new-tab
  navigation in `Jarvis.UI`; updated the prompt tooltip.
- Added Python endpoint, C# API-client, prompt parser, and view-model tests.

## Validation

- AI-server unit tests: **74 passed**, including **3** browser-resolution
  tests using mocked Ollama (no live network or Google request).
- WPF UI tests: **53 passed, 0 failed, 0 skipped**.
- Authenticated local API-client transport test: **1 passed**.
- Ruff passed for the new route, schema, and test files; mypy passed for
  `ai-server/src`.
- No live Ollama resolution or WebView2 navigation was performed.
- Full `scripts/check-all.ps1` and CI were not run.

Reproduction commands from the repository root:

```powershell
.\ai-server\.venv\Scripts\pytest.exe ai-server\tests\unit\test_browser_routes.py -q
.\ai-server\.venv\Scripts\ruff.exe check ai-server\src\jarvis_ai\api\routes_browser.py ai-server\src\jarvis_ai\schemas\models.py ai-server\tests\unit\test_browser_routes.py
Push-Location ai-server
.\.venv\Scripts\pytest.exe tests\unit
.\.venv\Scripts\mypy.exe src
Pop-Location
$out = Join-Path $env:TEMP 'jarvis-browser-open-tests'
dotnet test windows-host\tests\Jarvis.UI.Tests\Jarvis.UI.Tests.csproj --configuration Release --no-restore --disable-build-servers -m:1 -p:OutDir=$out
dotnet test windows-host\tests\Jarvis.E2E.Tests\Jarvis.E2E.Tests.csproj --configuration Release --no-restore --disable-build-servers -m:1 -p:OutDir=$out --filter FullyQualifiedName~ApiClient_Resolves_Browser_Target
git diff --check
```

## Decisions and limitations

- Only explicit browser-open phrasing is intercepted. Native desktop app
  requests do not launch anything; the model is instructed to clarify.
- Ollama maps the requested name to a URL without external lookup. This is a
  best-effort mapping; a wrong URL is possible and is visible in the address
  bar after navigation.
- The current user-operated browser remains permissive for HTTP/HTTPS as
  approved in ADR-0013. Python and C# API-client network policies are
  unchanged. Browser page contents are never sent to Ollama.
- No broader browser automation, DOM inspection, downloads, site permissions,
  or changes to the signed-authorization or verification architecture were
  made.
