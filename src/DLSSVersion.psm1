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

function Test-VersionNewer {
    <#
    .SYNOPSIS
    Safely compares two version strings to determine if the first is newer.
    .PARAMETER Version1
    First version string to compare.
    .PARAMETER Version2
    Second version string to compare against.
    .OUTPUTS
    Boolean: $true if Version1 > Version2, $false otherwise.
    #>
    param(
        [Parameter(Mandatory = $false)]
        [string]$Version1,

        [Parameter(Mandatory = $false)]
        [string]$Version2
    )

    # Handle null/empty/Unknown as lowest possible version
    $v1Clean = if ([string]::IsNullOrEmpty($Version1) -or $Version1 -eq "Unknown") { "0.0.0.0" } else { $Version1 }
    $v2Clean = if ([string]::IsNullOrEmpty($Version2) -or $Version2 -eq "Unknown") { "0.0.0.0" } else { $Version2 }

    # Normalize non-standard version formats
    # Handle versions like "3.1.0.0.0" (trim to 4 parts) or "3.1.0a" (remove letters)
    $v1Clean = $v1Clean -replace '[a-zA-Z]', ''
    $v2Clean = $v2Clean -replace '[a-zA-Z]', ''

    # Split and take only first 4 parts to handle "3.1.0.0.0" -> "3.1.0.0"
    $v1Parts = ($v1Clean -split '\.')[0..3]
    $v2Parts = ($v2Clean -split '\.')[0..3]

    # Pad with zeros if needed
    while ($v1Parts.Count -lt 4) { $v1Parts += "0" }
    while ($v2Parts.Count -lt 4) { $v2Parts += "0" }

    # Try to cast to version, fallback to comparison if fails
    try {
        $v1Num = [version]$($v1Parts -join '.')
        $v2Num = [version]$($v2Parts -join '.')
        return $v1Num -gt $v2Num
    }
    catch {
        # Fallback: numeric comparison of each segment
        for ($i = 0; $i -lt 4; $i++) {
            $p1 = 0
            $p2 = 0
            [int]::TryParse($v1Parts[$i], [ref]$p1) | Out-Null
            [int]::TryParse($v2Parts[$i], [ref]$p2) | Out-Null

            if ($p1 -gt $p2) { return $true }
            if ($p1 -lt $p2) { return $false }
        }
        return $false
    }
}

function Test-LongPathSupport {
    <#
    .SYNOPSIS
    Checks if Windows long path support (260+ char) is enabled via registry.
    .OUTPUTS
    Boolean: $true if enabled, $false otherwise.
    #>
    try {
        $regValue = Get-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem" -Name "LongPathsEnabled" -ErrorAction Stop
        return ($regValue.LongPathsEnabled -eq 1)
    }
    catch {
        return $false
    }
}

