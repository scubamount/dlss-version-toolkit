# Data Model: DLSS Version Toolkit

**Branch**: `001-dlss-version-checker` | **Date**: 2026-04-20 | **Phase**: 1

---

## Entity 1: DLSSVersion

Represents an installed DLSS version from NVIDIA NGX folder structure.

```powershell
[PSCustomObject]@{
    Location = [string]'Release' | 'Staging' | 'Global' # NGX location type
    BuildID = [string] # Folder name (e.g., "310.6.0.0")
    DLSS = [string]'310.6.0.0' # DLSS component version
    FrameGen = [string]'310.6.0.0' # Frame Gen component version
    StreamlineSDK = [string]'2.11.1.0' # Streamline SDK (sl.*) suite version
}
```

### Properties

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `Location` | string | Yes | One of: `Release`, `Staging`, `Global`. Identifies NGX folder source. |
| `BuildID` | string | Yes | Folder name from version directory. Format: `310.6.X.X` semantic version. |
| `DLSS` | string | Yes | DLSS core component version from config. Parsed from line like `dlss, 310.6.0.0`. |
| `FrameGen` | string | Yes | DLSS Frame Gen component version. Parsed from line like `dlssg, 310.6.0.0`. |
| `StreamlineSDK` | string | No | Streamline SDK (sl.*) suite version. Format: `2.11.X.X`. `Unknown` if not applicable (Release/Staging). |

### Validation Rules

- `Location`: Must be exact match to `Release`, `Staging`, or `Global` (case-sensitive)
- `BuildID`: Non-empty string, max 50 characters
- `DLSS`: Must match pattern `^\d+\.\d+\.\d+\.\d+$` (semantic version)
- `FrameGen`: Must match pattern `^\d+\.\d+\.\d+\.\d+$` or be `Unknown` if component not in config
- `StreamlineSDK`: Must match pattern `^\d+\.\d+\.\d+\.\d+$` or be `Unknown` if not applicable (Release/Staging locations)
- `Unknown` is valid for components not present in config (e.g., DeepDVC may not be in all configs)

### State Transitions

N/A -- DLSSVersion is a data transfer object (DTO), immutable after creation by scan.

---

## Entity 2: DLSSComponent

Represents individual DLSS-related components from NVIDIA NGX configuration file.

```powershell
[PSCustomObject]@{
    Name    = [string]  # Component name: 'dlss', 'dlssg', 'dlssd', 'deepdvc'
    Version = [string]  # Component version from config
}
```

### Properties

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `Name` | string | Yes | Component identifier. Valid: `dlss`, `dlssg`, `dlssd`, `deepdvc`. |
| `Version` | string | Yes | Version string from config file. May be `Unknown` if not in config. |

### Known Components

| Name | Config Key | DLL File | Description |
|------|-----------|----------|-------------|
| `dlss` | `dlss` | `nvngx_dlss.dll` | DLSS core upscaling |
| `dlssg` | `dlssg` | `nvngx_dlssg.dll` | DLSS Frame Generation |
| `dlssd` | `dlssd` | `nvngx_dlssd.dll` | DLSS Deep Learning (HDR) |
| `deepdvc` | `deepdvc` | `nvngx_deepdvc.dll` | DeepDVC (when available) |

### Validation Rules

- `Name`: Must be one of known component names (case-sensitive exact match)
- `Version`: Non-empty string; `Unknown` is valid when config lacks component entry

### State Transitions

N/A -- DLSSComponent is parsed from config file, not a stateful entity.

---

## Entity 3: UpgradeOperation

Represents an upgrade action from Staging to Release.

```powershell
[PSCustomObject]@{
    SourceVersion = [DLSSVersion]  # Staging version being upgraded from
    TargetVersion = [DLSSVersion]  # Release version being upgraded to
    Status        = [string]       # Operation status
    BackupPath    = [string]       # Path to backup (if created)
    ErrorMessage  = [string]       # Error details (if status is 'Failed')
}
```

