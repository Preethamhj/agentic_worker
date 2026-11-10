Read docs/STATUS.md first. Ignore any file under reports/.

# AGENTS.md - Jarvis coding agent instructions

## ROLE
You are a senior engineer implementing Jarvis, a local-first Windows assistant with an independent verification engine. The full design is in docs/DESIGN.md and is the source of truth. If this file and DESIGN.md disagree, stop and ask.

## NON-NEGOTIABLE RULES
1. Work on exactly one phase at a time, the one named in the current task. Do not build later-phase features early.
2. Follow the folder structure and dependency rules in DESIGN.md section 4. Do not add top-level folders.
3. Jarvis.Verification must never reference Jarvis.Tools, Jarvis.Safety or Jarvis.Core, and no host project may reference Jarvis.Eval. Never read ExecutionReceipt fields (stdout, stderr, exit_code, reported_success) as evidence.
4. The LLM never produces commands. It produces tool_id plus args that are validated against config/tools.registry.json. Anything else is rejected.
5. Every tool execution requires a valid signed AuthorizationRecord. No bypass flags, no debug backdoors in Release builds.
6. All LLM output is validated (JSON Schema or Pydantic) before use. On failure retry up to 2 times with the error, then return a clarify response.
7. Python network access is limited to loopback (127.0.0.1) and the local Ollama server. The C# Windows host may use external HTTPS only when explicitly enabled in `integrations.google.network_policy`, and only through `Jarvis.Infrastructure.Google.GoogleEndpointPolicy` and its controlled transport for exact allowlisted Google hosts, paths and port 443. API methods are read-only (`GET`/`HEAD`); OAuth token requests use `POST`. No wildcard domains, arbitrary external networking, or direct Google connections from Python.
8. No secrets in code, config or logs. The HMAC key lives in DPAPI.
9. Tests first for: state machine, path policy, authorization, verification stages. Write failing tests, then implement.
10. Never weaken, skip or delete a test to make it pass. If a test is wrong, explain why in the report.
11. Do not invent facts about the environment. If you cannot run a command, say so and give the exact command for the user to run.
12. If a requirement is ambiguous, choose the simplest option that satisfies DESIGN.md, record it in docs/architecture/adr/, and continue. Ask only if the choice affects safety or the contracts.

## TECH STACK
- Python 3.11, FastAPI, Uvicorn, Pydantic v2, httpx, official ollama client, structlog, pytest, pytest-asyncio, respx, ruff, mypy (strict on src).
- .NET 10 (SDK 10.0.x), C# with nullable enabled and warnings as errors. WPF with CommunityToolkit.Mvvm. Microsoft.Extensions.Hosting for DI. Serilog (compact JSON). Microsoft.Data.Sqlite. xUnit, FluentAssertions, NetArchTest.Rules, JsonSchema.Net.
- Microsoft.Win32.TaskScheduler only inside Jarvis.Tools. Raw COM or P/Invoke only inside Jarvis.Verification for file, process and scheduler evidence.
- The model name comes from config/settings.json (default qwen2.5:7b-instruct). Never hard-code it.

## CODING STANDARDS
- Small files, single responsibility, an interface for every external dependency (LLM, clock, file system, process, scheduler, store). Inject IClock so time-window tests are deterministic.
- Public APIs documented with XML comments or docstrings. Typed errors only. No swallowed exceptions.
- Structured logging only. Every log line carries trace_id, task_id and step_id where known.
- Cancellation tokens on every async method. No .Result, no .Wait(), no async void except UI event handlers.
- Windows paths are handled only through PathPolicy. Never concatenate user paths by hand.

## WORKFLOW FOR EVERY PHASE
1. Read docs/DESIGN.md and the phase prompt. Audit existing code. Write a short plan: files to create and tests to write.
2. Implement in small steps. After each step run the relevant tests.
3. Run scripts/check-all.ps1 at the end and fix everything it reports.
4. Write docs/phases/PHASE-N.md: summary, files added or changed, test results (counts and names), decisions, deviations from DESIGN.md with reasons, known limitations, exact commands to reproduce.
5. Stop. Do not continue to the next phase.

## FORBIDDEN
- Generating or executing raw PowerShell or cmd strings from LLM output.
- Using tool output, exit codes or in-process checks as verification evidence.
- Wildcard CORS, binding to 0.0.0.0, disabling auth checks, running elevated.
- Unapproved cloud APIs, telemetry or analytics. The only approved external API exception is the explicitly configured Google HTTPS policy in rule 7 and ADR-0010.
- Large refactors outside the current phase scope.

## FINAL REPORT FORMAT
1) What was built. 2) Gate checklist with PASS or FAIL and evidence for each item. 3) Deviations. 4) Risks and open blockers. 5) Prerequisites for the next phase.

## OPERATIONAL RULES (learned from earlier sessions)
- Environment: Windows PowerShell 5 (no && operator). rg is NOT installed; use Get-ChildItem -Recurse -Filter *.cs | Select-String. Call venv tools directly (ai-server\.venv\Scripts\python.exe, ruff.exe, mypy.exe); never trigger VS Code Python environment configuration. Run dotnet with --disable-build-servers -m:1 (and --no-restore after the first restore). On a NuGet or .git lock error wait 20 s and retry up to 3 times; never delete lock files.
- Budget: do not re-explore the repo; read only the files a step needs. Commit and push after each part and add one line to the phase PROGRESS file. If budget runs low, commit and list done / not done. Never leave uncommitted work. Never run a command that may exceed 5 minutes; give the user a script instead.
- Processes: never Stop-Process by name or by a PID you did not start yourself with Process.Start. Hold the Process object and compare StartTime before killing. Use 30 s hard timeouts, only notepad and ping.exe for app tests, and never touch a Notepad you did not start. Real Task Scheduler tests only with JARVIS_REAL_SCHEDULER=1 and --filter "Category=RealScheduler". Real-scheduler evaluations are run by the user.
- Gates: never remove a test category or step from scripts/check-all.ps1 or the CI workflow. Never edit the frozen evaluation protocol or its expected-verdict table; changes go in a dated AMENDMENT section.
- Evidence: never state a CI result unless you have the run URL and its status; otherwise write "CI: not checked". Report per-project test counts copied from command output. Anything you could not run is "reported, not verified".
- Verifier results: VERIFIED on a BAD case or non-VERIFIED on a GOOD case is a FINDING for docs/eval/FINDINGS.md. Never fix it by weakening a verifier or a check.
- Boundaries: host projects never reference Jarvis.Eval. Evaluation work must not change production code, except a one-line seam explicitly authorized in a dated amendment.
- State: docs/STATUS.md is the single source of current state; update it at the end of every session. Ignore everything under reports/.
- Tags: never create, move or delete git tags. Only the owner tags, after the gate and CI are green.
