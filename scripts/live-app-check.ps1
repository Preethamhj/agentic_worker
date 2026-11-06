$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$cliProject = Join-Path $repoRoot "windows-host\src\Jarvis.Cli\Jarvis.Cli.csproj"

Write-Warning "Windows 11 Notepad may hand off to an existing packaged-app process and may retain the opened tab after Jarvis exits. This check never terminates any Notepad process."

Write-Host "Notepad processes before the Jarvis run:"
$before = @(Get-Process -Name notepad -ErrorAction SilentlyContinue |
    Select-Object Id, StartTime, MainWindowTitle)
if ($before.Count -eq 0) {
    Write-Host "  (none)"
} else {
    $before | Format-Table -AutoSize
}

$runExitCode = 1
Push-Location $repoRoot
try {
    Write-Host "Running one approved Notepad launch through the CLI executor and verifier."
    & dotnet run --project $cliProject -- run "launch Notepad once"
    $runExitCode = $LASTEXITCODE
} finally {
    Pop-Location
}

Write-Host "Notepad processes after the Jarvis run:"
$after = @(Get-Process -Name notepad -ErrorAction SilentlyContinue |
    Select-Object Id, StartTime, MainWindowTitle)
if ($after.Count -eq 0) {
    Write-Host "  (none)"
} else {
    $after | Format-Table -AutoSize
}

Write-Host "Jarvis CLI exit code: $runExitCode"
exit $runExitCode
