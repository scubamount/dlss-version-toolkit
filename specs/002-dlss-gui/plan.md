# Implementation Plan: DLSS Version Toolkit WPF GUI

**Feature Branch**: `002-dlss-gui` | **Date**: 2026-05-08 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification: WPF GUI as single .exe with C# core logic, .NET 9, framework-dependent, advanced features (tray, auto-scan, custom paths, export).

---

## Summary

A WPF desktop application for inspecting and upgrading NVIDIA DLSS override versions. The app scans NGX Release, NGX Staging, AnWave/dlssglom, and Streamline SDK sources, displays version comparison, and enables one-click upgrade/sync. Runs in the system tray, performs periodic background scans, and shows notifications for new versions. Core logic implemented in C# (no PowerShell dependency). Distributed as a single .exe via .NET 9 framework-dependent deployment.

---

## Technical Context

**Language/Version**: C# / .NET 9
**Primary Dependencies**:
- `CommunityToolkit.Mvvm` (NuGet) — MVVM helpers, source-generated INotifyPropertyChanged and ICommand
- `Hardcodet.NotifyIcon.Wpf` (NuGet) — System tray integration
- `Microsoft.Extensions.Logging` (built-in) — Logging abstraction
- `System.Text.Json` (built-in) — Settings serialization

**Storage**: Local file system (NVIDIA NGX folders at C:\ProgramData\NVIDIA\NGX), App data at %APPDATA%\DLSSVersionToolkit\
**Testing**: xUnit for unit tests, integration tests with mock NGX folder structures
**Target Platform**: Windows 10 version 1908+ / Windows 11 x64
**Project Type**: WPF application + Core class library (MVVM, 2-project solution)
**Performance Goals**: Version scan in under 5 seconds, app startup in under 3 seconds

---

## Constitution Check

| Principle | Status | Justification |
|-----------|--------|---------------|
| **Single-File Executable** | PASS | `dotnet publish -r win-x64 --self-contained false -p:PublishSingleFile=true` produces one .exe |
| **Safe by Default** | PASS | Backup before any write, auto-restore on failure, confirm dialogs for destructive actions |
| **Windows-Native UX** | PASS | WPF with standard window chrome, Hardcodet.NotifyIcon.Wpf for tray, native file dialogs |
| **Self-Contained Core Logic** | PASS | All DLSS scanning/upgrading in C# Core library, no PowerShell for core functionality |
| **Single-Instance, Background-Aware** | PASS | Named mutex for single-instance, Timers.Timer for background scans, tray icon + notifications |

---

## Project Structure

```
src/
├── DLSSVersionToolkit.Core/                   # Core logic (no WPF dependency)
│   ├── DLSSVersionToolkit.Core.csproj
│   ├── Models/
│   │   ├── DLSSVersionEntry.cs
│   │   ├── ScanResult.cs
│   │   ├── UpgradeOperation.cs
│   │   ├── AppSettings.cs
│   │   └── Recommendation.cs
│   ├── Services/
│   │   ├── INgxScanner.cs
│   │   ├── NgxScanner.cs
│   │   ├── IGlobalScanner.cs
│   │   ├── GlobalScanner.cs
│   │   ├── IStreamlineScanner.cs
│   │   ├── StreamlineScanner.cs
│   │   ├── IVersionComparer.cs
│   │   ├── VersionComparer.cs
│   │   ├── IUpgradeService.cs
│   │   ├── UpgradeService.cs
│   │   ├── IBackupService.cs
│   │   ├── BackupService.cs
│   │   ├── ISettingsService.cs
│   │   ├── SettingsService.cs
│   │   ├── IConfigParser.cs
│   │   └── NgxConfigParser.cs
│   └── DLSSVersionToolkit.Core.csproj
├── DLSSVersionToolkit/                        # WPF application
│   ├── DLSSVersionToolkit.csproj
│   ├── App.xaml / App.xaml.cs
│   ├── MainWindow.xaml / MainWindow.xaml.cs
│   ├── ViewModels/
│   │   ├── MainViewModel.cs
│   │   └── SettingsViewModel.cs
│   ├── Views/
│   │   ├── DashboardView.xaml (merged into MainWindow)
│   │   ├── SettingsDialog.xaml
│   │   └── ConfirmDialog.xaml
│   ├── Services/
│   │   ├── TrayIconService.cs
│   │   ├── NotificationService.cs
│   │   └── ScanSchedulerService.cs
│   ├── Converters/
│   │   └── VersionToColorConverter.cs
│   └── Assets/
│       └── icon.ico
└── DLSSVersionToolkit.sln
```