function Get-NgxVersionConfig {
    <#
    .SYNOPSIS
    Parses nvngx_package_config.txt from a version folder to extract component versions.
    .PARAMETER FolderPath
    The version folder path to search for the config file.
    .OUTPUTS
    PSCustomObject with DLSS, FrameGen, DLSSD, and DeepDVC version strings.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$FolderPath
    )

    $dlssVersion = "Unknown"
    $frameGenVersion = "Unknown"
    $dlssdVersion = "Unknown"
    $deepdvcVersion = "Unknown"
    $warningMessage = $null

    # Check for reparse points to avoid following symlinks/junctions
    if (Test-Path $FolderPath) {
        try {
            $item = Get-Item -Path $FolderPath -ErrorAction Stop
            if ($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
                Write-Warning "Folder path '$FolderPath' is a reparse point (symlink/junction). Not following."
                return [PSCustomObject]@{
                    DLSS     = $dlssVersion
                    FrameGen = $frameGenVersion
                    DLSSD    = $dlssdVersion
                    DeepDVC  = $deepdvcVersion
                    Message  = "Skipped reparse point"
                }
            }
        }
        catch {
            Write-Warning "Failed to check reparse point for '$FolderPath': $($_.Exception.Message)"
        }
    }
    else {
        Write-Warning "Folder path '$FolderPath' does not exist."
        return [PSCustomObject]@{
            DLSS     = $dlssVersion
            FrameGen = $frameGenVersion
            DLSSD    = $dlssdVersion
            DeepDVC  = $deepdvcVersion
            Message  = "Folder not found"
        }
    }

    try {
        $configFile = Get-ChildItem -Path $FolderPath -Recurse -Filter $script:ConfigFileName -ErrorAction SilentlyContinue |
            Select-Object -First 1

        if ($null -eq $configFile) {
            Write-Warning "Config file '$script:ConfigFileName' not found in '$FolderPath'."
            return [PSCustomObject]@{
                DLSS     = $dlssVersion
                FrameGen = $frameGenVersion
                DLSSD    = $dlssdVersion
                DeepDVC  = $deepdvcVersion
                Message  = "Config file not found"
            }
        }

        # Check if config file is a reparse point
        try {
            $fileItem = Get-Item -Path $configFile.FullName -ErrorAction Stop
            if ($fileItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
                Write-Warning "Config file '$($configFile.FullName)' is a reparse point. Not following."
                return [PSCustomObject]@{
                    DLSS     = $dlssVersion
                    FrameGen = $frameGenVersion
                    DLSSD    = $dlssdVersion
                    DeepDVC  = $deepdvcVersion
                    Message  = "Skipped reparse point"
                }
            }
        }
        catch {
            Write-Warning "Failed to check reparse point for config file: $($_.Exception.Message)"
        }
    }
    catch {
        Write-Warning "Failed to enumerate config file in '$FolderPath': $($_.Exception.Message)"
        return [PSCustomObject]@{
            DLSS     = $dlssVersion
            FrameGen = $frameGenVersion
            DLSSD    = $dlssdVersion
            DeepDVC  = $deepdvcVersion
            Message  = "Error enumerating files"
        }
    }

    # Check file byte size before reading (10MB = 1048576 bytes)
    try {
        $fileInfo = Get-Item -Path $configFile.FullName -ErrorAction Stop
        if ($fileInfo.Length -gt 1048576) {
            Write-Warning "Config file is large ($($fileInfo.Length) bytes), parsing may be slow"
        }
    }
    catch {
        Write-Warning "Could not check config file size: $($_.Exception.Message)"
    }

    # Read config file with encoding detection
    # In PS 5.1+, Get-Content -Encoding UTF8 auto-detects BOM
    try {
        $content = Get-Content -Path $configFile.FullName -Encoding UTF8 -ErrorAction Stop
        $encodingUsed = "UTF8"
    }
    catch {
        try {
            # Fallback: default system encoding
            $content = Get-Content -Path $configFile.FullName -ErrorAction Stop
            $encodingUsed = "Default"
        }
        catch {
            Write-Warning "Failed to read config file with any encoding: $($_.Exception.Message)"
            return [PSCustomObject]@{
                DLSS     = $dlssVersion
                FrameGen = $frameGenVersion
                DLSSD    = $dlssdVersion
                DeepDVC  = $deepdvcVersion
                Message  = "Failed to read config"
            }
        }
    }

    # Handle empty file
    if ($content.Count -eq 0) {
        Write-Warning "Config file is empty"
        return [PSCustomObject]@{
            DLSS     = $dlssVersion
            FrameGen = $frameGenVersion
            DLSSD    = $dlssdVersion
            DeepDVC  = $deepdvcVersion
            Message  = "Config file empty"
        }
    }

    # Join content into single string for reliable -match/$Matches behavior
    # PowerShell -match on arrays returns true/false per element but does NOT populate $Matches
    $contentStr = $content -join "`n"

    # Handle corrupt config files (binary data, null bytes)
    if ($contentStr -match "(?s)\x00") {
        Write-Warning "Config file contains binary data (null bytes), likely corrupt: '$FolderPath'"
        return [PSCustomObject]@{
            DLSS     = $dlssVersion
            FrameGen = $frameGenVersion
            DLSSD    = $dlssdVersion
            DeepDVC  = $deepdvcVersion
            Message  = "Corrupt config (binary data)"
        }
    }

    # Parse DLSS version and validate format
    if ($contentStr -match "dlss,\s+([\d.]+)") {
        $parsedVersion = $Matches[1]
        if ($parsedVersion -match "^(?=[\d.]+$)(?!\.)[\d.]*\d[\d.]*$") {
            $dlssVersion = $parsedVersion
        }
        else {
            $warningMessage = if ($warningMessage) { "$warningMessage; DLSS version format invalid" } else { "DLSS version format invalid" }
            Write-Warning "Failed to parse DLSS version: format validation failed '$parsedVersion'"
        }
    }
    else {
        $warningMessage = if ($warningMessage) { "$warningMessage; DLSS not found" } else { "DLSS not found" }
        Write-Warning "Failed to parse DLSS version in '$FolderPath': pattern not matched"
    }

    # Parse FrameGen version and validate format
    if ($contentStr -match "dlssg,\s+([\d.]+)") {
        $parsedVersion = $Matches[1]
        if ($parsedVersion -match "^(?=[\d.]+$)(?!\.)[\d.]*\d[\d.]*$") {
            $frameGenVersion = $parsedVersion
        }
        else {
            $warningMessage = if ($warningMessage) { "$warningMessage; FrameGen version format invalid" } else { "FrameGen version format invalid" }
            Write-Warning "Failed to parse FrameGen version: format validation failed '$parsedVersion'"
        }
    }
    else {
        $warningMessage = if ($warningMessage) { "$warningMessage; FrameGen not found" } else { "FrameGen not found" }
        Write-Warning "Failed to parse FrameGen version in '$FolderPath': pattern not matched"
    }

    # Parse DLSSD version and validate format
    if ($contentStr -match "dlssd,\s+([\d.]+)") {
        $parsedVersion = $Matches[1]
        if ($parsedVersion -match "^(?=[\d.]+$)(?!\.)[\d.]*\d[\d.]*$") {
            $dlssdVersion = $parsedVersion
        }
        else {
            $warningMessage = if ($warningMessage) { "$warningMessage; DLSSD version format invalid" } else { "DLSSD version format invalid" }
            Write-Warning "Failed to parse DLSSD version: format validation failed '$parsedVersion'"
        }
    }
    else {
        $warningMessage = if ($warningMessage) { "$warningMessage; DLSSD not found" } else { "DLSSD not found" }
        Write-Warning "Failed to parse DLSSD version in '$FolderPath': pattern not matched"
    }

