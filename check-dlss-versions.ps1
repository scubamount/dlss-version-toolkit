# DLSS Version Checker
# Usage: Run this script to check installed DLSS versions and optionally upgrade to latest
# Quick check: powershell -ExecutionPolicy Bypass -File check-dlss-versions.ps1
# Full sync: powershell -ExecutionPolicy Bypass -File check-dlss-versions.ps1 -All

param(
    [switch]$Upgrade,
    [switch]$Compare,
    [switch]$Sync,
    [switch]$All,
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
        "C:\Users\jolti.PHANERON\Downloads\streamline-sdk-v2.11.1",
        "$env:USERPROFILE\Downloads\streamline-sdk"
    )
    foreach ($sp in $searchPaths) {
        if (Test-Path (Join-Path $sp "bin\x64\nvngx_dlss.dll")) {
            $StreamlinePath = $sp
            break
        }
    }
}

# Auto-detect AnWave/Global if not provided (common location)
if ($GlobalPath -eq "") {
    $globalSearch = @(
        "$env:USERPROFILE\Downloads\nvidiaDlssGlom",
        "$env:USERPROFILE\Downloads\dlssglom",
        "C:\Users\jolti.PHANERON\Downloads\nvidiaDlssGlom"
    )
    foreach ($gp in $globalSearch) {
        if (Test-Path (Join-Path $gp "nvidiaDlssGlom.exe")) {
            $GlobalPath = $gp
            break
        }
    }
}

# HOLISTIC: Everything in one command (-All) = Compare + Auto-Sync
if ($All) {
    Write-Host "=== DLSS Version Toolkit: Full Update ===" -ForegroundColor Cyan
    Write-Host ""
    
    # Step 1: Check dependencies
    Write-Host "[1/4] Checking dependencies..." -ForegroundColor Gray
    
    $issues = @()
    
    if ($StreamlinePath -eq "") {
        $issues += "Streamline SDK not found in Downloads folder"
    }
    if ($GlobalPath -eq "") {
        $issues += "AnWave (dlssglom) not found in Downloads folder"
    }
    
    if ($issues.Count -gt 0) {
        Write-Host "WARNING: Some components not found:" -ForegroundColor Yellow
        foreach ($issue in $issues) {
            Write-Host "  - $issue" -ForegroundColor Yellow
        }
    } else {
        Write-Host "  Streamline SDK: $StreamlinePath" -ForegroundColor Green
        Write-Host "  AnWave: $GlobalPath" -ForegroundColor Green
    }
    
    # Step 2: Compare all sources
    Write-Host ""
    Write-Host "[2/4] Comparing all sources..." -ForegroundColor Gray
    Compare-DLSSAllSources -StreamlinePath $StreamlinePath -GlobalPath $GlobalPath -ShowDetails
    
    # Step 3: Find what needs updating
    Write-Host ""
    Write-Host "[3/4] Determining updates needed..." -ForegroundColor Gray
    
    $analysis = Compare-DLSSAllSources -StreamlinePath $StreamlinePath -GlobalPath $GlobalPath
    
    # Step 4: Apply updates
    Write-Host ""
    Write-Host "[4/4] Applying updates..." -ForegroundColor Gray
    
    if ($analysis.Recommendations.Count -eq 0) {
        Write-Host "  All sources already at newest version!" -ForegroundColor Green
    } else {
        foreach ($rec in $analysis.Recommendations) {
            Write-Host "  -> $($rec.Description)" -ForegroundColor Yellow
        }
        
        Sync-DLSSVersions -StreamlinePath $StreamlinePath -GlobalPath $GlobalPath -Force
    }
    
    Write-Host ""
    Write-Host "=== Complete ===" -ForegroundColor Cyan
    exit 0
}

# Basic check (default mode)
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