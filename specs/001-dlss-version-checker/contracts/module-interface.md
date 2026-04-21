# Contract: PowerShell Module Interface

**Branch**: `001-dlss-version-checker` | **Date**: 2026-04-20
**Input**: `/speckit.plan` command - PowerShell module interface specification for Phase 1.

---

## Module Overview

**Module Name**: `DLSSVersion`
**Module File**: `DLSSVersion.psm1`
**Manifest File**: `DLSSVersion.psd1`

---

## Exported Functions

### `Get-DLSSVersions`

Scans NVIDIA NGX folders and returns installed DLSS versions.

```powershell
function Get-DLSSVersions {
    [CmdletBinding()]
    param()
    
    [DLSSVersion[]]$Result
}
```

| Property | Value |
|----------|-------|
| Verb | `Get` |
| Noun | `DLSSVersions` |
| Output Type | `DLSSVersion[]` (array of PSCustomObject) |

**Input**: None (no parameters)

**Output**: Array of DLSSVersion objects:
```powershell
[PSCustomObject]@{
    Location = "Release"  # or "Staging"
    BuildID = "310.6.0.0"
    DLSS = "310.6.0.0"
    FrameGen = "310.6.0.0"
}
```

**Example Usage**:
```powershell
Import-Module ./src/DLSSVersion.psm1
$versions = Get-DLSSVersions
$versions | Where-Object Location -eq 'Release'
```

---

### `Get-DLSSLatestVersion`

Returns the latest DLSS version across all locations.

```powershell
function Get-DLSSLatestVersion {
    [CmdletBinding()]
    param()
    
    [PSCustomObject]$Result
}
```

| Property | Value |
|----------|-------|
| Verb | `Get` |
| Noun | `DLSSLatestVersion` |
| Output Type | `PSCustomObject` |

**Input**: None (no parameters)

**Output**: Single DLSSVersion object representing latest version

**Example Usage**:
```powershell
Import-Module ./src/DLSSVersion.psm1
$latest = Get-DLSSLatestVersion
Write-Host "Latest: $($latest.DLSS) in $($latest.Location)"
```

---

### `Start-DLSSUpgrade`

Performs upgrade from Staging to Release DLSS.

```powershell
function Start-DLSSUpgrade {
    [CmdletBinding(ConfirmImpact='High')]
    param()
    
    [UpgradeOperation]$Result
}
```

| Property | Value |
|----------|-------|
| Verb | `Start` |
| Noun | `DLSSUpgrade` |
| Output Type | `UpgradeOperation` |
| Confirm Impact | `High` (prompts for confirmation) |

**Input**: None (no parameters)

**Output**: UpgradeOperation object:
```powershell
[PSCustomObject]@{
    SourceVersion = <DLSSVersion>  # Staging version
    TargetVersion = <DLSSVersion>  # Release version
    Status = "Completed"  # or "Failed", "Pending", etc.
    BackupPath = "C:\ProgramData\NVIDIA\NGX\models\dlss_override\versions Backup-2026-04-20-123045"
    ErrorMessage = $null  # if failed
}
```

**Example Usage**:
```powershell
Import-Module ./src/DLSSVersion.psm1
$result = Start-DLSSUpgrade -Confirm:$false
if ($result.Status -eq "Completed") {
    Write-Host "Upgrade successful"
}
```

---

## Module Manifest Fields

### DLSSVersion.psd1

```powershell
@{
    RootModule = 'DLSSVersion.psm1'
    ModuleVersion = '1.0.0'
    GUID = 'a1b2c3d4-e5f6-7890-abcd-ef1234567890'
    Author = 'DLSS Version Toolkit Contributors'
    Description = 'PowerShell module for checking and upgrading NVIDIA DLSS versions'
    PowerShellVersion = '5.1'
    FunctionsToExport = @(
        'Get-DLSSVersions',
        'Get-DLSSLatestVersion',
        'Start-DLSSUpgrade'
    )
    CmdletsToExport = @()
    VariablesToExport = @()
    AliasesToExport = @()
    PrivateData = @{
        PSData = @{
            Tags = @('DLSS', 'NVIDIA', 'Gaming', 'Version')
            ProjectUri = 'https://github.com/username/dlss-version-toolkit'
            LicenseUri = 'https://github.com/username/dlss-version-toolkit/blob/main/LICENSE'
            ReleaseNotes = 'Initial release'
        }
    }
}
```

### Required Fields

| Field | Description | Required |
|-------|-------------|----------|
| `RootModule` | Primary module file | Yes |
| `ModuleVersion` | Semantic version (x.y.z) | Yes |
| `GUID` | Unique identifier | Yes |
| `Author` | Module author | Yes |
| `Description` | Module purpose | Yes |
| `PowerShellVersion` | Minimum PS version | Yes |
| `FunctionsToExport` | Exported functions | Yes |

### Recommended Fields

| Field | Description | Recommended |
|-------|-------------|-------------|
| `ProjectUri` | GitHub repository | Yes |
| `LicenseUri` | License file link | Yes |
| `Tags` | PowerShell Gallery tags | Yes |
| `ReleaseNotes` | Version release notes | Yes |

---

## Export Configuration

### DLSSVersion.psm1

```powershell
# Module functions
function Get-DLSSVersions { ... }
function Get-DLSSLatestVersion { { ... }
function Start-DLSSUpgrade { ... }

# Export functions
Export-ModuleMember -Function @(
    'Get-DLSSVersions',
    'Get-DLSSLatestVersion',
    'Start-DLSSUpgrade'
)
```

---

## Pipeline Integration

Functions support PowerShell pipeline where applicable:

```powershell
# Example pipeline usage
Get-DLSSVersions | Where-Object Location -eq 'Release' | Format-Table

# Filter by location
Get-DLSSVersions | Sort-Object DLSS -Descending | Select-Object -First 1
```

---

## Error Handling

All functions use `$ErrorActionPreference` for non-terminating errors. Use `Try/Catch` for terminating errors:

```powershell
try {
    $versions = Get-DLSSVersions
} catch {
    Write-Error "Failed to get DLSS versions: $_"
}
```

---

## Module Loading

### Manual Import

```powershell
# Import from file
Import-Module ./src/DLSSVersion.psm1

# Import with version
Import-Module ./src/DLSSVersion.psm1 -MinimumVersion 1.0.0
```

### PowerShell Gallery Install

```powershell
# Install for current user
Install-Module DLSSVersion -Scope CurrentUser

# Import after install
Import-Module DLSSVersion
```

### With Function Aliases

After import, functions available by name:
```powershell
Get-DLSSVersions
Get-DLSSLatestVersion
Start-DLSSUpgrade
```

Get-Help integration:
```powershell
Get-Help Get-DLSSVersions
Get-Help Get-DLSSLatestVersion
Get-Help Start-DLSSUpgrade
```

---

## Tab Completion

Functions support standard PowerShell tab completion:

```powershell
Get-DLSSVersions -<Tab>
# No parameters to complete

Start-DLSSUpgrade -<Tab>
# -Confirm -WhatIf
```