# DLSS Version Checker
# Usage: Run this script to check installed DLSS versions and optionally upgrade to latest
# Check versions: powershell -ExecutionPolicy Bypass -File check-dlss-versions.ps1
# Full sync with compare: powershell -ExecutionPolicy Bypass -File check-dlss-versions.ps1 -Compare

param(
    [switch]$Upgrade,
    [switch]$Compare,
    [switch]$Sync,
    [string]$GlobalPath = "",
    [string]$StreamlinePath = ""
)

# Suppress PSReadLine warnings for cleaner output
$PSReadLineOptions = @{
    PredictionSource = $null
    PredictionViewStyle = $null
    ColorSettings = @{
        "MenuColor" = $null
        "SelectionColor" = $null
        "ListPredictionColor" = $null
    }
}

# Import module from same directory tree (src folder or locally installed)
$modulePath = Join-Path $PSScriptRoot "src\DLSSVersion.psm1"
$moduleDirs = @(
    $modulePath,
    "$PSScriptRoot\src",
    "$env:USERPROFILE\Documents\PowerShell\Modules\DLSSVersion",
    "$env:USERPROFILE\Documents\WindowsPowerShell\Modules\DLSSVersion"
)

$loaded = $false
foreach ($dir in $moduleDirs) {
    $dllPath = Join-Path $dir "DLSSVersion.psm1"
    if (Test-Path $dllPath) {
        Import-Module $dllPath -Force
        $loaded = $true
        break
    }
}

if (-not $loaded) {
    Write-Host "DLSSVersion module not found." -ForegroundColor Red
    Write-Host ""
    Write-Host "To install locally, run:" -ForegroundColor Yellow
    Write-Host "  powershell -ExecutionPolicy Bypass -File install.ps1"
    exit 1
}

# Auto-detect Streamline SDK path if not provided
if ($StreamlinePath -eq "") {
    $searchPaths = @(
        "$env:USERPROFILE\Downloads\streamline-sdk-v2.11.1",
        "C:\Users\jolti.PHANERON\Downloads\streamline-sdk-v2.11.1"
    )
    foreach ($sp in $searchPaths) {
        if (Test-Path (Join-Path $sp "bin\x64\nvngx_dlss.dll")) {
            $StreamlinePath = $sp
            break
        }
    }
}

# Handle Compare (-Compare) mode - compare all sources
if ($Compare) {
    Write-Host "=== DLSS Version Comparison ===" -ForegroundColor Cyan
    Write-Host ""
    
    if ($StreamlinePath -ne "") {
        Write-Host "Streamline SDK detected: $StreamlinePath" -ForegroundColor Green
    } else {
        Write-Host "WARNING: Streamline SDK not found in Downloads!" -ForegroundColor Yellow
        Write-Host "  Run: Compare-DLSSAllSources -StreamlinePath 'C:\path\to\streamline-sdk-...'" -ForegroundColor Yellow
    }
    
    Compare-DLSSAllSources -StreamlinePath $StreamlinePath -GlobalPath $GlobalPath -ShowDetails
    exit 0
}

# Handle Sync (-Sync) mode - find newest and sync
if ($Sync) {
    Write-Host "=== DLSS Version Sync ===" -ForegroundColor Cyan
    Write-Host ""
    
    $params = @{}
    if ($StreamlinePath -ne "") { $params["StreamlinePath"] = $StreamlinePath }
    if ($GlobalPath -ne "") { $params["GlobalPath"] = $GlobalPath }
    
    Sync-DLSSVersions @params -Confirm:$false
    exit 0
}

Write-Host "=== DLSS Version Checker ===" -ForegroundColor Cyan
Write-Host ""
    Write-Host "To install locally, run:" -ForegroundColor Yellow
    Write-Host "  powershell -ExecutionPolicy Bypass -File install.ps1"
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
