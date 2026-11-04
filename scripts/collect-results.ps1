[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$From,
    [Parameter(Mandatory = $true)][string]$To
)

$ErrorActionPreference = 'Stop'
$source = (Resolve-Path $From).Path
$destination = [IO.Path]::GetFullPath($To)
if (-not (Test-Path (Join-Path $source 'summary.csv'))) { throw "Missing summary.csv in $source" }
if (-not (Test-Path (Join-Path $source 'summary.md'))) { throw "Missing summary.md in $source" }

$rawFiles = @(Get-ChildItem -Path $source -Filter 'raw.jsonl' -File -Recurse | Sort-Object FullName)
if ($rawFiles.Count -eq 0) { throw "No raw.jsonl files found in $source" }
New-Item -ItemType Directory -Force -Path $destination | Out-Null
Copy-Item (Join-Path $source 'summary.csv') $destination -Force
Copy-Item (Join-Path $source 'summary.md') $destination -Force

$rawDestination = Join-Path $destination 'raw.jsonl'
$writer = [IO.StreamWriter]::new($rawDestination, $false, [Text.UTF8Encoding]::new($false))
$metadata = @()
$userProfile = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile).TrimEnd('\')
$absoluteUserPath = '(?i)[A-Z]:\\Users\\[^\\]+\\'
try {
    foreach ($rawFile in $rawFiles) {
        $reader = [IO.StreamReader]::new($rawFile.FullName, $true)
        try {
            while (($line = $reader.ReadLine()) -ne $null) {
                if (-not [string]::IsNullOrWhiteSpace($line)) {
                    if (($userProfile.Length -gt 0 -and $line.IndexOf($userProfile, [StringComparison]::OrdinalIgnoreCase) -ge 0) -or $line -match $absoluteUserPath) {
                        throw "Raw data contains an absolute user path: $($rawFile.FullName)"
                    }
                    $parsed = $line | ConvertFrom-Json
                    if ($parsed.row_type -eq 'metadata') { $metadata += $parsed }
                    $writer.WriteLine($line)
                }
            }
        } finally {
            $reader.Dispose()
        }
    }
} finally {
    $writer.Dispose()
}

$metadataPath = Join-Path $destination 'metadata.json'
$metadata | ConvertTo-Json -Depth 20 | Set-Content -Path $metadataPath -Encoding UTF8

Write-Host 'Collected files:'
Get-Item (Join-Path $destination 'summary.csv'),(Join-Path $destination 'summary.md'),$rawDestination,$metadataPath |
    Select-Object Name,Length
