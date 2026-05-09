# Plan.psm1 - DLSS Version Toolkit: Comparison and recommendation logic (cross-source version analysis)

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
        # Streamline SDK is optional (requires separate download) — not a warning
        if ($StreamlinePath -ne "") {
            Write-Host " Streamline SDK not found at specified path." -ForegroundColor DarkGray
        }
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

Export-ModuleMember -Function @('Compare-DLSSAllSources')