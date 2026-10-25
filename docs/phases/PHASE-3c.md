# Phase 3c: Host Verification Engine

Replace the host's `StubVerifier` with the independent `VerificationEngine`. A task may reach `completed` only when the engine returns `VERIFIED`.

Parts:
- A: Locate and document the verifier integration seams.
- B: Add escalation handling for `ESCALATE`.
- C: Wire the real verification engine into the host.
- D: Report true verification status to the AI server.
- E: Display verification evidence in CLI output and history.
- F: Add real-engine end-to-end verification tests for each tool.
- G: Prove executor-reported success cannot override failed independent evidence.
- H: Run regression gates and document the fault matrix.
