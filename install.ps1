# Local Install Script for DLSS Version Toolkit
# Run this to install the module to your local PowerShell modules folder

param(
    [switch]$Uninstall
)

$ModuleName = "DLSSVersion"
$SourcePath = $PSScriptRoot

# Find user's local module path
$UserModulePath = if ($env:PSModulePath) {
    ($env:PSModulePath -split ';')[0]
} else {
    "$HOME\Documents\PowerShell\Modules"
}

# Fallback for older Windows PowerShell
if (-not (Test-Path $UserModulePath)) {
    $UserModulePath = "$env:USERPROFILE\Documents\WindowsPowerShell\Modules"
}

$TargetPath = Join-Path $UserModulePath $ModuleName

if ($Uninstall) {
    if (Test-Path $TargetPath) {
        Remove-Item -Path $TargetPath -Recurse -Force
        Write-Host "Uninstalled $ModuleName from $TargetPath" -ForegroundColor Green
    } else {
        Write-Host "$ModuleName not found in $TargetPath" -ForegroundColor Yellow
    }
    exit 0
}

Write-Host "Installing $ModuleName..." -ForegroundColor Cyan
Write-Host "  Source: $SourcePath"
Write-Host "  Target: $TargetPath"

# Create target directory
if (-not (Test-Path $TargetPath)) {
    New-Item -ItemType Directory -Path $TargetPath -Force | Out-Null
}

# Copy source files
$files = Get-ChildItem -Path (Join-Path $SourcePath "src") -File
foreach ($file in $files) {
    $dest = Join-Path $TargetPath $file.Name
    Copy-Item -Path $file.FullName -Destination $dest -Force
    Write-Host "  Copied: $($file.Name)" -ForegroundColor Gray
}

Write-Host ""
Write-Host "Installed successfully!" -ForegroundColor Green
Write-Host ""
Write-Host "To use in any PowerShell session:" -ForegroundColor Cyan
Write-Host "  Import-Module $ModuleName"
Write-Host "  Get-DLSSVersions"
Write-Host ""
Write-Host "Or run directly:" -ForegroundColor Cyan
Write-Host "  powershell -ExecutionPolicy Bypass -File check-dlss-versions.ps1"