# Parse DeepDVC version and validate format
# DeepDVC is optional in NGX configs - some builds don't include it.
# Silently default to "Unknown" when absent; only warn on format errors.
if ($contentStr -match "deepdvc,\s+([\d.]+)") {
    $parsedVersion = $Matches[1]
    if ($parsedVersion -match "^(?=[\d.]+$)(?!\.)[\d.]*\d[\d.]*$") {
        $deepdvcVersion = $parsedVersion
    }
    else {
        $warningMessage = if ($warningMessage) { "$warningMessage; DeepDVC version format invalid" } else { "DeepDVC version format invalid" }
        Write-Warning "Failed to parse DeepDVC version: format validation failed '$parsedVersion'"
    }
}
# else: DeepDVC not present in config - this is normal, no warning needed

    return [PSCustomObject]@{
        DLSS     = $dlssVersion
        FrameGen = $frameGenVersion
        DLSSD    = $dlssdVersion
        DeepDVC  = $deepdvcVersion
        Message  = if ($warningMessage) { $warningMessage } else { "Success" }
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
    [ValidateNotNullOrEmpty()]
    [string]$GlobalPath
)

$dlssVersion = "Unknown"
$frameGenVersion = "Unknown"
$dlssdVersion = "Unknown"
$deepdvcVersion = "Unknown"
$streamlineVersion = "Unknown"

    # Add long path support for paths approaching 260-char limit
    $effectivePath = $GlobalPath
    if ($GlobalPath.Length -ge 250) {
        if (Test-LongPathSupport) {
            $effectivePath = "\\?\" + $GlobalPath
        } else {
            Write-Warning "Long path support not enabled (LongPathsEnabled registry key not set to 1). Path may fail if >= 260 chars."
        }
    }

    # Check if effective path is a reparse point (symbolic link, junction, etc.)
