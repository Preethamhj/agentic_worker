# ADR-0001: Key Architectural Decisions for Jarvis

## Status
Accepted

## Context
Jarvis is a local-first Windows assistant designed to convert natural language into allowlisted tool calls, execute them safely, and verify post-execution OS state independently. The initial HLD/LLD contained several ambiguities and open technical choices (e.g., Python vs C# verifier responsibility, gRPC vs REST API stack, PowerShell command generation vs native .NET tool execution).

## Decisions
1. **API Stack:** Use FastAPI with REST/JSON bound strictly to `127.0.0.1` and secured with a shared token header (`X-Jarvis-Token`). gRPC, Rust, and remote microservices are excluded from v1.
2. **Verification Location:** All evidence collection and verification decision logic run strictly in C# (`Jarvis.Verification`). Python only plans the task and verification plan.
3. **Verification Stages:** Standardized on a 10-stage pipeline.
4. **Safety & Execution:** The LLM produces only a `tool_id` and typed arguments. No freeform PowerShell strings from LLM output. Tool execution requires a DPAPI-signed `AuthorizationRecord`.
5. **Dependency Restrictions:** `Jarvis.Verification` must not reference `Jarvis.Tools`. `Jarvis.Contracts` is a leaf project referencing no other Jarvis assemblies.

## Consequences
- Clean structural separation between execution and independent verification.
- Predictable and testable contract schemas shared between Python and C#.
