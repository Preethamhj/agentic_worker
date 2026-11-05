# ADR-0002: .NET SDK Versioning and TargetFramework Alignment

* **Status:** Accepted
* **Date:** 2026-10-09
* **Authors:** Jarvis Architecture Team

## Context
Jarvis Windows host components target .NET 10. Developers and CI build environments must use the same SDK feature band policy. The local environment has .NET 10 SDKs installed, while the prior project configuration targeted .NET 8 and used a permissive roll-forward setting.

## Decision
1. **SDK Pinning via `global.json`:**
   Configure `global.json` at repository root and `windows-host/global.json` with version `10.0.300` and `rollForward` set to `latestFeature`. CI installs the latest .NET 10 SDK using `actions/setup-dotnet`. The check script rejects any SDK whose major version is not 10.

```json
{
  "sdk": {
    "version": "10.0.300",
    "rollForward": "latestFeature"
  }
}
```

2. **Centralized TargetFramework Alignment:**
   Projects target `<TargetFramework>net10.0</TargetFramework>` centrally in `windows-host/Directory.Build.props` for all class libraries, core services, contracts, CLI, and test assemblies. UI components in `Jarvis.UI.csproj` target `<TargetFramework>net10.0-windows</TargetFramework>`.

3. **Strict Quality Enforcements:**
   Enforce `<Nullable>enable</Nullable>`, `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, and `<ImplicitUsings>enable</ImplicitUsings>` across all C# projects. Enforce formatting verification via `dotnet format --verify-no-changes` in master CI checks (`check-all.ps1`).

## Consequences
- Guaranteed cross-developer and CI build reproducibility across .NET SDK installations.
- All 15 C# source and test projects maintain strict type safety, nullability checks, and target framework alignment.