try {
    $globalItem = Get-Item -Path $GlobalPath -ErrorAction Stop
    if ($globalItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
        Write-Warning "Global path '$GlobalPath' is a reparse point. Not following."
        return [PSCustomObject]@{
            DLSS           = $dlssVersion
            FrameGen       = $frameGenVersion
            DLSSD          = $dlssdVersion
            DeepDVC        = $deepdvcVersion
            StreamlineSDK  = $streamlineVersion
            Message        = "Skipped reparse point"
        }
    }
}
catch {
    Write-Warning "Failed to check reparse point for GlobalPath: $($_.Exception.Message)"
}

# Helper function to validate version string format
function Test-ValidVersionString {
    param([string]$Version)
    # Version should match pattern like "310.7.0.0" or "2.11.1.0"
    if ([string]::IsNullOrEmpty($Version)) { return $false }
    return $Version -match '^\d+\.\d+(\.\d+){1,3}$'
}

# Helper function to safely read DLL version with per-DLL error handling
function Read-DllVersion {
    param(
        [string]$DllPath,
        [string]$DllName
    )

    $version = "Unknown"

    if (-not (Test-Path $DllPath)) {
        Write-Verbose "DLL not found: $DllName"
        return $version
    }

    # Check if file is a reparse point
    try {
        $fileItem = Get-Item -Path $DllPath -ErrorAction Stop
        if ($fileItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) {
            Write-Warning "DLL '$DllName' is a reparse point. Skipping."
            return $version
        }
    }
    catch {
        Write-Warning "Failed to check attributes for '$DllName': $($_.Exception.Message)"
        return $version
    }

    try {
        $vi = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($DllPath)

        # Handle empty or null FileVersion
        if ([string]::IsNullOrEmpty($vi.FileVersion)) {
            Write-Warning "DLL '$DllName' has no FileVersion info (empty or null)."
            return $version
        }

        # Convert comma to period and validate format
        $version = $vi.FileVersion -replace ',', '.'

        if (-not (Test-ValidVersionString -Version $version)) {
            Write-Warning "DLL '$DllName' has invalid version format: '$version'. Expected pattern like 'X.Y.Z.W'"
            return $version
        }

        Write-Verbose "Read version '$version' from '$DllName'"
        return $version
    }
    catch [System.UnauthorizedAccessException] {
        Write-Warning "Access denied reading '$DllName': $($_.Exception.Message)"
    }
    catch [System.IO.FileNotFoundException] {
        Write-Warning "DLL file not found: '$DllName'"
    }
    catch {
        # Check for invalid PE file (not a valid Windows DLL)
        if ($_.Exception.Message -match "BadImageFormat|Not a valid Win32|invalid image") {
            Write-Warning "DLL '$DllName' is not a valid PE file: $($_.Exception.Message)"
        }
        else {
            Write-Warning "Failed to read version from '$DllName': $($_.Exception.Message)"
        }
    }

    return $version
}

# Read NGX DLL versions - each DLL has its own try/catch
$dlssDll = Join-Path $GlobalPath "nvngx_dlss.dll"
$dlssVersion = Read-DllVersion -DllPath $dlssDll -DllName "nvngx_dlss.dll"

$dlssgDll = Join-Path $GlobalPath "nvngx_dlssg.dll"
$frameGenVersion = Read-DllVersion -DllPath $dlssgDll -DllName "nvngx_dlssg.dll"

$dlssdDll = Join-Path $GlobalPath "nvngx_dlssd.dll"
$dlssdVersion = Read-DllVersion -DllPath $dlssdDll -DllName "nvngx_dlssd.dll"

$deepdvcDll = Join-Path $GlobalPath "nvngx_deepdvc.dll"
$deepdvcVersion = Read-DllVersion -DllPath $deepdvcDll -DllName "nvngx_deepdvc.dll"

# Read Streamline version from sl.common.dll (canonical SL version indicator)
$slCommonDll = Join-Path $GlobalPath "sl.common.dll"
$streamlineVersion = Read-DllVersion -DllPath $slCommonDll -DllName "sl.common.dll"

