# Local Install Script for DLSS Version Toolkit
# Run this to install the module to your local PowerShell modules folder

param(
    [switch]$Uninstall
)

$ModuleName = "DLSSVersion"
$SourcePath = Join-Path $PSScriptRoot "src"

# Find user's local module paths (check both Windows PowerShell and PowerShell 7+)
$modulePaths = @(
    "$env:USERPROFILE\Documents\PowerShell\Modules",
    "$env:USERPROFILE\Documents\WindowsPowerShell\Modules",
    "$HOME\Documents\PowerShell\Modules"
)

$targetPath = $null
foreach ($p in $modulePaths) {
    if (Test-Path $p) {
        $targetPath = $p
        break
    }
}

# Create first available path
if (-not $targetPath) {
    $targetPath = $modulePaths[0]
}
$targetPath = Join-Path $targetPath $ModuleName

if ($Uninstall) {
    if (Test-Path $targetPath) {
        Remove-Item -Path $targetPath -Recurse -Force
        Write-Host "Uninstalled $ModuleName from $targetPath" -ForegroundColor Green
    } else {
        Write-Host "$ModuleName not found in $targetPath" -ForegroundColor Yellow
    }
    exit 0
}

Write-Host "Installing $ModuleName..." -ForegroundColor Cyan
Write-Host "  Source: $SourcePath"
Write-Host "  Target: $targetPath"

# Create target directory
if (-not (Test-Path $targetPath)) {
    New-Item -ItemType Directory -Path $targetPath -Force | Out-Null
}

# Copy source files
$files = Get-ChildItem -Path $SourcePath -File
foreach ($file in $files) {
    $dest = Join-Path $targetPath $file.Name
    Copy-Item -Path $file.FullName -Destination $dest -Force
    Write-Host "  Copied: $($file.Name)" -ForegroundColor Gray
}

# Also show your current PSModulePath for debugging
Write-Host ""
Write-Host "Your PSModulePath:" -ForegroundColor Yellow
($env:PSModulePath -split ';') | ForEach-Object { Write-Host "  $_" -ForegroundColor Gray }

Write-Host ""
Write-Host "Installed to: $targetPath" -ForegroundColor Green
Write-Host ""
Write-Host "To use in any PowerShell session:" -ForegroundColor Cyan
Write-Host "  Import-Module $ModuleName"
Write-Host "  Get-DLSSVersions"
Write-Host ""
Write-Host "Or run directly:" -ForegroundColor Cyan
Write-Host "  powershell -ExecutionPolicy Bypass -File check-dlss-versions.ps1"