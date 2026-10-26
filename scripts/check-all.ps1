# check-all.ps1 - CI verification script
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path "$PSScriptRoot\.."
$windowsHostDir = "$repoRoot\windows-host"
$aiServerDir = "$repoRoot\ai-server"
$venvPython = "$aiServerDir\.venv\Scripts\python.exe"

if (-not (Test-Path $venvPython)) {
    # Fallback to system py -3.11 if venv not present
    $venvPython = "py -3.11"
}

Write-Host "=== Step 1: C# Build & Tests ===" -ForegroundColor Cyan
Push-Location $windowsHostDir
try {
    $sdkVersion = dotnet --version
    Write-Host "Using .NET SDK: $sdkVersion"
    if (-not $sdkVersion.StartsWith("10.")) { throw "Expected .NET 10 SDK, got $sdkVersion" }
    dotnet build Jarvis.sln --configuration Release --no-incremental
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

    $originalRealSchedulerSetting = $env:JARVIS_REAL_SCHEDULER
    try {
        Remove-Item Env:\JARVIS_REAL_SCHEDULER -ErrorAction SilentlyContinue
        dotnet test Jarvis.sln --configuration Release --no-build --filter "Category!=RealScheduler"
        if ($LASTEXITCODE -ne 0) { throw "dotnet test failed for non-real-scheduler tests" }

        $env:JARVIS_REAL_SCHEDULER = "1"
        dotnet test Jarvis.sln --configuration Release --no-build --filter "Category=RealScheduler"
        if ($LASTEXITCODE -ne 0) { throw "dotnet test failed for real-scheduler tests" }
    } finally {
        if ($null -eq $originalRealSchedulerSetting) {
            Remove-Item Env:\JARVIS_REAL_SCHEDULER -ErrorAction SilentlyContinue
        } else {
            $env:JARVIS_REAL_SCHEDULER = $originalRealSchedulerSetting
        }
    }
} finally {
    Pop-Location
}

Write-Host "`n=== Step 2: Python Tests & Linting ===" -ForegroundColor Cyan
Push-Location $aiServerDir
try {
    if (Test-Path "$aiServerDir\.venv\Scripts\pytest.exe") {
        & "$aiServerDir\.venv\Scripts\pytest.exe"
    } else {
        py -3.11 -m pytest
    }
    if ($LASTEXITCODE -ne 0) { throw "pytest failed" }

    if (Test-Path "$aiServerDir\.venv\Scripts\ruff.exe") {
        & "$aiServerDir\.venv\Scripts\ruff.exe" check src tests
    } else {
        py -3.11 -m ruff check src tests
    }
    if ($LASTEXITCODE -ne 0) { throw "ruff check failed" }

    if (Test-Path "$aiServerDir\.venv\Scripts\mypy.exe") {
        & "$aiServerDir\.venv\Scripts\mypy.exe" src
    } else {
        py -3.11 -m mypy src
    }
    if ($LASTEXITCODE -ne 0) { throw "mypy check failed" }
} finally {
    Pop-Location
}

Write-Host "`n=== Step 2b: Evaluation Python Tests & Linting ===" -ForegroundColor Cyan
Push-Location $repoRoot
try {
    & $venvPython -m pytest eval/tests
    if ($LASTEXITCODE -ne 0) { throw "eval pytest failed" }
    & "$aiServerDir\.venv\Scripts\ruff.exe" check eval
    if ($LASTEXITCODE -ne 0) { throw "eval ruff check failed" }
    & "$aiServerDir\.venv\Scripts\mypy.exe" --strict --explicit-package-bases eval
    if ($LASTEXITCODE -ne 0) { throw "eval mypy check failed" }
} finally {
    Pop-Location
}

Write-Host "`n=== Step 3: C# Formatting Verification ===" -ForegroundColor Cyan
Push-Location $windowsHostDir
try {
    dotnet format Jarvis.sln --verify-no-changes
    if ($LASTEXITCODE -ne 0) { throw "dotnet format check failed" }
} finally {
    Pop-Location
}

Write-Host "`n==========================================" -ForegroundColor Green
Write-Host " ALL CHECKS PASSED SUCCESSFULLY! " -ForegroundColor Green
Write-Host "==========================================" -ForegroundColor Green
