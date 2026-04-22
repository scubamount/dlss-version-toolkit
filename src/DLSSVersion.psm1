# DLSSVersion.psm1 - DLSS Version Toolkit Module
# Check and upgrade NVIDIA DLSS override versions on Windows
# PowerShell 5.1 compatible

# ============================================================================
# Private Variables (Not Exported)
# ============================================================================

$script:DefaultNgxBasePath = "C:\ProgramData\NVIDIA\NGX"
$script:ReleaseSubPath = "models\dlss_override\versions"
$script:StagingSubPath = "Staging\models\dlss_override\versions"
$script:ConfigFileName = "nvngx_package_config.txt"
$script:DllNames = @("nvngx_dlss.dll", "nvngx_dlssg.dll", "nvngx_dlssd.dll")
$script:BackupPrefix = ".dlss-backup-"

# Global (AnWave/dlssglom) DLL names and Streamline mapping
$script:GlobalDllNames = @(
    "nvngx_dlss.dll", "nvngx_dlssg.dll", "nvngx_dlssd.dll", "nvngx_deepdvc.dll",
    "sl.common.dll", "sl.dlss.dll", "sl.dlss_d.dll", "sl.dlss_g.dll",
    "sl.deepdvc.dll", "sl.directsr.dll", "sl.imgui.dll", "sl.interposer.dll",
    "sl.nis.dll", "sl.nvperf.dll", "sl.pcl.dll", "sl.reflex.dll"
)
$script:StreamlineDllToComponentName = @{
    "sl.common.dll"    = "sl.common"
    "sl.dlss.dll"      = "sl.dlss"
    "sl.dlss_d.dll"    = "sl.dlss_d"
    "sl.dlss_g.dll"    = "sl.dlss_g"
    "sl.deepdvc.dll"   = "sl.deepdvc"
    "sl.directsr.dll"  = "sl.directsr"
    "sl.imgui.dll"     = "sl.imgui"
    "sl.interposer.dll"= "sl.interposer"
    "sl.nis.dll"       = "sl.nis"
    "sl.nvperf.dll"    = "sl.nvperf"
    "sl.pcl.dll"       = "sl.pcl"
    "sl.reflex.dll"    = "sl.reflex"
}

# ============================================================================
# Private Functions (Not Exported)
# ============================================================================

function Get-NgxVersionConfig {
    <#
    .SYNOPSIS
    Parses nvngx_package_config.txt from a version folder to extract component versions.
    .PARAMETER FolderPath
    The version folder path to search for the config file.
    .OUTPUTS
    PSCustomObject with DLSS, FrameGen, DLSSD, and DeepDVC version strings.
    #>
    param(
        [Parameter(Mandatory = $true)]
        [string]$FolderPath
    )

    $dlssVersion = "Unknown"
    $frameGenVersion = "Unknown"
    $dlssdVersion = "Unknown"
    $deepdvcVersion = "Unknown"

    if (-not (Test-Path $FolderPath)) {
        return [PSCustomObject]@{
            DLSS    = $dlssVersion
            FrameGen = $frameGenVersion
            DLSSD   = $dlssdVersion
            DeepDVC = $deepdvcVersion
        }
    }

    try {
        $configFile = Get-ChildItem -Path $FolderPath -Recurse -Filter $script:ConfigFileName -ErrorAction SilentlyContinue |
            Select-Object -First 1

        if ($configFile -ne $null) {
            $content = Get-Content -Path $configFile.FullName -Raw -Encoding UTF8 -ErrorAction Stop

            if ($content -match "dlss,\s+([\d.]+)") {
                $dlssVersion = $Matches[1]
            }

            if ($content -match "dlssg,\s+([\d.]+)") {
                $frameGenVersion = $Matches[1]
            }

            if ($content -match "dlssd,\s+([\d.]+)") {
                $dlssdVersion = $Matches[1]
            }

            if ($content -match "deepdvc,\s+([\d.]+)") {
                $deepdvcVersion = $Matches[1]
            }
        }
    }
    catch {
        Write-Warning "Failed to read config in '$FolderPath': $($_.Exception.Message)"
    }

    return [PSCustomObject]@{
        DLSS     = $dlssVersion
        FrameGen = $frameGenVersion
        DLSSD    = $dlssdVersion
        DeepDVC  = $deepdvcVersion
    }
}

