# DLSS Version Toolkit

A PowerShell module for checking, comparing, and upgrading NVIDIA DLSS versions across all sources on Windows.

## What It Does

NVIDIA DLSS components exist in multiple locations on your system — NGX Release (active override), NGX Staging (driver-staged), AnWave/dlssglom (global injection), and Streamline SDK (downloaded SDK). This tool scans all of them, shows you which version is where, compares them, and lets you upgrade or sync to the newest.

**Supported components:** DLSS, Frame Generation (dlssg), DLSSD, DeepDVC, Streamline SDK

## Quick Start

```powershell
# One command — scan everything, compare, and sync:
powershell -ExecutionPolicy Bypass -File check-dlss-versions.ps1 -All

# Just check what's installed:
powershell -ExecutionPolicy Bypass -File check-dlss-versions.ps1
```

The `-All` flag auto-detects Streamline SDK and AnWave in your Downloads folder, scans all four sources, shows a comparison table, and applies any available updates.

## Installation

### Option 1: Run Directly (No Install)

```powershell
git clone https://github.com/your-repo/dlss-version-toolkit.git
cd dlss-version-toolkit

# Quick check (NGX Release + Staging only)
powershell -ExecutionPolicy Bypass -File check-dlss-versions.ps1

# Full scan and sync
powershell -ExecutionPolicy Bypass -File check-dlss-versions.ps1 -All
```

### Option 2: Install as PowerShell Module

```powershell
# Install (one-time)
powershell -ExecutionPolicy Bypass -File install.ps1

# Use from any session
Import-Module DLSSVersion
Get-DLSSVersions

# Uninstall
powershell -ExecutionPolicy Bypass -File install.ps1 -Uninstall
```

### Option 3: Manual Install

Copy `src/DLSSVersion.psm1` and `src/DLSSVersion.psd1` to:

```
$env:USERPROFILE\Documents\PowerShell\Modules\DLSSVersion\
```

## CLI Usage

The `check-dlss-versions.ps1` script is the main entry point:

| Flag | Description |
|------|-------------|
| *(none)* | Show installed versions from NGX Release + Staging |
| `-Upgrade` | Upgrade Release to the latest Staging version |
| `-Compare` | Compare versions across all detected sources |
| `-Sync` | Copy newest DLLs to target locations |
| `-All` | Full workflow: compare + sync (recommended) |
| `-GlobalPath "C:\path"` | Specify AnWave/dlssglom folder |
| `-StreamlinePath "C:\path"` | Specify Streamline SDK folder |

### Examples

```powershell
# Basic version check
.\check-dlss-versions.ps1

# Upgrade Release to latest Staging
.\check-dlss-versions.ps1 -Upgrade

# Compare all sources with specific paths
.\check-dlss-versions.ps1 -Compare -GlobalPath "C:\Tools\nvidiaDlssGlom" -StreamlinePath "C:\SDKs\streamline-sdk-v2.11.1"

# Full holistic update
.\check-dlss-versions.ps1 -All
```

## PowerShell Module API

After `Import-Module DLSSVersion`, these commands are available:

### Get-DLSSVersions

Scan all installed DLSS versions from NGX Release, Staging, and optionally AnWave/Global.

```powershell
# Scan NGX only
Get-DLSSVersions

# Include AnWave/dlssglom
Get-DLSSVersions -GlobalPath "C:\Path\To\nvidiaDlssGlom"
```

Returns objects with: `Location`, `BuildID`, `DLSS`, `FrameGen`, `DLSSD`, `DeepDVC`, `StreamlineSDK`

### Get-DLSSLatestVersion

Find the latest installed version across all locations.

```powershell
# Latest from any location
Get-DLSSLatestVersion

# Latest from a specific location
Get-DLSSLatestVersion -Location "Staging"

# Latest by component
Get-DLSSLatestVersion -Component "FrameGen"

# Include Global in search
Get-DLSSLatestVersion -GlobalPath "C:\Path\To\nvidiaDlssGlom"
```

Parameters:
- `-Location` — Filter: `Release`, `Staging`, or `Global`
- `-Component` — Compare by: `DLSS` (default), `FrameGen`, `DLSSD`, `DeepDVC`

### Start-DLSSUpgrade

Upgrade NGX Release to the latest Staging version. Creates a timestamped backup first; rolls back automatically on failure.

```powershell
Start-DLSSUpgrade
```

Process: backup Release → copy Staging DLLs → copy Staging config → verify. Supports `-WhatIf` and `-Confirm`.

### Compare-DLSSAllSources

Compare versions across NGX Release, Staging, Streamline SDK, and AnWave.

```powershell
# Auto-detect Streamline SDK in Downloads
Compare-DLSSAllSources -ShowDetails

# With specific paths
Compare-DLSSAllSources -StreamlinePath "C:\SDKs\streamline-sdk" -GlobalPath "C:\Tools\nvidiaDlssGlom" -ShowDetails
```

Returns a hashtable with `Sources`, `Newest` (per component), and `Recommendations`.

### Sync-DLSSVersions

