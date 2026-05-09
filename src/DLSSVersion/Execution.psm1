# Execution.psm1 - DLSS Version Toolkit: Write operations (backup, upgrade, sync, rollback)
# PowerShell 5.1 compatible

# ============================================================================
# Private Variables (Not Exported) - Required by functions in this module
# ============================================================================

$script:DefaultNgxBasePath = "C:\ProgramData\NVIDIA\NGX"
$script:ReleaseSubPath = "models\dlss_override\versions"
$script:StagingSubPath = "Staging\models\dlss_override\versions"
$script:ConfigFileName = "nvngx_package_config.txt"
$script:DllNames = @("nvngx_dlss.dll", "nvngx_dlssg.dll", "nvngx_dlssd.dll")
$script:BackupPrefix = ".dlss-backup-"

# ============================================================================
# Public Functions (Exported) - Write/Mutation Operations
# ============================================================================

function New-UpgradeOperation {
<#
.SYNOPSIS
Creates an UpgradeOperation tracking object.
.PARAMETER SourceVersion
The Staging DLSSVersion being upgraded from.
.PARAMETER TargetVersion
The Release DLSSVersion being upgraded to.
.OUTPUTS
PSCustomObject with upgrade operation properties.
#>
param(
    [Parameter(Mandatory = $false)]
    $SourceVersion = $null,

    [Parameter(Mandatory = $false)]
    $TargetVersion = $null
)

    return [PSCustomObject]@{
        SourceVersion = $SourceVersion
        TargetVersion = $TargetVersion
        Status = "Pending"
        BackupPath = ""
        ErrorMessage = ""
    }
}

function New-DLSSBackup {
<#
.SYNOPSIS
Creates a timestamped backup of the Release version folder.
.PARAMETER ReleaseFolderPath
The Release version folder to back up.
.PARAMETER VersionsParentPath
The parent directory where the backup will be placed.
.OUTPUTS
String path to the backup, or $null if backup failed.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$ReleaseFolderPath,

    [Parameter(Mandatory = $true)]
    [string]$VersionsParentPath
)

    # Validate source exists and is a directory
    if (-not (Test-Path $ReleaseFolderPath)) {
        Write-Error "ERROR: Release folder does not exist: $ReleaseFolderPath"
        return $null
    }

    $sourceItem = Get-Item -Path $ReleaseFolderPath -ErrorAction SilentlyContinue
    if ($sourceItem -and -not $sourceItem.PSIsContainer) {
        Write-Error "ERROR: Release folder path is not a directory: $ReleaseFolderPath"
        return $null
    }

    # Check if destination already exists (avoid overwrite without explicit consent)
    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $backupName = "$script:BackupPrefix$timestamp"
    $backupPath = Join-Path $VersionsParentPath $backupName

    if (Test-Path $backupPath) {
        Write-Error "ERROR: Backup path already exists: $backupPath"
        return $null
    }

    # Add long path support for paths approaching 260-char limit
    $effectiveSourcePath = $ReleaseFolderPath
    $effectiveBackupPath = $backupPath
    if ($ReleaseFolderPath.Length -ge 250) {
        if (Test-LongPathSupport) {
            $effectiveSourcePath = "\\?\" + $ReleaseFolderPath
            $effectiveBackupPath = "\\?\" + $backupPath
        } else {
            Write-Warning "Long path support not enabled. Backup/restore may fail for long paths."
        }
    }

    # Count source files for verification
    $sourceFileCount = (Get-ChildItem -Path $effectiveSourcePath -Recurse -File -ErrorAction SilentlyContinue).Count

    try {
        Copy-Item -Path $effectiveSourcePath -Destination $effectiveBackupPath -Recurse -Force -ErrorAction Stop

        # Verify backup was created successfully
        if (-not (Test-Path $effectiveBackupPath)) {
            Write-Error "ERROR: Backup verification failed - backup path does not exist after copy."
            return $null
        }

        # Compare file counts to ensure backup is complete
        $backupFileCount = (Get-ChildItem -Path $effectiveBackupPath -Recurse -File -ErrorAction SilentlyContinue).Count
        if ($backupFileCount -ne $sourceFileCount) {
            Write-Error "ERROR: Backup verification failed - file count mismatch (source: $sourceFileCount, backup: $backupFileCount)"
            # Clean up incomplete backup
            Remove-Item -Path $effectiveBackupPath -Recurse -Force -ErrorAction SilentlyContinue
            return $null
        }

        return $backupPath
    }
    catch {
        Write-Error "ERROR: Backup failed - $($_.Exception.Message)"
        # Clean up partial backup if it exists
        if (Test-Path $effectiveBackupPath) {
            Remove-Item -Path $effectiveBackupPath -Recurse -Force -ErrorAction SilentlyContinue
        }
        return $null
    }
}

