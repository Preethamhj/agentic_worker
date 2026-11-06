[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$windowsHost = Join-Path $repoRoot 'windows-host'
$solution = Join-Path $windowsHost 'Jarvis.sln'
$checkAll = Join-Path $repoRoot 'scripts\check-all.ps1'
$runId = [Guid]::NewGuid().ToString('N')
$logRoot = Join-Path $env:TEMP "jarvis-gate-$runId"
New-Item -ItemType Directory -Force -Path $logRoot | Out-Null
$originalSchedulerSetting = $env:JARVIS_REAL_SCHEDULER
$startedProcesses = @()
$runTotals = @()
$checkAllExitCodes = @()

function Quote-Argument([string]$Value) {
    if ($Value.Contains('"')) { $Value = $Value.Replace('"', '\"') }
    if ($Value -match '\s') { return '"' + $Value + '"' }
    return $Value
}

function Invoke-Bounded([string]$Name, [string]$FilePath, [string[]]$Arguments, [string]$WorkingDirectory, [int]$TimeoutSeconds = 300) {
    $stdoutPath = Join-Path $logRoot "$Name.stdout.log"
    $stderrPath = Join-Path $logRoot "$Name.stderr.log"
    $argumentLine = ($Arguments | ForEach-Object { Quote-Argument $_ }) -join ' '
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $FilePath
    $startInfo.Arguments = $argumentLine
    $startInfo.WorkingDirectory = $WorkingDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $startInfo
    if (-not $process.Start()) { throw "Could not start $Name ($FilePath)." }
    $startTime = $process.StartTime
    $script:startedProcesses += [PSCustomObject]@{ PID = $process.Id; StartTime = $startTime; Name = $Name }
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        try {
            $current = Get-Process -Id $process.Id -ErrorAction Stop
            if ($current.StartTime -eq $startTime) { $current.Kill() }
        } catch [System.ArgumentException] {
        } catch [System.InvalidOperationException] {
        }
        throw "$Name exceeded its ${TimeoutSeconds}s timeout; only its own recorded PID was terminated. Logs: $logRoot"
    }
    $process.WaitForExit()
    $process.Refresh()
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    Set-Content -LiteralPath $stdoutPath -Value $stdout -Encoding UTF8
    Set-Content -LiteralPath $stderrPath -Value $stderr -Encoding UTF8
    [PSCustomObject]@{ ExitCode = [int]$process.ExitCode; Stdout = $stdout; Stderr = $stderr; Log = $stdoutPath }
}

function Get-TestTotals([string]$Text) {
    $totals = [ordered]@{}
    $pattern = 'Passed!\s+- Failed:\s*(\d+), Passed:\s*(\d+), Skipped:\s*(\d+), Total:\s*(\d+).*? - ([^ ]+\.dll)'
    $summaryMatches = [regex]::Matches($Text, $pattern)
    foreach ($match in $summaryMatches) {
        $project = [IO.Path]::GetFileNameWithoutExtension($match.Groups[5].Value)
        $totals[$project] = [PSCustomObject]@{
            Failed = [int]$match.Groups[1].Value
            Passed = [int]$match.Groups[2].Value
            Skipped = [int]$match.Groups[3].Value
            Total = [int]$match.Groups[4].Value
        }
    }
    if ($summaryMatches.Count -eq 0) {
        $blocks = [regex]::Split($Text, '(?im)(?=^\s*Test run for )')
        foreach ($block in $blocks) {
            $assemblyMatch = [regex]::Match($block, '(?im)^\s*Test run for .*[\\/](?<assembly>[^\\/\s]+)\.dll\b')
            $totalMatch = [regex]::Match($block, '(?im)^\s*Total tests:\s*(?<total>\d+)')
            if (-not $assemblyMatch.Success -or -not $totalMatch.Success) { continue }
            $project = $assemblyMatch.Groups['assembly'].Value
            $failed = 0
            $passed = 0
            $skipped = 0
            $failedMatch = [regex]::Match($block, '(?im)^\s*Failed:\s*(\d+)')
            $passedMatch = [regex]::Match($block, '(?im)^\s*Passed:\s*(\d+)')
            $skippedMatch = [regex]::Match($block, '(?im)^\s*Skipped:\s*(\d+)')
            if ($failedMatch.Success) { $failed = [int]$failedMatch.Groups[1].Value }
            if ($passedMatch.Success) { $passed = [int]$passedMatch.Groups[1].Value }
            if ($skippedMatch.Success) { $skipped = [int]$skippedMatch.Groups[1].Value }
            $totals[$project] = [PSCustomObject]@{
                Failed = $failed
                Passed = $passed
                Skipped = $skipped
                Total = [int]$totalMatch.Groups['total'].Value
            }
        }
    }
    return $totals
}

