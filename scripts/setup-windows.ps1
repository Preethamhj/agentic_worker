# setup-windows.ps1 - Environment Setup for Jarvis Agent
[CmdletBinding()]
param(
    [switch]$SkipOllamaPull,
    [switch]$SkipOllamaCheck
)

$ErrorActionPreference = "Stop"

Write-Host "=== Jarvis Windows Environment Setup ===" -ForegroundColor Cyan

# Status tracking
$status = [ordered]@{
    "winget" = "MISSING"
    "dotnet" = "MISSING"
    "python" = "MISSING"
    "ollama" = "MISSING"
    "venv"   = "MISSING"
    "model"  = "MISSING"
}

# 1. Check winget
try {
    $wingetVer = & winget --version 2>$null
    $status["winget"] = "OK ($wingetVer)"
} catch {
    Write-Warning "winget is missing! Please install App Installer from Microsoft Store."
}

# 2. Check dotnet
try {
    $dotnetVer = & dotnet --version 2>$null
    if ($dotnetVer.StartsWith("8.") -or $dotnetVer.StartsWith("9.") -or $dotnetVer.StartsWith("10.")) {
        $status["dotnet"] = "OK ($dotnetVer)"
    } else {
        $status["dotnet"] = "FAIL (Need 8.x+, got $dotnetVer)"
    }
} catch {
    Write-Warning "dotnet SDK missing. Installing via winget..."
    & winget install Microsoft.DotNet.SDK.8 --accept-source-agreements --accept-package-agreements
}

# 3. Check python 3.11
$pythonExe = "py"
$pythonArgs = @("-3.11")
try {
    $pyVer = & py -3.11 -c "import sys; print(sys.version.split()[0])" 2>$null
    if ($pyVer.StartsWith("3.11")) {
        $status["python"] = "OK (3.11 - $pyVer)"
    } else {
        $status["python"] = "FAIL (Got $pyVer)"
    }
} catch {
    try {
        $pyVer = & python --version 2>$null
        if ($pyVer.Contains("3.11")) {
            $pythonExe = "python"
            $pythonArgs = @()
            $status["python"] = "OK ($pyVer)"
        } else {
            $status["python"] = "FAIL (Need 3.11.x)"
        }
    } catch {
        Write-Warning "Python 3.11 missing. Installing via winget..."
        & winget install Python.Python.3.11 --accept-source-agreements --accept-package-agreements
    }
}

# 4. Check Ollama
if ($SkipOllamaCheck) {
    $status["ollama"] = "SKIPPED (not required for this environment)"
} else {
    try {
        $ollamaVer = & ollama --version 2>$null
        $status["ollama"] = "OK ($ollamaVer)"
    } catch {
        Write-Warning "Ollama missing. Installing via winget..."
        & winget install Ollama.Ollama --accept-source-agreements --accept-package-agreements
    }
}

# 5. Create ai-server/.venv and install dependencies
$repoRoot = Resolve-Path "$PSScriptRoot\.."
$aiServerDir = "$repoRoot\ai-server"
$venvDir = "$aiServerDir\.venv"

if (-not (Test-Path $venvDir)) {
    Write-Host "Creating Python virtual environment in $venvDir..." -ForegroundColor Yellow
    & $pythonExe $pythonArgs -m venv $venvDir
}

$venvPython = "$venvDir\Scripts\python.exe"
if (Test-Path $venvPython) {
    Write-Host "Installing ai-server dependencies..." -ForegroundColor Yellow
    & $venvPython -m pip install --upgrade pip
    & $venvPython -m pip install -r "$aiServerDir\requirements.lock"
    & $venvPython -m pip install -e "$aiServerDir" --no-deps
    $status["venv"] = "OK ($venvDir)"
} else {
    $status["venv"] = "FAIL (venv python not found)"
}

# 6. Model pull
$settingsPath = "$repoRoot\config\settings.json"
if (Test-Path $settingsPath) {
    $settings = Get-Content $settingsPath | ConvertFrom-Json
    $modelName = $settings.model
    if ($SkipOllamaPull) {
        $status["model"] = "SKIPPED ($modelName)"
    } else {
        Write-Host "Pulling Ollama model '$modelName'..." -ForegroundColor Yellow
        try {
            & ollama pull $modelName
            $status["model"] = "OK ($modelName)"
        } catch {
            $status["model"] = "FAIL (Ollama pull failed for $modelName)"
        }
    }
}

# Status report
Write-Host "`n=== Environment Status Summary ===" -ForegroundColor Cyan
foreach ($key in $status.Keys) {
    $val = $status[$key]
    $color = if ($val.StartsWith("OK")) { "Green" } elseif ($val.StartsWith("SKIPPED")) { "Yellow" } else { "Red" }
    Write-Host ("{0,-10} : {1}" -f $key, $val) -ForegroundColor $color
}

# Exit non-zero if any failures
$hasFail = $false
foreach ($val in $status.Values) {
    if ($val.StartsWith("FAIL") -or $val.StartsWith("MISSING")) {
        $hasFail = $true
    }
}

if ($hasFail) {
    Write-Host "`nSetup encountered errors. Please fix missing items and re-run." -ForegroundColor Red
    exit 1
} else {
    Write-Host "`nSetup completed successfully!" -ForegroundColor Green
    exit 0
}