function Start-DLSSUpgrade {
<#
.SYNOPSIS
Upgrades the Release DLSS version to the latest Staging version.
.DESCRIPTION
Compares the current Release DLSS version with the latest available
Staging version. If Staging is newer, copies the DLSS files from
Staging to Release. Creates a backup before making changes and
attempts automatic rollback on failure.
.PARAMETER Path
Base NGX directory path. Defaults to C:\ProgramData\NVIDIA\NGX.
Override for testing with fixture directories.
.OUTPUTS
UpgradeOperation object with SourceVersion, TargetVersion, Status,
BackupPath, and ErrorMessage properties.
#>
[CmdletBinding(ConfirmImpact = 'High', SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $false)]
    [string]$Path = $script:DefaultNgxBasePath
)

    # Get current Release version
    $releaseVersion = Get-DLSSLatestVersion -Path $Path -Location "Release"

    # Get latest Staging version
    $stagingVersion = Get-DLSSLatestVersion -Path $Path -Location "Staging"

    # Create upgrade operation tracking object
    $operation = New-UpgradeOperation -SourceVersion $stagingVersion -TargetVersion $releaseVersion

    # Check if staging is available
    if ($stagingVersion -eq $null) {
        Write-Host "No staging versions available for upgrade." -ForegroundColor Yellow
        $operation.Status = "Failed"
        $operation.ErrorMessage = "No staging versions available"
        return $operation
    }

    # Check if release is available
    if ($releaseVersion -eq $null) {
        Write-Host "No Release version found. Cannot determine upgrade eligibility." -ForegroundColor Yellow
        $operation.Status = "Failed"
        $operation.ErrorMessage = "No Release version found"
        return $operation
    }

    # Compare versions using Test-VersionNewer (safe comparison)
    if (-not (Test-VersionNewer -Version1 $stagingVersion.DLSS -Version2 $releaseVersion.DLSS)) {
        Write-Host "Release is already up to date (DLSS $($releaseVersion.DLSS))" -ForegroundColor Green
        $operation.Status = "Completed"
        return $operation
    }

    # ShouldProcess check
    if ($PSCmdlet.ShouldProcess("Release DLSS", "Upgrade from $($releaseVersion.DLSS) to $($stagingVersion.DLSS)")) {
        $operation.Status = "InProgress"
        Write-Host "Upgrading from DLSS $($releaseVersion.DLSS) to $($stagingVersion.DLSS)..." -ForegroundColor Cyan

        # Locate the Release version folder on disk
        $releaseVersionsPath = Join-Path $Path $script:ReleaseSubPath
        if (-not (Test-Path $releaseVersionsPath)) {
            Write-Error "ERROR: Release path not found: $releaseVersionsPath"
            $operation.Status = "Failed"
            $operation.ErrorMessage = "Release path not found"
            return $operation
        }

        $releaseFolder = Get-ChildItem -Path $releaseVersionsPath -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -eq $releaseVersion.BuildID } |
            Select-Object -First 1

        if ($releaseFolder -eq $null) {
            Write-Error "ERROR: Cannot locate release version folder matching BuildID $($releaseVersion.BuildID) on disk."
            $operation.Status = "Failed"
            $operation.ErrorMessage = "Cannot locate release version folder"
            return $operation
        }


        # --- Step 1: Create backup ---
        Write-Host "Creating backup..." -ForegroundColor Gray
        $backupPath = New-DLSSBackup -ReleaseFolderPath $releaseFolder.FullName -VersionsParentPath $releaseVersionsPath

        if ($backupPath -eq $null) {
            Write-Error "ERROR: Failed to create backup. Upgrade aborted."
            Write-Host "Ensure you are running as Administrator if access is denied." -ForegroundColor Yellow
            $operation.Status = "Failed"
            $operation.ErrorMessage = "Backup failed"
            return $operation
        }

        $operation.BackupPath = $backupPath
        Write-Host "Backup created: $(Split-Path $backupPath -Leaf)" -ForegroundColor Gray

        # Locate the Staging version folder on disk
        $stagingVersionsPath = Join-Path $Path $script:StagingSubPath
        $stagingFolder = Get-ChildItem -Path $stagingVersionsPath -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -eq $stagingVersion.BuildID } |
            Select-Object -First 1

        if ($stagingFolder -eq $null) {
            Write-Error "ERROR: Cannot locate staging version folder matching BuildID $($stagingVersion.BuildID) on disk."
            $operation.Status = "Failed"
            $operation.ErrorMessage = "Cannot locate staging version folder"
            return $operation
        }

        # --- Step 2: Copy DLLs and config from Staging to Release ---
        $copyFailed = $false
        $copyErrorMessage = ""

        try {
            $ErrorActionPreference = "Stop"

            # Copy DLLs: nvngx_dlss.dll, nvngx_dlssg.dll, nvngx_dlssd.dll
            $stagingDlls = Get-ChildItem -Path $stagingFolder.FullName -Recurse -File -ErrorAction Stop |
                Where-Object { $script:DllNames -contains $_.Name }

            foreach ($dll in $stagingDlls) {
                # Find the matching DLL in the release folder
                $releaseDll = Get-ChildItem -Path $releaseFolder.FullName -Recurse -Filter $dll.Name -ErrorAction SilentlyContinue |
                    Select-Object -First 1

                if ($releaseDll -ne $null) {
                    Copy-Item -Path $dll.FullName -Destination $releaseDll.FullName -Force -ErrorAction Stop
                    Write-Host " Updated: $($dll.Name)" -ForegroundColor Green
                }
                else {
                    Write-Warning "Could not find $($dll.Name) in release folder to replace."
                }
            }

            # Copy config: nvngx_package_config.txt (verbatim copy, preserves encoding)
            $stagingConfig = Get-ChildItem -Path $stagingFolder.FullName -Recurse -Filter $script:ConfigFileName -ErrorAction SilentlyContinue |
                Select-Object -First 1

            if ($stagingConfig -ne $null) {
                $releaseConfig = Get-ChildItem -Path $releaseFolder.FullName -Recurse -Filter $script:ConfigFileName -ErrorAction SilentlyContinue |
                    Select-Object -First 1

                if ($releaseConfig -ne $null) {
                    Copy-Item -Path $stagingConfig.FullName -Destination $releaseConfig.FullName -Force -ErrorAction Stop
                    Write-Host " Updated config from staging" -ForegroundColor Green
                }
                else {
                    Write-Warning "Could not find nvngx_package_config.txt in release folder."
                }
            }
            else {
                Write-Warning "Could not find nvngx_package_config.txt in staging folder."
            }
        }
        catch {
            $copyFailed = $true
            $copyErrorMessage = $_.Exception.Message
        }
        finally {
            $ErrorActionPreference = "Continue"
        }

        if ($copyFailed) {
            $operation.Status = "Failed"
            $operation.ErrorMessage = "Copy failed: $copyErrorMessage"
            Write-Host "ERROR: $copyErrorMessage" -ForegroundColor Red

            # Attempt rollback from backup
            Write-Host "Attempting restore from backup..." -ForegroundColor Yellow
            $restoreResult = Restore-DLSSBackup -BackupPath $backupPath -ReleaseFolderPath $releaseFolder.FullName

            if ($restoreResult) {
                $operation.Status = "RolledBack"
                Write-Host "Rolled back to previous version from backup." -ForegroundColor Yellow
            }
            else {
                Write-Host "ERROR: Rollback also failed! Backup available at $backupPath for manual restore." -ForegroundColor Red
            }

            Write-Host "Ensure you are running as Administrator if access is denied." -ForegroundColor Yellow
            return $operation
        }

        $operation.Status = "Completed"
        Write-Host ""
        Write-Host "Upgrade complete!" -ForegroundColor Green
    }

    return $operation
}

