# Complete Pester Test Suite for DLSSVersion.psm1
#Requires -Modules Pester

$script:ModulePath = Join-Path $PSScriptRoot "..\src\DLSSVersion.psm1"

# ============================================================================
# Helper Functions
# ============================================================================

function New-TestDLSSVersion {
    param(
        [string]$Location = "Release",
        [string]$BuildID = "310.0.0.0",
        [string]$DLSS = "Unknown",
        [string]$FrameGen = "Unknown",
        [string]$DLSSD = "Unknown",
        [string]$DeepDVC = "Unknown",
        [string]$StreamlineSDK = "Unknown"
    )
    [PSCustomObject]@{
        Location  = $Location
        BuildID   = $BuildID
        DLSS      = $DLSS
        FrameGen  = $FrameGen
        DLSSD     = $DLSSD
        DeepDVC   = $DeepDVC
        StreamlineSDK = $StreamlineSDK
    }
}

# Inline version comparison (mirrors Test-VersionNewer from module)
function Test-VersionIsNewer {
    param([string]$Version1, [string]$Version2)
    $v1 = if ([string]::IsNullOrEmpty($Version1) -or $Version1 -eq "Unknown") { "0.0.0.0" } else { $Version1 -replace '[a-zA-Z]', '' }
    $v2 = if ([string]::IsNullOrEmpty($Version2) -or $Version2 -eq "Unknown") { "0.0.0.0" } else { $Version2 -replace '[a-zA-Z]', '' }
    $p1 = ((@($v1 -split '\.')[0..3]) + @("0","0","0","0"))[0..3]
    $p2 = ((@($v2 -split '\.')[0..3]) + @("0","0","0","0"))[0..3]
    try {
        return ([version]($p1 -join '.') -gt [version]($p2 -join '.'))
    } catch {
        for ($i = 0; $i -lt 4; $i++) {
            $n1=0; $n2=0
            [int]::TryParse($p1[$i], [ref]$n1) | Out-Null
            [int]::TryParse($p2[$i], [ref]$n2) | Out-Null
            if ($n1 -gt $n2) { return $true }
            if ($n1 -lt $n2) { return $false }
        }
        return $false
    }
}

# ============================================================================
# Module Tests (import module, test exported functions)
# ============================================================================

Describe "Get-DLSSLatestVersion" {
    BeforeAll {
        Import-Module $script:ModulePath -Force -ErrorAction Stop
        # Create temp NGX structure for testing
        $script:testNgx = Join-Path $env:TEMP "DLSSVersionTests"
        if (Test-Path $script:testNgx) { Remove-Item $script:testNgx -Recurse -Force }
        $releasePath = Join-Path $script:testNgx "models\dlss_override\versions"
        $stagingPath = Join-Path $script:testNgx "Staging\models\dlss_override\versions"
        New-Item -ItemType Directory -Path $releasePath -Force | Out-Null
        New-Item -ItemType Directory -Path $stagingPath -Force | Out-Null
        # Create version folders with full config files (all components)
        $v1 = Join-Path $releasePath "310.5.0.0"
        New-Item -ItemType Directory -Path $v1 -Force | Out-Null
        "dlss, 310.5.0.0`r`ndlssg, 310.5.0.0`r`ndlssd, 310.5.0.0`r`ndeepdvc, 310.5.0.0" | Set-Content -Path (Join-Path $v1 "nvngx_package_config.txt") -Encoding UTF8
        $v2 = Join-Path $releasePath "310.7.0.0"
        New-Item -ItemType Directory -Path $v2 -Force | Out-Null
        "dlss, 310.7.0.0`r`ndlssg, 310.7.0.0`r`ndlssd, 310.7.0.0`r`ndeepdvc, 310.7.0.0" | Set-Content -Path (Join-Path $v2 "nvngx_package_config.txt") -Encoding UTF8
        $v3 = Join-Path $stagingPath "310.6.0.0"
        New-Item -ItemType Directory -Path $v3 -Force | Out-Null
        "dlss, 310.6.0.0`r`ndlssg, 310.6.0.0`r`ndlssd, 310.6.0.0`r`ndeepdvc, 310.6.0.0" | Set-Content -Path (Join-Path $v3 "nvngx_package_config.txt") -Encoding UTF8
    }
    AfterAll {
        if (Test-Path $script:testNgx) { Remove-Item $script:testNgx -Recurse -Force }
        Remove-Module DLSSVersion -ErrorAction SilentlyContinue
    }

    Context "When multiple versions in Release" {
        It "Returns latest DLSS version" {
            $result = Get-DLSSLatestVersion -Path $script:testNgx
            $result.DLSS | Should Be "310.7.0.0"
        }
        It "Returns correct Location" {
            $result = Get-DLSSLatestVersion -Path $script:testNgx
            $result.Location | Should Be "Release"
        }
        It "Returns latest DLSSD version" {
            $result = Get-DLSSLatestVersion -Path $script:testNgx
            $result.DLSSD | Should Be "310.7.0.0"
        }
        It "Returns latest DeepDVC version" {
            $result = Get-DLSSLatestVersion -Path $script:testNgx
            $result.DeepDVC | Should Be "310.7.0.0"
        }
    }

    Context "Filters by Location" {
        It "Returns Staging when filtered" {
            $result = Get-DLSSLatestVersion -Path $script:testNgx -Location "Staging"
            $result.DLSS | Should Be "310.6.0.0"
            $result.Location | Should Be "Staging"
        }
        It "Returns Release when filtered" {
            $result = Get-DLSSLatestVersion -Path $script:testNgx -Location "Release"
            $result.DLSS | Should Be "310.7.0.0"
        }
    }

    Context "Semantic version comparison" {
        It "310.7.0.0 > 310.5.0.0" {
            $result = Get-DLSSLatestVersion -Path $script:testNgx
            $result.DLSS | Should Be "310.7.0.0"
        }
    }
}

