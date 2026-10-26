[CmdletBinding()]
param(
    [int]$Reps = 2,
    [Parameter(Mandatory = $true)][string]$Out,
    [switch]$Smoke
)

$ErrorActionPreference = 'Stop'
$startedAt = Get-Date
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$dll = Join-Path $repoRoot 'windows-host\src\Jarvis.Eval\bin\Release\net10.0-windows\Jarvis.Eval.dll'
$python = Join-Path $repoRoot 'ai-server\.venv\Scripts\python.exe'
$outRoot = [IO.Path]::GetFullPath($Out)
New-Item -ItemType Directory -Force -Path $outRoot | Out-Null

if (-not (Test-Path $dll)) { throw "Eval DLL not found: $dll" }
if (-not (Test-Path $python)) { throw "Python venv not found: $python" }

$tools = @(
    @{ Name = 'fs.create_file'; Directory = 'fs-create-file' },
    @{ Name = 'fs.create_folder'; Directory = 'fs-create-folder' },
    @{ Name = 'fs.search_files'; Directory = 'fs-search-files' },
    @{ Name = 'app.launch'; Directory = 'app-launch' },
    @{ Name = 'task.schedule_reminder'; Directory = 'task-schedule-reminder' }
)

foreach ($tool in $tools) {
    $toolOut = Join-Path $outRoot $tool.Directory
    New-Item -ItemType Directory -Force -Path $toolOut | Out-Null
    $log = Join-Path $toolOut 'run.log'
    $arguments = @($dll, 'run', '--seed', '20261025', '--reps', $Reps, '--tools', $tool.Name, '--scheduler', 'fake', '--out', $toolOut)
    if ($Smoke) { $arguments += '--smoke' }
    & dotnet @arguments *> $log
    if ($LASTEXITCODE -ne 0) { throw "Eval failed for $($tool.Name); see $log" }
    $raw = Join-Path $toolOut 'raw.jsonl'
    if (Test-Path $raw) {
        & $python (Join-Path $repoRoot 'eval\analyze.py') $raw --out $toolOut *> (Join-Path $toolOut 'analyze.log')
        if ($LASTEXITCODE -ne 0) { throw "Analysis failed for $($tool.Name); see $toolOut" }
    }
}

Write-Host '=== Final cleanup check ==='
Write-Host 'Scheduled tasks under \\Jarvis\\:'
try { Get-ScheduledTask -TaskPath '\Jarvis\' -ErrorAction Stop | Select-Object TaskPath,TaskName,State } catch { Write-Host $_.Exception.Message }
Write-Host 'Notepad/ping processes started after this script began:'
Get-Process -Name notepad,ping -ErrorAction SilentlyContinue |
    Where-Object { $_.StartTime -ge $startedAt } |
    Select-Object Id,ProcessName,StartTime,HasExited
$sandbox = Join-Path $env:LOCALAPPDATA 'Jarvis\eval-sandbox'
Write-Host "Eval sandbox: $sandbox"
if (Test-Path $sandbox) { Get-ChildItem $sandbox -Directory | Select-Object FullName }
