[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$settingsPath = Join-Path $repoRoot 'config\settings.json'
$settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
$runId = [Guid]::NewGuid().ToString('N')
$liveRoot = Join-Path $env:LOCALAPPDATA "Jarvis\workspace\live-tests\$runId"
$pythonServer = $null
$pythonStartTime = $null
$originalNoBytecode = $env:PYTHONDONTWRITEBYTECODE
$originalTemp = $env:TEMP
$originalTmp = $env:TMP
$registeredTask = $false
$taskName = "reminder-live-$runId"
$taskSchedulerFolder = $null
$checkFailures = 0
$healthCheckError = ''

function Write-Check([string]$Name, [bool]$Passed, [string]$Details, [string]$Remedy) {
    if ($Passed) {
        Write-Host "[PASS] $Name - $Details" -ForegroundColor Green
        return
    }

    $script:checkFailures++
    Write-Host "[FAIL] $Name - $Details" -ForegroundColor Red
    Write-Host "       Remedy: $Remedy"
}

function Test-LoopbackUrl([string]$Url) {
    $uri = [Uri]$Url
    return $uri.IsAbsoluteUri -and $uri.Scheme -eq 'http' -and $uri.Host -eq '127.0.0.1'
}

function Get-HealthResponse([string]$Url) {
    try {
        $response = Invoke-RestMethod -Uri $Url -Method Get -TimeoutSec 4 -ErrorAction Stop
        if ($null -eq $response -or $response.status -ne 'ok') {
            $script:healthCheckError = "Health endpoint returned status '$($response.status)'."
        }
        return $response
    } catch {
        $script:healthCheckError = $_.Exception.Message
        return $null
    }
}

function Wait-HealthResponse([string]$Url, [int]$TimeoutSeconds) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $health = Get-HealthResponse $Url
        if ($null -ne $health -and $health.status -eq 'ok') {
            return $health
        }
        Start-Sleep -Milliseconds 500
    } while ([DateTime]::UtcNow -lt $deadline)
    return $null
}