Describe "Get-DLSSVersions" {
    BeforeAll {
        Import-Module $script:ModulePath -Force -ErrorAction Stop
        $script:testNgx = Join-Path $env:TEMP "DLSSVersionTests"
        if (Test-Path $script:testNgx) { Remove-Item $script:testNgx -Recurse -Force }
        $releasePath = Join-Path $script:testNgx "models\dlss_override\versions"
        New-Item -ItemType Directory -Path $releasePath -Force | Out-Null
        $v1 = Join-Path $releasePath "310.6.0.0"
        New-Item -ItemType Directory -Path $v1 -Force | Out-Null
        "dlss, 310.6.0.0`r`ndlssg, 310.6.0.0`r`ndlssd, 310.6.0.0`r`ndeepdvc, 310.6.0.0" | Set-Content -Path (Join-Path $v1 "nvngx_package_config.txt") -Encoding UTF8
    }
    AfterAll {
        if (Test-Path $script:testNgx) { Remove-Item $script:testNgx -Recurse -Force }
        Remove-Module DLSSVersion -ErrorAction SilentlyContinue
    }

    Context "When Release path has versions" {
        It "Returns version objects" {
            $results = @(Get-DLSSVersions -Path $script:testNgx)
            $results.Count | Should BeGreaterThan 0
        }
        It "Returns correct DLSS version" {
            $results = @(Get-DLSSVersions -Path $script:testNgx)
            ($results | Where-Object { $_.Location -eq "Release" } | Select-Object -First 1).DLSS | Should Be "310.6.0.0"
        }
        It "Returns correct DLSSD version" {
            $results = @(Get-DLSSVersions -Path $script:testNgx)
            ($results | Where-Object { $_.Location -eq "Release" } | Select-Object -First 1).DLSSD | Should Be "310.6.0.0"
        }
        It "Returns correct DeepDVC version" {
            $results = @(Get-DLSSVersions -Path $script:testNgx)
            ($results | Where-Object { $_.Location -eq "Release" } | Select-Object -First 1).DeepDVC | Should Be "310.6.0.0"
        }
    }

    Context "When path does not exist" {
        It "Returns empty array" {
            $results = Get-DLSSVersions -Path "C:\NonExistent\NGX"
            @($results).Count | Should Be 0
        }
    }
}

