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
$realSchedulerTotals = @()
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
    foreach ($match in [regex]::Matches($Text, $pattern)) {
        $project = [IO.Path]::GetFileNameWithoutExtension($match.Groups[5].Value)
        $totals[$project] = [PSCustomObject]@{
            Failed = [int]$match.Groups[1].Value
            Passed = [int]$match.Groups[2].Value
            Skipped = [int]$match.Groups[3].Value
            Total = [int]$match.Groups[4].Value
        }
    }
    return $totals
}

try {
    Push-Location $repoRoot
    try {
        $branch = (git branch --show-current).Trim()
        $head = (git rev-parse HEAD).Trim()
        $status = @(git status --short)
        $productionDiff = @(git diff --unified=0 5b68602..HEAD -- windows-host/src ':!windows-host/src/Jarvis.Eval')

        $preexistingAppProcesses = @(Get-Process -Name notepad,ping -ErrorAction SilentlyContinue |
            ForEach-Object {
                try {
                    [PSCustomObject]@{
                        ProcessName = $_.ProcessName
                        PID = $_.Id
                        StartTime = $_.StartTime
                        WindowTitle = $_.MainWindowTitle
                    }
                } catch {
                    throw "Could not safely inspect a pre-existing Notepad/ping process: $($_.Exception.Message)"
                }
            })
        if ($preexistingAppProcesses.Count -gt 0) {
            $details = ($preexistingAppProcesses | ForEach-Object {
                "$($_.ProcessName) PID=$($_.PID) START=$($_.StartTime.ToUniversalTime().ToString('o')) TITLE=$($_.WindowTitle)"
            }) -join '; '
            throw "Gate aborted before tests because pre-existing Notepad/ping processes exist: $details. Close only processes you own and rerun."
        }

        Remove-Item Env:\JARVIS_REAL_SCHEDULER -ErrorAction SilentlyContinue
        for ($run = 1; $run -le 3; $run++) {
            $result = Invoke-Bounded "solution-run-$run" 'dotnet' @(
                'test', $solution, '--configuration', 'Release', '--no-build', '--no-restore',
                '--disable-build-servers', '-m:1', '--filter', 'Category!=RealScheduler',
                '--logger', 'console;verbosity=minimal'
            ) $windowsHost
            if ($result.ExitCode -ne 0) { throw "Non-real solution test run $run failed; see $($result.Log)" }
            $runTotals += [PSCustomObject]@{ Run = $run; Projects = (Get-TestTotals $result.Stdout) }
        }

        $env:JARVIS_REAL_SCHEDULER = '1'
        $schedulerResult = Invoke-Bounded 'real-scheduler' 'dotnet' @(
            'test', $solution, '--configuration', 'Release', '--no-build', '--no-restore',
            '--disable-build-servers', '-m:1', '--filter', 'Category=RealScheduler',
            '--logger', 'console;verbosity=minimal'
        ) $windowsHost
        $realSchedulerTotals = Get-TestTotals $schedulerResult.Stdout
        if ($schedulerResult.ExitCode -ne 0) { throw "RealScheduler run failed; see $($schedulerResult.Log)" }

        for ($run = 1; $run -le 2; $run++) {
            $checkResult = Invoke-Bounded "check-all-$run" 'powershell.exe' @(
                '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $checkAll
            ) $repoRoot
            $checkAllExitCodes += $checkResult.ExitCode
        }

        Write-Host "branch=$branch"
        Write-Host "HEAD=$head"
        Write-Host 'app_process_preflight=clear'
        Write-Host "git_status_short=$($status -join ' | ')"
        Write-Host "production_diff=$($productionDiff -join ' | ')"
        Write-Host "release_non_real_totals=$($runTotals | ConvertTo-Json -Compress -Depth 8)"
        $serializedRuns = @($runTotals | ForEach-Object { ConvertTo-Json $_.Projects -Compress -Depth 8 })
        $totalsDiffer = (@($serializedRuns | Select-Object -Unique).Count -gt 1)
        Write-Host "release_totals_differ=$totalsDiffer"
        Write-Host "real_scheduler_totals=$(ConvertTo-Json $realSchedulerTotals -Compress -Depth 8)"
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
        if ($totalsDiffer) { Write-Warning "Per-project totals differ across the three full Release solution runs. Logs: $logRoot" }
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