function Get-GlobalDllVersions {
<#
.SYNOPSIS
Reads DLL versions from file metadata in a Global (AnWave/dlssglom) folder.
.DESCRIPTION
Unlike Release/Staging which use nvngx_package_config.txt, Global stores
DLLs directly in a flat folder. Versions are read from DLL file metadata
using [System.Diagnostics.FileVersionInfo]::GetVersionInfo().
.PARAMETER GlobalPath
Path to the Global (AnWave) folder containing the DLLs.
.OUTPUTS
PSCustomObject with DLSS, FrameGen, DLSSD, DeepDVC, and StreamlineSDK version strings.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$GlobalPath
)

$dlssVersion = "Unknown"
$frameGenVersion = "Unknown"
$dlssdVersion = "Unknown"
$deepdvcVersion = "Unknown"
$streamlineVersion = "Unknown"

if (-not (Test-Path $GlobalPath)) {
    return [PSCustomObject]@{
        DLSS        = $dlssVersion
        FrameGen    = $frameGenVersion
        DLSSD       = $dlssdVersion
        DeepDVC     = $deepdvcVersion
        StreamlineSDK = $streamlineVersion
    }
}

try {
    # Read NGX DLL versions from file metadata
    $dlssDll = Join-Path $GlobalPath "nvngx_dlss.dll"
    if (Test-Path $dlssDll) {
        $vi = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($dlssDll)
        $dlssVersion = $vi.FileVersion -replace ',', '.'
    }

    $dlssgDll = Join-Path $GlobalPath "nvngx_dlssg.dll"
    if (Test-Path $dlssgDll) {
        $vi = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($dlssgDll)
        $frameGenVersion = $vi.FileVersion -replace ',', '.'
    }

    $dlssdDll = Join-Path $GlobalPath "nvngx_dlssd.dll"
    if (Test-Path $dlssdDll) {
        $vi = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($dlssdDll)
        $dlssdVersion = $vi.FileVersion -replace ',', '.'
    }

    $deepdvcDll = Join-Path $GlobalPath "nvngx_deepdvc.dll"
    if (Test-Path $deepdvcDll) {
        $vi = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($deepdvcDll)
        $deepdvcVersion = $vi.FileVersion -replace ',', '.'
    }

    # Read Streamline version from sl.common.dll (canonical SL version indicator)
    $slCommonDll = Join-Path $GlobalPath "sl.common.dll"
    if (Test-Path $slCommonDll) {
        $vi = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($slCommonDll)
        $streamlineVersion = $vi.FileVersion -replace ',', '.'
    }
}
catch {
    Write-Warning "Failed to read Global DLL versions from '$GlobalPath': $($_.Exception.Message)"
}

return [PSCustomObject]@{
    DLSS        = $dlssVersion
    FrameGen    = $frameGenVersion
    DLSSD       = $dlssdVersion
    DeepDVC     = $deepdvcVersion
    StreamlineSDK = $streamlineVersion
}
}