function Sync-DLSSVersions {
<#
.SYNOPSIS
Syncs newest DLSS/Streamline versions to target location.
.PARAMETER Source
Source location: "StreamlineSDK", "Staging", or "Global"
.PARAMETER Target
Target location: "NGX_Release", "AnWave", or "Global"
.PARAMETER StreamlinePath
Path to Streamline SDK folder.
.PARAMETER GlobalPath
Path to AnWave/dlssglom folder.
.PARAMETER Force
Overwrite without confirmation.
.OUTPUTS
Array of sync operations and results.
#>
[CmdletBinding(ConfirmImpact = 'High', SupportsShouldProcess = $true)]
param(
    [Parameter(Mandatory = $false)]
    [ValidateSet("", "StreamlineSDK", "Staging", "Global")]
    [string]$Source = "",

    [Parameter(Mandatory = $false)]
    [ValidateSet("", "NGX_Release", "AnWave")]
    [string]$Target = "",

    [Parameter(Mandatory = $false)]
    [string]$StreamlinePath = "",

    [Parameter(Mandatory = $false)]
    [string]$GlobalPath = "",

    [switch]$Force
)

    # Get comparison first
    Write-Host "Analyzing versions across all sources..." -ForegroundColor Cyan
    $analysis = Compare-DLSSAllSources -StreamlinePath $StreamlinePath -GlobalPath $GlobalPath

    if ($analysis.Recommendations.Count -eq 0) {
        Write-Host "No updates needed - all sources are at newest version." -ForegroundColor Green
        return
    }

    Write-Host ""
    Write-Host "=== Recommended Actions ===" -ForegroundColor Yellow
    $analysis.Recommendations | Format-Table -AutoSize

    $results = @()

    foreach ($rec in $analysis.Recommendations) {
        # Skip if user specified source/target and this doesn't match
        if ($Source -ne "" -and $rec.From -ne $Source) { continue }
        if ($Target -ne "" -and $rec.To -ne $Target) { continue }

        # Determine source DLL paths
        $src = $analysis.Sources[$rec.From]
        if (-not $src) { continue }

        # Determine target path
        $dstPath = ""
        if ($rec.To -eq "NGX_Release") {
            $dstPath = Join-Path $script:DefaultNgxBasePath $script:ReleaseSubPath
        } elseif ($rec.To -eq "AnWave" -or $rec.To -eq "Global") {
            $dstPath = $GlobalPath
        }

        if ($dstPath -eq "" -or -not (Test-Path $dstPath)) {
            Write-Warning "Target path not found or not specified: $($rec.To)"
            continue
        }

        # Use ShouldProcess for -WhatIf/-Confirm support
        if ($PSCmdlet.ShouldProcess("Sync from $($rec.From) to $($rec.To)", "Sync DLSS versions")) {
            if (-not $Force) {
                Write-Host "Sync requested from $($rec.From) to $($rec.To). Use -Force to skip confirmation." -ForegroundColor Yellow
                continue
            }

            # Copy DLLs with idempotency check
            if ($src.DllPaths) {
                foreach ($dll in $src.DllPaths.Keys) {
                    $srcFile = $src.DllPaths[$dll]
                    $dstFile = Join-Path $dstPath $dll

                    # Idempotency: check if target already has same or newer version
                    if (Test-Path $dstFile) {
                        try {
                            $srcVi = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($srcFile)
                            $dstVi = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($dstFile)
                            if ($srcVi.FileVersion -and $dstVi.FileVersion) {
                                if (-not (Test-VersionNewer -Version1 $srcVi.FileVersion -Version2 $dstVi.FileVersion)) {
                                    Write-Warning "Skipping $dll - target already has same or newer version ($($dstVi.FileVersion))"
                                    continue
                                }
                            }
                        } catch {
                            # If version check fails, proceed with copy
                        }
                    }

                    Copy-Item -Path $srcFile -Destination $dstFile -Force
                    Write-Host " Copied: $dll" -ForegroundColor Green
                }
            }
            elseif ($rec.From -eq "Staging" -and $rec.To -eq "NGX_Release") {
                # Staging→NGX: copy DLLs and config from staging version folder to release version folder
                $stagingVersionsPath = Join-Path $script:DefaultNgxBasePath $script:StagingSubPath
                $releaseVersionsPath = Join-Path $script:DefaultNgxBasePath $script:ReleaseSubPath
                $stagingFolder = Get-ChildItem -Path $stagingVersionsPath -Directory -ErrorAction SilentlyContinue |
                    Sort-Object Name -Descending | Select-Object -First 1
                $releaseFolder = Get-ChildItem -Path $releaseVersionsPath -Directory -ErrorAction SilentlyContinue |
                    Sort-Object Name -Descending | Select-Object -First 1
                if ($stagingFolder -and $releaseFolder) {
                    $stagingDlls = Get-ChildItem -Path $stagingFolder.FullName -Recurse -File -ErrorAction SilentlyContinue |
                        Where-Object { $script:DllNames -contains $_.Name -or $_.Name -eq $script:ConfigFileName }
                    foreach ($file in $stagingDlls) {
                        $targetFile = Join-Path $releaseFolder.FullName $file.Name
                        if (Test-Path $targetFile) {
                            Copy-Item -Path $file.FullName -Destination $targetFile -Force
                            Write-Host " Copied: $($file.Name)" -ForegroundColor Green
                        }
                    }
                }
            }
        }

        $results += [PSCustomObject]@{
            From = $rec.From
            To = $rec.To
            Status = "Completed"
        }
    }
    return $results
}

# ============================================================================
# Export Module Members
# ============================================================================

Export-ModuleMember -Function @(
    'New-UpgradeOperation',
    'New-DLSSBackup',
    'Start-DLSSUpgrade',
    'Sync-DLSSVersions'
)
