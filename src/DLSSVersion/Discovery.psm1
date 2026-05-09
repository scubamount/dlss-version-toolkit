# Discovery.psm1 - DLSS Version Toolkit: Version scanning and discovery (NGX config parsing, DLL reading, source scanning)

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
# Discovery Functions (Exported)
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

    # Read config file as single string (-Raw handles encoding, size, and join in one pass)
    try {
        $contentStr = Get-Content -Path $configFile.FullName -Encoding UTF8 -Raw -ErrorAction Stop
        if ([string]::IsNullOrEmpty($contentStr)) {
            Write-Warning "Config file is empty"
            return [PSCustomObject]@{
                DLSS = $dlssVersion
                FrameGen = $frameGenVersion
                DLSSD = $dlssdVersion
                DeepDVC = $deepdvcVersion
                Message = "Config file empty"
            }
        }
        if ($contentStr.Length -gt 1048576) {
            Write-Warning "Config file is large ($($contentStr.Length) chars), parsing may be slow"
        }
    }
    catch {
        try {
            # Fallback: default system encoding
            $contentStr = Get-Content -Path $configFile.FullName -Raw -ErrorAction Stop
            if ([string]::IsNullOrEmpty($contentStr)) {
                Write-Warning "Config file is empty"
                return [PSCustomObject]@{
                    DLSS = $dlssVersion
                    FrameGen = $frameGenVersion
                    DLSSD = $dlssdVersion
                    DeepDVC = $deepdvcVersion
                    Message = "Config file empty"
                }
            }
        }
        catch {
            Write-Warning "Failed to read config file: $($_.Exception.Message)"
            return [PSCustomObject]@{
                DLSS = $dlssVersion
                FrameGen = $frameGenVersion
                DLSSD = $dlssdVersion
                DeepDVC = $deepdvcVersion
                Message = "Failed to read config"
            }
        }
    }

    # Handle corrupt config files (binary data, null bytes)
    if ($contentStr -match '\x00') {
        Write-Warning "Config file contains binary data (null bytes), likely corrupt: '$FolderPath'"
        return [PSCustomObject]@{
            DLSS = $dlssVersion
            FrameGen = $frameGenVersion
            DLSSD = $dlssdVersion
            DeepDVC = $deepdvcVersion
            Message = "Corrupt config (binary data)"
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

    $results = [System.Collections.ArrayList]::new()

    $releasePath = Join-Path $Path $script:ReleaseSubPath
    $stagingPath = Join-Path $Path $script:StagingSubPath

    # Scan Release path
    if (Test-Path $releasePath) {
        try {
            $versionFolders = Get-ChildItem -Path $releasePath -Directory -ErrorAction Stop
            foreach ($folder in $versionFolders) {
                try {
                    $config = Get-NgxVersionConfig -FolderPath $folder.FullName
                $results.Add((New-DLSSVersionObject -Location "Release" -BuildID $folder.Name `
                    -DLSS $config.DLSS -FrameGen $config.FrameGen `
                    -DLSSD $config.DLSSD -DeepDVC $config.DeepDVC)) | Out-Null
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
                $results.Add((New-DLSSVersionObject -Location "Staging" -BuildID $folder.Name `
                    -DLSS $config.DLSS -FrameGen $config.FrameGen `
                    -DLSSD $config.DLSSD -DeepDVC $config.DeepDVC)) | Out-Null
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
            $results.Add((New-DLSSVersionObject -Location "Global" -BuildID $buildId `
                -DLSS $globalConfig.DLSS -FrameGen $globalConfig.FrameGen `
                -DLSSD $globalConfig.DLSSD -DeepDVC $globalConfig.DeepDVC `
                -StreamlineSDK $globalConfig.StreamlineSDK)) | Out-Null
        }
        catch {
            Write-Warning "Cannot scan Global path '$GlobalPath': $($_.Exception.Message)"
        }
    }

    return @($results.ToArray())
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
    [ValidateSet("DLSS", "FrameGen", "DLSSD", "DeepDVC", "StreamlineSDK")]
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

# ============================================================================
# Export Module Members
# ============================================================================

Export-ModuleMember -Function @(
    'Get-NgxVersionConfig',
    'Get-GlobalDllVersions',
    'Get-DLSSVersions',
    'Get-DLSSLatestVersion',
    'Get-StreamlineVersions'
)