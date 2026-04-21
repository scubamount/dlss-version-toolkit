# Contract: PowerShell Module Interface

**Branch**: `001-dlss-version-checker` | **Date**: 2026-04-20 | **Phase**: 1

---

## Module Overview

**Module Name**: `DLSSVersion`
**Module File**: `DLSSVersion.psm1`
**Manifest File**: `DLSSVersion.psd1`
**Location**: `src/DLSSVersion.psm1` and `src/DLSSVersion.psd1`

---

## Exported Functions

### `Get-DLSSVersions`

Scans NVIDIA NGX folders and returns installed DLSS versions.

```powershell
function Get-DLSSVersions {
    [CmdletBinding()]
    param(
        [string]$Path = "C:\ProgramData\NVIDIA\NGX"
    )

    # Returns: DLSSVersion[]
}
```

| Property | Value |
|----------|-------|
| Verb | `Get` |
| Noun | `DLSSVersions` |
| Output Type | `DLSSVersion[]` (array of PSCustomObject) |

**Parameters**:

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `$Path` | string | No | `C:\ProgramData\NVIDIA\NGX` | Base NGX directory path. Override for testing with fixture directories. |

**Output**: Array of DLSSVersion objects:
```powershell
[PSCustomObject]@{
    Location = "Release"  # or "Staging"
    BuildID  = "310.6.0.0"
    DLSS     = "310.6.0.0"
    FrameGen = "310.6.0.0"
}
```

**Example Usage**:
```powershell
# Production usage (default path)
Import-Module ./src/DLSSVersion.psm1
$versions = Get-DLSSVersions

# Test usage (fixture path)
$versions = Get-DLSSVersions -Path "$TestDrive\NGX"

# Filter by location
$versions | Where-Object Location -eq 'Release'
```

---

### `Get-DLSSLatestVersion`

Returns the latest DLSS version across all locations.

```powershell
function Get-DLSSLatestVersion {
    [CmdletBinding()]
    param(
        [string]$Path = "C:\ProgramData\NVIDIA\NGX"
    )

    # Returns: PSCustomObject (single DLSSVersion)
}
```

| Property | Value |
|----------|-------|
| Verb | `Get` |
| Noun | `DLSSLatestVersion` |
| Output Type | `PSCustomObject` (single DLSSVersion) |

**Parameters**:

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `$Path` | string | No | `C:\ProgramData\NVIDIA\NGX` | Base NGX directory path. Override for testing. |

**Output**: Single DLSSVersion object representing the latest version (sorted by `[version]` descending). Returns `$null` if no versions found.

**Example Usage**:
```powershell
Import-Module ./src/DLSSVersion.psm1
$latest = Get-DLSSLatestVersion
if ($null -ne $latest) {
    Write-Host "Latest: $($latest.DLSS) in $($latest.Location)"
}
```

---

### `Start-DLSSUpgrade`

Performs upgrade from Staging to Release DLSS with backup/restore safety.

```powershell
function Start-DLSSUpgrade {
    [CmdletBinding(ConfirmImpact='High', SupportsShouldProcess=$true)]
    param(
        [string]$Path = "C:\ProgramData\NVIDIA\NGX"
    )

    # Returns: UpgradeOperation
}
```

| Property | Value |
|----------|-------|
| Verb | `Start` |
| Noun | `DLSSUpgrade` |
| Output Type | `UpgradeOperation` |
| Confirm Impact | `High` (prompts for confirmation unless `-Confirm:$false`) |
| Supports ShouldProcess | Yes (enables `-WhatIf` for dry-run) |

**Parameters**:

| Parameter | Type | Required | Default | Description |
|-----------|------|----------|---------|-------------|
| `$Path` | string | No | `C:\ProgramData\NVIDIA\NGX` | Base NGX directory path. Override for testing. |

**Output**: UpgradeOperation object:
```powershell
[PSCustomObject]@{
    SourceVersion = <DLSSVersion>   # Staging version
    TargetVersion = <DLSSVersion>   # Release version
    Status        = "Completed"     # or "Failed", "RolledBack", etc.
    BackupPath    = "C:\...\versions\.dlss-backup-20260420-143052"
    ErrorMessage  = $null           # populated if failed
}
```

**Example Usage**:
```powershell
Import-Module ./src/DLSSVersion.psm1

# With confirmation prompt
$result = Start-DLSSUpgrade

# Skip confirmation (for automation)
$result = Start-DLSSUpgrade -Confirm:$false

# Dry run (no changes made)
$result = Start-DLSSUpgrade -WhatIf

# Test with fixture path
$result = Start-DLSSUpgrade -Path "$TestDrive\NGX" -Confirm:$false

if ($result.Status -eq "Completed") {
    Write-Host "Upgrade successful"
}
```

---

## Module Manifest Fields

### DLSSVersion.psd1