function Get-NotepadSnapshot {
    $snapshot = @()
    foreach ($process in @(Get-Process -Name notepad -ErrorAction SilentlyContinue)) {
        try {
            $snapshot += [PSCustomObject]@{
                PID = $process.Id
                StartTimeTicks = $process.StartTime.ToUniversalTime().Ticks
            }
        } catch {
            throw "Could not safely inspect Notepad PID $($process.Id): $($_.Exception.Message)"
        }
    }
    return ,$snapshot
}

function Write-NotepadSnapshot([string]$Label, [object[]]$Snapshot) {
    $details = @($Snapshot | ForEach-Object { "$($_.PID):$($_.StartTimeTicks)" }) -join ','
    Write-Host "notepad_processes_${Label}=$details"
}

function Assert-NoNewNotepad([string]$Label, [object[]]$Before, [object[]]$After) {
    $prior = @{}
    foreach ($process in $Before) {
        $prior["$($process.PID):$($process.StartTimeTicks)"] = $true
    }
    $newProcesses = @($After | Where-Object {
        -not $prior.ContainsKey("$($_.PID):$($_.StartTimeTicks)")
    })
    if ($newProcesses.Count -gt 0) {
        $details = ($newProcesses | ForEach-Object {
            "PID=$($_.PID) START_TICKS=$($_.StartTimeTicks)"
        }) -join '; '
        throw "$Label created or left Notepad process(es): $details. Existing Notepad processes are allowed; the gate does not terminate any process."
    }
}

function Write-TestFailureDetails([string]$Text) {
    $lines = $Text -split "`r?`n"
    $foundFailure = $false
    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ($lines[$index] -match '^\s*Failed\s+(.+?)(?:\s+\[[^\]]+\])?\s*$') {
            $foundFailure = $true
            Write-Host "Failing test: $($matches[1])" -ForegroundColor Red
            $printed = 0
            for ($detailIndex = $index + 1; $detailIndex -lt $lines.Count -and $printed -lt 6; $detailIndex++) {
                if (-not [string]::IsNullOrWhiteSpace($lines[$detailIndex])) {
                    Write-Host "  $($lines[$detailIndex])"
                    $printed++
                }
            }
        }
    }
    if (-not $foundFailure) {
        Write-Host 'No individual failed-test line was present; first six error lines:' -ForegroundColor Red
        $errorLines = @($lines | Where-Object { $_ -match '(?i)\b(error|exception|failed)\b' } | Select-Object -First 6)
        if ($errorLines.Count -eq 0) { $errorLines = @($lines | Select-Object -Last 6) }
        foreach ($line in $errorLines) { Write-Host "  $line" }
    }
}

