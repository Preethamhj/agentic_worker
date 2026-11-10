# ADR-0012: On-demand Gmail details and local-model summary

Status: Accepted, amended (2026-11-10)

## Context
The user wants to list today's Gmail messages without spending time summarizing every result, exclude spam, and explain an individual message only when explicitly requested.

## Decision
- `GmailPromptParser` recognizes explicit mail-list prompts. Today's requests are bounded by local-day `after:` and `before:` Unix timestamps; the Gmail query always excludes `in:spam`. The result list shows only sender, subject, and date metadata, up to 100 messages.
- Selecting a row does not load its body. The Explain action or an "explain it" prompt fetches only the selected message with `messages.get?format=full` through the controlled `GoogleApiClientFactory`; text/plain (or tag-stripped HTML) is used and attachments are not loaded.
- `OllamaMailSummarizer` is called only for that selected message. It posts the message text to the configured loopback `ollama_host` `/api/chat`, with automatic redirects and proxies disabled, and uses the model from settings. The body sent to the model is capped at 12,000 characters. The request timeout is 180 seconds.
- OAuth tokens stay in the C# host. Only the selected mail text goes to Ollama. Python is not involved and still has no Google access.

## Consequences / risks
- Mail content is sent to the local model only after an explicit per-message request; email text is untrusted and the summarizer has no tools.
- The prompt-bar mail path bypasses the tool/authorization pipeline because it is read-only and user-initiated; this is a deliberate deviation from the all-actions-through-pipeline default, limited to Gmail reads.
- Requests are heuristic-parsed. The dated Gmail query limits today's listing; non-today queries remain subject to the 10/1 result count chosen by the plural/singular prompt form.
- Not verified live (Gmail, Ollama).
