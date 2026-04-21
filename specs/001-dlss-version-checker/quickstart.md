# Quickstart Guide: DLSS Version Toolkit

**Branch**: `001-dlss-version-checker` | **Date**: 2026-04-20
**Input**: `/speckit.plan` command - Quickstart guide for Phase 1.

---

## User Quickstart

### Prerequisites

- Windows 10 version 2004+ or Windows 11
- NVIDIA GPU with DLSS support
- NVIDIA App with DLSS override enabled

### Install Option 1: Direct Execution (No Install)

```powershell
# Clone or download the repository
# Run directly without installation

powershell -ExecutionPolicy Bypass -File check-dlss-versions.ps1
```

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