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
11. **Live planner and progress:** With the configured local Ollama model available, submit a file-creation request phrased in natural language. Confirm startup and planning progress are visible, then verify the approved action and its evidence. The model may take time to respond.
12. **Decision visibility and focus:** Confirm the approval dialog belongs to the main window, appears in the taskbar and above other windows, and initially focuses **Deny**. Repeat for escalation and confirm **Abort** has focus.
13. **Cancellation:** Cancel during planning and, separately, during an approval dialog. Confirm the UI stops waiting and does not report verified. Check the history and destination because cancellation cannot undo an operation already started; inference may continue until the local server request ends.

Record pass/fail and any observed issue for each step. Do not mark the Phase 4 manual validation complete until all steps have been exercised by the owner.