### Properties

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `SourceVersion` | DLSSVersion | Yes | The staging version being copied to Release. |
| `TargetVersion` | DLSSVersion | Yes | The current Release version being replaced. |
| `Status` | string | Yes | Operation state. Valid values below. |
| `BackupPath` | string | No | Path to backup folder (set after backup creation). Format: `.dlss-backup-<yyyyMMdd-HHmmss>` |
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
     +-> [Failed] (backup exists, manual restore possible)
     |       |
     |       +-> [RolledBack] (auto-restore succeeded)
     |
     +-> [Failed] (no backup, manual intervention required)
```

Transition rules:
- `Pending` -> `InProgress`: Backup directory created and verified (file count matches source).
- `InProgress` -> `Completed`: All DLL and config files copied successfully from Staging to Release.
- `InProgress` -> `Failed`: Any copy operation threw an error. Backup exists for restore attempt.
- `Failed` -> `RolledBack`: Automatic restore from backup succeeded. All original files restored.
- `Failed` (terminal): Automatic restore also failed, or backup was never created. User must manually intervene.

### Validation Rules

- `SourceVersion`: Must be valid DLSSVersion with Location = `Staging`
- `TargetVersion`: Must be valid DLSSVersion with Location = `Release`
- `Status`: Must be exact match to valid status value (case-sensitive)
- `BackupPath`: Must be valid Windows path when Status is `InProgress`, `Completed`, `Failed`, or `RolledBack`
- `ErrorMessage`: Required when Status = `Failed`, otherwise unused

---

## Entity 4: StreamlineComponent

Represents individual Streamline (sl.*) DLL components from AnWave/dlssglom injection.

```powershell
[PSCustomObject]@{
    Name     = [string]   # Component name: 'sl.common', 'sl.dlss', 'sl.dlss_d', 'sl.dlss_g', etc.
    Version = [string]   # Component version from DLL file metadata (e.g., '2.11.1.0')
    DllFile = [string]   # DLL filename (e.g., 'sl.common.dll')
    FileSize = [long]    # File size in bytes
}
```

### Properties

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `Name` | string | Yes | Component identifier. Valid names below. |
| `Version` | string | Yes | Version from DLL file metadata. Format: `2.11.X.X`. |
| `DllFile` | string | Yes | DLL filename. |
| `FileSize` | long | Yes | File size in bytes from filesystem. |

### Known Components

| Name | DLL File | Description |
|------|----------|-------------|
| `sl.common` | `sl.common.dll` | Streamline common library |
| `sl.dlss` | `sl.dlss.dll` | Streamline DLSS plugin |
| `sl.dlss_d` | `sl.dlss_d.dll` | Streamline DLSS Ray Reconstruction plugin |
| `sl.dlss_g` | `sl.dlss_g.dll` | Streamline DLSS Frame Generation plugin |
| `sl.deepdvc` | `sl.deepdvc.dll` | Streamline DeepDVC plugin |
| `sl.directsr` | `sl.directsr.dll` | Streamline DirectSR plugin |
| `sl.imgui` | `sl.imgui.dll` | Streamline ImGui debug plugin |
| `sl.interposer` | `sl.interposer.dll` | Streamline interposer module |
| `sl.nis` | `sl.nis.dll` | Streamline NIS plugin |
| `sl.nvperf` | `sl.nvperf.dll` | Streamline NVPerf plugin |
| `sl.pcl` | `sl.pcl.dll` | Streamline PCL plugin |
| `sl.reflex` | `sl.reflex.dll` | Streamline Reflex plugin |

### Validation Rules

- `Name`: Must be one of known component names (case-sensitive exact match)
- `Version`: Must match pattern `^\d+\.\d+\.\d+\.\d+$` (semantic version)
- `DllFile`: Must match pattern `^sl\..+\.dll$` (case-sensitive)
- `FileSize`: Must be greater than 0

### State Transitions

N/A -- StreamlineComponent is parsed from DLL metadata, not a stateful entity.

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

1. Read file with `Get-Content -Path $file -Raw` to get single string (preserves encoding, avoids line-split issues)
2. Match component versions using regex: `dlss,\s+([\d.]+)` for each known component
3. If component not found in file, set version to `Unknown`
4. Never use `Set-Content` to write config files during upgrade -- use `Copy-Item` to preserve original encoding

### File Encoding

- Source encoding: UTF-8 (NVIDIA standard)
- Read with: `Get-Content -Raw` (handles UTF-8 correctly in both PS5.1 and PS7)
- Copy with: `Copy-Item` (preserves byte-for-byte encoding, avoids PS5.1 UTF-16LE default on Set-Content)

---

## NGX Folder Structure

NVIDIA NGX uses hierarchical folder structure:

```
C:\ProgramData\NVIDIA\NGX\
├── models\dlss_override\versions\
│   └── {BuildID}\
│       └── (subfolder)\
│           ├── nvngx_dlss.dll
│           ├── nvngx_dlssg.dll
│           └── nvngx_package_config.txt
├── Staging\models\dlss_override\versions\
│   └── {BuildID}\
│       └── (subfolder)\
│           ├── nvngx_dlss.dll
│           ├── nvngx_dlssg.dll
│           └── nvngx_package_config.txt
```

### Paths (Reference)

| Location | Base Path | Notes |
|----------|----------|-------|
| Release | `C:\ProgramData\NVIDIA\NGX\models\dlss_override\versions\` | User-accessible DLSS |
| Staging | `C:\ProgramData\NVIDIA\NGX\Staging\models\dlss_override\versions\` | NVIDIA driver staging |
| Global | (AnWave install directory) | DLL injection override via AnWave/dlssglom |

### Path Override for Testing

All functions that accept NGX paths support a `-Path` parameter with a default of the real NGX base path. This enables test isolation via Pester's `$TestDrive`:

```powershell
# Production usage (default path)
Get-DLSSVersions