function Write-ServerLogTail([string]$Name, [string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    Write-Host "$Name (last 30 lines):"
    Get-Content -LiteralPath $Path -Tail 30 | ForEach-Object { Write-Host "       $_" }
}

function Stop-OwnedProcess {
    if ($null -eq $script:pythonServer) {
        return
    }

    try {
        $current = $script:pythonServer
        if (-not $current.HasExited -and $current.Id -eq $script:pythonServer.Id -and $current.StartTime.ToUniversalTime() -eq $script:pythonStartTime) {
            $current.Kill()
            if (-not $current.WaitForExit(5000)) {
                throw "Python AI server PID $($current.Id) did not exit within five seconds."
            }
        }
    } finally {
        $script:pythonServer.Dispose()
        $script:pythonServer = $null
    }
}

function Test-SchedulerCreateDelete {
    if ($env:OS -ne 'Windows_NT') {
        throw 'Windows Task Scheduler is unavailable on this operating system.'
    }

    $service = New-Object -ComObject 'Schedule.Service'
    $service.Connect()
    $root = $service.GetFolder('\')
    try {
        $script:taskSchedulerFolder = $service.GetFolder('\Jarvis')
    } catch [System.Runtime.InteropServices.COMException] {
        $script:taskSchedulerFolder = $root.CreateFolder('Jarvis', $null)
    }

    $definition = $service.NewTask(0)
    $definition.RegistrationInfo.Author = 'Jarvis live-check'
    $definition.RegistrationInfo.Description = 'Temporary no-op capability check; never starts.'
    $definition.Principal.UserId = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
    $definition.Principal.LogonType = 3
    $definition.Principal.RunLevel = 0
    $definition.Settings.Enabled = $true
    $definition.Settings.StartWhenAvailable = $false
    $trigger = $definition.Triggers.Create(1)
    $trigger.Enabled = $true
    $trigger.StartBoundary = [DateTime]::Now.AddDays(1).ToString('s')
    $action = $definition.Actions.Create(0)
    $action.Path = $env:ComSpec
    $action.Arguments = '/d /c exit 0'

    $user = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
    [void]$script:taskSchedulerFolder.RegisterTaskDefinition(
        $script:taskName,
        $definition,
        6,
        $user,
        $null,
        3,
        $null)
    $script:registeredTask = $true
    if ($null -eq $script:taskSchedulerFolder.GetTask($script:taskName)) {
        throw 'Task Scheduler returned no task after registration.'
    }
}

New-Item -ItemType Directory -Force -Path $liveRoot | Out-Null
try {
    $configuredModel = [string]$settings.model
    $ollamaUrl = [string]$settings.ollama_host
    $ollamaTagsUrl = "$($ollamaUrl.TrimEnd('/'))/api/tags"
    $ollamaReachable = $false
    $modelPresent = $false
    $ollamaDetails = 'Not checked.'
    $ollamaConfigValid = $false
    try {
        $ollamaUri = [Uri]$ollamaUrl
        $ollamaConfigValid = (Test-LoopbackUrl $ollamaUrl) -and $ollamaUri.Port -eq 11434
    } catch {
        $ollamaDetails = 'Configured Ollama URL is invalid.'
    }
    if ($ollamaConfigValid) {
        try {
            $tags = Invoke-RestMethod -Uri $ollamaTagsUrl -Method Get -TimeoutSec 4 -ErrorAction Stop
            $ollamaReachable = $true
            $modelPresent = @($tags.models | Where-Object { $_.name -ceq $configuredModel }).Count -gt 0
            $ollamaDetails = "Configured model '$configuredModel'."
        } catch {
            $ollamaDetails = $_.Exception.Message
        }
    } else {
        $ollamaDetails = "Configured Ollama URL must use http://127.0.0.1."
    }
    Write-Check 'Ollama loopback endpoint' $ollamaReachable $ollamaDetails "Start the local Ollama service on 127.0.0.1:11434; do not expose it on a network interface."
    if (-not $modelPresent) {
        Write-Check 'Configured Ollama model' $false "Model '$configuredModel' was not found in /api/tags." "If you choose to install it, run: ollama pull $configuredModel"
    } else {
        Write-Check 'Configured Ollama model' $true "'$configuredModel' is present in /api/tags." ''
    }

    $port = [int]$settings.ai_server_port
    $healthUrl = "http://127.0.0.1:$port/health"
    $pythonPath = Join-Path $repoRoot 'ai-server\.venv\Scripts\python.exe'
    $health = $null
    $serverHost = [string]$settings.ai_server_host
    if ($serverHost -ne '127.0.0.1' -or -not (Test-LoopbackUrl $healthUrl)) {
        Write-Check 'AI server loopback binding' $false "Configured host '$serverHost' and health URL '$healthUrl' must be loopback-only." 'Set ai_server_host in config\settings.json to 127.0.0.1.'
        Write-Check 'Python AI server starts and answers health' $false 'Refused to start a server with a non-loopback configured host.' 'Set ai_server_host in config\settings.json to 127.0.0.1.'
    } elseif (Test-Path -LiteralPath $pythonPath) {
        $health = Get-HealthResponse $healthUrl
        $serverStartError = $null
        if ($null -eq $health) {
            $stdoutPath = Join-Path $liveRoot 'ai-server.stdout.log'
            $stderrPath = Join-Path $liveRoot 'ai-server.stderr.log'
            $arguments = "-m uvicorn jarvis_ai.api.main:app --host 127.0.0.1 --port $port"
            $env:PYTHONDONTWRITEBYTECODE = '1'
            $env:TEMP = $liveRoot
            $env:TMP = $liveRoot
            try {
                try {
                    $pythonServer = Start-Process -FilePath $pythonPath -ArgumentList $arguments `
                        -WorkingDirectory (Join-Path $repoRoot 'ai-server') `
                        -RedirectStandardOutput $stdoutPath -RedirectStandardError $stderrPath `
                        -PassThru
                } catch {
                    $serverStartError = $_.Exception.Message
                }
            } finally {
                $env:PYTHONDONTWRITEBYTECODE = $originalNoBytecode
                $env:TEMP = $originalTemp
                $env:TMP = $originalTmp
            }
            if ($null -ne $pythonServer) {
                $pythonStartTime = $pythonServer.StartTime.ToUniversalTime()
                $health = Wait-HealthResponse $healthUrl 30
            }
        }
        $healthy = $null -ne $health -and $health.status -eq 'ok'
        $details = if ($healthy) {
            "Health endpoint returned status '$($health.status)' and LLM '$($health.llm)'."
        } elseif ($null -ne $serverStartError) {
            "Could not start Python server: $serverStartError"
        } else {
            "Health endpoint did not return status 'ok' within 30 seconds. Last error: $healthCheckError"
        }
        if (-not $healthy -and $null -ne $pythonServer) {
            Write-ServerLogTail 'Python AI server stdout' $stdoutPath
            Write-ServerLogTail 'Python AI server stderr' $stderrPath
        }
        Write-Check 'Python AI server starts and answers health' $healthy $details "Check port $port and the emitted server log tail; then run: `"$pythonPath`" -m uvicorn jarvis_ai.api.main:app --host 127.0.0.1 --port $port"
    } else {
        Write-Check 'Python AI server starts and answers health' $false "Python venv not found at '$pythonPath'." "Run scripts\setup-windows.ps1 and retry."
    }

    $cliAssembly = Join-Path $repoRoot 'windows-host\src\Jarvis.Cli\bin\Release\net10.0-windows\Jarvis.Cli.dll'
    $doctorOutput = ''
    $doctorPassed = $false
    if (Test-Path -LiteralPath $cliAssembly) {
        $previousTemp = $env:TEMP
        $previousTmp = $env:TMP
        $doctorExitCode = 1
        try {
            $env:TEMP = $liveRoot
            $env:TMP = $liveRoot
            $doctorOutput = (& dotnet $cliAssembly doctor 2>&1 | Out-String)
            $doctorExitCode = $LASTEXITCODE
            $doctorPassed = $doctorExitCode -eq 0
        } finally {
            $env:TEMP = $previousTemp
            $env:TMP = $previousTmp
        }
        Write-Host $doctorOutput.TrimEnd()
        Write-Check 'jarvis doctor' $doctorPassed "Doctor exit code: $doctorExitCode." "Build the Release CLI and resolve each [FAIL] line printed by doctor."
    } else {
        Write-Check 'jarvis doctor' $false "CLI assembly not found at '$cliAssembly'." 'Run: dotnet build windows-host\src\Jarvis.Cli\Jarvis.Cli.csproj --configuration Release --disable-build-servers -m:1'
    }

    $workspaceRoot = [Environment]::GetEnvironmentVariable('JARVIS_ALLOWED_ROOT')
    if ([string]::IsNullOrWhiteSpace($workspaceRoot)) {
        $workspaceRoot = Join-Path $env:LOCALAPPDATA 'Jarvis\workspace'
    } else {
        $workspaceRoot = [Environment]::ExpandEnvironmentVariables($workspaceRoot)
    }
    $workspaceExists = Test-Path -LiteralPath $workspaceRoot -PathType Container
    Write-Check 'Jarvis workspace root' $workspaceExists "Configured root: '$workspaceRoot'." 'Run jarvis doctor once to initialize the configured workspace root, or correct JARVIS_ALLOWED_ROOT.'

    $dpapiPassed = $doctorOutput -match '\[PASS\] Windows DPAPI key store operational'
    Write-Check 'DPAPI key store' $dpapiPassed 'jarvis doctor performs a DPAPI-backed key-store check.' 'Run jarvis doctor in a normal, non-elevated Windows user session and resolve its DPAPI error.'

    $schedulerPassed = $false
    $schedulerDetails = ''
    try {
        Test-SchedulerCreateDelete
        $schedulerPassed = $true
        $schedulerDetails = "Created '$taskName' under \Jarvis; it is scheduled one day ahead and is never started."
    } catch {
        $schedulerDetails = $_.Exception.Message
    } finally {
        if ($registeredTask -and $null -ne $taskSchedulerFolder) {
            try {
                $taskSchedulerFolder.DeleteTask($taskName, 0)
                $registeredTask = $false
            } catch [System.Runtime.InteropServices.COMException] {
                $schedulerPassed = $false
                $schedulerDetails = "Cleanup failed for '$taskName': $($_.Exception.Message)"
            }
        }
    }
    Write-Check 'Task Scheduler create/delete' $schedulerPassed $schedulerDetails 'Use a normal Windows user session and allow per-user task registration under \Jarvis; inspect Task Scheduler permissions.'

    $notifierPath = Join-Path $repoRoot 'windows-host\src\Jarvis.Cli\ReminderDispatcher.cs'
    $notifierPresent = Test-Path -LiteralPath $notifierPath
    if ($notifierPresent) {
        $notifierSource = Get-Content -LiteralPath $notifierPath -Raw
        $notifierPresent = $notifierSource.Contains('class WindowsReminderNotifier')
            -and $notifierSource.Contains('ShowBalloonTip')
    }
    Write-Check 'Reminder notifier mechanism' $notifierPresent 'WindowsReminderNotifier uses a Windows notification icon balloon.' 'Restore the WindowsReminderNotifier implementation and its visible notification mechanism.'

    Write-Host ''
    Write-Host "Planner default: rule-based fast path; configured Ollama model '$configuredModel' is the fallback for unmatched requests."
    Write-Host 'The live check never downloads or installs a model.'
    if ($checkFailures -gt 0) {
        Write-Host "$checkFailures live-check item(s) failed." -ForegroundColor Red
        exit 1
    }
    Write-Host 'All live-check items passed.' -ForegroundColor Green
    exit 0
} finally {
    if ($registeredTask -and $null -ne $taskSchedulerFolder) {
        try {
            $taskSchedulerFolder.DeleteTask($taskName, 0)
        } catch {
            Write-Error "Could not remove temporary Task Scheduler task '$taskName': $($_.Exception.Message)"
        }
    }
    try {
        Stop-OwnedProcess
    } finally {
        $env:PYTHONDONTWRITEBYTECODE = $originalNoBytecode
        $env:TEMP = $originalTemp
        $env:TMP = $originalTmp
        if (Test-Path -LiteralPath $liveRoot) {
            Remove-Item -LiteralPath $liveRoot -Recurse -Force
        }
    }
}
