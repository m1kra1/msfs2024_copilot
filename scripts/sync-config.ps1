# Copy canonical PackageSources extras config + voices into the shipped Packages tree.
# Usage (from repo root):
#   powershell -ExecutionPolicy Bypass -File scripts/sync-config.ps1

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path (Join-Path $RepoRoot "private-utility-copilot-voice"))) {
    $RepoRoot = Get-Location
}

$PkgRoot = Join-Path $RepoRoot "private-utility-copilot-voice"
$Src = Join-Path $PkgRoot "PackageSources\extras"
$Dst = Join-Path $PkgRoot "Packages\private-utility-copilot-voice\extras"

if (-not (Test-Path (Join-Path $Src "config"))) {
    throw "Source config not found: $Src\config"
}

New-Item -ItemType Directory -Force -Path $Dst | Out-Null
Copy-Item -Recurse -Force (Join-Path $Src "config") $Dst
if (Test-Path (Join-Path $Src "voices")) {
    Copy-Item -Recurse -Force (Join-Path $Src "voices") $Dst
}

Write-Host "Synced config/voices:"
Write-Host "  $Src  ->  $Dst"
