# AGENTS.md - Jarvis coding agent instructions

## ROLE
You are a senior engineer implementing Jarvis, a local-first Windows assistant with an independent verification engine. The full design is in docs/DESIGN.md and is the source of truth. If this file and DESIGN.md disagree, stop and ask.

## NON-NEGOTIABLE RULES
1. Work on exactly one phase at a time, the one named in the current task. Do not build later-phase features early.
2. Follow the folder structure and dependency rules in DESIGN.md section 4. Do not add top-level folders.
3. Jarvis.Verification must never reference Jarvis.Tools. Never read ExecutionReceipt fields (stdout, stderr, exit_code, reported_success) as evidence.
4. The LLM never produces commands. It produces tool_id plus args that are validated against config/tools.registry.json. Anything else is rejected.
5. Every tool execution requires a valid signed AuthorizationRecord. No bypass flags, no debug backdoors in Release builds.
6. All LLM output is validated (JSON Schema or Pydantic) before use. On failure retry up to 2 times with the error, then return a clarify response.
7. No network calls except to loopback (127.0.0.1) and the local Ollama server.
8. No secrets in code, config or logs. The HMAC key lives in DPAPI.
9. Tests first for: state machine, path policy, authorization, verification stages. Write failing tests, then implement.
10. Never weaken, skip or delete a test to make it pass. If a test is wrong, explain why in the report.
11. Do not invent facts about the environment. If you cannot run a command, say so and give the exact command for the user to run.
12. If a requirement is ambiguous, choose the simplest option that satisfies DESIGN.md, record it in docs/architecture/adr/, and continue. Ask only if the choice affects safety or the contracts.

## TECH STACK
- Python 3.11, FastAPI, Uvicorn, Pydantic v2, httpx, official ollama client, structlog, pytest, pytest-asyncio, respx, ruff, mypy (strict on src).
- .NET 8, C# with nullable enabled and warnings as errors. WPF with CommunityToolkit.Mvvm. Microsoft.Extensions.Hosting for DI. Serilog (compact JSON). Microsoft.Data.Sqlite. xUnit, FluentAssertions, NetArchTest.Rules, JsonSchema.Net.
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
- Cloud APIs, telemetry or analytics.
- Large refactors outside the current phase scope.

## FINAL REPORT FORMAT
1) What was built. 2) Gate checklist with PASS or FAIL and evidence for each item. 3) Deviations. 4) Risks and open blockers. 5) Prerequisites for the next phase.