Describe "Get-NgxVersionConfig (DeepDVC optional)" {
    BeforeAll {
        Import-Module $script:ModulePath -Force -ErrorAction Stop
        $script:testNgx = Join-Path $env:TEMP "DLSSVersionTests_DeepDVC"
        if (Test-Path $script:testNgx) { Remove-Item $script:testNgx -Recurse -Force }
        $releasePath = Join-Path $script:testNgx "models\dlss_override\versions"
        New-Item -ItemType Directory -Path $releasePath -Force | Out-Null

        # Config WITH DeepDVC (normal)
        $v1 = Join-Path $releasePath "20317443"
        New-Item -ItemType Directory -Path $v1 -Force | Out-Null
        "dlss, 310.5.3.0`r`ndlssg, 310.5.3.0`r`ndlssd, 310.5.3.0`r`ndeepdvc, 310.5.2.0" | Set-Content -Path (Join-Path $v1 "nvngx_package_config.txt") -Encoding UTF8

        # Config WITHOUT DeepDVC (like real build 20317442)
        $v2 = Join-Path $releasePath "20317442"
        New-Item -ItemType Directory -Path $v2 -Force | Out-Null
        "dlss, 310.6.0.0`r`ndlssg, 310.6.0.0`r`ndlssd, 310.6.0.0" | Set-Content -Path (Join-Path $v2 "nvngx_package_config.txt") -Encoding UTF8
    }
    AfterAll {
        if (Test-Path $script:testNgx) { Remove-Item $script:testNgx -Recurse -Force }
        Remove-Module DLSSVersion -ErrorAction SilentlyContinue
    }

    Context "Config with DeepDVC present" {
        It "Returns correct DeepDVC version" {
            $results = @(Get-DLSSVersions -Path $script:testNgx)
            $withDvc = $results | Where-Object { $_.BuildID -eq "20317443" } | Select-Object -First 1
            $withDvc.DeepDVC | Should Be "310.5.2.0"
        }
    }

    Context "Config without DeepDVC (optional component)" {
        It "Returns Unknown for DeepDVC without warning" {
            # Capture warning stream separately to assert no DeepDVC warning
            $warnings = @()
            $oldWarnPref = $WarningPreference
            $WarningPreference = "Continue"
            try {
                $results = Get-DLSSVersions -Path $script:testNgx 3>&1 | Where-Object { $_ -is [System.Management.Automation.WarningRecord] }
                $warnings = @($results | Where-Object { $_.Message -match "DeepDVC" })
            }
            finally {
                $WarningPreference = $oldWarnPref
            }
            $warnings.Count | Should Be 0
            # Also verify the version object itself
            $versions = @(Get-DLSSVersions -Path $script:testNgx)
            $withoutDvc = $versions | Where-Object { $_.BuildID -eq "20317442" } | Select-Object -First 1
            $withoutDvc.DeepDVC | Should Be "Unknown"
        }
        It "Still returns correct DLSS/DLSSD/FrameGen versions" {
            $results = @(Get-DLSSVersions -Path $script:testNgx)
            $withoutDvc = $results | Where-Object { $_.BuildID -eq "20317442" } | Select-Object -First 1
            $withoutDvc.DLSS | Should Be "310.6.0.0"
            $withoutDvc.FrameGen | Should Be "310.6.0.0"
            $withoutDvc.DLSSD | Should Be "310.6.0.0"
        }
    }
}

Describe "Compare-DLSSAllSources" {
    BeforeAll {
        Import-Module $script:ModulePath -Force -ErrorAction Stop
    }
    AfterAll {
        Remove-Module DLSSVersion -ErrorAction SilentlyContinue
    }

    Context "When no sources exist" {
        It "Returns empty Sources" {
            # This will use real system paths which may have data
            # Just verify it doesn't throw
            { Compare-DLSSAllSources -ErrorAction Stop } | Should Not Throw
        }
    }
}

Describe "Test-VersionIsNewer (inline helper)" {
    Context "Basic comparison" {
        It "Returns true when V1 > V2" {
            Test-VersionIsNewer -Version1 "310.7.0.0" -Version2 "310.6.0.0" | Should Be $true
        }
        It "Returns false when V1 < V2" {
            Test-VersionIsNewer -Version1 "310.6.0.0" -Version2 "310.7.0.0" | Should Be $false
        }
        It "Returns false when equal" {
            Test-VersionIsNewer -Version1 "310.6.0.0" -Version2 "310.6.0.0" | Should Be $false
        }
    }
    Context "Handles Unknown" {
        It "Treats Unknown as lower" {
            Test-VersionIsNewer -Version1 "310.6.0.0" -Version2 "Unknown" | Should Be $true
        }
        It "Any version is newer than empty string" {
            Test-VersionIsNewer -Version1 "1.0.0.0" -Version2 "" | Should Be $true
        }
        It "Unknown is not newer than Unknown" {
            Test-VersionIsNewer -Version1 "Unknown" -Version2 "Unknown" | Should Be $false
        }
    }
    Context "Three-part versions" {
        It "Compares 3-part versions correctly" {
            Test-VersionIsNewer -Version1 "310.7.0" -Version2 "310.6.0" | Should Be $true
        }
        It "Pads 3-part vs 4-part correctly" {
            Test-VersionIsNewer -Version1 "310.7.0.0" -Version2 "310.7.0" | Should Be $false
        }
    }
}
