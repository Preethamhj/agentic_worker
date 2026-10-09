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
    dotnet build --configuration Release --no-incremental
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

    dotnet test --configuration Release --no-build
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed" }
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

Write-Host "`n=== Step 3: C# Formatting Verification ===" -ForegroundColor Cyan
Push-Location $windowsHostDir
try {
    dotnet format --verify-no-changes
    if ($LASTEXITCODE -ne 0) { throw "dotnet format check failed" }
} finally {
    Pop-Location
}

Write-Host "`n==========================================" -ForegroundColor Green
Write-Host " ALL CHECKS PASSED SUCCESSFULLY! " -ForegroundColor Green
Write-Host "==========================================" -ForegroundColor Green
