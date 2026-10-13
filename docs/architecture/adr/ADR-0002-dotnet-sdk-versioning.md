# ADR-0002: .NET SDK Versioning and TargetFramework Alignment

* **Status:** Accepted
* **Date:** 2026-10-09
* **Authors:** Jarvis Architecture Team

## Context
Jarvis Windows host components are developed in C# targeting .NET 8 LTS. Developers and CI build environments may have newer SDK versions installed (e.g. .NET 10 SDK). Without explicit SDK pinning and roll-forward configuration in `global.json`, build tooling may encounter SDK resolution mismatches across environments.

## Decision
1. **SDK Pinning via `global.json`:**
   Configure `global.json` at repository root and `windows-host/global.json` with version `10.0.100` and `rollForward` set to `latestFeature`. Both local development and CI environments align on the .NET 10 SDK, ensuring reproducible builds and eliminating multi-SDK resolution variance.

```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature"
  }
}
```

2. **Centralized TargetFramework Alignment:**
   Projects target `<TargetFramework>net8.0</TargetFramework>` centrally in `windows-host/Directory.Build.props` for all class libraries, core services, contracts, CLI, and test assemblies. UI components in `Jarvis.UI.csproj` target `<TargetFramework>net8.0-windows</TargetFramework>`.

3. **Strict Quality Enforcements:**
   Enforce `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, and `<ImplicitUsings>enable</ImplicitUsings>` across all C# projects. Enforce formatting verification via `dotnet format --verify-no-changes` in master CI checks (`check-all.ps1`).

## Consequences
- Guaranteed cross-developer and CI build reproducibility across .NET SDK installations.
- All 15 C# source and test projects maintain strict type safety, nullability checks, and target framework alignment.
