# Phase 1 Data Model: DLSS Version Toolkit

**Branch**: `001-dlss-version-checker` | **Date**: 2026-04-20
**Input**: `/speckit.plan` command - Data model documentation for Phase 1.

---

## Entity 1: DLSSVersion

Represents an installed DLSS version from NVIDIA NGX folder structure.

```powershell
[PSCustomObject]@{
    Location = [string]'Release' | 'Staging'        # NGX location type
    BuildID = [string]                          # Folder name (e.g., "310.6.0.0")
    DLSS = [string]'310.6.0.0'               # DLSS component version
    FrameGen = [string]'310.6.0.0'          # Frame Gen component version
}
```

### Properties

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `Location` | string | Yes | One of: `Release`, `Staging`. Identifies NGX folder source. |
| `BuildID` | string | Yes | Folder name from version directory. Format: `310.6.X.X` semantic version. |
| `DLSS` | string | Yes | DLSS core component version from config. Parsed from line like `dlss, 310.6.0.0`. |
| `FrameGen` | string | Yes | DLSS Frame Gen component version. Parsed from line like `dlssg, 310.6.0.0`. |

### Validation Rules

- `Location`: Must be exact match to `Release` or `Staging` (case-sensitive)
- `BuildID`: Non-empty string, max 50 characters
- `DLSS`: Must match pattern `^\d+\.\d+\.\d+\.\d+$` (semantic version)
- `FrameGen`: Must match pattern `^\d+\.\d+\.\d+\.\d+$` or be `Unknown` if component not in config
- `Unknown` is valid for components not present in config (e.g., DeepDVC may not be in all configs)

### State Transitions

N/A — DLSSVersion is a data transfer object (DTO), immutable during scanning.

---

## Entity 2: DLSSComponent

Represents individual DLSS-related components from NVIDIA NGX configuration file.

```powershell
[PSCustomObject]@{
    Name = [string]            # Component name: 'dlss', 'dlssg', 'dlssd', 'deepdvc'
    Version = [string]         # Component version from config
}
```

### Properties

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `Name` | string | Yes | Component identifier. Valid: `dlss`, `dlssg`, `dlssd`, `deepdvc`. |
| `Version` | string | Yes | Version string from config file. May be `Unknown` if not in config. |

### Known Components

| Name | Config Key | Description |
|------|-----------|-------------|
| `dlss` | `dlss` | DLSS core upscaling |
| `dlssg` | `dlssg` | DLSS Frame Generation |
| `dlssd` | `dlssd` | DLSS Deep Learning (HDR) |
| `deepdvc` | `deepdvc` | DeepDVC (when available) |

### Validation Rules

- `Name`: Must be one of known component names (case-sensitive exact match)
- `Version`: Non-empty string; `Unknown` is valid when config lacks component entry

### State Transitions

N/A — DLSSComponent is parsed from config file, not a stateful entity.

---

## Entity 3: UpgradeOperation

Represents an upgrade action from Staging to Release.

```powershell
[PSCustomObject]@{
    SourceVersion = [DLSSVersion]           # Staging version being upgraded from
    TargetVersion = [DLSSVersion]          # Release version being upgraded to
    Status = [string]                       # Operation status: 'Pending', 'InProgress', 'Completed', 'Failed', 'RolledBack'
    BackupPath = [string]                    # Path to backup (if created)
    ErrorMessage = [string]                # Error details (if status is 'Failed')
}
```

### Properties

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `SourceVersion` | DLSSVersion | Yes | The staging version being copied to Release. |
| `TargetVersion` | DLSSVersion | Yes | The current Release version being replaced. |
| `Status` | string | Yes | Operation state. Valid values below. |
| `BackupPath` | string | No | Path to backup folder (set after backup creation). |
| `ErrorMessage` | string | No | Error details when Status is `Failed`. |

### Status Values

| Status | Description |
|--------|-------------|
| `Pending` | Upgrade requested, not yet started. |
| `InProgress` | Backup created, files being copied. |
| `Completed` | All files copied successfully. |
| `Failed` | Operation failed, backup available for manual restore. |
| `RolledBack` | Operation failed, backup restored automatically. |

### State Transitions

```
[Pending] --> [InProgress] --> [Completed]
                              |
                              +-> [Failed]
                              |         |
                              |         +-> [RolledBack]
                              |         (auto-restore on failure)
                              |
                              +-> [Failed]
                                  (manual restore only)
```

### Validation Rules

- `SourceVersion`: Must be valid DLSSVersion with Location = `Staging`
- `TargetVersion`: Must be valid DLSSVersion with Location = `Release`
- `Status`: Must be exact match to valid status value (case-sensitive)
- `BackupPath`: Must be valid Windows path when Status != `Pending`
- `ErrorMessage`: Required when Status = `Failed`, otherwise unused

---

## Configuration File Format

NVIDIA NGX uses `nvngx_package_config.txt` with line-delimited component versions:

```
dlss, 310.6.0.0
dlssg, 310.6.0.0
dlssd, 310.6.0.0
deepdvc, 310.6.0.0
```

### Parse Rules

1. Read file with `-Raw` flag to get single string
2. Split by newline `` `n `` to get lines
3. For each line matching pattern `^(\w+),\s*([\d.]+)$`:
   - Group 1 = component name
   - Group 2 = component version
4. If component not found in file, set version to `Unknown`

---

## NGX Folder Structure

NVIDIA NGX uses hierarchical folder structure:

```
C:\ProgramData\NVIDIA\NGX\
├── models\dlss_override\versions\
│   └── {BuildID}\
│       └── (DLSS files)
├── Staging\models\dlss_override\versions\
│   └── {BuildID}\
│       └── (Staging DLSS files)
```

### Paths (Reference)

| Location | Base Path | Notes |
|----------|----------|-------|
| Release | `C:\ProgramData\NVIDIA\NGX\models\dlss_override\versions\` | User-accessible DLSS |
| Staging | `C:\ProgramData\NVIDIA\NGX\Staging\models\dlss_override\versions\` | NVIDIA driver staging |

### Scan Rules

1. Check if base path exists (`Test-Path`)
2. Enumerate version folders (`Get-ChildItem -Directory`)
3. For each version folder, find config file: `nvngx_package_config.txt`
4. Parse config to get component versions

---

## Version Comparison

Versions compare using .NET `[version]` type for semantic sorting.

```powershell
# Example comparison
$ver1 = [version]"310.6.0.0"
$ver2 = [version]"310.7.0.0"
$ver1 -lt $ver2  # True
```

### Sorting Rules

1. Parse version strings to `[version]` objects
2. Sort descending: newest first
3. Default version for parsing failures: `[version]"0.0.0.0"` (sorts to bottom)