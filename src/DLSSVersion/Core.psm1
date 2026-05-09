# Core.psm1 - DLSS Version Toolkit: Core utilities (version comparison, DLL reading, object factories)

# ============================================================================
# Shared Script Variables
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
# Core Utility Functions
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

function Test-ValidVersionString {
    <#
    .SYNOPSIS
    Validates that a version string matches the expected format.
    .PARAMETER Version
    Version string to validate.
    .OUTPUTS
    Boolean: $true if valid, $false otherwise.
    #>
    param(
        [string]$Version
    )

    # Version should match pattern like "310.7.0.0" or "2.11.1.0"
    if ([string]::IsNullOrEmpty($Version)) { return $false }
    return $Version -match '^\d+\.\d+(\.\d+){1,3}$'
}

function Read-DllVersion {
    <#
    .SYNOPSIS
    Reads the file version from a DLL using Windows file metadata.
    .PARAMETER DllPath
    Full path to the DLL file.
    .PARAMETER DllName
    Name of the DLL (for logging/display purposes).
    .OUTPUTS
    Version string, or "Unknown" if reading fails.
    #>
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

# ============================================================================
# Module Exports
# ============================================================================

Export-ModuleMember -Function @(
    'Test-VersionNewer',
    'Test-LongPathSupport',
    'Test-ValidVersionString',
    'Read-DllVersion',
    'New-DLSSVersionObject'
)