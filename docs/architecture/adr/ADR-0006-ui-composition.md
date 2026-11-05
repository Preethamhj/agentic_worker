# ADR-0006: Shared host composition for the WPF UI

- Status: Accepted
- Date: 2026-11-05
- Phase: 4

## Context

`Jarvis.Cli.Program.RunCommandAsync` currently constructs the planner, safety
gate, authorization service, executors, verification engine, queue, and
`TaskOrchestrator` in one command path. `Jarvis.UI` is only a WPF shell today;
its project currently references Safety, Tools, and Verification directly.
Phase 4 requires the UI to use the same orchestration and approval policies as
the CLI without exposing executors or verifier internals to the UI.

## Decision

Extract host composition and task submission behind a shared composition
library under `windows-host/src` (proposed project: `Jarvis.Composition`). The
library will construct and own the existing host dependency graph and expose
an asynchronous orchestration-facing interface to its hosts. The CLI and WPF
UI will both use this interface; neither UI views nor view models will create
executors, safety gates, authorization services, or verifiers.

`Jarvis.Composition` may reference the concrete host modules needed to compose
the graph. `Jarvis.UI` will reference only Contracts, Core, Infrastructure,
and the composition library. The CLI remains the command-line adapter over the
same composition. Approval and escalation continue through injected
`IApprovalProvider` and `IEscalationHandler` implementations; the UI adds no
auto-approval or execution bypass.

The extraction must preserve the CLI's current planner, policy, authorization,
execution, independent verification, audit, reporting, and terminal-state
behavior. Existing CLI E2E tests are the compatibility gate during extraction.

## Consequences

- There is one orchestration/composition path for CLI and WPF.
- UI code can display task and evidence models and provide user decisions but
  cannot directly invoke a tool executor or verifier.
- Composition extraction is an implementation prerequisite for wiring the
  WPF prompt in Phase 4; it does not change policy or tool behavior.
- No new top-level repository folder or external dependency is needed.