Copy the newest DLLs from one source to another based on comparison results.

```powershell
# Sync Streamline SDK → NGX Release
Sync-DLSSVersions -Source StreamlineSDK -Target NGX_Release -Force

# Sync Streamline SDK → AnWave
Sync-DLSSVersions -Source StreamlineSDK -Target AnWave -GlobalPath "C:\Tools\nvidiaDlssGlom" -Force
```

Parameters:
- `-Source` — `StreamlineSDK`, `Staging`, or `Global`
- `-Target` — `NGX_Release` or `AnWave`
- `-Force` — Skip confirmation prompt
- Supports `-WhatIf` and `-Confirm`

### Get-StreamlineVersions

Scan a local Streamline SDK folder for component versions.

```powershell
# Auto-detect in Downloads
Get-StreamlineVersions

# Specific path
Get-StreamlineVersions -Path "C:\SDKs\streamline-sdk-v2.11.1"
```

## Version Sources

| Source | Location on Disk | How Versions Are Read |
|--------|-----------------|----------------------|
| **NGX Release** | `C:\ProgramData\NVIDIA\NGX\models\dlss_override\versions\` | `nvngx_package_config.txt` |
| **NGX Staging** | `C:\ProgramData\NVIDIA\NGX\Staging\models\dlss_override\versions\` | `nvngx_package_config.txt` |
| **AnWave / dlssglom** | User-specified folder | DLL file metadata |
| **Streamline SDK** | `Downloads\streamline-sdk*\bin\x64\` | DLL file metadata |

> **Note:** NGX configs always include DLSS, FrameGen, and DLSSD. DeepDVC is optional — some builds don't include it, and the toolkit silently reports `Unknown` for those.

## Example Output

### Get-DLSSVersions

```
Location BuildID   DLSS       FrameGen   DLSSD      DeepDVC    StreamlineSDK
-------- -------   ----       --------   -----      -------    -------------
Release  20317442  310.6.0.0  310.6.0.0  310.6.0.0  Unknown    Unknown
Staging  20317443  310.5.3.0  310.5.3.0  310.5.3.0  310.5.2.0 Unknown
Staging  20317696  310.6.0.0  310.6.0.0  310.6.0.0  310.6.0.0 Unknown

Latest available: DLSS 310.6.0.0, Frame Gen 310.6.0.0, DLSSD 310.6.0.0 in Release build 20317442
```

### Compare-DLSSAllSources -ShowDetails

```
=== Version Comparison ===
Source         DLSS       FrameGen   DLSSD      DeepDVC    Streamline
-------------- ---------- ---------- ---------- ---------- ----------
NGX_Release    310.6.0.0  310.6.0.0  310.6.0.0  Unknown    N/A
NGX_Staging    310.6.0.0  310.6.0.0  310.6.0.0  310.6.0.0 N/A
StreamlineSDK  310.6.0.0  310.6.0.0  310.6.0.0  310.6.0.0 2.11.1.0

=== Newest Versions ===
DLSS: 310.6.0.0 (from NGX_Release)
FrameGen: 310.6.0.0 (from NGX_Release)
DLSSD: 310.6.0.0 (from NGX_Release)
DeepDVC: 310.6.0.0 (from NGX_Staging)
StreamlineSDK: 2.11.1.0 (from StreamlineSDK)
```

## Troubleshooting

### "Access Denied" When Upgrading

Run PowerShell as Administrator. The upgrade writes to `C:\ProgramData\NVIDIA\NGX\`, which requires elevated permissions.

### "No DLSS versions found"

Ensure the NVIDIA App is installed and DLSS override is enabled. The NGX folder structure is created when the DLSS override feature is first used.

### DeepDVC Shows "Unknown"

This is normal — some NVIDIA driver builds don't include DeepDVC in the NGX config file. The toolkit handles this gracefully.

### StreamlineSDK Shows "Unknown"

NGX Release and Staging don't contain Streamline SDK DLLs. Use `-All` or `-Compare` to include the Streamline SDK source.

### Module Not Loading

```powershell
Import-Module ./src/DLSSVersion.psm1 -Verbose
```

### Encoding Issues

The tool reads config files as UTF-8. If you see garbled output, ensure your terminal supports UTF-8.

## Project Structure

```
dlss-version-toolkit/
├── check-dlss-versions.ps1   # CLI entry point
├── install.ps1                # Local install/uninstall script
├── src/
│   ├── DLSSVersion.psm1       # Module implementation
│   └── DLSSVersion.psd1       # Module manifest
├── tests/
│   └── DLSSVersion.Tests.ps1  # Pester test suite (24 tests)
├── bucket/
│   └── dlss-version-toolkit.json  # Scoop manifest
├── winget/
│   └── dlss-version-toolkit.yaml  # Winget manifest
└── LICENSE                    # MIT
```

## Requirements

- Windows 10 version 2004+ or Windows 11
- NVIDIA GPU with DLSS support
- NVIDIA App with DLSS override enabled
- PowerShell 5.1+ (included with Windows)

## License

MIT
