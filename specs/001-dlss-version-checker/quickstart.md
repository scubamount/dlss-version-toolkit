# Quickstart Guide: DLSS Version Toolkit

**Branch**: `001-dlss-version-checker` | **Date**: 2026-04-20 | **Phase**: 1

---

## User Quickstart

### Prerequisites

- Windows 10 version 2004+ or Windows 11 x64
- NVIDIA GPU with DLSS support
- NVIDIA App with DLSS override enabled
- No additional software installation required (PowerShell 5.1 ships with Windows)

### Install Option 1: Direct Execution (Zero Install)

```powershell
# Download check-dlss-versions.ps1 from GitHub releases
# Run directly -- no installation, no dependencies

powershell -ExecutionPolicy Bypass -File check-dlss-versions.ps1
```

This is the fastest way to use the tool. Download the single script file and run it. The script auto-imports the DLSSVersion module from the `src/` directory.

### Install Option 2: PowerShell Gallery

```powershell
# Install the module (one-time, no admin required)
Install-Module DLSSVersion -Scope CurrentUser

# Import and use
Import-Module DLSSVersion
Get-DLSSVersions
```

After installation, functions are available in any PowerShell session after `Import-Module DLSSVersion`.

### Install Option 3: Scoop

```powershell
# Add bucket (one-time)
scoop bucket add ngc

# Install
scoop install dlss-version-toolkit

# Use (Scoop adds to PATH)
dlss-versions
```

### Install Option 4: winget

```powershell
# Install
winget install --id DLSSVersionToolkit.DLSSVersionToolkit

# Use (winget adds to PATH)
dlss-versions
```

---

## Usage

### Check DLSS Versions (Default, Read-Only)

```powershell
# Direct execution
powershell -ExecutionPolicy Bypass -File check-dlss-versions.ps1

# Module usage
Import-Module DLSSVersion
Get-DLSSVersions
```

Output:
```
=== DLSS Version Checker ===

Location BuildID    DLSS        FrameGen
-------- --------    ----        --------
Release  310.6.0.0  310.6.0.0   310.6.0.0
Staging  310.7.0.0  310.7.0.0   310.7.0.0

Latest available: DLSS 310.7.0.0 (Frame Gen 310.7.0.0) in Staging build 310.7.0.0
```

### Find Latest Version Only

```powershell
# Module usage
Import-Module DLSSVersion
$latest = Get-DLSSLatestVersion
Write-Host "Latest: $($latest.DLSS) in $($latest.Location)"
```

### Upgrade to Latest Staging

```powershell
# Direct execution with upgrade flag
powershell -ExecutionPolicy Bypass -File check-dlss-versions.ps1 -Upgrade

# Module usage (prompts for confirmation)
Import-Module DLSSVersion
Start-DLSSUpgrade

# Module usage (skip confirmation prompt)
Start-DLSSUpgrade -Confirm:$false

# Dry run (show what would happen, no changes)
Start-DLSSUpgrade -WhatIf
```

The upgrade process:
1. Creates a timestamped backup of your current Release DLSS (`.dlss-backup-<timestamp>` folder)
2. Copies the latest Staging DLL files to Release using `Copy-Item` (preserves encoding)
3. Copies the Staging config file to Release
4. Displays per-file success messages
5. If any step fails, attempts automatic rollback from backup

---

## Common Issues

### "Access Denied" When Upgrading

Run PowerShell as Administrator. The upgrade writes to `C:\ProgramData\NVIDIA\NGX\`, which requires elevated permissions. Read-only check mode does not require admin.

### "No DLSS versions found"

Ensure NVIDIA App is installed and DLSS override is enabled. The tool scans:
- `C:\ProgramData\NVIDIA\NGX\models\dlss_override\versions\` (Release)
- `C:\ProgramData\NVIDIA\NGX\Staging\models\dlss_override\versions\` (Staging)

### "NVIDIA NGX folder not found"

Install a game with DLSS support or enable DLSS in NVIDIA App. The NGX folder structure is created by the NVIDIA driver when DLSS override is first used.

### "ExecutionPolicy" Errors

Use the `-ExecutionPolicy Bypass` flag as shown in the examples. This is a Windows security feature that prevents unsigned scripts from running by default.

---

## Developer Quickstart

### Prerequisites for Development

- PowerShell 5.1+ (Windows PowerShell, ships with Windows)
- Pester 5.x (for testing): `Install-Module Pester -Scope CurrentUser -Force`
- Git (for version control)

### Setup

```powershell
# Clone repository
git clone https://github.com/username/dlss-version-toolkit.git
cd dlss-version-toolkit