**Structure Decision**: Two-project separation (Core + WPF) ensures core logic is unit-testable without launching WPF. Core library has no WPF dependency.

---

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|---------------------------------------|
| Two-project solution | Required for testability — WPF app testability requires UI thread. Core logic must be testable without WPF. | Single project: Core logic tightly coupled to WPF, cannot run unit tests without UI thread. |
| Hardcodet.NotifyIcon.Wpf NuGet | WPF has no native tray icon support. Need WinForms NotifyIcon wrapped for WPF. | Direct WinForms NotifyIcon requires System.Windows.Forms reference and manual message handling. Hardcodet is the standard WPF wrapper. |
| CommunityToolkit.Mvvm NuGet | Reduces boilerplate for INotifyPropertyChanged and ICommand. Clean source generation. | Manual INotifyPropertyChanged is verbose (5+ lines per property). Toolkit is lightweight and well-maintained. |

---

## Implementation Phases

### Phase 1: Project Setup (Foundation)

- [ ] T001 Create solution structure: `DLSSVersionToolkit.sln`, `DLSSVersionToolkit.Core` class library, `DLSSVersionToolkit` WPF app
- [ ] T002 Add NuGet dependencies: `CommunityToolkit.Mvvm` to WPF project, `Hardcodet.NotifyIcon.Wpf` to WPF project
- [ ] T003 Configure WPF project for single-file publish: `DLSSVersionToolkit.csproj` with publish settings
- [ ] T004 Add placeholder icon (simple .ico file) in Assets
- [ ] T005 Set up logging: `FileLogger` implementation, log file per day in `%APPDATA%\DLSSVersionToolkit\logs\`
- [ ] T006 Set up settings persistence: `AppSettings.cs` model, `SettingsService.cs` with JSON read/write to `%APPDATA%\DLSSVersionToolkit\settings.json`
- [ ] T007 Implement single-instance enforcement: `NamedMutex` on startup in App.xaml.cs
- [ ] T008 Configure DI container in App.xaml.cs for service injection

### Phase 2: Core Library — Scanning (User Story 1, 4)

- [ ] T009 [P] Define `DLSSVersionEntry`, `ScanResult`, `Recommendation` models in `DLSSVersionToolkit.Core/Models/`
- [ ] T010 [P] Implement `NgxConfigParser`: parse `nvngx_package_config.txt` with regex, handle encoding, reparse points, corrupt files
- [ ] T011 [P] Implement `NgxScanner`: scan NGX Release and Staging folders, enumerate version subfolders, call config parser, return DLSSVersionEntry list
- [ ] T012 [P] Implement `GlobalScanner`: scan AnWave folder, read DLL versions from `FileVersionInfo`, handle missing DLLs and reparse points
- [ ] T013 [P] Implement `StreamlineScanner`: scan Streamline SDK folder, read DLL versions from `FileVersionInfo`, handle auto-detection in Downloads
- [ ] T014 [P] Implement `VersionComparer`: compare versions across sources, find newest per component, generate Recommendations
- [ ] T015 Integrate all scanners into a unified `IScanService` / `ScanService` that calls all sources and returns `ScanResult`

### Phase 3: Core Library — Upgrade/Sync (User Story 2, 8)

- [ ] T016 [P] Implement `BackupService`: create timestamped backup to `.dlss-backup-<timestamp>`, verify file count, restore on failure
- [ ] T017 [P] Implement `UpgradeService`: upgrade from Staging to NGX Release, sync from Streamline SDK or AnWave to NGX Release, backup before writes, auto-restore on failure, return `UpgradeOperation`
- [ ] T018 Write unit tests for all Core services using xUnit with mock filesystems

### Phase 4: WPF UI — Main Window (User Story 1, 7)

- [ ] T019 Define dark theme styles in App.xaml: button styles, DataGrid styles, color resources (#76B900 green, #1E1E1E bg, #2D2D2D surface)
- [ ] T020 Build MainWindow layout: toolbar with [Scan Now], [Upgrade Release], [Sync from...], [Export], [Settings] buttons
- [ ] T021 [P] Implement `MainViewModel`: expose `ObservableCollection<DLSSVersionEntry> Versions`, `ObservableCollection<Recommendation> Recommendations`, commands for Scan, Upgrade, Sync, Export, Settings
- [ ] T022 [P] Build version comparison DataGrid in MainWindow with source icons, sortable columns, newest-highlighted cells
- [ ] T023 Build recommendation bar at bottom of dashboard
- [ ] T024 Build status bar with last scan time, next scan countdown, status indicator

### Phase 5: WPF UI — Settings Dialog (User Story 5)

- [ ] T025 [P] Implement `SettingsViewModel`: bind to `AppSettings`, expose Browse commands, validation
- [ ] T026 [P] Build SettingsDialog.xaml: custom path fields with Browse buttons, checkboxes for auto-scan/minimize-to-tray/start-minimized
- [ ] T027 Wire Settings dialog from MainViewModel's SettingsCommand, save settings via SettingsService

### Phase 6: WPF UI — System Tray (User Story 3, 4)

- [ ] T028 [P] Implement `TrayIconService`: wrap Hardcodet.NotifyIcon.Wpf, set up icon, tooltip, context menu
- [ ] T029 [P] Build tray context menu: Show Dashboard, Check Now, separator, Exit
- [ ] T030 Implement `ScanSchedulerService`: Timers.Timer for 4-hour periodic scan, startup scan, scan on "Check Now" from tray
- [ ] T031 Wire window close to minimize-to-tray (hide window, show tray icon)
- [ ] T032 Wire window restore on tray icon click (left-click or "Show Dashboard")
- [ ] T033 Implement notification on new version detection: `NotificationService` using Hardcodet balloon tip

### Phase 7: Export (User Story 6)

- [ ] T034 Implement `ExportService`: write CSV and JSON export files from `ScanResult`
- [ ] T035 Wire Export button in MainWindow to open save file dialog and export

### Phase 8: Polish & Testing

- [ ] T036 Add unit tests for all Core services (xUnit)
- [ ] T037 Integration test: create mock NGX folder structure, verify scan, upgrade, sync
- [ ] T038 Test single-instance enforcement
- [ ] T039 Test system tray minimize/restore/notification
- [ ] T040 Test settings persistence across app restarts
- [ ] T041 Build final single-file .exe and verify it runs standalone

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — can start immediately
- **Phase 2 (Core Scanning)**: Depends on Phase 1 completion — all scanners depend on project structure
- **Phase 3 (Core Upgrade)**: Depends on Phase 2 completion — upgrade service uses scanners and backup service
- **Phase 4 (WPF Main Window)**: Depends on Phase 2 + Phase 3 — needs ViewModels backed by real services
- **Phase 5 (WPF Settings)**: Depends on Phase 4 — uses same ViewModel pattern
- **Phase 6 (WPF Tray)**: Depends on Phase 4 — tray interacts with main window
- **Phase 7 (Export)**: Depends on Phase 2 — export uses ScanResult model
- **Phase 8 (Polish)**: Depends on all previous phases

### User Story Dependencies

- **User Story 1 (View)**: Requires Phase 2 (scanning) + Phase 4 (UI)
- **User Story 2 (Upgrade)**: Requires Phase 3 (upgrade service) + Phase 4 (UI)
- **User Story 3 (Tray)**: Requires Phase 6 (tray service)
- **User Story 4 (Auto-scan)**: Requires Phase 6 (scheduler) + Phase 2 (scanning)
- **User Story 5 (Custom Paths)**: Requires Phase 5 (settings UI) + Phase 2 (scanning with custom paths)
- **User Story 6 (Export)**: Requires Phase 7 (export service)
- **User Story 7 (Comparison)**: Requires Phase 2 (version comparer) + Phase 4 (UI table)
- **User Story 8 (Sync)**: Requires Phase 3 (upgrade service) + Phase 4 (UI)

---

## MVP Scope

- MVP = Phase 1 + Phase 2 + Phase 4 (US1 only) — a working WPF dashboard that shows DLSS versions
- MVP does NOT include: upgrade/sync, tray, settings, export, periodic scanning
- MVP is testable by: launching app, seeing version table, clicking Scan Now

---

## Build & Publish Commands

```bash
# Development build
dotnet build DLSSVersionToolkit.sln

# Run tests
dotnet test

# Publish single-file exe (framework-dependent)
dotnet publish src/DLSSVersionToolkit/DLSSVersionToolkit.csproj `
    -r win-x64 `
    --self-contained false `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=false `
    -o ./publish
```