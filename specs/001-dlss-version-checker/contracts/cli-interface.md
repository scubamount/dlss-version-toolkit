# Contract: CLI Interface

**Branch**: `001-dlss-version-checker` | **Date**: 2026-04-20
**Input**: `/speckit.plan` command - CLI interface specification for Phase 1.

---

## Interface Overview

The CLI interface is the standalone script entry point: `check-dlss-versions.ps1`.

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
1. Scan Release folder: `C:\ProgramData\NVIDIA\NGX\models\dlss_override\versions\`
2. Scan Staging folder: `C:\ProgramData\NVIDIA\NGX\Staging\models\dlss_override\versions\`
3. Parse `nvngx_package_config.txt` from each version folder
4. Display combined results in table format
5. Show latest available version

### Upgrade Mode

**Usage**:
```
powershell -ExecutionPolicy Bypass -File check-dlss-versions.ps1 -Upgrade
```

**Behavior**:
1. First perform check (as in Check Mode)
2. Find latest Staging version
3. Find current Release version
4. If Staging > Release:
   - Create timestamped backup of Release folder
   - Copy DLL files from Staging to Release
   - Copy config file from Staging to Release
   - Display success message
5. If Staging <= Release:
   - Display "Release is already up to date" message

---

## Input Parameters

### `-Upgrade` [switch]

| Property | Value |
|----------|-------|
| Type | `[switch]` |
| Alias | `-Up` |
| Required | No |
| Default | `$false` (check mode) |

**Description**: When specified, performs upgrade from Staging to Release. Without this flag, tool operates in read-only check mode.

---

## Output Format

### Table Output

```
=== DLSS Version Checker ===

Location   BuildID     DLSS       FrameGen
---------  --------   ---------  --------
Release   310.6.0.0  310.6.0.0  310.6.0.0
Staging   310.7.0.0  310.7.0.0  310.7.0.0

Latest available: DLSS 310.7.0.0 (Frame Gen 310.7.0.0) in Staging build 310.7.0.0
```

### Error Handling Output

```
=== DLSS Version Checker ===

Location   BuildID     DLSS       FrameGen
---------  --------   ---------  --------
No DLSS versions found.

Latest available: none
```

### Upgrade Output

```
=== DLSS Version Checker ===

Location   BuildID     DLSS       FrameGen
---------  --------   ---------  --------
Release   310.6.0.0  310.6.0.0  310.6.0.0
Staging   310.7.0.0  310.7.0.0  310.7.0.0

Latest available: DLSS 310.7.0.0 (Frame Gen 310.7.0.0) in Staging build 310.7.0.0

Upgrading to DLSS 310.7.0.0 from Staging build 310.7.0.0...
  Updated: nvngx_dlss.dll
  Updated: nvngx_dlssg.dll
  Updated config from staging

Upgrade complete!
```

---

## Exit Codes

| Code | Meaning |
|------|---------|
| `0` | Success (versions displayed, or upgrade completed) |
| `1` | Error (path not found, access denied, parse error) |

---

## Display Conventions

- **Header**: `=== DLSS Version Checker ===` (Cyan color)
- **Table columns**: Location (10 chars), BuildID (10), DLSS (10), FrameGen (10)
- **Latest line**: Yellow color
- **Success messages**: Green color
- **Warning messages**: Yellow color
- **Error messages**: Red color

---

## Edge Cases

### No DLSS Installed

Output:
```
No DLSS versions found.
```
Exit code: `0` (not an error, just nothing to display)

### No Staging Available for Upgrade

Output:
```
No staging versions available for upgrade.
```
Exit code: `0`

### Release Already Up to Date

Output:
```
Release is already up to date (310.7.0.0 >= 310.7.0.0).
```
Exit code: `0`

### Access Denied (Read)

Output:
```
Error: Access denied reading NVIDIA NGX folders.
```
Exit code: `1`

### Access Denied (Write during upgrade)

Output:
```
Error: Access denied. Run as Administrator to upgrade.
Error: Backup created at <path>. Manual restore required.
```
Exit code: `1`

### Backup Failed

Output:
```
Error: Failed to create backup. Upgrade aborted.
```
Exit code: `1`

---

## File Paths (Reference)

| Purpose | Path |
|--------|------|
| Release base | `C:\ProgramData\NVIDIA\NGX\models\dlss_override\versions\` |
| Staging base | `C:\ProgramData\NVIDIA\NGX\Staging\models\dlss_override\versions\` |
| Config file | `nvngx_package_config.txt` (in version folder) |
| DLL files | `nvngx_dlss.dll`, `nvngx_dlssg.dll` (in version folder) |