```powershell
@{
    RootModule        = 'DLSSVersion.psm1'
    ModuleVersion     = '1.0.0'
    GUID              = 'a1b2c3d4-e5f6-7890-abcd-ef1234567890'
    Author            = 'DLSS Version Toolkit Contributors'
    Description       = 'PowerShell module for checking and upgrading NVIDIA DLSS override versions'
    PowerShellVersion = '5.1'
    FunctionsToExport = @(
        'Get-DLSSVersions',
        'Get-DLSSLatestVersion',
        'Start-DLSSUpgrade'
    )
    CmdletsToExport   = @()
    VariablesToExport = @()
    AliasesToExport   = @()
    PrivateData       = @{
        PSData = @{
            Tags         = @('DLSS', 'NVIDIA', 'Gaming', 'Version')
            ProjectUri   = 'https://github.com/username/dlss-version-toolkit'
            LicenseUri   = 'https://github.com/username/dlss-version-toolkit/blob/main/LICENSE'
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
| `GUID` | Unique identifier (generate with `[guid]::NewGuid()`) | Yes |
| `Author` | Module author | Yes |
| `Description` | Module purpose | Yes |
| `PowerShellVersion` | Minimum PS version (`5.1`) | Yes |
| `FunctionsToExport` | Explicit list of exported functions | Yes |

### Recommended Fields

| Field | Description | Recommended |
|-------|-------------|-------------|
| `ProjectUri` | GitHub repository URL | Yes |
| `LicenseUri` | License file link | Yes |
| `Tags` | PowerShell Gallery search tags | Yes |
| `ReleaseNotes` | Version release notes | Yes |

---

## Export Configuration

### DLSSVersion.psm1

```powershell
# Module-scope variables (not exported)
$script:ReleaseSubPath = "models\dlss_override\versions"
$script:StagingSubPath = "Staging\models\dlss_override\versions"
$script:ConfigFileName = "nvngx_package_config.txt"
$script:DllPattern = "nvngx_*.dll"
$script:BackupPrefix = ".dlss-backup-"

# Internal helper functions (not exported)
function Get-DLSSVersionFromConfig { ... }
function New-DLSSBackup { ... }
function Restore-DLSSBackup { ... }

# Exported functions
function Get-DLSSVersions { ... }
function Get-DLSSLatestVersion { ... }
function Start-DLSSUpgrade { ... }

# Explicit export (prevents helper leakage)
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
# Pipeline filtering
Get-DLSSVersions | Where-Object Location -eq 'Release' | Format-Table

# Sort and select
Get-DLSSVersions | Sort-Object { [version]$_.DLSS } -Descending | Select-Object -First 1

# Combine with other commands
Get-DLSSVersions | Where-Object { $_.DLSS -ne "Unknown" } | Measure-Object
```

---

## Error Handling

All functions use `[CmdletBinding()]` for proper error handling:

- **Non-terminating errors**: Respect `$ErrorActionPreference`. Use `-ErrorAction SilentlyContinue` for expected failures (e.g., missing folders).
- **Terminating errors**: Use `try/catch` for critical operations (backup creation, file copy during upgrade). Throw terminating errors with `Write-Error -ErrorAction Stop` or `throw`.
- **Upgrade-specific errors**: `Start-DLSSUpgrade` catches all errors during file operations, attempts rollback, and returns an `UpgradeOperation` with `Status = "Failed"` or `"RolledBack"` rather than throwing.

```powershell
# Example: consumer error handling
try {
    $versions = Get-DLSSVersions -ErrorAction Stop
} catch {
    Write-Host "Error: $_" -ForegroundColor Red
    exit 1
}
```

---

## Module Loading

### Manual Import (Development)

```powershell
# Import from file
Import-Module ./src/DLSSVersion.psm1

# Import with verbose output (debugging)
Import-Module ./src/DLSSVersion.psm1 -Verbose

# Check exported functions
Get-Command -Module DLSSVersion
```

### PowerShell Gallery Install (Production)

```powershell
# Install for current user (no admin required)
Install-Module DLSSVersion -Scope CurrentUser

# Import after install
Import-Module DLSSVersion

# Update to latest version
Update-Module DLSSVersion
```

### Get-Help Integration

```powershell
# View function help
Get-Help Get-DLSSVersions
Get-Help Get-DLSSLatestVersion
Get-Help Start-DLSSUpgrade

# View full help with examples
Get-Help Start-DLSSUpgrade -Full

# View online help (if ProjectUri set)
Get-Help Start-DLSSUpgrade -Online
```

---

## Tab Completion

Functions support standard PowerShell tab completion:

```powershell
Get-DLSSVersions -<Tab>
# -Path -Debug -ErrorAction -ErrorVariable -OutVariable -OutBuffer -Verbose -WarningAction -WarningVariable

Get-DLSSLatestVersion -<Tab>
# -Path -Debug -ErrorAction ...

Start-DLSSUpgrade -<Tab>
# -Path -Confirm -WhatIf -Debug -ErrorAction ...
```

---

## ShouldProcess Support

`Start-DLSSUpgrade` supports `-WhatIf` and `-Confirm` via `[CmdletBinding(SupportsShouldProcess=$true)]`:

```powershell
# Dry run: show what would happen without making changes
Start-DLSSUpgrade -WhatIf

# Prompt before each action
Start-DLSSUpgrade -Confirm

# Skip confirmation (for scripted/automated use)
Start-DLSSUpgrade -Confirm:$false
```

This satisfies Constitution Principle II (Safe by Default) by providing both a confirmation gate and a dry-run capability.

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