return [PSCustomObject]@{
    DLSS           = $dlssVersion
    FrameGen      = $frameGenVersion
    DLSSD         = $dlssdVersion
    DeepDVC       = $deepdvcVersion
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
    .PARAMETER Component
    Optional: Which component to compare. Valid values: "DLSS" (default), "FrameGen", "DLSSD", "DeepDVC".
    .OUTPUTS
    Single DLSSVersion object, or $null if no valid versions found.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $false)]
        [string]$Path = $script:DefaultNgxBasePath,

        [Parameter(Mandatory = $false)]
        [string]$GlobalPath = "",

        [Parameter(Mandatory = $false)]
        [ValidateSet("", "Release", "Staging", "Global")]
        [string]$Location = "",

        [Parameter(Mandatory = $false)]
        [ValidateSet("DLSS", "FrameGen", "DLSSD", "DeepDVC")]
        [string]$Component = "DLSS"
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

    # Find the latest version using Test-VersionNewer for safe comparison
    $latestVersion = $null
    $latestData = $null

    foreach ($v in $allVersions) {
        # Get the version string for the specified component
        $versionString = $v.$Component

        # Skip if version is invalid (null, empty, or Unknown)
        if ([string]::IsNullOrEmpty($versionString) -or $versionString -eq "Unknown") {
            continue
        }

        if ($null -eq $latestVersion) {
            $latestVersion = $versionString
            $latestData = $v
        }
        else {
            # Use Test-VersionNewer to safely compare
            if (Test-VersionNewer -Version1 $versionString -Version2 $latestVersion) {
                $latestVersion = $versionString
                $latestData = $v
            }
        }
    }

    # Return null if no valid versions were found
    if ($null -eq $latestData) {
        return $null
    }

    return $latestData
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
        $releaseVersionsPath = Join-Path $Path $script:ReleaseSubPath"
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

        $releaseFolder = Get-ChildItem -Path $releaseVersionsPath -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -eq $releaseVersion.BuildID } |
            Select-Object -First 1

        if ($releaseFolder -eq $null) {
            # Fallback: use the first release folder
            $releaseFolder = Get-ChildItem -Path $releaseVersionsPath -Directory -ErrorAction SilentlyContinue |
                Select-Object -First 1
        }

        if ($releaseFolder -eq $null) {
            Write-Warning "Cannot locate release version folder on disk."
            $operation.Status = "Failed"
            $operation.ErrorMessage = "Cannot locate release version folder"
            return $operation
        }

        # Locate the Staging version folder on disk
        $stagingVersionsPath = Join-Path $Path $script:StagingSubPath"
        $stagingFolder = Get-ChildItem -Path $stagingVersionsPath -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -eq $stagingVersion.BuildID } |
            Select-Object -First 1

        if ($stagingFolder -eq $null) {
            Write-Error "ERROR: Cannot locate staging version folder matching BuildID $($stagingVersion.BuildID) on disk."
            $operation.Status = "Failed"
            $operation.ErrorMessage = "Cannot locate staging version folder"
            return $operation
        }

        if ($stagingFolder -eq $null) {
            Write-Warning "Cannot locate staging version folder on disk."
            $operation.Status = "Failed"
            $operation.ErrorMessage = "Cannot locate staging version folder"
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
                Write-Warning "Could not find nvngx_package_config.txt in release folder."
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
# Streamline SDK Integration Functions
# ============================================================================

function Get-StreamlineVersions {
    <#
    .SYNOPSIS
    Gets DLSS/Streamline versions from a Streamline SDK folder.
    .PARAMETER Path
    Path to the Streamline SDK (folder containing bin\x64 with DLLs).
    .OUTPUTS
    PSCustomObject with component versions or $null if not found.
    #>
    param(
        [Parameter(Mandatory = $false)]
        [string]$Path = ""
    )

    $result = [PSCustomObject]@{
        Source = "StreamlineSDK"
        BasePath = $Path
        Exists = $false
        DLSS = "Unknown"
        FrameGen = "Unknown"
        DLSSD = "Unknown"
        DeepDVC = "Unknown"
        StreamlineSDK = "Unknown"
        DllPaths = @{}
    }

    if ($Path -eq "") {
        # Auto-detect: scan Downloads for any streamline-sdk folder
        $searchBase = if ([string]::IsNullOrEmpty($env:USERPROFILE)) { $null } else { Join-Path $env:USERPROFILE "Downloads" }
        if ($searchBase -and (Test-Path $searchBase)) {
            $found = Get-ChildItem -Path $searchBase -Directory -ErrorAction SilentlyContinue | 
                Where-Object { $_.Name -match 'streamline-sdk' -and (Test-Path (Join-Path $_.FullName "bin\x64\nvngx_dlss.dll")) } | 
                Select-Object -First 1
            if ($found) {
                $Path = $found.FullName
            }
        }
    }

    if ($Path -eq "" -or -not (Test-Path $Path)) {
        return $result
    }

    $binPath = Join-Path $Path "bin\x64"
    if (-not (Test-Path $binPath)) {
        $binPath = $Path
    }

    if (-not (Test-Path (Join-Path $binPath "nvngx_dlss.dll"))) {
        return $result
    }

    $result.Exists = $true
    $result.BasePath = $binPath

    # Read versions from DLL metadata
    $dlls = @{
        "nvngx_dlss.dll" = "DLSS"
        "nvngx_dlssg.dll" = "FrameGen"
        "nvngx_dlssd.dll" = "DLSSD"
        "nvngx_deepdvc.dll" = "DeepDVC"
        "sl.common.dll" = "StreamlineSDK"
    }

    foreach ($dll in $dlls.Keys) {
        $fullPath = Join-Path $binPath $dll
        if (Test-Path $fullPath) {
            try {
                $vi = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($fullPath)
                if (-not [string]::IsNullOrEmpty($vi.FileVersion)) {
                    $version = $vi.FileVersion -replace ',', '.'
                    $prop = $dlls[$dll]
                    $result.$prop = $version
                    $result.DllPaths[$dll] = $fullPath
                } else {
                    Write-Warning "DLL '$dll' has no FileVersion info"
                }
            } catch {
                Write-Warning "Failed to read version from '$dll': $($_.Exception.Message)"
            }
        }
    }

    return $result
}

function Compare-DLSSAllSources {
    <#
    .SYNOPSIS
    Compares DLSS/Streamline versions across all sources.
    .PARAMETER StreamlinePath
    Path to local Streamline SDK. Auto-detected if not provided.
    .PARAMETER GlobalPath  
    Path to AnWave/dlssglom folder.
    .PARAMETER ShowDetails
    Show detailed comparison table.
    .OUTPUTS
    Hashtable with all sources and recommendations.
    #>
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $false)]
        [string]$StreamlinePath = "",

        [Parameter(Mandatory = $false)]
        [string]$GlobalPath = "",

        [Parameter(Mandatory = $false)]
        [switch]$ShowDetails
    )

    $sources = @{}

    # NGX Release
    Write-Host "Scanning NGX Release..." -ForegroundColor Gray
    $ngxRelease = Get-DLSSLatestVersion -Location "Release"
    if ($ngxRelease) {
        $sources["NGX_Release"] = @{
            Location = "Release"
            DLSS = $ngxRelease.DLSS
            FrameGen = $ngxRelease.FrameGen
            DLSSD = $ngxRelease.DLSSD
            DeepDVC = $ngxRelease.DeepDVC
            StreamlineSDK = "N/A"
        }
    }

    # NGX Staging
    Write-Host "Scanning NGX Staging..." -ForegroundColor Gray
    $ngxStaging = Get-DLSSLatestVersion -Location "Staging"
    if ($ngxStaging) {
        $sources["NGX_Staging"] = @{
            Location = "Staging"
            DLSS = $ngxStaging.DLSS
            FrameGen = $ngxStaging.FrameGen
            DLSSD = $ngxStaging.DLSSD
            DeepDVC = $ngxStaging.DeepDVC
            StreamlineSDK = "N/A"
        }
    }

    # Streamline SDK
    Write-Host "Scanning Streamline SDK..." -ForegroundColor Gray
    $sl = Get-StreamlineVersions -Path $StreamlinePath
    if ($sl.Exists) {
        $sources["StreamlineSDK"] = @{
            Location = "StreamlineSDK"
            DLSS = $sl.DLSS
            FrameGen = $sl.FrameGen
            DLSSD = $sl.DLSSD
            DeepDVC = $sl.DeepDVC
            StreamlineSDK = $sl.StreamlineSDK
            Path = $sl.BasePath
            DllPaths = $sl.DllPaths
        }
    } else {
        Write-Host "WARNING: Streamline SDK not found at specified path!" -ForegroundColor Yellow
    }

    # AnWave/Global
    if ($GlobalPath -ne "") {
        Write-Host "Scanning AnWave (Global)..." -ForegroundColor Gray
        $globalScan = Get-DLSSVersions -GlobalPath $GlobalPath | Where-Object { $_.Location -eq "Global" } | Select-Object -First 1
        if ($globalScan) {
            $sources["AnWave_Global"] = @{
                Location = "Global"
                DLSS = $globalScan.DLSS
                FrameGen = $globalScan.FrameGen
                DLSSD = $globalScan.DLSSD
                DeepDVC = $globalScan.DeepDVC
                StreamlineSDK = $globalScan.StreamlineSDK
                Path = $GlobalPath
            }
        }
    }

    # Find newest for each component
    $newest = @{
        DLSS = @{ Version = "0.0.0.0"; Source = "" }
        FrameGen = @{ Version = "0.0.0.0"; Source = "" }
        DLSSD = @{ Version = "0.0.0.0"; Source = "" }
        DeepDVC = @{ Version = "0.0.0.0"; Source = "" }
        StreamlineSDK = @{ Version = "0.0.0.0"; Source = "" }
    }

    foreach ($src in $sources.Keys) {
        $s = $sources[$src]
        foreach ($comp in @("DLSS", "FrameGen", "DLSSD", "DeepDVC", "StreamlineSDK")) {
            if ($s.$comp -ne "Unknown" -and $s.$comp -ne "N/A") {
                if (Test-VersionNewer -Version1 $s.$comp -Version2 $newest[$comp].Version) {
                    $newest[$comp] = @{ Version = $s.$comp; Source = $src }
                }
            }
        }
    }

    # Recommendations
    $recommendations = @()

    # Check if Streamline SDK is newer than NGX
    if ($sources["StreamlineSDK"] -and $sources["NGX_Release"]) {
        $sl = $sources["StreamlineSDK"]
        $ngx = $sources["NGX_Release"]
        if ($sl.DLSS -ne "Unknown" -and $ngx.DLSS -ne "Unknown") {
            if (Test-VersionNewer -Version1 $sl.DLSS -Version2 $ngx.DLSS) {
                $recommendations += [PSCustomObject]@{
                    Action = "Update_NGX_from_Streamline"
                    Description = "Streamline SDK has newer DLSS ($($sl.DLSS)) > ($($ngx.DLSS))"
                    From = "StreamlineSDK"
                    To = "NGX Release"
                }
            }
        }
    }

    if ($ShowDetails) {
        Write-Host ""
        Write-Host "=== Version Comparison ===" -ForegroundColor Cyan
        $table = @()
        foreach ($src in $sources.Keys) {
            $s = $sources[$src]
            $table += [PSCustomObject]@{
                Source = $src
                DLSS = $s.DLSS
                FrameGen = $s.FrameGen
                DLSSD = $s.DLSSD
                DeepDVC = $s.DeepDVC
                Streamline = $s.StreamlineSDK
            }
        }
        $table | Format-Table -AutoSize

        Write-Host "=== Newest Versions ===" -ForegroundColor Cyan
        foreach ($comp in $newest.Keys) {
            $n = $newest[$comp]
            Write-Host "$comp`: $($n.Version) (from $($n.Source))" -ForegroundColor Yellow
        }
    }

    return @{
        Sources = $sources
        Newest = $newest
        Recommendations = $recommendations
    }
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
                    Write-Host "  Copied: $dll" -ForegroundColor Green
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
    'Get-DLSSVersions',
    'Get-DLSSLatestVersion',
    'Start-DLSSUpgrade',
    'Get-StreamlineVersions',
    'Sync-DLSSVersions',
    'Compare-DLSSAllSources'
)