function New-DLSSVersionObject {
    <#
    .SYNOPSIS
    Creates a DLSSVersion entity object.
    .PARAMETER Location
    Either "Release", "Staging", or "Global".
    .PARAMETER BuildID
    The version folder name (build identifier).
    .PARAMETER DLSS
    The DLSS version string.
    .PARAMETER FrameGen
    The Frame Generation version string.
    .PARAMETER DLSSD
    The DLSSD version string.
    .PARAMETER DeepDVC
    The DeepDVC version string.
    .PARAMETER StreamlineSDK
    The Streamline SDK version string.
    .OUTPUTS
    PSCustomObject with Location, BuildID, DLSS, FrameGen, DLSSD, DeepDVC, StreamlineSDK properties.
    #>
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet("Release", "Staging", "Global")]
        [string]$Location,

        [Parameter(Mandatory = $true)]
        [string]$BuildID,

        [Parameter(Mandatory = $false)]
        [string]$DLSS = "Unknown",

        [Parameter(Mandatory = $false)]
        [string]$FrameGen = "Unknown",

        [Parameter(Mandatory = $false)]
        [string]$DLSSD = "Unknown",

        [Parameter(Mandatory = $false)]
        [string]$DeepDVC = "Unknown",

        [Parameter(Mandatory = $false)]
        [string]$StreamlineSDK = "Unknown"
    )

    return [PSCustomObject]@{
        Location = $Location
        BuildID  = $BuildID
        DLSS     = $DLSS
        FrameGen = $FrameGen
        DLSSD   = $DLSSD
        DeepDVC  = $DeepDVC
StreamlineSDK = $StreamlineSDK
    }
}

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
        Status        = "Pending"
        BackupPath    = ""
        ErrorMessage  = ""
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

    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $backupName = "$script:BackupPrefix$timestamp"
    $backupPath = Join-Path $VersionsParentPath $backupName

    try {
        Copy-Item -Path $ReleaseFolderPath -Destination $backupPath -Recurse -Force -ErrorAction Stop

        if (-not (Test-Path $backupPath)) {
            Write-Host "ERROR: Backup verification failed - backup path does not exist after copy." -ForegroundColor Red
            return $null
        }

        return $backupPath
    }
    catch {
        Write-Host "ERROR: Backup failed - $($_.Exception.Message)" -ForegroundColor Red
        return $null
    }
}

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

    try {
        $ErrorActionPreference = "Stop"

        # Remove current (potentially corrupted) release folder contents
        Get-ChildItem -Path $ReleaseFolderPath -Recurse -ErrorAction SilentlyContinue |
            Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

        # Restore from backup
        $backupItems = Get-ChildItem -Path $BackupPath -Recurse -ErrorAction Stop
        foreach ($item in $backupItems) {
            $relativePath = $item.FullName.Substring($BackupPath.Length)
            $destPath = Join-Path $ReleaseFolderPath $relativePath
            $destDir = Split-Path $destPath -Parent

            if (-not (Test-Path $destDir)) {
                New-Item -ItemType Directory -Path $destDir -Force | Out-Null
            }

            if ($item.PSIsContainer -eq $false) {
                Copy-Item -Path $item.FullName -Destination $destPath -Force -ErrorAction Stop
            }
        }

        return $true
    }
    catch {
        return $false
    }
    finally {
        $ErrorActionPreference = "Continue"
    }
}

# ============================================================================
# Public Functions (Exported)
# ============================================================================