# Test usage (fixture path)
Get-DLSSVersions -Path "$TestDrive\NGX"
```

### Scan Rules

1. Check if base path exists (`Test-Path`)
2. Enumerate version folders (`Get-ChildItem -Directory`)
3. For each version folder, find config file recursively: `nvngx_package_config.txt`
4. Parse config to get component versions using regex matching
5. Return array of DLSSVersion objects

---

## Global Override Path (AnWave / dlssglom)

AnWave (dlssglom) provides global DLSS/Streamline DLL overrides by injecting DLLs from its own folder.
Unlike NGX Release/Staging which use nvngx_package_config.txt, Global overrides store DLLs directly
in the AnWave installation directory. Version info is read from DLL file metadata (FileVersion).

### Path Configuration

| Location | Base Path | Notes |
|----------|----------|-------|
| Global | (configurable, default: AnWave install directory) | DLL injection from own folder |

Default Global path: The directory containing `nvidiaDlssGlom.exe`

### Scan Rules (Global)

1. Check if Global path exists (Test-Path)
2. Enumerate DLL files matching known component names (`nvngx_*.dll`, `sl.*.dll`)
3. Extract version from DLL file metadata using `[System.Diagnostics.FileVersionInfo]::GetVersionInfo()`
4. Return single DLSSVersion object with Location = "Global" and component versions from DLL metadata

---

## Backup Path Format

During upgrade, backups are stored within the same parent directory:

```
C:\ProgramData\NVIDIA\NGX\models\dlss_override\versions\.dlss-backup-20260420-143052\
```

Format: `.dlss-backup-<yyyyMMdd-HHmmss>`

- Dot prefix: sorts separately from version build folders in directory listings
- Timestamp: precise to the second, prevents collisions on repeated upgrades
- Location: same parent as Release versions, same ACL context (no permission issues)
- Retention: backups are never auto-deleted; users can manually clean up old backups

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
4. Use `try { [version]$str } catch { [version]"0.0.0.0" }` for safe parsing (PS5.1 compatible)