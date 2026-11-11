# Phase 8: Prompt-bar internet search

## Summary

The main prompt now recognizes explicit internet/browser search requests,
including “find ... on the internet,” “browser search for ...,” and “Google
search for ...”. It sends the query text to an authenticated loopback AI
endpoint, where local Ollama refines it into a concise search query. If the
model is unavailable or cannot return valid structured output, the original
query is used. WPF URL-encodes the query into a Google results-page address
and opens it in the dedicated WebView2 browser.

The user confirmed no Google Search API credentials are currently available.
Therefore Google results remain inside WebView2 for the user to choose;
Jarvis does not fetch results, ask Ollama to rank result links, or display
result lists itself. This does not add arbitrary HTTP networking to C# or
Python. Search API retrieval/ranking requires a separate credentialed,
reviewed follow-up. See [ADR-0014](../architecture/adr/ADR-0014-prompt-bar-browser-navigation.md).

## Files changed or created

- Updated `AGENTS.md`, `docs/DESIGN.md`, and `docs/threat-model.md` with the
  search-query and browser-only result boundary.
- Updated ADR-0013 and ADR-0014 to document WebView2 search navigation,
  local query refinement, and the lack of automated result ranking.
- Updated `docs/STATUS.md` and added this phase report.
- Extended `ai-server/src/jarvis_ai/api/routes_browser.py` with an
  authenticated structured query-refinement route and a fallback to the
  original query; added the response schema.
- Added a browser-search DTO and authenticated loopback client call to
  `Jarvis.Contracts` and `Jarvis.Infrastructure`; extended the session
  contract/composition.
- Added internet-search intent parsing, a Google URL builder, and WPF
  prompt-to-browser routing.
- Added Python endpoint, C# API-client, prompt parser, and view-model tests.

## Validation

- AI-server unit tests: **76 passed** (five browser route cases, mocked Ollama).
- Ruff passed for the changed AI server route/schema/tests.
- Mypy passed for all **30** AI-server source files.
- WPF UI tests: **65 passed, 0 failed, 0 skipped**.
- Authenticated local search API-client test: **1 passed**.
- No live Ollama call, Google search, or WebView2 navigation was performed.
- `scripts/check-all.ps1` and CI were not run.

Reproduction commands from the repository root:

```powershell
Push-Location ai-server
.\.venv\Scripts\pytest.exe tests\unit
.\.venv\Scripts\ruff.exe check src\jarvis_ai\api\routes_browser.py src\jarvis_ai\schemas\models.py tests\unit\test_browser_routes.py
.\.venv\Scripts\mypy.exe src
Pop-Location
$out = Join-Path $env:TEMP 'jarvis-browser-search-tests'
dotnet test windows-host\tests\Jarvis.UI.Tests\Jarvis.UI.Tests.csproj --configuration Release --no-restore --disable-build-servers -m:1 -p:OutDir=$out
dotnet test windows-host\tests\Jarvis.E2E.Tests\Jarvis.E2E.Tests.csproj --configuration Release --no-restore --disable-build-servers -m:1 -p:OutDir=$out --filter FullyQualifiedName~ApiClient_Prepares_Browser_Search
git diff --check
```

## Decisions and limitations

- Internet search intent must include a web/internet/browser indicator, so
  local file and Gmail searches remain on their existing paths.
- Ollama refines the query only. The Google search itself runs within the
  user-operated WebView2 profile; result page contents are not read by Jarvis.
- Since no Search API key or search-engine ID is configured, Ollama cannot
  choose among actual Google result links. The browser presents the results
  for the user to select.
- Python remains loopback/Ollama-only; Google API endpoint policy and signed
  authorization/verification architecture were not changed.