function Get-DLSSVersions {
    <#
    .SYNOPSIS
    Gets all installed DLSS versions from Release, Staging, and Global locations.
    .DESCRIPTION
    Scans the NVIDIA NGX Release and Staging directories, and optionally
    the Global (AnWave/dlssglom) directory for installed DLSS versions
    and returns detailed version information including DLSS, FrameGen (dlssg),
    DLSSD, DeepDVC, and StreamlineSDK component versions.
    .PARAMETER Path
    Base NGX directory path. Defaults to C:\ProgramData\NVIDIA\NGX.
    Override for testing with fixture directories.
    .PARAMETER GlobalPath
    Path to the Global (AnWave/dlssglom) folder. If not specified, Global
    scanning is skipped.
    .OUTPUTS
    Array of DLSSVersion objects with Location, BuildID, DLSS, FrameGen,
    DLSSD, DeepDVC, and StreamlineSDK properties.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $false)]
        [string]$Path = $script:DefaultNgxBasePath,

        [Parameter(Mandatory = $false)]
        [string]$GlobalPath = ""
    )

    $results = @()

    $releasePath = Join-Path $Path $script:ReleaseSubPath
    $stagingPath = Join-Path $Path $script:StagingSubPath

    # Scan Release path
    if (Test-Path $releasePath) {
        try {
            $versionFolders = Get-ChildItem -Path $releasePath -Directory -ErrorAction Stop
            foreach ($folder in $versionFolders) {
                try {
                    $config = Get-NgxVersionConfig -FolderPath $folder.FullName
                    $results += New-DLSSVersionObject -Location "Release" -BuildID $folder.Name `
                        -DLSS $config.DLSS -FrameGen $config.FrameGen `
                        -DLSSD $config.DLSSD -DeepDVC $config.DeepDVC
                }
                catch {
                    Write-Warning "Access denied or error reading Release version folder '$($folder.Name)': $($_.Exception.Message)"
                }
            }
        }
        catch {
            Write-Warning "Cannot scan Release path '$releasePath': $($_.Exception.Message)"
        }
    }

    # Scan Staging path
    if (Test-Path $stagingPath) {
        try {
            $versionFolders = Get-ChildItem -Path $stagingPath -Directory -ErrorAction Stop
            foreach ($folder in $versionFolders) {
                try {
                    $config = Get-NgxVersionConfig -FolderPath $folder.FullName
                    $results += New-DLSSVersionObject -Location "Staging" -BuildID $folder.Name `
                        -DLSS $config.DLSS -FrameGen $config.FrameGen `
                        -DLSSD $config.DLSSD -DeepDVC $config.DeepDVC
                }
                catch {
                    Write-Warning "Access denied or error reading Staging version folder '$($folder.Name)': $($_.Exception.Message)"
                }
            }
        }
        catch {
            Write-Warning "Cannot scan Staging path '$stagingPath': $($_.Exception.Message)"
        }
    }

    # Scan Global path (AnWave/dlssglom)
    if ($GlobalPath -ne "" -and (Test-Path $GlobalPath)) {
        try {
            $globalConfig = Get-GlobalDllVersions -GlobalPath $GlobalPath
            # Use the DLSS version as BuildID for Global (no folder-based BuildID)
            $buildId = if ($globalConfig.DLSS -ne "Unknown") { $globalConfig.DLSS } else { "unknown" }
            $results += New-DLSSVersionObject -Location "Global" -BuildID $buildId `
                -DLSS $globalConfig.DLSS -FrameGen $globalConfig.FrameGen `
                -DLSSD $globalConfig.DLSSD -DeepDVC $globalConfig.DeepDVC `
                -StreamlineSDK $globalConfig.StreamlineSDK
        }
        catch {
            Write-Warning "Cannot scan Global path '$GlobalPath': $($_.Exception.Message)"
        }
    }

    return $results
}

function Get-DLSSLatestVersion {
    <#
    .SYNOPSIS
    Gets the latest installed DLSS version across all locations.
    .DESCRIPTION
    Retrieves all DLSS versions and returns the one with the highest
    semantic version number. Optionally filters by location.
    .PARAMETER Path
    Base NGX directory path. Defaults to C:\ProgramData\NVIDIA\NGX.
    Override for testing with fixture directories.
    .PARAMETER GlobalPath
    Path to the Global (AnWave/dlssglom) folder. If not specified, Global
    scanning is skipped.
    .PARAMETER Location
    Optional filter: "Release", "Staging", or "Global". Default is all locations.
    .OUTPUTS
    Single DLSSVersion object, or $null if no versions found.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $false)]
        [string]$Path = $script:DefaultNgxBasePath,

        [Parameter(Mandatory = $false)]
        [string]$GlobalPath = "",

        [Parameter(Mandatory = $false)]
        [ValidateSet("", "Release", "Staging", "Global")]
        [string]$Location = ""
    )

    $allVersions = Get-DLSSVersions -Path $Path -GlobalPath $GlobalPath

    if ($allVersions.Count -eq 0) {
        return $null
    }

    # Filter by location if specified
    if ($Location -ne "") {
        $filtered = @()
        foreach ($v in $allVersions) {
            if ($v.Location -eq $Location) {
                $filtered += $v
            }
        }
        $allVersions = $filtered
    }

    if ($allVersions.Count -eq 0) {
        return $null
    }

    # Convert versions for comparison
    $versionList = @()
    foreach ($v in $allVersions) {
        try {
            $versionNum = [version]$v.DLSS
        }
        catch {
            $versionNum = [version]"0.0.0.0"
        }

        $versionList += [PSCustomObject]@{
            VersionObj  = $versionNum
            VersionData = $v
        }
    }

    # Sort and return latest
    $sorted = $versionList | Sort-Object VersionObj -Descending
    $latest = $sorted | Select-Object -First 1

    return $latest.VersionData
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

    # Compare versions
    try {
        $currentVer = [version]$releaseVersion.DLSS
    }
    catch {
        $currentVer = [version]"0.0.0.0"
    }

    try {
        $newVer = [version]$stagingVersion.DLSS
    }
    catch {
        $newVer = [version]"0.0.0.0"
    }

    if ($newVer -le $currentVer) {
        Write-Host "Release is already up to date (DLSS $currentVer)" -ForegroundColor Green
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
            Write-Host "ERROR: Release path not found: $releaseVersionsPath" -ForegroundColor Red
            $operation.Status = "Failed"
            $operation.ErrorMessage = "Release path not found"
            return $operation
        }

        $releaseFolder = Get-ChildItem -Path $releaseVersionsPath -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -eq $releaseVersion.BuildID } |
            Select-Object -First 1

        if ($releaseFolder -eq $null) {
            # Fallback: use the first release folder
            $releaseFolder = Get-ChildItem -Path $releaseVersionsPath -Directory -ErrorAction SilentlyContinue |
                Select-Object -First 1
        }

        if ($releaseFolder -eq $null) {
            Write-Host "ERROR: Cannot locate release version folder on disk." -ForegroundColor Red
            $operation.Status = "Failed"
            $operation.ErrorMessage = "Cannot locate release version folder"
            return $operation
        }

        # Locate the Staging version folder on disk
        $stagingVersionsPath = Join-Path $Path $script:StagingSubPath
        $stagingFolder = Get-ChildItem -Path $stagingVersionsPath -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -eq $stagingVersion.BuildID } |
            Select-Object -First 1

        if ($stagingFolder -eq $null) {
            # Fallback: use the first staging folder
            $stagingFolder = Get-ChildItem -Path $stagingVersionsPath -Directory -ErrorAction SilentlyContinue |
                Select-Object -First 1
        }

        if ($stagingFolder -eq $null) {
            Write-Host "ERROR: Cannot locate staging version folder on disk." -ForegroundColor Red
            $operation.Status = "Failed"
            $operation.ErrorMessage = "Cannot locate staging version folder"
            return $operation
        }

        # --- Step 1: Create backup ---
        Write-Host "Creating backup..." -ForegroundColor Gray
        $backupPath = New-DLSSBackup -ReleaseFolderPath $releaseFolder.FullName -VersionsParentPath $releaseVersionsPath

        if ($backupPath -eq $null) {
            Write-Host "ERROR: Failed to create backup. Upgrade aborted." -ForegroundColor Red
            Write-Host "Ensure you are running as Administrator if access is denied." -ForegroundColor Yellow
            $operation.Status = "Failed"
            $operation.ErrorMessage = "Backup failed"
            return $operation
        }

        $operation.BackupPath = $backupPath
        Write-Host "Backup created: $(Split-Path $backupPath -Leaf)" -ForegroundColor Gray

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
                    Write-Host "  Updated: $($dll.Name)" -ForegroundColor Green
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
                    Write-Host "  Updated config from staging" -ForegroundColor Green
                }
                else {
                    Write-Warning "Could not find nvngx_package_config.txt in release folder."
                }
            }
            else {
                Write-Host "  No staging config found; release config unchanged" -ForegroundColor Yellow
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

# ============================================================================
# Export Module Members
# ============================================================================

Export-ModuleMember -Function @(
    'Get-DLSSVersions',
    'Get-DLSSLatestVersion',
    'Start-DLSSUpgrade'
)