# Verify module loads
Import-Module ./src/DLSSVersion.psm1

# Check exported functions
Get-Command -Module DLSSVersion
# Expected output: Get-DLSSVersions, Get-DLSSLatestVersion, Start-DLSSUpgrade
```

### Run Tests

```powershell
# Install Pester if not already installed
Install-Module Pester -Scope CurrentUser -Force

# Run all tests
Invoke-Pester ./tests

# Run specific test file
Invoke-Pester ./tests/DLSSVersion.Tests.ps1

# Run with detailed output
Invoke-Pester ./tests -Output Detailed

# Run a specific test by name
Invoke-Pester -Path ./tests/DLSSVersion.Tests.ps1 -TestName "Get-DLSSVersions returns Release versions"
```

Tests use Pester's `$TestDrive` for filesystem isolation -- no real NVIDIA NGX directories required. Test fixtures are created in `BeforeAll` blocks and auto-cleaned after each `Describe` block.

### Build Module for Distribution

```powershell
# The module is ready to publish as-is from src/
# No build step required -- PowerShell modules are plain text

# Verify manifest before publishing
Test-ModuleManifest ./src/DLSSVersion.psd1

# Publish to PowerShell Gallery (requires API key)
Publish-Module -Path ./src -NuGetApiKey <your-key>
```

### Local Testing Against Real NGX Directories

```powershell
# Import module
Import-Module ./src/DLSSVersion.psm1

# Test scan functions (read-only, no admin required)
$versions = Get-DLSSVersions
$versions | Format-Table

# Test latest version detection
$latest = Get-DLSSLatestVersion

# Test upgrade with dry run (no changes made)
Start-DLSSUpgrade -WhatIf

# Test upgrade for real (requires admin, creates backup)
# Start-DLSSUpgrade -Confirm:$false
```

### Project Structure

```
dlss-version-toolkit/
├── check-dlss-versions.ps1   # CLI entry point (thin wrapper, ~20 LOC)
├── src/
│   ├── DLSSVersion.psm1      # Module implementation (core functions)
│   └── DLSSVersion.psd1      # Module manifest (metadata for Gallery)
├── tests/
│   └── DLSSVersion.Tests.ps1 # Pester tests (dev-only dependency)
└── README.md
```

---

## Help

```powershell
# Get help for module functions
Get-Help Get-DLSSVersions
Get-Help Get-DLSSLatestVersion
Get-Help Start-DLSSUpgrade

# Full help with parameter details and examples
Get-Help Start-DLSSUpgrade -Full

# CLI help (view script header comments)
Get-Content check-dlss-versions.ps1 | Select-Object -First 5
```

---

## Troubleshooting

### Test Failures

Run individual tests to isolate the issue:
```powershell
Invoke-Pester -Path ./tests/DLSSVersion.Tests.ps1 -TestName "Get-DLSSVersions"
```

### Module Not Loading

Check for syntax errors with verbose import:
```powershell
Import-Module ./src/DLSSVersion.psm1 -Verbose
```

### Encoding Issues

The tool uses `Get-Content -Raw` for reads and `Copy-Item` for file copies during upgrade. If you see garbled text in config files, the original NVIDIA config may have non-standard encoding. The tool preserves original encoding via `Copy-Item`.

### Path Issues

Verify NVIDIA NGX paths exist:
```powershell
Test-Path "C:\ProgramData\NVIDIA\NGX\models\dlss_override\versions\"
Test-Path "C:\ProgramData\NVIDIA\NGX\Staging\models\dlss_override\versions\"
```

### PS5.1 Compatibility

The module uses only PS5.1-compatible syntax. If you encounter parse errors, ensure you are not accidentally using PS7-only features (ternary `?:`, null-coalescing `??`, chain operators `&&`/`||`).

That's it. The tool will scan your NVIDIA NGX folders and display installed versions.

### Install Option 2: PowerShell Gallery

```powershell
# Install the module (one-time)
Install-Module DLSSVersion -Scope CurrentUser

# Import and use
Import-Module DLSSVersion
Get-DLSSVersions
```

### Install Option 3: Scoop

```powershell
# Add bucket (one-time)
scoop bucket add ngc

# Install
scoop install dlss-version-toolkit