try {
    Push-Location $repoRoot
    try {
        $branch = (git branch --show-current).Trim()
        $head = (git rev-parse HEAD).Trim()
        $status = @(git status --short)
        $productionDiff = @(git diff --unified=0 5b68602..HEAD -- windows-host/src ':!windows-host/src/Jarvis.Eval')

        $buildResult = Invoke-Bounded 'release-build' 'dotnet' @(
            'build', $solution, '--configuration', 'Release', '--no-incremental',
            '--disable-build-servers', '-m:1'
        ) $windowsHost
        if ($buildResult.ExitCode -ne 0) {
            throw "Release rebuild failed; see $($buildResult.Log)"
        }

        Remove-Item Env:\JARVIS_REAL_SCHEDULER -ErrorAction SilentlyContinue
        $initialNotepadSnapshot = @(Get-NotepadSnapshot)
        Write-NotepadSnapshot 'before' $initialNotepadSnapshot
        for ($run = 1; $run -le 3; $run++) {
            $result = Invoke-Bounded "solution-run-$run" 'dotnet' @(
                'test', $solution, '--configuration', 'Release', '--no-build', '--no-restore',
                '--disable-build-servers', '-m:1', '--filter', 'Category!=RealScheduler',
                '--logger', 'console;verbosity=normal'
            ) $windowsHost
            $afterRunNotepad = @(Get-NotepadSnapshot)
            Write-NotepadSnapshot "after_run_$run" $afterRunNotepad
            Assert-NoNewNotepad "Non-real solution test run $run" $initialNotepadSnapshot $afterRunNotepad
            if ($result.ExitCode -ne 0) {
                Write-TestFailureDetails ($result.Stdout + "`n" + $result.Stderr)
                throw "Non-real solution test run $run failed; see $($result.Log)"
            }
            $runTotals += [PSCustomObject]@{ Run = $run; Projects = (Get-TestTotals $result.Stdout) }
        }

        if (@($runTotals | Where-Object { $_.Projects.Count -eq 0 }).Count -gt 0) {
            throw "Could not parse per-project test totals from one or more solution runs. Logs: $logRoot"
        }
        $serializedRuns = @($runTotals | ForEach-Object { ConvertTo-Json $_.Projects -Compress -Depth 8 })
        $totalsDiffer = (@($serializedRuns | Select-Object -Unique).Count -gt 1)
        Write-Host "release_non_real_totals=$($runTotals | ConvertTo-Json -Compress -Depth 8)"
        Write-Host "release_totals_differ=$totalsDiffer"
        if ($totalsDiffer) {
            throw "Per-project totals differ across the three full Release solution runs. Logs: $logRoot"
        }

        for ($run = 1; $run -le 2; $run++) {
            $beforeCheckAllNotepad = @(Get-NotepadSnapshot)
            $checkResult = Invoke-Bounded "check-all-$run" 'powershell.exe' @(
                '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $checkAll
            ) $repoRoot
            $checkAllExitCodes += $checkResult.ExitCode
            $afterCheckAllNotepad = @(Get-NotepadSnapshot)
            Write-NotepadSnapshot "after_check_all_$run" $afterCheckAllNotepad
            Assert-NoNewNotepad "check-all run $run" $beforeCheckAllNotepad $afterCheckAllNotepad
            if ($checkResult.ExitCode -ne 0) {
                Write-TestFailureDetails ($checkResult.Stdout + "`n" + $checkResult.Stderr)
                throw "check-all run $run failed; see $($checkResult.Log)"
            }
        }

        Write-Host "branch=$branch"
        Write-Host "HEAD=$head"
        Write-Host 'preexisting_notepad_processes_are_allowed=true'
        Write-Host "git_status_short=$($status -join ' | ')"
        Write-Host "production_diff=$($productionDiff -join ' | ')"
        Write-Host "check_all_exit_codes=$($checkAllExitCodes -join ',')"
        Write-Host 'cleanup_tasks_under_Jarvis:'
        try { Get-ScheduledTask -TaskPath '\Jarvis\' -ErrorAction Stop | Select-Object TaskPath,TaskName,State | ConvertTo-Json -Compress } catch { Write-Host $_.Exception.Message }
        Write-Host 'cleanup_processes:'
        Get-Process -Name notepad,calc,CalculatorApp,mspaint,ping -ErrorAction SilentlyContinue |
            Select-Object Id,ProcessName,StartTime |
            ConvertTo-Json -Compress
        $sandbox = Join-Path $env:LOCALAPPDATA 'Jarvis\eval-sandbox'
        $reminders = Join-Path $env:LOCALAPPDATA 'Jarvis\reminders'
        Write-Host "cleanup_eval_sandbox=$sandbox exists=$(Test-Path $sandbox)"
        if (Test-Path $sandbox) { Get-ChildItem $sandbox -Directory | Select-Object Name | ConvertTo-Json -Compress }
        Write-Host "cleanup_reminders=$reminders exists=$(Test-Path $reminders)"
        if (Test-Path $reminders) { Get-ChildItem $reminders -File | Select-Object Name,Length | ConvertTo-Json -Compress }
        Write-Host "processes_started_by_gate=$(ConvertTo-Json $startedProcesses -Compress -Depth 5)"
        Write-Host 'CI: check the Actions tab yourself'
    } finally {
        Pop-Location
    }
} finally {
    if ($null -eq $originalSchedulerSetting) {
        Remove-Item Env:\JARVIS_REAL_SCHEDULER -ErrorAction SilentlyContinue
    } else {
        $env:JARVIS_REAL_SCHEDULER = $originalSchedulerSetting
    }
}
