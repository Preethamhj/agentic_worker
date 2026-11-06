[CmdletBinding()]
param(
    [int]$Reps = 2,
    [string]$Out = '',
    [switch]$DryRun,
    [switch]$RealReminderSubset,
    [switch]$AllowInRepo
)

$ErrorActionPreference = 'Stop'
$startedAt = Get-Date
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path.TrimEnd('\')
if ([string]::IsNullOrWhiteSpace($Out)) {
    $Out = Join-Path $env:LOCALAPPDATA ("Jarvis\eval-results\" + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}
$outRoot = [IO.Path]::GetFullPath($Out)
$repoPrefix = $repoRoot + '\'
if (-not $AllowInRepo -and ($outRoot.Equals($repoRoot, [StringComparison]::OrdinalIgnoreCase) -or $outRoot.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase))) {
    throw "Output path is inside the repository; choose another path or explicitly pass -AllowInRepo."
}
if ($Reps -lt 1) { throw '-Reps must be at least 1.' }
if ($RealReminderSubset -and $env:JARVIS_REAL_SCHEDULER -ne '1') {
    throw '-RealReminderSubset requires JARVIS_REAL_SCHEDULER=1.'
}

$dll = Join-Path $repoRoot 'windows-host\src\Jarvis.Eval\bin\Release\net10.0-windows\Jarvis.Eval.dll'
$python = Join-Path $repoRoot 'ai-server\.venv\Scripts\python.exe'
$analyzer = Join-Path $repoRoot 'eval\analyze.py'
if (-not $DryRun) {
    if (-not (Test-Path $dll)) { throw "Eval DLL not found: $dll" }
    if (-not (Test-Path $python)) { throw "Python venv not found: $python" }
    New-Item -ItemType Directory -Force -Path $outRoot | Out-Null
}

$preexisting = @()
if (-not $DryRun) {
    $preexisting = @(Get-Process -Name Jarvis.TestApp,ping -ErrorAction SilentlyContinue |
        ForEach-Object {
            try {
                [PSCustomObject]@{
                    ProcessName = $_.ProcessName
                    Id = $_.Id
                    StartTime = $_.StartTime
                    WindowTitle = $_.MainWindowTitle
                }
            } catch {
                throw "Could not safely inspect a pre-existing Jarvis.TestApp/ping process: $($_.Exception.Message)"
            }
        })
    if ($preexisting.Count -gt 0) {
        $details = ($preexisting | ForEach-Object {
            "$($_.ProcessName) PID=$($_.Id) StartTime=$($_.StartTime.ToUniversalTime().ToString('o')) WindowTitle=$($_.WindowTitle)"
        }) -join '; '
        throw "App evaluation aborted because Jarvis.TestApp/ping processes already exist: $details. Close only processes you own, then rerun."
    }
} else {
    Write-Host 'DRY RUN: no directories or files will be created.'
    Write-Host 'DRY RUN: would preflight Jarvis.TestApp.exe and ping.exe and stop if either exists.'
}

$tools = @(
    @{ Name = 'fs.create_file'; Directory = 'fs-create-file' },
    @{ Name = 'fs.create_folder'; Directory = 'fs-create-folder' },
    @{ Name = 'fs.search_files'; Directory = 'fs-search-files' },
    @{ Name = 'app.launch'; Directory = 'app-launch' },
    @{ Name = 'task.schedule_reminder'; Directory = 'task-schedule-reminder' }
)
if ($RealReminderSubset) {
    $tools = @($tools | Where-Object { $_.Name -eq 'task.schedule_reminder' })
}

foreach ($tool in $tools) {
    $toolOut = Join-Path $outRoot $tool.Directory
    $log = Join-Path $toolOut 'run.log'
    $repetitions = if ($RealReminderSubset) { 1 } else { $Reps }
    $scheduler = if ($RealReminderSubset) { 'real' } else { 'fake' }
    $arguments = @($dll, 'run', '--seed', '20261025', '--reps', $repetitions, '--tools', $tool.Name, '--scheduler', $scheduler, '--out', $toolOut)
    $display = 'dotnet ' + (($arguments | ForEach-Object {
        if ($_ -match '\s') { '"' + $_ + '"' } else { $_ }
    }) -join ' ')
    if ($DryRun) {
        Write-Host $display
        continue
    }
    New-Item -ItemType Directory -Force -Path $toolOut | Out-Null
    & dotnet @arguments *> $log
    if ($LASTEXITCODE -ne 0) { throw "Eval failed for $($tool.Name); see $log" }
}

$analysisArguments = @($analyzer, $outRoot, '--out', $outRoot)
$analysisDisplay = '"' + $python + '" ' + (($analysisArguments | ForEach-Object {
    if ($_ -match '\s') { '"' + $_ + '"' } else { $_ }
}) -join ' ')
if ($DryRun) {
    Write-Host $analysisDisplay
    Write-Host "Output directory: $outRoot"
    Write-Host 'DRY RUN: would perform one combined analysis over every raw.jsonl under the output directory.'
    Write-Host 'DRY RUN: would query scheduled tasks under \Jarvis\, notepad/calc/CalculatorApp/mspaint/ping processes, eval sandbox, and reminders folder.'
    exit 0
}

& $python @analysisArguments *> (Join-Path $outRoot 'analyze.log')
if ($LASTEXITCODE -ne 0) { throw "Combined analysis failed; see $(Join-Path $outRoot 'analyze.log')" }

Write-Host '=== Final cleanup check ==='
Write-Host 'Scheduled tasks under \Jarvis\:'
try { Get-ScheduledTask -TaskPath '\Jarvis\' -ErrorAction Stop | Select-Object TaskPath,TaskName,State } catch { Write-Host $_.Exception.Message }
Write-Host 'Notepad/calc/CalculatorApp/mspaint/ping processes started after this script began:'
Get-Process -Name notepad,calc,CalculatorApp,mspaint,ping -ErrorAction SilentlyContinue |
    Where-Object { $_.StartTime -ge $startedAt } |
    Select-Object Id,ProcessName,StartTime,HasExited
$sandbox = Join-Path $env:LOCALAPPDATA 'Jarvis\eval-sandbox'
Write-Host "Eval sandbox: $sandbox"
if (Test-Path $sandbox) { Get-ChildItem $sandbox -Directory | Select-Object FullName }
$reminders = Join-Path $env:LOCALAPPDATA 'Jarvis\reminders'
Write-Host "Reminders folder: $reminders"
if (Test-Path $reminders) { Get-ChildItem $reminders -File | Select-Object Name,Length }
Write-Host "Combined results: $outRoot"
