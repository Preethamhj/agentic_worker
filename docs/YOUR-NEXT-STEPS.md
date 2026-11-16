# 🎯 NEXT STEPS FOR YOU (Immediate Actions)

**Your decisions needed today:**

---

## 1️⃣ RUN CORE-1 VERIFICATION (When ready)

```powershell
Set-Location (git rev-parse --show-toplevel)
powershell .\scripts\run-core-1-verification.ps1
```

**What it does:**
- ✓ Checks Ollama server is running and configured
- ✓ Checks Python venv is healthy
- ✓ Runs live search test (real Ollama model)
- ✓ Runs all 98 verification engine tests
- ✓ Runs full gate (489+ C# tests, Python, format, arch)

**Expected time:** 10–15 minutes  
**Expected result:** All steps pass, gate shows "ALL CHECKS PASSED"

**After it completes:** Reply with the summary from the log file (or upload the `.txt` files)

---

## 2️⃣ CLARIFY APP PRIORITIES (So I know what to build next)

Pick one from each category:

**For CORE-2 Notification:**
- [ ] WPF toast window (simple, fully under our control)
- [ ] System toast (Windows 10+ notifications)
- [ ] Tray balloon (minimal)

**For Extended Apps (which first?):**
- [ ] Outlook (emails, calendar)
- [ ] Teams (chat, presence)
- [ ] Browser (navigation, form fill)
- [ ] Word/Excel (document edit)
- [ ] All equally (I'll do Outlook first, you pick after)

**For Browser Control (if you pick browser):**
- [ ] Simple: Just launch URL in Edge/Chrome
- [ ] Advanced: Full DOM automation (click, fill, wait)
- [ ] Both: Simple default + advanced when complex

---

## 3️⃣ REVIEW & APPROVE ROADMAP

Read through: `docs/DEV-ROADMAP-NEXT.md`

**Questions:**
- Does the phasing make sense?
- Any tools/features we should add or remove?
- Any timeline concerns?
- Should I start on CORE-2 after CORE-1 passes?

Reply: "Roadmap approved" or "Please adjust X, Y, Z"

---

## 4️⃣ DECIDE ON F7/F8 EVALUATION AMENDMENT

**Background:** The verifier contract fix (commit `fc4bc7c`) changes how search verification works. Two eval test cases (F7, F8) now behave differently than frozen expectations.

**The fix is correct.** Receipt output is never evidence by design.

**Your choice:**

**Option A:** I write and commit the amendment now
- 2-line entry in `docs/eval/PROTOCOL.md`
- Explains the fix, keeps frozen table unchanged
- Clean history before we merge to master

**Option B:** You handle it after testing
- I note it for your review
- You decide when to formally amend

**Recommend:** Option A (cleaner, documented, ready for merge)

---

## 📋 BUNDLED OWNER COMMANDS (After CORE-1 passes)

Once you confirm CORE-1 all-green, I'll give you **CORE-2 commands** bundled the same way.

Expected: `scripts/run-core-2-notification-browser.ps1`

---

## 📊 WHAT YOU'LL HAVE AFTER THIS SESSION

✅ **Committed:**
- Verifier contract fix (independent enumeration for search)
- Live test infrastructure (B3 groundwork)
- Dev roadmap (phased core → extended)
- CORE-1 bundled test script

📂 **Ready to run:**
- `scripts/run-core-1-verification.ps1` → Full pipeline check

📖 **Ready to review:**
- `docs/DEV-ROADMAP-NEXT.md` → Next 1–2 weeks of work
- `docs/SESSION-SUMMARY-20261107.md` → What I did + reasoning

⏳ **Awaiting:**
- CORE-1 test results
- App priority decision
- Roadmap approval
- Amendment decision

---

## 🚀 ONCE YOU REPLY WITH DECISIONS

I will immediately start:

1. **CORE-2 development** (notification + browser tool)
   - Tests first (mock notification, browser executor tests)
   - Implementation
   - Integration
   - Bundle commands for you to run

2. **Push to new branch:** `core-2-notification-browser`
   - You review, I make any requested changes
   - Merge when ready

3. Continue CORE → EXTENDED phases in sequence

---

## 📞 COMMUNICATION PROTOCOL (Going Forward)

**What I need from you:**
- ✓ CORE-1 test results (copy the `.txt` log output)
- ✓ Quick decisions (app priorities, notification style, etc.)
- ✓ Approvals (before I merge branches)
- ✓ Real testing (reminders, app launches, etc.)

**What I'll give you:**
- ✓ Bundled commands (one script = all tests)
- ✓ Clear pass/fail per step
- ✓ Detailed logs (timestamps, error traces)
- ✓ Commit history (clean, one feature per commit)
- ✓ No surprises (changes only what's listed, no scope creep)

---

## ⏱️ TIMELINE ESTIMATE

If CORE-1 passes today and you give decisions:
- **CORE-2** ready by tomorrow (1 day)
- **CORE-3** ready by +2 days
- **EXTENDED-1 (Outlook)** ready by +4–5 days
- **Total:** One week to first real extended app fully tested

---

**NEXT: Wait for your CORE-1 result. I'll be ready to move immediately on CORE-2.**

*Files created this session:*
- `docs/DEV-ROADMAP-NEXT.md` (11 KB)
- `scripts/run-core-1-verification.ps1` (6 KB)
- `docs/SESSION-SUMMARY-20261107.md` (9 KB)

*Branch pushed:* `live-ops` (fc4bc7c — verifier fix + B3 groundwork)

---

## 💬 READY. AWAITING YOUR INPUT.

**What's next from you:**
1. Run the CORE-1 script (whenever you're ready)
2. Reply with results + decisions
3. I start CORE-2 immediately
