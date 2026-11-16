# Threat model notes

## Filesystem creation timestamps

Windows filesystem creation timestamps may be rounded or have limited resolution. Stage 5 therefore tolerates a target creation time up to 100 ms before the host-provided `ExecutionObservation.StartedAtUtc`. This tolerance is not proof that the authorized step created the target: Stage 6 also requires the target to be absent from the before-snapshot and present in the after-snapshot. A pre-existing file with identical content still fails, even if its creation timestamp falls inside the 100 ms tolerance.

## Filesystem snapshot coverage

Create-tool watch scopes default to the target's parent folder and are enumerated non-recursively. Changes outside that immediate folder are not detected by those snapshots. Metadata-first comparison hashes only entries with changed metadata (plus the authorized target); a same-size edit whose last-write time is restored can therefore escape detection for an unrelated watched file. This is an explicit performance tradeoff, not a claim of complete filesystem monitoring.

## Reminder task and message integrity

Scheduled reminder actions run the absolute executable path signed by `AuthorizationService`, with exactly `--jarvis-reminder <32-hex-task-id>` as arguments. The title and message are stored separately in `%LOCALAPPDATA%\Jarvis\reminders\<task-id>.json`; the executor uses exclusive file creation before registering the task. The dispatcher validates the ID again, reads only that ID-derived file, displays the payload, and deletes it. Message text is limited to 200 Unicode characters and rejects control characters; the registry schema and host policy enforce the same rule.

The independent verifier compares the registered Task Scheduler command and arguments with signed expected fields, and compares the data file's title/message with the authorization's canonical arguments. Missing or changed data therefore fails verification. It normalizes only CRLF and CR line endings to LF when comparing arguments, because XML/platform serialization may normalize line endings; it does not normalize whitespace, quoting, or other characters.

This check is a point-in-time integrity check, not protection from a process running as the same Windows user changing the task or data file after verification and before its trigger. The dispatcher validates the payload's structure and character limits but does not possess the authorization record and cannot re-check the authorized message. A same-user process can also read the per-user data file before dispatch. Protecting against those same-user races would require a stronger authenticated payload/dispatch contract and remains a known limitation.

## External Google HTTPS boundary

The Python AI server remains restricted to loopback and its local Ollama
service by `install_network_guard()`. The guard does not allow Google
destinations. Calendar, Gmail, Drive, and Meet requests must originate in the
C# Windows host and use the `GoogleApiClientFactory` transport; access is
denied unless `integrations.google.network_policy.enabled` is explicitly
true.

The user-operated WebView2 workspace is a separate, explicitly authorized
network surface. It can load user-selected HTTP/HTTPS sites, including their
page subresources, in a Jarvis-owned profile; these requests do not pass
through `GoogleApiClientFactory` and are not restricted to Google's endpoint
allowlist. This exception does not permit arbitrary `HttpClient` use in
Jarvis or external access from Python. A named-site prompt may send the site
name to authenticated loopback Ollama for URL resolution; WPF validates the
HTTP/HTTPS URL before navigation. An explicit web-search prompt may send its
text to local Ollama for query refinement, and C# builds a URL-encoded
Google results-page URL. The search itself and its result traffic occur only
inside user-operated WebView2. That flow does not scrape or rank results;
the user selects from the browser page. ADR-0015 permits local observation, while ADR-0017 requires
the user to review and confirm the exact bounded projection for each
page-aware planner request. The projection omits URLs, link destinations,
attributes, and input values; link references are temporary IDs, and the
target mapping remains in host memory. Credential-like markers, common token
forms, and credential-bearing target parameters block disclosure or target
selection. This is conservative pattern detection, not a proof that arbitrary
visible page text contains no password or secret; the preview remains
mandatory and detection limitations are an open risk.
Page text is untrusted and may contain prompt injection. The model can return
only a validated search or temporary link ID. Opening a link is separately
registered, approved, signed, and revalidated against the current observation;
the signed record contains a target fingerprint and host, not the URL. The
browser adapter uses CDP `Page.navigate` only for that already-approved
HTTP/HTTPS target. Independent verification checks browser-engine address
evidence; the CDP response is not evidence. Form filling, booking, purchases,
downloads, and page-supplied scripts remain unsupported. Native desktop
applications are not launched by the browser. Top-level
non-HTTP(S) navigations remain blocked, and downloads and site permission
requests remain denied. Ollama's named-site URL may be incorrect, so the
destination is visible in the browser address bar; see ADR-0014.

The C# policy matches exact DNS hostnames and service path prefixes, HTTPS,
and port 443. It does not use a `*.googleapis.com` rule. The transport does
not use the system proxy, resolves only an approved hostname, rejects
non-public DNS results, and leaves default TLS certificate validation
enabled. Automatic redirects are disabled; redirects returned by Google are
rejected if their destination is not also an approved endpoint. Request
timeouts and cancellation apply to HTTP and connection work. No request URLs,
credentials, OAuth tokens, or response bodies are logged by this transport.
API requests are limited to GET/HEAD; OAuth token and revocation endpoints
accept POST and the authorization and signing-key endpoints accept GET.
The installed Google ID-token validation API has no cancellation-token
parameter, so that certificate request is bounded by the configured HTTP
timeout rather than the caller's cancellation token.

The system-browser OAuth authorization step is not an HTTP request made by
Jarvis's .NET client. Once opened, the browser's page loads and provider
redirects are outside the handler's enforcement boundary. The policy therefore
does not claim to restrict all browser traffic to the listed Google hosts.
No authorization code or access/refresh token is passed to Python or Ollama.
The API host/path policy limits service routing but does not itself enforce
per-operation read-only behavior; that remains governed by OAuth scopes and
future tool authorization. The WPF account surface reports provider
revocation separately from local DPAPI credential deletion; a failed provider
response is never reported as confirmed revocation.

The Calendar and Gmail reads are explicit user-initiated read-only display
actions, not LLM-selected tools. Calendar reads are bounded to ten upcoming
primary-calendar events. Gmail prompt-bar searches exclude spam and show up
to 100 subject/sender/date rows; today's requests include a local-day date
range. Message bodies are retrieved only when a specific row is explicitly
explained, and only that message is sent to the configured loopback Ollama
model. Attachments are not retrieved. The Ollama transport disables proxies
and redirects and has a 180-second timeout. The shared configuration disables
Drive and Meet for this milestone. Live Google data access is not performed
by unit tests.
