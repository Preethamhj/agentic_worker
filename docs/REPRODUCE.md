# Reproducing builds, tests, and evaluation

Run commands from the repository root in Windows PowerShell. The checked-in gate expects the .NET 10 SDK and the project Python environment at `ai-server\.venv`.

## Build

```powershell
dotnet restore windows-host\Jarvis.sln --disable-build-servers -m:1
dotnet build windows-host\Jarvis.sln --configuration Release --no-incremental --no-restore --disable-build-servers -m:1
```

## Tests and checks

The full repository gate is:

```powershell
.\scripts\check-all.ps1
```

This script also sets `JARVIS_REAL_SCHEDULER=1` and runs the `Category=RealScheduler` tests. Run it only in the intended desktop account after reviewing the tests' real scheduled-task effects. To run the solution without the real scheduler category:

```powershell
dotnet test windows-host\Jarvis.sln --configuration Release --no-build --no-restore --disable-build-servers -m:1 --filter "Category!=RealScheduler"
```

Python tests, lint, typing, and formatting can be run separately:

```powershell
Push-Location ai-server
.\.venv\Scripts\pytest.exe
.\.venv\Scripts\ruff.exe check src tests
.\.venv\Scripts\mypy.exe src
Pop-Location

.\ai-server\.venv\Scripts\python.exe -m pytest eval\tests
.\ai-server\.venv\Scripts\ruff.exe check eval
.\ai-server\.venv\Scripts\mypy.exe --strict --explicit-package-bases eval
dotnet format windows-host\Jarvis.sln --verify-no-changes --no-restore
```

## Development evaluation

Build Release first, then run the deterministic fixed-seed smoke with the fake scheduler. Choose an output folder outside the repository; the runner's default behavior refuses in-repository results:

```powershell
dotnet build windows-host\Jarvis.sln --configuration Release --disable-build-servers -m:1
$evalOut = Join-Path $env:LOCALAPPDATA 'Jarvis\eval-results\dev-smoke'
.\scripts\run-eval.ps1 -Reps 1 -Out $evalOut
```

This runs all configured tools with the fake scheduler and performs combined analysis and cleanup reporting. Do not pass `-RealReminderSubset` unless intentionally running the real-scheduler reminder check with `JARVIS_REAL_SCHEDULER=1`.

## Frozen paper evaluation

The paper evaluation is valid only on the unchanged `eval-paper-v1` tag. The protocol is the authority for its analysis and reporting. From a clean checkout, build and run:

```powershell
git switch --detach eval-paper-v1
dotnet build windows-host\Jarvis.sln --configuration Release --disable-build-servers -m:1
$paperOut = Join-Path $env:LOCALAPPDATA 'Jarvis\eval-results\paper-v1'
.\scripts\run-eval.ps1 -Reps 5 -Out $paperOut
```

The real-reminder subset is a separate, explicit owner-only run:

```powershell
$priorSchedulerSetting = $env:JARVIS_REAL_SCHEDULER
try {
    $env:JARVIS_REAL_SCHEDULER = '1'
    $realOut = Join-Path $env:LOCALAPPDATA 'Jarvis\eval-results\paper-v1-real-reminder'
    .\scripts\run-eval.ps1 -RealReminderSubset -Out $realOut
} finally {
    if ($null -eq $priorSchedulerSetting) {
        Remove-Item Env:\JARVIS_REAL_SCHEDULER -ErrorAction SilentlyContinue
    } else {
        $env:JARVIS_REAL_SCHEDULER = $priorSchedulerSetting
    }
}
```

Do not modify evaluation code, analysis, fault injectors, baselines, or the pre-registered expected-verdict table for a paper run. See [the evaluation protocol](eval/PROTOCOL.md) for seed, run boundaries, safety preconditions, and interpretation rules.

## Captured environment

The latest checked-in raw evaluation metadata is the first row in `docs/eval/results/dev-run-4/raw.jsonl`. The table below reports only fields present in that metadata; unavailable fields are not inferred.

| Environment field | Value in raw metadata |
|---|---|
| OS build | `Microsoft Windows NT 10.0.26300.0` |
| .NET version (`dotnet_version`) | `10.0.9` (metadata does not distinguish SDK from runtime) |
| Python version | Not recorded |
| Ollama model used | Not recorded |

`config/settings.json` currently selects `qwen2.5:7b-instruct`, but the raw evaluation metadata does not prove which Ollama model was installed or loaded for that run. Future evaluation metadata should record both Python and Ollama model versions to make this table complete.
