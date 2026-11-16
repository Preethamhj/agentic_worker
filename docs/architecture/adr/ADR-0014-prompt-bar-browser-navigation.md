# ADR-0014: Prompt-bar browser navigation and web search

Status: Accepted (2026-11-10)

## Context

The WPF browser workspace previously accepted only manually entered
destinations. An explicit main-prompt request to open a website or search
the public internet should use WebView2 rather than being sent through the
general Jarvis task planner, which could route it into the unrelated OS tool
path. No Google Programmable Search API key or search-engine ID is configured.

## Decision

- Recognize explicit `in browser open ...` / `open ... in browser` requests
  and internet/web search requests. Other prompts retain the existing
  Gmail/task routing.
- For a named site, send only the extracted target to an authenticated
  endpoint on the local AI server. Python uses its existing loopback-only
  Ollama client and validates a strict structured response; the WPF applies
  `BrowserAddressPolicy` before opening a new tab.
- For an internet search, send the user's search request to the authenticated
  local AI server. Ollama returns only a concise search query; if it is
  unavailable or returns invalid data, the original query is used.
- C# constructs a URL for Google's public search page using URL-encoded query
  text. The search executes only when the user-operated WebView2 navigates to
  that page; Jarvis does not make a Google HTTP request or scrape result
  pages. The user chooses among the results in the browser.
- Neither flow runs Jarvis tools, launches native apps, inspects page content,
  clicks controls, or executes scripts. Page text/results are not sent back
  to Ollama. A user search instruction authorizes this top-level navigation.
- Continue to block non-HTTP(S) top-level navigation and deny downloads and
  site permissions. Python's network guard and C# API-client network policy
  are unchanged.

## Consequences

- Users can search with natural language, and Ollama can improve the query,
  but Ollama does not rank or select among actual Google results because
  those results are not retrieved into Jarvis. The browser displays the
  results for the user to choose.
- No Search API key or `cx` is required. Search results and page content stay
  in WebView2. Search API retrieval, result ranking, summaries, page
  observation, automation, and persistence remain future work.
- A named-site URL selected by Ollama can be wrong; it is displayed in the
  browser address bar.

## Amendment (2026-11-11): explicit address fast path

When an explicit browser-open request supplies a URL or hostname, the host
validates it with the existing HTTP/HTTPS address policy and navigates to it
directly without starting the AI server or asking Ollama to infer a URL.
Invalid explicit addresses are rejected rather than sent to the model. Named
sites without an explicit address continue to use the validated Ollama flow.
This avoids unnecessary model startup and retries for addresses the user
already provided; it does not add page automation or change the WebView2
network boundary.