# Use
dlss-versions
```

### Install Option 4: winget

```powershell
# Install
winget install --id DLSSVersionToolkit.DLSSVersionToolkit

# Use
dlss-versions
```

---

## Usage

### Check DLSS Versions

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

Location   BuildID     DLSS       FrameGen
---------  --------   ---------  --------
Release   310.6.0.0  310.6.0.0  310.6.0.0
Staging   310.7.0.0  310.7.0.0  310.7.0.0

Latest available: DLSS 310.7.0.0 (Frame Gen 310.7.0.0) in Staging build 310.7.0.0
```

### Upgrade to Latest

```powershell
# Direct execution with upgrade flag
powershell -ExecutionPolicy Bypass -File check-dlss-versions.ps1 -Upgrade

# Module
Start-DLSSUpgrade
```

The tool will:
1. Create a timestamped backup of your current Release DLSS
2. Copy the latest Staging files to Release
3. Display success message

---

## Common Issues

### "Access Denied" When Upgrading

Run PowerShell as Administrator. The upgrade writes to `C:\ProgramData\NVIDIA\`.

### "No DLSS versions found"

Ensure NVIDIA App is installed and DLSS override is enabled. The tool scans:
- `C:\ProgramData\NVIDIA\NGX\models\dlss_override\versions\`
- `C:\ProgramData\NVIDIA\NGX\Staging\models\dlss_override\versions\`

### "NVIDIA NGX folder not found"

Install a game with DLSS support or enable DLSS in NVIDIA App.

---

## Developer Quickstart

### Prerequisites for Development

- PowerShell 5.1+
- Pester (for testing): `Install-Module Pester -Scope CurrentUser -Force`

### Setup

```powershell
# Clone repository
git clone https://github.com/username/dlss-version-toolkit.git
cd dlss-version-toolkit

# Verify module loads
Import-Module ./src/DLSSVersion.psm1

# Check functions exported
Get-Command -Module DLSSVersion
```

### Run Tests

```powershell
# All tests
Invoke-Pester ./tests

# Specific test file
Invoke-Pester ./tests/DLSSVersion.Tests.ps1

# With coverage
Invoke-Pester ./tests -Coverage ./src/DLSSVersion.psm1
```

### Build Module

```powershell
# Create distribution package
$modulePath = "./src"
$outputPath = "./dist"

# Copy module files to distribution folder
Copy-Item $modulePath/DLSSVersion.psm1 $outputPath
Copy-Item $modulePath/DLSSVersion.psd1 $outputPath
Copy-Item ./check-dlss-versions.ps1 $outputPath

# Publish to PowerShell Gallery (requires account)
Publish-Module -Path $outputPath -NuGetApiKey <your-key>
```

### Local Testing

```powershell
# Import module for testing
Import-Module ./src/DLSSVersion.psm1

# Test scan functions
$versions = Get-DLSSVersions
$versions | Format-Table

# Test upgrade (with -Confirm:$false to skip prompt)
$result = Start-DLSSUpgrade -Confirm:$false

# Verify output matches expected format
$result.Status | Should -Be "Completed"
```

### Project Structure

```
dlss-version-toolkit/
├── check-dlss-versions.ps1    # CLI entry point
├── src/
│   ├── DLSSVersion.psm1      # Module implementation
│   └── DLSSVersion.psd1       # Module manifest
├── tests/
│   └── DLSSVersion.Tests.ps1   # Pester tests
└── README.md
```

---

## Help

```powershell
# Get help for module functions
Get-Help Get-DLSSVersions
Get-Help Get-DLSSLatestVersion
Get-Help Start-DLSSUpgrade

# CLI help (view script comments)
Get-Content check-dlss-versions.ps1 | Select-Object -First 10
```

---

## Troubleshooting

### Test Failures

Run individual tests to debug:
```powershell
Invoke-Pester -Path ./tests/DLSSVersion.Tests.ps1 -TestName "Get-DLSSVersions returns Release versions"
```

### Module Not Loading

Check for errors:
```powershell
Import-Module ./src/DLSSVersion.psm1 -Verbose
```

### Encoding Issues

The tool uses UTF8 encoding. If you see garbled text, ensure your terminal supports UTF8.

### Path Issues

Verify NVIDIA NGX paths exist:
```powershell
Test-Path "C:\ProgramData\NVIDIA\NGX\models\dlss_override\versions\"
Test-Path "C:\ProgramData\NVIDIA\NGX\Staging\models\dlss_override\versions\"
```