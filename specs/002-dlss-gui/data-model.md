# Data Model: DLSS Version Toolkit WPF GUI

**Feature Branch**: `002-dlss-gui` | **Date**: 2026-05-08

---

## Entity 1: DLSSVersionEntry

Represents a detected DLSS version from any source.

```csharp
public class DLSSVersionEntry
{
    public string Source { get; set; }           // "NGX_Release", "NGX_Staging", "AnWave", "StreamlineSDK"
    public string BuildID { get; set; }          // Folder name or version string
    public string DLSS { get; set; }             // e.g., "310.6.0.0"
    public string FrameGen { get; set; }         // e.g., "310.6.0.0"
    public string DLSSD { get; set; }            // e.g., "310.6.0.0" or "Unknown"
    public string DeepDVC { get; set; }          // e.g., "310.6.0.0" or "Unknown"
    public string Streamline { get; set; }       // e.g., "2.11.1.0" or "N/A" or "Unknown"
    public string Path { get; set; }             // Full path to version folder
    public bool IsNewestDLSS { get; set; }       // Highlight flag
    public bool IsNewestFG { get; set; }
    public bool IsNewestDLSSD { get; set; }
    public bool IsNewestDeepDVC { get; set; }
    public DateTime ScannedAt { get; set; }
}
```

---

## Entity 2: ScanResult

Represents a complete scan of all configured sources.

```csharp
public class ScanResult
{
    public List<DLSSVersionEntry> Sources { get; set; }
    public Dictionary<string, VersionInfo> NewestPerComponent { get; set; }  // "DLSS" -> {Version, Source}
    public List<Recommendation> Recommendations { get; set; }
    public DateTime ScannedAt { get; set; }
    public TimeSpan Duration { get; set; }
    public List<string> Warnings { get; set; }
    public List<string> Errors { get; set; }
}

public class VersionInfo
{
    public string Version { get; set; }
    public string Source { get; set; }
}

public class Recommendation
{
    public string Action { get; set; }          // "Update_NGX_from_Streamline", "UpToDate", etc.
    public string Description { get; set; }
    public string FromSource { get; set; }
    public string ToTarget { get; set; }
}
```

---

## Entity 3: UpgradeOperation

Represents an upgrade or sync operation with state tracking.

```csharp
public class UpgradeOperation
{
    public string OperationId { get; set; }
    public string SourceType { get; set; }      // "Staging", "StreamlineSDK", "AnWave"
    public string TargetType { get; set; }       // "NGX_Release"
    public string SourcePath { get; set; }
    public string TargetPath { get; set; }
    public OperationStatus Status { get; set; }
    public string BackupPath { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string ErrorMessage { get; set; }
    public List<string> FilesCopied { get; set; }
}

public enum OperationStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    RolledBack
}
```

---

## Entity 4: AppSettings

Persisted user configuration.

```csharp
public class AppSettings
{
    public string NgxBasePath { get; set; }         // Default: "C:\\ProgramData\\NVIDIA\\NGX"
    public string AnWavePath { get; set; }          // Empty = auto-detect
    public string StreamlinePath { get; set; }      // Empty = auto-detect
    public bool AutoScanEnabled { get; set; }        // Default: true
    public bool StartMinimized { get; set; }         // Default: false
    public bool MinimizeToTray { get; set; }         // Default: true
    public int ScanIntervalHours { get; set; }       // Default: 4
    public bool NotifyOnNewVersion { get; set; }     // Default: true
}
```

---

## NGX Folder Structure (same as Phase 1)

```
C:\ProgramData\NVIDIA\NGX\
├── models\dlss_override\versions\
│   └── {BuildID}\
│       └── (subfolder)\
│           ├── nvngx_dlss.dll
│           ├── nvngx_dlssg.dll
│           ├── nvngx_dlssd.dll
│           ├── nvngx_deepdvc.dll (optional)
│           └── nvngx_package_config.txt
├── Staging\models\dlss_override\versions\
│   └── {BuildID}\
│       └── (subfolder)\
│           ├── nvngx_dlss.dll
│           ├── nvngx_dlssg.dll
│           ├── nvngx_dlssd.dll
│           ├── nvngx_deepdvc.dll (optional)
│           └── nvngx_package_config.txt
```

---

## Config File Format (nvngx_package_config.txt)

```
dlss, 310.6.0.0
dlssg, 310.6.0.0
dlssd, 310.6.0.0
deepdvc, 310.6.0.0
```

Parse with regex per component: `dlss,\s+([\d.]+)`, `dlssg,\s+([\d.]+)`, `dlssd,\s+([\d.]+)`, `deepdvc,\s+([\d.]+)`. If component not found in file, set version to "Unknown".

---

## Auto-Detection Rules

**AnWave**: Search `%USERPROFILE%\Downloads` for folder matching `dlssglom|nvidiaDlssGlom|AnWave` with `nvidiaDlssGlom.exe` present.

**Streamline SDK**: Search `%USERPROFILE%\Downloads` for folder matching `streamline-sdk` with `bin\x64\nvngx_dlss.dll` present.

---

## Version Comparison

Use `System.Version` for semantic comparison. Normalize non-standard formats (trim extra parts, remove letters). Handle "Unknown" as lowest possible version.

---

## File Locations

| Purpose | Path |
|---------|------|
| Settings | `%APPDATA%\DLSSVersionToolkit\settings.json` |
| Logs | `%APPDATA%\DLSSVersionToolkit\logs\` |
| Backups | `C:\ProgramData\NVIDIA\NGX\models\dlss_override\versions\.dlss-backup-<yyyyMMdd-HHmmss>` |