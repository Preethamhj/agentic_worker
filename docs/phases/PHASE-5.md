# Phase 5: Verification Evaluation Harness

Evaluate whether authorization-anchored independent verification (the real `VerificationEngine`) detects false-success reports and unauthorized side effects that simpler baselines miss.

## Parts

- **A — Protocol:** freeze the research question, verifier definitions, fault matrix, task catalogue, metrics, repetitions, sandbox rules, and validity threats before results code.
- **B — Core abstractions:** define verifier, case, verdict, and one-execution/four-verdict runner contracts.
- **C — Verifiers:** implement receipt, self-check, predicate-only, and real-engine verifiers with behavioral tests.
- **D — Faults and architecture:** implement evaluation-only fault injection and prove host projects do not depend on `Jarvis.Eval`.
- **E — Catalogue:** generate and validate the deterministic fixed-seed task catalogue.
- **F — Runner and sandbox:** add bounded/resumable CLI evaluation, fake and explicitly gated real scheduler modes, and smoke coverage.
- **G — Analysis:** implement standard-library Python metrics and synthetic-data tests.
- **H — Dry run and close-out:** run smoke and one fake-scheduler repetition, analyze and commit development results, and execute release validation.

The frozen protocol is [../eval/PROTOCOL.md](../eval/PROTOCOL.md). Development results are not paper results.
