# DLSS Version Toolkit

A Windows PowerShell tool for checking and upgrading NVIDIA DLSS versions across multiple sources:

- **NVIDIA NGX Release** (`C:\ProgramData\NVIDIA\NGX\models\dlss_override\versions\`) - Active DLSS override from NVIDIA App
- **NVIDIA NGX Staging** (`C:\ProgramData\NVIDIA\NGX\Staging\models\dlss_override\versions\`) - Driver-staged DLSS versions
- **AnWave / dlssglom** (custom folder) - Global DLL injection overrides

Supports DLSS 3.x, DLSS Frame Generation, DLSS Deep Learning (HDR), DeepDVC, and Streamline SDK plugin versions. Includes upgrade capability to copy newer Staging versions to Release location with automatic backup.

**Use cases:**
- Check what DLSS version is currently active on your system
- Compare Release vs Staging vs Global override versions
- Upgrade Release DLSS to match the latest Staging version
- Scan AnWave/dlssglom folder for global override versions

## Prerequisites

- Windows 10 version 2004+ or Windows 11
- NVIDIA GPU with DLSS support
- NVIDIA App with DLSS override enabled
- PowerShell 5.1+ (included with Windows)

## Installation

### Option 1: Direct Execution (Recommended - No Install Required)

Download or clone the repository and run the script directly:

```powershell
# Clone or download, then run:
powershell -ExecutionPolicy Bypass -File check-dlss-versions.ps1
```

### Option 2: Local Install to PowerShell Modules

Run the included installer to install to your local PowerShell modules folder:

```powershell
# Install locally (one-time)
powershell -ExecutionPolicy Bypass -File install.ps1

# Now you can use from any session:
Import-Module DLSSVersion
Get-DLSSVersions

# Or run the CLI directly:
check-dlss-versions

# To uninstall:
powershell -ExecutionPolicy Bypass -File install.ps1 -Uninstall
```

### Option 3: Manual Install

Copy the `src` folder contents to your PowerShell modules folder:

```powershell
# Target location:
$env:USERPROFILE\Documents\PowerShell\Modules\DLSSVersion\

# Copy DLSSVersion.psm1 and DLSSVersion.psd1 to that folder
```

## Usage

### Check Installed DLSS Versions

```powershell
# Direct execution
powershell -ExecutionPolicy Bypass -File check-dlss-versions.ps1

# Module
Import-Module DLSSVersion
Get-DLSSVersions
```

Output:

```text
=== DLSS Version Checker ===

Location BuildID    DLSS      FrameGen  DLSSD     DeepDVC  StreamlineSDK
-------- -------    ----      --------  -----     -------  -------------
Release  310.6.0.0  310.6.0.0 310.6.0.0 310.6.0.0 310.6.0 Unknown
Staging  310.7.0.0  310.7.0.0 310.7.0.0 310.7.0.0 310.7.0 Unknown

Latest available: DLSS 310.7.0.0, Frame Gen 310.7.0.0, DLSSD 310.7.0.0, DeepDVC 310.7.0.0 in Staging build 310.7.0.0
```

### Check Global Override Versions (AnWave / dlssglom)

AnWave (dlssglom) provides global DLSS/Streamline DLL overrides by injecting DLLs from its own folder. Use the `-GlobalPath` parameter to scan it alongside your NGX versions:

```powershell
# Direct execution with AnWave path
powershell -ExecutionPolicy Bypass -File check-dlss-versions.ps1 -GlobalPath "C:\Path\To\nvidiaDlssGlom"

# Module
Import-Module DLSSVersion
Get-DLSSVersions -GlobalPath "C:\Path\To\nvidiaDlssGlom"
```

Output with Global:

```text
=== DLSS Version Checker ===

Location BuildID    DLSS      FrameGen  DLSSD     DeepDVC  StreamlineSDK
-------- -------    ----      --------  -----     -------  -------------
Release  310.6.0.0  310.6.0.0 310.6.0.0 310.6.0.0 310.6.0 Unknown
Staging  310.7.0.0  310.7.0.0 310.7.0.0 310.7.0.0 310.7.0 Unknown
Global   310.6.0.0  310.6.0.0 310.6.0.0 310.6.0.0 310.6.0 2.11.1.0

Latest available: DLSS 310.7.0.0, Frame Gen 310.7.0.0, DLSSD 310.7.0.0, DeepDVC 310.7.0.0 in Staging build 310.7.0.0
```

> **Note**: Unlike Release/Staging which read versions from `nvngx_package_config.txt`, Global overrides read versions directly from DLL file metadata using `[System.Diagnostics.FileVersionInfo]`.

Output:

```
=== DLSS Version Checker ===

Location BuildID    DLSS       FrameGen
-------- --------    ----       --------
Release  310.6.0.0  310.6.0.0  310.6.0.0
Staging  310.7.0.0  310.7.0.0  310.7.0.0

Latest available: DLSS 310.7.0.0 (Frame Gen 310.7.0.0) in Staging build 310.7.0.0
```

### Upgrade to Latest Staging Version

```powershell
# Direct execution with upgrade flag
powershell -ExecutionPolicy Bypass -File check-dlss-versions.ps1 -Upgrade

# Module
Import-Module DLSSVersion
Start-DLSSUpgrade
```

The upgrade process:

1. Creates a timestamped backup of your current Release DLSS
2. Copies the latest Staging DLL files to the Release location
3. Copies the Staging configuration file to the Release location
4. Displays per-file success messages

### Get the Latest Version Info

```powershell
Import-Module DLSSVersion
$latest = Get-DLSSLatestVersion
Write-Host "Latest: $($latest.DLSS) in $($latest.Location)"

# Filter by location
$global = Get-DLSSLatestVersion -GlobalPath "C:\Path\To\nvidiaDlssGlom" -Location "Global"
```

### Get Help

```powershell
Get-Help Get-DLSSVersions
Get-Help Get-DLSSLatestVersion
Get-Help Start-DLSSUpgrade
```

## Common Issues

### "Access Denied" When Upgrading

Run PowerShell as Administrator. The upgrade writes to `C:\ProgramData\NVIDIA\NGX\`, which requires elevated permissions.

### "No DLSS versions found"

Ensure the NVIDIA App is installed and DLSS override is enabled. The tool scans these paths:

- `C:\ProgramData\NVIDIA\NGX\models\dlss_override\versions\` (Release)
- `C:\ProgramData\NVIDIA\NGX\Staging\models\dlss_override\versions\` (Staging)
- (AnWave install directory) — use `-GlobalPath` to specify

### "NVIDIA NGX folder not found"

Install a game with DLSS support or enable DLSS in the NVIDIA App. The NGX folder structure is created when the DLSS override feature is first used.

### Module Not Loading

Check for errors with verbose output:

```powershell
Import-Module ./src/DLSSVersion.psm1 -Verbose
```

### Encoding Issues

The tool uses UTF-8 encoding for file reads. If you see garbled text, ensure your terminal supports UTF-8.

## Project Structure

```
dlss-version-toolkit/
├── check-dlss-versions.ps1 # CLI entry point (standalone script)
├── src/
│   ├── DLSSVersion.psm1 # Module implementation
│   └── DLSSVersion.psd1 # Module manifest
├── specs/001-dlss-version-checker/
│   ├── data-model.md # Schema (DLSSVersion, DLSSComponent, UpgradeOperation, StreamlineComponent)
│   ├── spec.md # Feature specification
│   ├── contracts/module-interface.md
│   └── research.md
├── tests/
│   └── DLSSVersion.Tests.ps1 # Pester tests (dev-only)
├── bucket/
│   └── dlss-version-toolkit.json # Scoop manifest
├── winget/
│   └── dlss-version-toolkit.yaml # winget manifest
└── README.md
```

## Supported Locations

| Location | Source | Version Source | Description |
|----------|--------|----------------|-------------|
| Release | NGX ProgramData | nvngx_package_config.txt | Active DLSS override |
| Staging | NGX ProgramData | nvngx_package_config.txt | NVIDIA driver staging |
| Global | AnWave/dlssglom folder | DLL file metadata | Global DLL injection override |
dlss-version-toolkit/
├── check-dlss-versions.ps1    # CLI entry point (standalone script)
├── src/
│   ├── DLSSVersion.psm1       # Module implementation
│   └── DLSSVersion.psd1       # Module manifest
├── tests/
│   └── DLSSVersion.Tests.ps1  # Pester tests (dev-only)
├── bucket/
│   └── dlss-version-toolkit.json  # Scoop manifest
├── winget/
│   └── dlss-version-toolkit.yaml  # winget manifest
└── README.md
```

## License

MIT
