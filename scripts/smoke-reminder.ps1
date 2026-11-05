[CmdletBinding()]
param(
    [ValidateRange(2, 10)]
    [int]$DelayMinutes = 2
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $repoRoot "windows-host\src\Jarvis.Cli\Jarvis.Cli.csproj"
$publishDir = Join-Path $repoRoot "windows-host\src\Jarvis.Cli\bin\Release\net10.0-windows"
$cliPath = Join-Path $publishDir "Jarvis.Cli.exe"
$taskPath = $null
$dataPath = $null

try {
    Push-Location $repoRoot
    try {
        dotnet publish $projectPath --configuration Release --output $publishDir
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed with exit code $LASTEXITCODE."
        }
    } finally {
        Pop-Location
    }

    if (-not (Test-Path -LiteralPath $cliPath -PathType Leaf)) {
        throw "Published CLI apphost was not found at '$cliPath'."
    }

    $beforeTaskPaths = @(
        Get-ScheduledTask -TaskPath "\Jarvis\" -ErrorAction SilentlyContinue |
            ForEach-Object { "\Jarvis\$($_.TaskName)" }
    )
    $prompt = "remind me Jarvis smoke reminder in $DelayMinutes minutes"
    Write-Host "Published executable: $cliPath"
    Write-Host "Prompt: $prompt"
    $cliOutput = "y" | & $cliPath run $prompt 2>&1
    $cliExitCode = $LASTEXITCODE
    $cliOutput | ForEach-Object { Write-Host $_ }
    if ($cliExitCode -ne 0) {
        throw "Published CLI exited with code $cliExitCode."
    }

    $newTasks = @(
        Get-ScheduledTask -TaskPath "\Jarvis\" -ErrorAction SilentlyContinue |
            Where-Object { "\Jarvis\$($_.TaskName)" -notin $beforeTaskPaths }
    )
    if ($newTasks.Count -ne 1) {
        throw "Expected exactly one new Jarvis scheduled task; found $($newTasks.Count)."
    }

    $taskName = $newTasks[0].TaskName
    if ($taskName -notmatch '^reminder-([0-9a-f]{32})$') {
        throw "Registered task name '$taskName' is not reminder-<32-hex-task-id>."
    }
    $taskIdFromName = $Matches[1]
    $taskPath = "\Jarvis\$taskName"
    [xml]$registeredXml = Export-ScheduledTask -TaskName $taskName -TaskPath "\Jarvis\"
    $taskEnabled = $registeredXml.SelectSingleNode("//*[local-name()='Settings']/*[local-name()='Enabled']")
    $disallowBattery = $registeredXml.SelectSingleNode("//*[local-name()='DisallowStartIfOnBatteries']")
    $stopBattery = $registeredXml.SelectSingleNode("//*[local-name()='StopIfGoingOnBatteries']")
    $executionLimit = $registeredXml.SelectSingleNode("//*[local-name()='ExecutionTimeLimit']")
    $startWhenAvailable = $registeredXml.SelectSingleNode("//*[local-name()='StartWhenAvailable']")
    $multipleInstances = $registeredXml.SelectSingleNode("//*[local-name()='MultipleInstancesPolicy']")
    $logonType = $registeredXml.SelectSingleNode("//*[local-name()='Principal']/*[local-name()='LogonType']")
    $runLevel = $registeredXml.SelectSingleNode("//*[local-name()='Principal']/*[local-name()='RunLevel']")
    $deleteExpired = $registeredXml.SelectSingleNode("//*[local-name()='DeleteExpiredTaskAfter']")
    if ($taskEnabled.InnerText -ne "true"
        -or $disallowBattery.InnerText -ne "false"
        -or $stopBattery.InnerText -ne "false"
        -or [System.Xml.XmlConvert]::ToTimeSpan($executionLimit.InnerText) -ne [TimeSpan]::FromMinutes(5)
        -or $startWhenAvailable.InnerText -ne "true"
        -or $multipleInstances.InnerText -ne "IgnoreNew"
        -or $logonType.InnerText -ne "InteractiveToken"
        -or $runLevel.InnerText -ne "LeastPrivilege"
        -or [System.Xml.XmlConvert]::ToTimeSpan($deleteExpired.InnerText) -ne [TimeSpan]::FromHours(1)) {
        throw "Registered task settings differ from the signed reminder scheduler policy."
    }

    $trigger = $registeredXml.SelectSingleNode("//*[local-name()='TimeTrigger']")
    if ($null -eq $trigger -or $registeredXml.SelectNodes("//*[local-name()='TimeTrigger']").Count -ne 1) {
        throw "Registered task XML must contain exactly one time trigger."
    }
    $endBoundary = $trigger.SelectSingleNode("./*[local-name()='EndBoundary']")
    if ($null -eq $endBoundary) {
        throw "Registered task XML did not contain an EndBoundary."
    }

    $exec = $registeredXml.SelectSingleNode("//*[local-name()='Exec']")
    if ($null -eq $exec) {
        throw "Registered task XML did not contain an Exec element."
    }
    $actionPath = [System.IO.Path]::GetFullPath($exec.Command)
    $actionArguments = [string]$exec.Arguments
    Write-Host "Registered XML Exec:"
    Write-Host ("<Exec><Command>{0}</Command><Arguments>{1}</Arguments></Exec>" -f $exec.Command, $actionArguments)
    if (-not [string]::Equals($actionPath, [System.IO.Path]::GetFullPath($cliPath), [StringComparison]::OrdinalIgnoreCase)) {
        throw "Scheduled action is not the published CLI apphost."
    }
    if ($actionArguments -notmatch '^--jarvis-reminder ([0-9a-f]{32})$') {
        throw "Scheduled action arguments are not exactly the reminder switch and a task ID."
    }

    $taskId = $Matches[1]
    if ($taskId -cne $taskIdFromName) {
        throw "Task name ID and signed action argument ID do not match."
    }
    $dataPath = Join-Path $env:LOCALAPPDATA "Jarvis\reminders\$taskId.json"
    $dataExistsBefore = Test-Path -LiteralPath $dataPath -PathType Leaf
    Write-Host "Reminder data file exists before trigger: $dataExistsBefore"
    if (-not $dataExistsBefore) {
        throw "Reminder executor did not create the task-ID-keyed data file."
    }

    $boundary = $registeredXml.SelectSingleNode("//*[local-name()='TimeTrigger']/*[local-name()='StartBoundary']")
    if ($null -eq $boundary) {
        throw "Registered task XML did not contain a one-time StartBoundary."
    }
    $scheduledAt = [DateTime]::Parse(
        $boundary.InnerText,
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::AssumeLocal)
    $expectedEndBoundary = $scheduledAt.AddMinutes(10).ToString("s", [Globalization.CultureInfo]::InvariantCulture)
    if ($endBoundary.InnerText -ne $expectedEndBoundary) {
        throw "EndBoundary '$($endBoundary.InnerText)' does not equal trigger start plus ten minutes ('$expectedEndBoundary')."
    }
    $sleepSeconds = [Math]::Max(1, [Math]::Ceiling(($scheduledAt - (Get-Date)).TotalSeconds) + 20)
    Write-Host "Waiting once for the scheduled trigger ($sleepSeconds seconds); no polling."
    Start-Sleep -Seconds $sleepSeconds

    $consumed = -not (Test-Path -LiteralPath $dataPath -PathType Leaf)
    Write-Host "Reminder data file consumed by dispatcher: $consumed"
    if (-not $consumed) {
        throw "Reminder dispatcher did not consume the data file after the trigger."
    }
} finally {
    if ($taskPath) {
        Unregister-ScheduledTask -TaskName $taskPath.Substring("\Jarvis\".Length) -TaskPath "\Jarvis\" -Confirm:$false -ErrorAction SilentlyContinue
    }
    if ($dataPath -and (Test-Path -LiteralPath $dataPath -PathType Leaf)) {
        Remove-Item -LiteralPath $dataPath -Force
    }
}
