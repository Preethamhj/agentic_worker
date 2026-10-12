# ADR-0004: Solution File Format (Single Standard .sln Format)

* **Status:** Accepted
* **Date:** 2026-10-12
* **Authors:** Jarvis Windows Host Architecture Team

## Context
Visual Studio 2022 (v17.10+) introduced `.slnx` (an experimental/preview XML-based solution format). However, having both `.sln` and `.slnx` in the same directory causes MSBuild ambiguity (`MSB1011: Specify which project or solution file to use because this folder contains more than one project or solution file`).
Furthermore, standard .NET 8 SDK environments (such as GitHub Actions `windows-latest` with `actions/setup-dotnet@v4` pinned to .NET 8.0.x) natively support standard `.sln` without requiring preview feature flags.

## Decision
1. **Single Solution Format (`Jarvis.sln`):**
   Standardize strictly on `Jarvis.sln` as the single canonical solution file in `windows-host/`. Remove `Jarvis.slnx`.
2. **CI Tooling Alignment:**
   Align `scripts/check-all.ps1`, `global.json`, and `.github/workflows/ci.yml` around `Jarvis.sln`.
   - `global.json` specifies .NET 8.0.100 with `rollForward: latestMajor`.
   - CI environment uses `actions/setup-dotnet@v4` with `dotnet-version: '8.0.x'`.
   - Local environments with .NET 10 (e.g. SDK 10.0.201 / 10.0.300) roll forward seamlessly.

## Consequences
- Zero MSBuild ambiguity or file-selection warnings.
- 100% build compatibility across `dotnet build`, `dotnet test`, `dotnet format`, Visual Studio, JetBrains Rider, and CI build agents.
