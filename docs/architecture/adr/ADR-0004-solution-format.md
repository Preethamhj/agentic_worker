# ADR-0004: Solution File Format (.sln and .slnx Compatibility)

* **Status:** Accepted
* **Date:** 2026-10-09
* **Authors:** Jarvis Windows Host Architecture Team

## Context
Visual Studio 2022 (v17.10+) introduced `.slnx` (XML-based solution format). While modern .NET 8 / .NET 10 SDK CLI tools support `.slnx`, some standard `dotnet` CLI commands, cross-platform build scripts, and IDE integrations expect the standard `.sln` solution file format.

## Decision
1. **Dual Solution Support:**
   Maintain both `Jarvis.sln` (standard solution file format) and `Jarvis.slnx` (XML solution format) in `windows-host/`.
2. **CI Tooling Alignment:**
   Configure `scripts/check-all.ps1` and CI commands to target `windows-host/Jarvis.sln` directly, ensuring 100% build compatibility across all environments, CLI versions, and build agents.

## Consequences
- Guaranteed build compatibility across all versions of `dotnet` CLI, Visual Studio, Visual Studio Code (C# Dev Kit), and JetBrains Rider.
- Clean project structure in both `.sln` and `.slnx` formats.
