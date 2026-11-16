# Jarvis Development Roadmap: Core Functionality → Extended Apps

**Status:** Current work analysis + strategic pivot plan  
**Updated:** 2026-11-07  
**Focus:** Functional completeness (Python LLM → C# execution → verification) → App expansion

---

## Current State Analysis

### ✅ What's **Fully Functional**
1. **Core engine loop** (orchestrator state machine)
   - Task lifecycle: `created` → `authorizing` → `executing` → `verifying` → `completed/failed`
   - Sequential queue, timeout handling, signal propagation
   
2. **Safety & Authorization**
   - Signed authorization records with HMAC (DPAPI-backed key store)
   - Tool registry validation (config/tools.registry.json)
   - Path policy with allowlist enforcement
   - Full audit trail (SQLite + Serilog JSON)

3. **File System Tools** (fs.*) 
   - `fs.create_file`, `fs.create_folder`, `fs.search_files`
   - Independent verification via P/Invoke file enumeration
   - Unit tests + integration tests green

4. **Verification Engine**
   - 10-stage pipeline: trigger, authorization, evidence, validation, attribute match
   - Independent OS evidence collectors (file, process, scheduler)
   - Per-tool verification logic in C#

5. **Reminders** (task.schedule_reminder) — Fake scheduler working
   - Task Scheduler integration via COM/WinRT
   - Dispatcher (jarvis.exe --jarvis-reminder)
   - Toast notification capability (exists but not live-tested)

6. **Python AI Server**
   - Rule-based planner (no LLM calls) — **fast path, deterministic**
   - Ollama integration (official client, JSON-schema output)
   - Session memory (SQLite, session_id isolation)
   - Prompt templates for each tool
   - Response formatter and audit log updates

7. **UI/CLI**
   - WPF UI shows task status, evidence, decision dialogs
   - CLI (jarvis.exe --help) with commands
   - Server/model status display

### ⚠️ What's **Partial / Needs Work**

| Component | Status | Issue | Impact |
|-----------|--------|-------|--------|
| **Verifier: Search evidence** | Blocked | F7/F8 eval: receipt-output handling vs frozen expectations | Non-blocking for live use (correct behavior now), needs eval amendment |
| **Live model testing** | Opt-in only | B2 E2E skips unless `JARVIS_LIVE=1` | Real Ollama results not verified |
| **Reminder dispatcher** | Console-only | No visible notification tested end-to-end | User can't see it fired |
| **Real Task Scheduler** | Opt-in only | RealScheduler tests skip unless env var set | Can't verify actual Windows task system |
| **Approval UI** | Basic | Dialog focus, keyboard shortcuts not complete | User approval flow feels unpolished |
| **Error clarity** | Partial | Some failure reasons are technical, not user-friendly | Users need better "why it failed" messages |

### ❌ What's **Not Implemented**

1. **Extended app support:**
   - Outlook (emails, calendar)
   - Teams (messages, presence)
   - Word/Excel (document edit, formulas)
   - Browser (URL nav, form fill, tab management)
   - File Manager (batch operations)
   - Calculator, Paint, etc.

2. **Advanced LLM paths:**
   - Ambiguity resolution (ask user clarifying questions)
   - Multi-turn conversations within a task
   - Confidence scoring and fallback chains

3. **Phase 6+ features** (explicitly deferred):
   - Vector DB memory
   - Recursive snapshots
   - Complex approvals (delegation, escalation tiers)

4. **Robustness:**
   - Ollama down/slow handling (framework exists, tests minimal)
   - Concurrent task queueing
   - Long-running task cancellation UI

---

## Strategic Pivot: Functional Core → Extended Apps

### **PHASE CORE-1: Verify & Polish Real Execution Pipeline**

**Goal:** Prove the full chain works with real Ollama, real Windows Task Scheduler, real Notepad, real file operations.

**Tasks:**
- [ ] Run `scripts/live-check.ps1` → Confirm Ollama, Python, scheduler all green
- [ ] Run B2 live test with real model: `JARVIS_LIVE=1 dotnet test ... LiveSearchEndToEndTests`
  - Capture latency, verify plan JSON, verify report says "VERIFIED"
- [ ] Run reminder end-to-end: prompt "remind me to test in 2 minutes" → watch task fire → see notification
- [ ] Resolve F7/F8 eval expectation via amendment (1–2 lines in PROTOCOL.md)
- [ ] Full gate passes: `scripts/check-all.ps1`

**Owner command bundle:**
```powershell
# 1. Verify Ollama and Python
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\live-check.ps1

# 2. Run live search E2E (real model)
$env:JARVIS_LIVE=1; dotnet test .\windows-host\tests\Jarvis.E2E.Tests\Jarvis.E2E.Tests.csproj --disable-build-servers -m:1 -k "LiveSearchEndToEndTests"

# 3. Run full gate (no real scheduler, not elevated)
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\check-all.ps1

# 4. Test reminder (real scheduler, elevated required)
# To be provided after gate passes
```

**Outcome:** Green gate + live model evidence → Proceed to extended apps.

---

### **PHASE CORE-2: Notification & Browser Foundation**

**Goal:** Users see results. Extend to browser control.

**Subtasks:**
1. **Reminder notification**: Upgrade console → WPF toast or system toast (choose simplest)
   - Test: Manual launch + verify visible, clickable
   
2. **Browser tool skeleton**:
   - Add to `tools.registry.json`: `browser.navigate_url`, `browser.fill_form`, `browser.click_element`
   - C# wrapper: Win32 process launch → `msedge.exe --new-window <url>` or Chromium COM
   - No full browser automation yet; just skeleton + manual testing
   
3. **Response formatter**: Show what the browser did (URL, form fields filled, button clicked)
   - Evidence: Screenshot? Element path? Keep it simple for now.

**Files to modify:**
- `config/tools.registry.json` → add browser.* tool IDs
- `windows-host/src/Jarvis.Tools/BrowserLaunchExecutor.cs` → new file
- `windows-host/tests/Jarvis.Tools.Tests/BrowserLaunchExecutorTests.cs` → basic tests
- `ai-server/src/jarvis_ai/planner/prompts/` → add browser templates

**Owner command:**
```powershell
# Build + run new browser tool tests
dotnet test .\windows-host\tests\Jarvis.Tools.Tests\Jarvis.Tools.Tests.csproj --disable-build-servers -m:1 -k "Browser"

# Check all (must stay green)
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\check-all.ps1
```

---

### **PHASE CORE-3: LLM Planner Switch + Ambiguity**

**Goal:** Use real Ollama for all prompts (not just fallback).

**Current state:**
- Rule-based planner handles ~60 % of common prompts (fast, no model)
- Ollama used as fallback (works but untested at scale)
- No clarification flow (if prompt is ambiguous, LLM must guess)

**Changes:**
1. **Default planner**: Switch to LLM-first for safety/simplicity
   - Keep rule path as optimization (skip LLM if rule matches + high confidence)
   - Log which path was taken (rule vs LLM) for telemetry

2. **Clarification handler**: On ambiguous input, LLM returns structured clarify response
   - Example: "create a file" → ask: location? content? name?
   - UI shows dialog, user picks, LLM re-plans with context
   - One retry; on second ambiguity, fail gracefully

3. **Prompt quality**: Review each tool's prompt template
   - Is it asking the LLM to be precise?
   - Does it include constraints (paths, file size, timeout)?
   - Does it give examples?

**Files to modify:**
- `ai-server/src/jarvis_ai/planner/planner.py` → switch default
- `ai-server/src/jarvis_ai/planner/prompts/` → add/refine clarify templates
- `windows-host/src/Jarvis.Cli/Program.cs` + UI → handle clarify response
- Tests: prompt injection, unicode paths, long prompts

**Owner command:**
```powershell
# Run all planner tests (including live LLM cases if JARVIS_LIVE=1)
$env:JARVIS_LIVE=1; pytest .\ai-server\tests\live\test_planner_live.py -v
pytest .\ai-server\tests\unit\planner\ -v
python -m pytest .\ai-server\tests\unit\ --tb=short

# Gate
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\check-all.ps1
```

---

### **PHASE EXTENDED-1: Outlook Tool**

**Goal:** Users can send emails, check calendar.

**Tools to add:**
- `outlook.send_email` → To, Subject, Body, CC/BCC, attachment paths
- `outlook.check_calendar` → Get events for today/next N days
- `outlook.create_event` → Title, time, attendees

**Implementation:**
- Use `Microsoft.Office.Interop.Outlook` (COM interop) or REST/Graph API
  - COM is local-only, simpler, no auth. Use COM.
  - Verifier collects evidence: MAPI store, registry checks
- Executor calls Outlook COM, returns receipt (emails sent, events read)
- Verifier: check Sent folder, Calendar store for the created items

**Files:**
- `windows-host/src/Jarvis.Tools/OutlookExecutor.cs`
- `windows-host/src/Jarvis.Verification/OutlookEvidenceCollector.cs`
- Tests + prompts

**Owner command:**
```powershell
# Test (requires Outlook installed)
dotnet test .\windows-host\tests\Jarvis.Tools.Tests\ -k "Outlook" --disable-build-servers -m:1
```

---

### **PHASE EXTENDED-2: Teams & Word/Excel**

**Similar pattern to Outlook:**
- `teams.send_message`, `teams.check_status`
- `word.open_document`, `word.add_heading`, `word.save`
- `excel.open_sheet`, `excel.set_cell`, `excel.calculate`

Each needs:
- Interop wrapper (Teams SDK or COM, Word/Excel interop)
- Evidence collector (file timestamps, registry, process checks)
- Executor + verifier tests

---

### **PHASE CORE-4: Dynamic Tool Discovery**

**Goal:** Users can say "add a script to run" or "integrate with my app".

**Not full plugins yet, but:**
- Users provide a PowerShell/Python script path
- Jarvis wraps it, validates output, verifies side effects
- Registry updated per-user or per-session

**Deferred:** Full plugin model with code signing, sandboxing. Too risky for Phase 1.

---

## Implementation Order (Recommended)

1. **CORE-1** (2–3 days): Verify real pipeline, fix eval amendment
2. **CORE-2** (1–2 days): Toast notification + browser skeleton
3. **CORE-3** (2–3 days): LLM planner switch + clarification
4. **EXTENDED-1** (2–3 days): Outlook tool
5. **EXTENDED-2** (3–5 days): Teams, Word, Excel
6. **CORE-4** (defer or later): Dynamic tool discovery

**Total for core + first extended tools: ~2–3 weeks** (with owner time for real testing, model time for testing).

---

## What Stays Unchanged

- Authorization & verification invariants (never weaken)
- Audit trail completeness
- Signed authorization requirement
- Local-only network binding
- Structured schema validation

---

## Open Blockers

1. **Eval amendment needed**: F7/F8 expectations vs corrected verifier behavior
   - **Action**: 2-line amendment in `docs/eval/PROTOCOL.md` (dated, reason, frozen table unchanged)
   
2. **Real Task Scheduler end-to-end not verified**
   - **Action**: Owner runs reminder e2e with real scheduler, confirms notification visible

3. **Ollama model latency unknown at scale**
   - **Action**: Collect metrics from CORE-1 live runs

---

## Success Criteria

- [ ] Gate passes (CORE-1)
- [ ] Live model search E2E: verified result, latency < 10s
- [ ] Real reminder fires + notification visible (CORE-2)
- [ ] One new extended app (Outlook) fully tested (EXTENDED-1)
- [ ] All tests pass, no weakened invariants
