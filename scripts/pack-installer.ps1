# Build host into package extras, sync payload, publish CoPilotVoiceSetup distribution folder.
# Usage (from repo root):
#   powershell -ExecutionPolicy Bypass -File scripts/pack-installer.ps1
# Output:
#   dist/CoPilotVoiceSetup/  (Setup.exe + deps + payload/private-utility-copilot-voice)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path (Join-Path $RepoRoot "private-utility-copilot-voice"))) {
    $RepoRoot = $PSScriptRoot
    if (-not (Test-Path (Join-Path $RepoRoot "private-utility-copilot-voice"))) {
        $RepoRoot = Get-Location
    }
}

$PkgRoot = Join-Path $RepoRoot "private-utility-copilot-voice"
$HostProj = Join-Path $PkgRoot "Sources\Host\CoPilotVoiceHost\CoPilotVoiceHost.csproj"
$SetupProj = Join-Path $PkgRoot "Sources\Installer\CoPilotVoiceSetup\CoPilotVoiceSetup.csproj"
$PackageSourcesExtras = Join-Path $PkgRoot "PackageSources\extras"
$PackagesTree = Join-Path $PkgRoot "Packages\private-utility-copilot-voice"
$Dist = Join-Path $RepoRoot "dist\CoPilotVoiceSetup"

Write-Host "==> Publish host (framework-dependent win-x64) -> PackageSources/extras"
dotnet publish $HostProj -c Release -r win-x64 --self-contained false -o $PackageSourcesExtras
if ($LASTEXITCODE -ne 0) { throw "Host publish failed" }

Write-Host "==> Sync extras into Packages tree"
$PkgExtras = Join-Path $PackagesTree "extras"
New-Item -ItemType Directory -Force -Path $PkgExtras | Out-Null
# Host binaries
Copy-Item -Force (Join-Path $PackageSourcesExtras "CoPilotVoiceHost.exe") $PkgExtras
Copy-Item -Force (Join-Path $PackageSourcesExtras "CoPilotVoiceHost.dll") $PkgExtras
Copy-Item -Force (Join-Path $PackageSourcesExtras "CoPilotVoiceHost.deps.json") $PkgExtras
Copy-Item -Force (Join-Path $PackageSourcesExtras "CoPilotVoiceHost.pdb") $PkgExtras -ErrorAction SilentlyContinue
Copy-Item -Force (Join-Path $PackageSourcesExtras "CoPilotVoiceHost.runtimeconfig.json") $PkgExtras -ErrorAction SilentlyContinue
# Config + voices
if (Test-Path (Join-Path $PackageSourcesExtras "config")) {
    Copy-Item -Recurse -Force (Join-Path $PackageSourcesExtras "config") $PkgExtras
}
if (Test-Path (Join-Path $PackageSourcesExtras "voices")) {
    Copy-Item -Recurse -Force (Join-Path $PackageSourcesExtras "voices") $PkgExtras
}
foreach ($f in @("SimConnect.dll","SimConnect.cfg","Microsoft.FlightSimulator.SimConnect.dll","System.Speech.dll","run_copilot.bat")) {
    $src = Join-Path $PackageSourcesExtras $f
    if (Test-Path $src) { Copy-Item -Force $src $PkgExtras }
}

Write-Host "==> Publish installer -> $Dist"
if (Test-Path $Dist) { Remove-Item -Recurse -Force $Dist }
dotnet publish $SetupProj -c Release -r win-x64 --self-contained false -o $Dist
if ($LASTEXITCODE -ne 0) { throw "Installer publish failed" }

Write-Host "==> Copy package payload next to Setup"
$Payload = Join-Path $Dist "payload\private-utility-copilot-voice"
New-Item -ItemType Directory -Force -Path (Split-Path $Payload) | Out-Null
if (Test-Path $Payload) { Remove-Item -Recurse -Force $Payload }
Copy-Item -Recurse -Force $PackagesTree $Payload

Write-Host ""
Write-Host "Done. Distribution folder:"
Write-Host "  $Dist"
Write-Host "Run:  $Dist\CoPilotVoiceSetup.exe"
Write-Host "Requires: .NET 8 Desktop Runtime on the target PC."
