# DLSS Version Toolkit — WPF GUI Constitution

## Core Principles

### I. Single-File Executable

The app MUST be built as a single .exe file (framework-dependent .NET 9) that requires no installer. Users download one file and run it. No separate DLLs, no registry writes, no AppData required at startup.

**Rationale**: Simplicity is the primary UX goal. An installer is friction. A single .exe that just runs is the ideal distribution model for a utility tool.

### II. Safe by Default

All mutating operations (DLL copy, config overwrite) MUST create a timestamped backup before writes. If any step fails mid-operation, the system MUST restore from backup automatically. NGX Release is the canonical location — all sync/upgrade operations target it.

**Rationale**: Users run this tool against system-level NVIDIA files. A mistake could break DLSS for all games. Safety defaults prevent accidental damage.

### III. Windows-Native UX

The app MUST use WPF with Windows-native styling. Standard window chrome (minimize/maximize/close), system tray integration via NotifyIcon, and native file dialogs. No Electron, no web stack, no cross-platform abstractions.

**Rationale**: Native Windows UI provides the best integration with the OS. WPF produces a single .exe and matches the aesthetic of Windows 10/11. Electron adds 150MB+ and a browser runtime — overkill for a version checker.

### IV. Self-Contained Core Logic

All DLSS scanning and upgrade logic MUST be implemented in C# within the app. The PowerShell module from Phase 1 is replaced by native C# code. No PowerShell dependency for core functionality.

**Rationale**: Keeping core logic in C# ensures the .exe is fully self-contained. Users do not need PowerShell to be installed for the app to work. The rewrite also gives us a chance to improve the code structure.

### V. Single-Instance, Background-Aware

The app MUST enforce single-instance (only one running copy at a time). When minimized, it MUST run in the system tray and perform periodic background scans. Notifications alert users to new versions.

**Rationale**: A version monitoring tool that requires manual launching every time is forgettable. Background operation with tray presence keeps the tool alive and useful between launches.

## Platform Constraints

- **Runtime**: .NET 9 (framework-dependent — users need .NET 9 installed)
- **OS**: Windows 10 version 1908+ or Windows 11
- **Privileges**: Standard user for reading; Admin for writing to `C:\ProgramData\`
- **NVIDIA**: Requires NVIDIA App with DLSS override enabled; reads NGX model directories
- **Optional Sources**: AnWave and Streamline SDK are optional user downloads
- **Encoding**: All config file reads use UTF-8. DLL metadata reads use `FileVersionInfo`.

## Development Workflow

- **Build**: `dotnet build` produces a single .exe via framework-dependent publish
- **Testing**: Unit tests via xUnit/NUnit. Integration tests with mock NGX folder structures.
- **Versioning**: Semantic versioning. Git tag → GitHub Release with .exe attached.
- **Distribution**: GitHub Releases (direct .exe download). Scoop and winget manifests as secondary channels.
- **PowerShell Scripts**: Deprecated. The .exe replaces `check-dlss-versions.ps1` and the PowerShell module.

## Governance

This constitution supersedes all previous specifications. Any deviation MUST be justified with:
1. The specific problem that requires the deviation
2. Why following the principle would cause user harm or tool failure
3. A plan to return to compliance

Amendments require documentation of the change, rationale, and impact analysis.

**Version**: 2.0.0 | **Ratified**: 2026-05-08 | **Last Amended**: 2026-05-08