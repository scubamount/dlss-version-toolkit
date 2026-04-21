# DLSS Version Toolkit

A Windows PowerShell tool for checking and upgrading NVIDIA DLSS versions installed via the NVIDIA App DLSS override feature.

## Prerequisites

- Windows 10 version 2004+ or Windows 11
- NVIDIA GPU with DLSS support
- NVIDIA App with DLSS override enabled
- PowerShell 5.1+ (included with Windows)

## Installation

### Option 1: Direct Execution (No Install)

Download `check-dlss-versions.ps1` and run it directly. No installation required.

```powershell
powershell -ExecutionPolicy Bypass -File check-dlss-versions.ps1
```

### Option 2: PowerShell Gallery

```powershell
# Install the module (one-time)
Install-Module DLSSVersion -Scope CurrentUser

# Import and use
Import-Module DLSSVersion
Get-DLSSVersions
```

### Option 3: Scoop

```powershell
# Add the bucket (one-time)
scoop bucket add dlss-version-toolkit <bucket-url>

# Install
scoop install dlss-version-toolkit

# Use
check-dlss-versions
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
