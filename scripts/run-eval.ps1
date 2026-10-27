[CmdletBinding()]
param(
    [int]$Reps = 2,
    [Parameter(Mandatory = $true)][string]$Out,
    [switch]$Smoke,
    [switch]$AllowPreexistingProcesses,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$startedAt = Get-Date
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$dll = Join-Path $repoRoot 'windows-host\src\Jarvis.Eval\bin\Release\net10.0-windows\Jarvis.Eval.dll'
$python = Join-Path $repoRoot 'ai-server\.venv\Scripts\python.exe'
$outRoot = [IO.Path]::GetFullPath($Out)
if (-not $DryRun) { New-Item -ItemType Directory -Force -Path $outRoot | Out-Null }

if (-not (Test-Path $dll)) { throw "Eval DLL not found: $dll" }
if (-not (Test-Path $python)) { throw "Python venv not found: $python" }

if (-not $DryRun) {
    $preexisting = Get-Process -Name notepad,ping -ErrorAction SilentlyContinue |
        ForEach-Object {
            try { [PSCustomObject]@{ ProcessName = $_.ProcessName; Id = $_.Id; StartTime = $_.StartTime; WindowTitle = $_.MainWindowTitle } } catch { }
        }
    if (@($preexisting).Count -gt 0 -and -not $AllowPreexistingProcesses) {
        $details = @($preexisting | ForEach-Object { "$($_.ProcessName) PID=$($_.Id) StartTime=$($_.StartTime.ToUniversalTime().ToString('o')) WindowTitle=$($_.WindowTitle)" }) -join '; '
        throw "App evaluation aborted because pre-existing Notepad/ping processes were found: $details. Close only processes you own, or rerun with -AllowPreexistingProcesses."
    }
} else {
    Write-Host 'DRY RUN: would preflight notepad.exe and ping.exe and fail unless -AllowPreexistingProcesses is supplied.'
}

$tools = @(
    @{ Name = 'fs.create_file'; Directory = 'fs-create-file' },
    @{ Name = 'fs.create_folder'; Directory = 'fs-create-folder' },
    @{ Name = 'fs.search_files'; Directory = 'fs-search-files' },
    @{ Name = 'app.launch'; Directory = 'app-launch' },
    @{ Name = 'task.schedule_reminder'; Directory = 'task-schedule-reminder' }
)

foreach ($tool in $tools) {
    $toolOut = Join-Path $outRoot $tool.Directory
    if (-not $DryRun) { New-Item -ItemType Directory -Force -Path $toolOut | Out-Null }
    $log = Join-Path $toolOut 'run.log'
    $arguments = @($dll, 'run', '--seed', '20261025', '--reps', $Reps, '--tools', $tool.Name, '--scheduler', 'fake', '--out', $toolOut)
    if ($Smoke) { $arguments += '--smoke' }
    if ($AllowPreexistingProcesses) { $arguments += '--allow-preexisting-processes' }
    $display = 'dotnet ' + (($arguments | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }) -join ' ')
    if ($DryRun) {
        Write-Host $display
        Write-Host "analyze: $python $(Join-Path $repoRoot 'eval\analyze.py') $(Join-Path $toolOut 'raw.jsonl') --out $toolOut"
        continue
    }
    & dotnet @arguments *> $log
    if ($LASTEXITCODE -ne 0) { throw "Eval failed for $($tool.Name); see $log" }
    $raw = Join-Path $toolOut 'raw.jsonl'
    if (Test-Path $raw) {
        & $python (Join-Path $repoRoot 'eval\analyze.py') $raw --out $toolOut *> (Join-Path $toolOut 'analyze.log')
        if ($LASTEXITCODE -ne 0) { throw "Analysis failed for $($tool.Name); see $toolOut" }
    }
}

$sandbox = Join-Path $env:LOCALAPPDATA 'Jarvis\eval-sandbox'
if ($DryRun) {
    Write-Host 'DRY RUN: would query scheduled tasks under \\Jarvis\\, Notepad/ping started after the script start, and the eval sandbox.'
} else {
    Write-Host '=== Final cleanup check ==='
    Write-Host 'Scheduled tasks under \\Jarvis\\:'
    try { Get-ScheduledTask -TaskPath '\Jarvis\' -ErrorAction Stop | Select-Object TaskPath,TaskName,State } catch { Write-Host $_.Exception.Message }
    Write-Host 'Notepad/ping processes started after this script began:'
    Get-Process -Name notepad,ping -ErrorAction SilentlyContinue |
        Where-Object { $_.StartTime -ge $startedAt } |
        Select-Object Id,ProcessName,StartTime,HasExited
    Write-Host "Eval sandbox: $sandbox"
    if (Test-Path $sandbox) { Get-ChildItem $sandbox -Directory | Select-Object FullName }
}
