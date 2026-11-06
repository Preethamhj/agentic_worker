# Phase 4 owner manual test checklist

Run Jarvis from a normal, non-elevated Windows desktop session. For file tasks, choose a destination shown under the UI's read-only allowed roots. Use a disposable test folder and do not use the real Task Scheduler for these checks.

1. **Approve a file creation:** Enter a prompt to create a disposable text file inside an allowed root. Confirm the dialog shows the tool, canonical arguments, and risk badge. Approve it; confirm the task panel reports success only after independent verification and the evidence panel shows the verification reasons.
2. **Deny an action:** Submit another file-creation prompt and select **Deny**. Confirm no file is created, the task does not report “verified,” and history records a denied approval with the interactive approver.
3. **Close means deny:** Start a new approval request and close its window using the close control. Confirm the task is denied and history records a denial rather than approval.
4. **Approval timeout:** Start a new approval request and leave the dialog unanswered until its timeout. Confirm the task fails closed and history records approver `timeout`.
5. **Failed verification reasons:** Arrange a safe disposable task whose expected postcondition cannot be met, then submit it. Confirm the task panel says “not verified” and the evidence panel displays the failure reasons and stage outcomes.
6. **Escalation retry:** When an independent verification enters escalation, select **Retry verification**. Confirm only verification is retried and no tool execution is repeated.
7. **Escalation abort:** Trigger another escalation and select **Abort**. Confirm the task ends failed and remains “not verified.”
8. **History attribution:** Open the History tab after the approval tests. Confirm it shows state transitions, policy validation, approval decisions with approver, verification events, and any displayed legacy rows.
9. **Doctor and settings:** Open Settings and doctor. Confirm model, allowed roots, and doctor results are visible and read-only.
10. **Low-risk behavior:** In a Debug build, test a low-risk request with the same low-risk auto-approval setting used by the console and confirm it follows that existing policy. Confirm medium/high-risk approvals still require a human decision. Repeat in Release and confirm there is no auto-approval.

Record pass/fail and any observed issue for each step. Do not mark the Phase 4 manual validation complete until all steps have been exercised by the owner.

## Live Notepad outcome classes

The owner may run `scripts\live-app-check.ps1` for one real Notepad launch through the normal Jarvis executor and independent verifier. It prints the CLI's per-stage outcomes and reasons, plus Notepad process snapshots before and after. It deliberately does not stop any Notepad process; on Windows 11 the launcher may hand off to an existing packaged-app process and opened tabs can persist.

- **VERIFIED:** independent evidence matched the authorized Notepad image during the verification window.
- **FAILED:** the executor or an independent check failed; use the printed stage detail and reasons to diagnose it.
- **ESCALATE or another non-VERIFIED outcome:** Jarvis did not establish the authorized postcondition. Treat it as not verified.
- A changed process list is diagnostic only and is not itself a verification verdict. A hand-off may leave the existing process set unchanged.
