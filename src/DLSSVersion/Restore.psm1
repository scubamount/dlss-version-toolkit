# Restore.psm1 - DLSS Version Toolkit: Backup restore operations

function Restore-DLSSBackup {
<#
.SYNOPSIS
Restores Release folder from a backup.
.PARAMETER BackupPath
Path to the backup folder.
.PARAMETER ReleaseFolderPath
Path to the Release folder to restore.
.OUTPUTS
Boolean indicating success.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$BackupPath,

    [Parameter(Mandatory = $true)]
    [string]$ReleaseFolderPath
)

# Validate BackupPath exists and is a directory
if (-not (Test-Path $BackupPath)) {
    Write-Error "ERROR: Backup folder does not exist: $BackupPath"
    return $false
}

$backupItem = Get-Item -Path $BackupPath -ErrorAction SilentlyContinue
if ($backupItem -and -not $backupItem.PSIsContainer) {
    Write-Error "ERROR: Backup path is not a directory: $BackupPath"
    return $false
}

# Validate ReleaseFolderPath exists
if (-not (Test-Path $ReleaseFolderPath)) {
    Write-Error "ERROR: Release folder does not exist: $ReleaseFolderPath"
    return $false
}

# Add long path support for paths approaching 260-char limit
$effectiveBackupPath = $BackupPath
$effectiveReleasePath = $ReleaseFolderPath
if ($BackupPath.Length -ge 250) {
    if (Test-LongPathSupport) {
        $effectiveBackupPath = "\\?\" + $BackupPath
    } else {
        Write-Warning "Long path support not enabled. Restore may fail for long paths."
    }
}
if ($ReleaseFolderPath.Length -ge 250) {
    if (Test-LongPathSupport) {
        $effectiveReleasePath = "\\?\" + $ReleaseFolderPath
    } else {
        Write-Warning "Long path support not enabled. Restore may fail for long paths."
    }
}

try {
    $ErrorActionPreference = "Stop"

    # Remove current (potentially corrupted) release folder contents
    Get-ChildItem -Path $effectiveReleasePath -Recurse -ErrorAction Stop |
        Remove-Item -Recurse -Force -ErrorAction Stop

    # Restore from backup
    $backupItems = Get-ChildItem -Path $effectiveBackupPath -Recurse -ErrorAction Stop
    if ($backupItems.Count -eq 0) {
        Write-Error "ERROR: Backup folder is empty: $BackupPath"
        return $false
    }

    foreach ($item in $backupItems) {
        $relativePath = $item.FullName.Substring($effectiveBackupPath.Length)
        $destPath = Join-Path $effectiveReleasePath $relativePath
        $destDir = Split-Path $destPath -Parent

        if (-not (Test-Path $destDir)) {
            New-Item -ItemType Directory -Path $destDir -Force | Out-Null
        }

        if ($item.PSIsContainer -eq $false) {
            Copy-Item -Path $item.FullName -Destination $destPath -Force -ErrorAction Stop
        }
    }

    # Verify restore success - check that files were actually restored
    $restoredFileCount = (Get-ChildItem -Path $effectiveReleasePath -Recurse -File -ErrorAction SilentlyContinue).Count
    if ($restoredFileCount -eq 0) {
        Write-Error "ERROR: Restore verification failed - no files found in destination after restore"
        return $false
    }

    return $true
}
catch {
    Write-Error "ERROR: Restore failed - $($_.Exception.Message)"
    return $false
}
finally {
    $ErrorActionPreference = "Continue"
}
}

Export-ModuleMember -Function @('Restore-DLSSBackup')
