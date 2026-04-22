# DLSS Version Checker
# Usage: Run this script to check installed DLSS versions and optionally upgrade to latest
# Check versions: powershell -ExecutionPolicy Bypass -File check-dlss-versions.ps1
# With global overrides: powershell -ExecutionPolicy Bypass -File check-dlss-versions.ps1 -GlobalPath "C:\path\to\AnWave"
# Upgrade to latest: powershell -ExecutionPolicy Bypass -File check-dlss-versions.ps1 -Upgrade

param(
    [switch]$Upgrade,
    [string]$GlobalPath = ""
)

# Import module from same directory
$modulePath = Join-Path $PSScriptRoot "src\DLSSVersion.psm1"
if (Test-Path $modulePath) {
    Import-Module $modulePath -Force
} else {
    Write-Error "Cannot find DLSSVersion module at $modulePath"
    exit 1
}

Write-Host "=== DLSS Version Checker ===" -ForegroundColor Cyan
Write-Host ""

$params = @{}
if ($GlobalPath -ne "") {
    $params["GlobalPath"] = $GlobalPath
}

$versions = Get-DLSSVersions @params

if ($versions) {
    $versions | Format-Table Location, BuildID, DLSS, FrameGen, DLSSD, DeepDVC, StreamlineSDK -AutoSize
} else {
    Write-Host "No DLSS versions found." -ForegroundColor Yellow
}

Write-Host ""

$latest = Get-DLSSLatestVersion @params

if ($latest) {
    $componentParts = @("DLSS $($latest.DLSS)")
    if ($latest.FrameGen -ne "Unknown") { $componentParts += "Frame Gen $($latest.FrameGen)" }
    if ($latest.DLSSD -ne "Unknown") { $componentParts += "DLSSD $($latest.DLSSD)" }
    if ($latest.DeepDVC -ne "Unknown") { $componentParts += "DeepDVC $($latest.DeepDVC)" }
    if ($latest.StreamlineSDK -ne "Unknown") { $componentParts += "Streamline $($latest.StreamlineSDK)" }
    $componentSummary = $componentParts -join ", "
    Write-Host "Latest available: $componentSummary in $($latest.Location) build $($latest.BuildID)" -ForegroundColor Yellow
}
else {
    Write-Host "Latest available: none" -ForegroundColor Yellow
}

if ($Upgrade) {
    Write-Host ""
    Start-DLSSUpgrade
}
