# Contract: CLI Interface

**Branch**: `001-dlss-version-checker` | **Date**: 2026-04-20 | **Phase**: 1

---

## Interface Overview

The CLI interface is the standalone script entry point: `check-dlss-versions.ps1`. This script imports the `DLSSVersion` module and provides a user-friendly command-line experience with two modes: check (read-only, default) and upgrade (requires explicit flag).

### Execution Modes

1. **Check Mode** (default): Scan and display installed DLSS versions
2. **Upgrade Mode** (`-Upgrade` flag): Upgrade Release DLSS to latest Staging version

---

## Commands

### Check Mode (default)

**Usage**:
```
powershell -ExecutionPolicy Bypass -File check-dlss-versions.ps1
```

**Behavior**:
1. Import DLSSVersion module from `src/DLSSVersion.psm1`
2. Call `Get-DLSSVersions` to scan Release and Staging folders
3. Display combined results in table format via `Format-Table -AutoSize`
4. Call `Get-DLSSLatestVersion` to identify the newest version
5. Display latest available version summary line
6. Exit with code 0

### Upgrade Mode

**Usage**:
```
powershell -ExecutionPolicy Bypass -File check-dlss-versions.ps1 -Upgrade
```

**Behavior**:
1. First perform check (as in Check Mode) -- display versions and latest
2. Call `Start-DLSSUpgrade` to perform the upgrade
3. If Staging > Release:
   - Create timestamped backup of Release folder (`.dlss-backup-<timestamp>`)
   - Copy DLL files from Staging to Release using `Copy-Item`
   - Copy config file from Staging to Release using `Copy-Item`
   - Display per-file success messages
   - Display "Upgrade complete!" message
4. If Staging <= Release:
   - Display "Release is already up to date" message
5. If no Staging available:
   - Display "No staging versions available for upgrade" message
6. If upgrade fails mid-operation:
   - Attempt automatic restore from backup
   - If restore succeeds: display "Rolled back to previous version" message
   - If restore fails: display backup path for manual restore
   - Exit with code 1

---

## Input Parameters

### `-Upgrade` [switch]

| Property | Value |
|----------|-------|
| Type | `[switch]` |
| Required | No |
| Default | `$false` (check mode) |

**Description**: When specified, performs upgrade from Staging to Release. Without this flag, tool operates in read-only check mode. This flag is the sole safety gate per Constitution Principle II (Safe by Default).

---

## Output Format

### Table Output (Check Mode)

```
=== DLSS Version Checker ===

Location BuildID    DLSS        FrameGen
-------- --------    ----        --------
Release  310.6.0.0  310.6.0.0   310.6.0.0
Staging  310.7.0.0  310.7.0.0   310.7.0.0

Latest available: DLSS 310.7.0.0 (Frame Gen 310.7.0.0) in Staging build 310.7.0.0
```

Columns: Location, BuildID, DLSS, FrameGen (as defined in data-model.md DLSSVersion entity).

### No Versions Found

```
=== DLSS Version Checker ===

No DLSS versions found.

Latest available: none
```

### Upgrade Output (Success)

```
=== DLSS Version Checker ===

Location BuildID    DLSS        FrameGen
-------- --------    ----        --------
Release  310.6.0.0  310.6.0.0   310.6.0.0
Staging  310.7.0.0  310.7.0.0   310.7.0.0

Latest available: DLSS 310.7.0.0 (Frame Gen 310.7.0.0) in Staging build 310.7.0.0

Upgrading to DLSS 310.7.0.0 from Staging build 310.7.0.0...
 Backup created: .dlss-backup-20260420-143052
 Updated: nvngx_dlss.dll
 Updated: nvngx_dlssg.dll
 Updated config from staging

Upgrade complete!
```

### Upgrade Output (Rolled Back)

```
Error: Failed to copy nvngx_dlssg.dll.
Rolled back to previous version from backup.
```

---

## Exit Codes

| Code | Meaning |
|------|---------|
| `0` | Success (versions displayed, or upgrade completed, or no action needed) |
| `1` | Error (access denied, backup failed, upgrade failed and rollback also failed) |

Note: "No DLSS versions found" and "Release already up to date" return exit code 0 -- these are informational, not errors.

---

## Display Conventions

- **Header**: `=== DLSS Version Checker ===` (Cyan, Write-Host -ForegroundColor Cyan)
- **Table**: Default `Format-Table -AutoSize` output (no custom formatting required)
- **Latest line**: Yellow color
- **Success messages**: Green color (per-file "Updated:" lines, "Upgrade complete!")
- **Warning messages**: Yellow color ("No staging versions available", "Release already up to date")
- **Error messages**: Red color (access denied, backup failure, upgrade failure)
- **Backup path**: Displayed in upgrade output so user knows where the backup is

---

## Edge Cases

### No DLSS Installed

Output: `No DLSS versions found.`
Exit code: `0` (not an error, just nothing to display)

### No Staging Available for Upgrade

Output: `No staging versions available for upgrade.`
Exit code: `0`

### Release Already Up to Date

Output: `Release is already up to date (310.7.0.0 >= 310.7.0.0).`
Exit code: `0`

### Access Denied (Read)

Output: `Error: Access denied reading NVIDIA NGX folders.`
Exit code: `1`

### Access Denied (Write during upgrade)

Output:
```
Error: Access denied. Run as Administrator to upgrade.
Backup created at <path>. Manual restore required.
```
Exit code: `1`

### Backup Failed

Output: `Error: Failed to create backup. Upgrade aborted.`
Exit code: `1`

### Upgrade Failed, Rollback Succeeded

Output:
```
Error: Failed to copy nvngx_dlssg.dll.
Rolled back to previous version from backup.
```
Exit code: `1`

### Upgrade Failed, Rollback Also Failed

Output:
```
Error: Failed to copy nvngx_dlssg.dll.
Error: Rollback failed. Backup available at <path> for manual restore.
```
Exit code: `1`

---

## Script-to-Module Integration

The standalone script (`check-dlss-versions.ps1`) imports the module and delegates all logic:

```powershell
# Simplified flow of check-dlss-versions.ps1
param([switch]$Upgrade)

Import-Module (Join-Path $PSScriptRoot "src\DLSSVersion.psm1")

$versions = Get-DLSSVersions
$versions | Format-Table -AutoSize

$latest = Get-DLSSLatestVersion
# Display latest summary

if ($Upgrade) {
    Start-DLSSUpgrade
}
```

This ensures the script is a thin wrapper (~20 LOC) and all business logic lives in the module.

---

## File Paths (Reference)

| Purpose | Path |
|--------|------|
| Release base | `C:\ProgramData\NVIDIA\NGX\models\dlss_override\versions\` |
| Staging base | `C:\ProgramData\NVIDIA\NGX\Staging\models\dlss_override\versions\` |
| Config file | `nvngx_package_config.txt` (in version subfolder) |
| DLL files | `nvngx_dlss.dll`, `nvngx_dlssg.dll` (in version subfolder) |
| Backup format | `.dlss-backup-<yyyyMMdd-HHmmss>` (in Release versions parent) |