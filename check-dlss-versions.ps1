# DLSS Version Checker Skill
# Usage: Run this script to check installed DLSS versions and optionally upgrade to latest
#   Check versions:  powershell -ExecutionPolicy Bypass -File check-dlss-versions.ps1
#   Upgrade to latest: powershell -ExecutionPolicy Bypass -File check-dlss-versions.ps1 -Upgrade

param(
    [switch]$Upgrade
)

$ErrorActionPreference = "SilentlyContinue"

function Get-DLSSVersions {
    $results = @()

    $releasePath = "C:\ProgramData\NVIDIA\NGX\models\dlss_override\versions"
    if (Test-Path $releasePath) {
        $versions = Get-ChildItem -Path $releasePath -Directory
        foreach ($ver in $versions) {
            $configFile = Get-ChildItem -Path $ver.FullName -Recurse -Filter "nvngx_package_config.txt" -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($configFile) {
                $content = Get-Content $configFile.FullName -Raw
                if ($content -match "dlss,\s+([\d.]+)") { $dlssVersion = $Matches[1] } else { $dlssVersion = "Unknown" }
                if ($content -match "dlssg,\s+([\d.]+)") { $dlssgVersion = $Matches[1] } else { $dlssgVersion = "Unknown" }
                $results += [PSCustomObject]@{
                    Location = "Release"
                    BuildID = $ver.Name
                    DLSS = $dlssVersion
                    FrameGen = $dlssgVersion
                }
            }
        }
    }

    $stagingPath = "C:\ProgramData\NVIDIA\NGX\Staging\models\dlss_override\versions"
    if (Test-Path $stagingPath) {
        $versions = Get-ChildItem -Path $stagingPath -Directory
        foreach ($ver in $versions) {
            $configFile = Get-ChildItem -Path $ver.FullName -Recurse -Filter "nvngx_package_config.txt" -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($configFile) {
                $content = Get-Content $configFile.FullName -Raw
                if ($content -match "dlss,\s+([\d.]+)") { $dlssVersion = $Matches[1] } else { $dlssVersion = "Unknown" }
                if ($content -match "dlssg,\s+([\d.]+)") { $dlssgVersion = $Matches[1] } else { $dlssgVersion = "Unknown" }
                $results += [PSCustomObject]@{
                    Location = "Staging"
                    BuildID = $ver.Name
                    DLSS = $dlssVersion
                    FrameGen = $dlssgVersion
                }
            }
        }
    }

    return $results
}

function Get-LatestVersion {
    $versions = Get-DLSSVersions
    $versionList = @()
    foreach ($v in $versions) {
        try { $versionNum = [version]$v.DLSS } catch { $versionNum = [version]"0.0.0.0" }
        $versionList += [PSCustomObject]@{VersionObj = $versionNum; VersionString = $v.DLSS; Location = $v.Location; BuildID = $v.BuildID; FrameGen = $v.FrameGen}
    }
    $latest = $versionList | Sort-Object VersionObj -Descending | Select-Object -First 1
    return $latest
}

function Upgrade-DLSS {
    $versions = Get-DLSSVersions
    
    $stagingVersions = @()
    foreach ($v in $versions) {
        if ($v.Location -eq "Staging") {
            try { $versionNum = [version]$v.DLSS } catch { $versionNum = [version]"0.0.0.0" }
            $stagingVersions += [PSCustomObject]@{VersionObj = $versionNum; VersionString = $v.DLSS; BuildID = $v.BuildID; FrameGen = $v.FrameGen}
        }
    }
    $latestStaging = $stagingVersions | Sort-Object VersionObj -Descending | Select-Object -First 1
    
    $releasePath = "C:\ProgramData\NVIDIA\NGX\models\dlss_override\versions"
    
    if ($latestStaging -and (Test-Path $releasePath)) {
        $releaseVersions = Get-ChildItem -Path $releasePath -Directory
        $targetRelease = $releaseVersions | Select-Object -First 1
        
        if ($targetRelease) {
            Write-Host "Upgrading to DLSS $($latestStaging.VersionString) from Staging build $($latestStaging.BuildID)..." -ForegroundColor Cyan
            
            $stagingDlls = Get-ChildItem -Path "C:\ProgramData\NVIDIA\NGX\Staging\models\dlss_override\versions\$($latestStaging.BuildID)" -Recurse -Filter "nvngx_*.dll"
            
            foreach ($dll in $stagingDlls) {
                $subDir = Get-ChildItem -Path $targetRelease.FullName -Directory | Select-Object -First 1
                if ($subDir) {
                    $targetPath = $subDir.FullName
                    $targetDll = Get-ChildItem -Path $targetPath -Filter $dll.Name -ErrorAction SilentlyContinue | Select-Object -First 1
                    if ($targetDll) {
                        Copy-Item -Path $dll.FullName -Destination $targetDll.FullName -Force
                        Write-Host "  Updated: $($dll.Name)" -ForegroundColor Green
                    }
                }
            }
            
        $configFile = Get-ChildItem -Path $targetRelease.FullName -Recurse -Filter "nvngx_package_config.txt" -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($configFile) {
            # Read staging config to get per-component versions
            $stagingConfig = Get-ChildItem -Path "C:\ProgramData\NVIDIA\NGX\Staging\models\dlss_override\versions\$($latestStaging.BuildID)" -Recurse -Filter "nvngx_package_config.txt" -ErrorAction SilentlyContinue | Select-Object -First 1
            if ($stagingConfig) {
                Copy-Item -Path $stagingConfig.FullName -Destination $configFile.FullName -Force
                Write-Host " Updated config from staging" -ForegroundColor Green
            } else {
                # Fallback: copy staging config verbatim
                $content = Get-Content $configFile.FullName -Raw
                Set-Content -Path $configFile.FullName -Value $content -NoNewline
            Write-Host " No staging config found; release config unchanged" -ForegroundColor Yellow
            }
            }
            
            Write-Host "`nUpgrade complete!" -ForegroundColor Green
        }
    }
}

Write-Host "=== DLSS Version Checker ===" -ForegroundColor Cyan
Write-Host ""

$versions = Get-DLSSVersions
$versions | Format-Table -AutoSize

Write-Host ""
$latest = Get-LatestVersion
Write-Host "Latest available: DLSS $($latest.VersionString) (Frame Gen $($latest.FrameGen)) in $($latest.Location) build $($latest.BuildID)" -ForegroundColor Yellow

if ($Upgrade) {
    Write-Host ""
    Upgrade-DLSS
}
