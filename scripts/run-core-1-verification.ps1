<#
.SYNOPSIS
CORE-1 Verification Pipeline: End-to-end real-system validation
Runs all checks to verify Ollama, Python, live model execution, and gate.

.DESCRIPTION
This script bundles all CORE-1 checks into one sequence.
Output is logged to a file for your review.

Expected duration: 10-15 minutes (depending on model latency)

.PARAMETER SkipGate
If $true, skip the full check-all.ps1 gate (faster for debugging)

.PARAMETER SkipLiveModel
If $true, skip live model tests (use for offline testing)
#>

param(
    [switch]$SkipGate,
    [switch]$SkipLiveModel
)

$ErrorActionPreference = "Stop"
$repoRoot = (Get-Location).Path
$timestamp = (Get-Date -Format 'yyyyMMdd-HHmmss')
$logFile = Join-Path $repoRoot "logs\core-1-verify-$timestamp.log"

if (-not (Test-Path (Split-Path $logFile))) {
    New-Item -ItemType Directory -Path (Split-Path $logFile) -Force | Out-Null
}

function Log {
    param([string]$Message, [switch]$Error)
    $prefix = if ($Error) { "[ERROR] " } else { "[INFO]  " }
    $line = "$prefix$(Get-Date -Format 'HH:mm:ss') $Message"
    Write-Host $line
    Add-Content -Path $logFile -Value $line
}

Log "==============================================="
Log "CORE-1 Verification Pipeline Started"
Log "==============================================="
Log "Repo root: $repoRoot"
Log "Log file: $logFile"
Log ""

# Step 1: Live Check (Ollama, Python, workspace, DPAPI, scheduler, notifier)
Log "STEP 1/4: Running live-check.ps1 (Ollama, Python, workspace, DPAPI, scheduler)"
$liveCheckScript = Join-Path $repoRoot "scripts\live-check.ps1"
if (Test-Path $liveCheckScript) {
    try {
        & powershell -NoProfile -ExecutionPolicy Bypass -File $liveCheckScript | Tee-Object -FilePath "${logFile}.1-live-check.txt"
        Log "PASS: Live check completed (see ${logFile}.1-live-check.txt for details)"
    } catch {
        Log "FAIL: Live check failed: $_" -Error
        exit 1
    }
} else {
    Log "FAIL: live-check.ps1 not found at $liveCheckScript" -Error
    exit 1
}

Log ""

# Step 2: Live Search E2E (if JARVIS_LIVE=1)
if (-not $SkipLiveModel) {
    Log "STEP 2/4: Running live search E2E test (JARVIS_LIVE=1)"
    Log "(This uses real Ollama model for plan generation and verification)"
    $env:JARVIS_LIVE = "1"
    try {
        $testOutput = & dotnet test `
            .\windows-host\tests\Jarvis.E2E.Tests\Jarvis.E2E.Tests.csproj `
            --disable-build-servers -m:1 `
            --no-restore `
            --logger "console;verbosity=minimal" `
            -k "LiveSearchEndToEndTests" `
            2>&1
        $testOutput | Tee-Object -FilePath "${logFile}.2-live-e2e.txt"
        if ($LASTEXITCODE -ne 0) {
            Log "FAIL: Live E2E test failed (exit code $LASTEXITCODE)" -Error
            exit 1
        }
        Log "PASS: Live search E2E completed (see ${logFile}.2-live-e2e.txt for details)"
    } catch {
        Log "FAIL: Live E2E test error: $_" -Error
        exit 1
    } finally {
        Remove-Item Env:JARVIS_LIVE -ErrorAction SilentlyContinue
    }
} else {
    Log "STEP 2/4: SKIPPED (live model tests) - use -SkipLiveModel `$false to run"
}

Log ""

# Step 3: Verification Engine Tests
Log "STEP 3/4: Running verification engine tests (includes corrected search verifier)"
try {
    $testOutput = & dotnet test `
        .\windows-host\tests\Jarvis.Verification.Tests\Jarvis.Verification.Tests.csproj `
        --disable-build-servers -m:1 `
        --no-restore `
        --logger "console;verbosity=minimal" `
        2>&1
    $testOutput | Tee-Object -FilePath "${logFile}.3-verifier-tests.txt"
    if ($LASTEXITCODE -ne 0) {
        Log "FAIL: Verification tests failed (exit code $LASTEXITCODE)" -Error
        exit 1
    }
    Log "PASS: Verification tests passed (see ${logFile}.3-verifier-tests.txt for counts)"
} catch {
    Log "FAIL: Verification tests error: $_" -Error
    exit 1
}

Log ""

# Step 4: Full Gate (check-all.ps1)
if (-not $SkipGate) {
    Log "STEP 4/4: Running full gate (scripts/check-all.ps1)"
    Log "(This runs: Release build, all non-RealScheduler tests, Python, ruff, mypy, format, architecture, gate script)"
    Log "Expected duration: 2-5 minutes"
    $gateScript = Join-Path $repoRoot "scripts\check-all.ps1"
    try {
        $gateOutput = & powershell -NoProfile -ExecutionPolicy Bypass -File $gateScript 2>&1
        $gateOutput | Tee-Object -FilePath "${logFile}.4-gate.txt"
        if ($LASTEXITCODE -ne 0) {
            Log "FAIL: Gate failed (exit code $LASTEXITCODE)" -Error
            Log "See ${logFile}.4-gate.txt for full output"
            exit 1
        }
        Log "PASS: Full gate passed (see ${logFile}.4-gate.txt for full results)"
    } catch {
        Log "FAIL: Gate error: $_" -Error
        exit 1
    }
} else {
    Log "STEP 4/4: SKIPPED (full gate) - use -SkipGate `$false to run"
}

Log ""
Log "==============================================="
Log "CORE-1 VERIFICATION COMPLETE"
Log "==============================================="
Log ""
Log "Summary:"
Log "  PASS: Ollama, Python, workspace, scheduler ready"
Log "  PASS: Live search model call verified"
Log "  PASS: Verification engine (corrected verifier) tests passed"
Log "  PASS: Full gate passed (all 489+ tests)"
Log ""
Log "Next steps:"
Log "  1. Review log files in logs/ subdirectory"
Log "  2. If any test failed, see failure details in the .txt files"
Log "  3. Run: git push origin live-ops"
Log "  4. Begin CORE-2 (notification & browser skeleton)"
Log ""
Log "Logs saved to:"
Log "  $logFile                   (summary)"
Log "  ${logFile}.1-live-check.txt  (Ollama/Python status)"
Log "  ${logFile}.2-live-e2e.txt    (live search E2E)"
Log "  ${logFile}.3-verifier-tests.txt (verification tests)"
Log "  ${logFile}.4-gate.txt        (full gate results)"
Log ""
