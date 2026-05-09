# Tasks: DLSS Version Toolkit — WPF GUI

**Input**: Design documents from `/specs/002-dlss-gui/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, ui-layout.md

**Organization**: Tasks grouped by phase to enable sequential progression through implementation.

## Format: `[ID] [P?] [Phase] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Phase]**: Which implementation phase this belongs to
- Include exact file paths in descriptions

---

## Phase 1: Setup (Project Foundation)

**Purpose**: Create solution structure, configure build, set up logging and settings.

- [ ] T001 [Phase 1] Create solution `DLSSVersionToolkit.sln` at `src/DLSSVersionToolkit.sln`
- [ ] T002 [Phase 1] Create `DLSSVersionToolkit.Core` class library project at `src/DLSSVersionToolkit.Core/DLSSVersionToolkit.Core.csproj` with .NET 9 target
- [ ] T003 [Phase 1] Create `DLSSVersionToolkit` WPF project at `src/DLSSVersionToolkit/DLSSVersionToolkit.csproj` with .NET 9 target, add project reference to Core
- [ ] T004 [Phase 1] Add NuGet package `CommunityToolkit.Mvvm` to `DLSSVersionToolkit` WPF project
- [ ] T005 [Phase 1] Add NuGet package `Hardcodet.NotifyIcon.Wpf` to `DLSSVersionToolkit` WPF project
- [ ] T006 [Phase 1] Configure `DLSSVersionToolkit.csproj` for single-file publish: `OutputType=WinExe`, `<PublishSingleFile>true</PublishSingleFile>`, `<IncludeNativeLibrariesForSelfExtract>false</IncludeNativeLibrariesForSelfExtract>`, `RuntimeIdentifier=win-x64`
- [ ] T007 [Phase 1] Add placeholder `icon.ico` (simple green "D" icon) at `src/DLSSVersionToolkit/Assets/icon.ico`, set as application icon in csproj
- [ ] T008 [P] [Phase 1] Implement `FileLogger.cs` in `DLSSVersionToolkit.Core/Services/` using `Microsoft.Extensions.Logging`: write to `%APPDATA%\DLSSVersionToolkit\logs\dlss-version-toolkit-<date>.log`, keep last 7 days
- [ ] T009 [P] [Phase 1] Implement `AppSettings.cs` model in `DLSSVersionToolkit.Core/Models/`: NgxBasePath, AnWavePath, StreamlinePath, AutoScanEnabled, StartMinimized, MinimizeToTray, ScanIntervalHours, NotifyOnNewVersion
- [ ] T010 [P] [Phase 1] Implement `SettingsService.cs` in `DLSSVersionToolkit.Core/Services/`: read/write JSON to `%APPDATA%\DLSSVersionToolkit\settings.json` using `System.Text.Json`, with file locking
- [ ] T011 [Phase 1] Implement single-instance enforcement in `App.xaml.cs`: use `Mutex("Global\\DLSSVersionToolkit_SingleInstance")`, if already owned bring existing window to foreground and exit, otherwise acquire and continue
- [ ] T012 [Phase 1] Configure simple DI container in `App.xaml.cs`: register all services (NgxScanner, GlobalScanner, StreamlineScanner, VersionComparer, BackupService, UpgradeService, SettingsService, ScanService, TrayIconService, NotificationService, ScanSchedulerService)

---

## Phase 2: Core Library — Models

**Purpose**: Define all data models used across the application.

- [ ] T013 [P] [Phase 2] Implement `DLSSVersionEntry.cs` in `DLSSVersionToolkit.Core/Models/`: Source, BuildID, DLSS, FrameGen, DLSSD, DeepDVC, Streamline, Path, IsNewestDLSS, IsNewestFG, IsNewestDLSSD, IsNewestDeepDVC, ScannedAt
- [ ] T014 [P] [Phase 2] Implement `VersionInfo.cs` in `DLSSVersionToolkit.Core/Models/`: Version, Source
- [ ] T015 [P] [Phase 2] Implement `Recommendation.cs` in `DLSSVersionToolkit.Core/Models/`: Action, Description, FromSource, ToTarget
- [ ] T016 [P] [Phase 2] Implement `ScanResult.cs` in `DLSSVersionToolkit.Core/Models/`: Sources (List<DLSSVersionEntry>), NewestPerComponent (Dictionary<string, VersionInfo>), Recommendations (List<Recommendation>), ScannedAt, Duration, Warnings, Errors
- [ ] T017 [P] [Phase 2] Implement `UpgradeOperation.cs` in `DLSSVersionToolkit.Core/Models/`: OperationId, SourceType, TargetType, SourcePath, TargetPath, Status (enum: Pending/InProgress/Completed/Failed/RolledBack), BackupPath, StartedAt, CompletedAt, ErrorMessage, FilesCopied

---

## Phase 3: Core Library — Services (Scanning)

**Purpose**: Implement all scanning and comparison logic in the Core library.

- [ ] T018 [P] [Phase 3] Implement `IConfigParser.cs` interface and `NgxConfigParser.cs` in `DLSSVersionToolkit.Core/Services/`: parse `nvngx_package_config.txt` using regex patterns for dlss, dlssg, dlssd, deepdvc. Handle: UTF-8 encoding, large files (>1MB warning), binary data (null bytes), missing components (return "Unknown"), reparse points (skip with warning)
- [ ] T019 [P] [Phase 3] Implement `INgxScanner.cs` interface and `NgxScanner.cs` in `DLSSVersionToolkit.Core/Services/`: scan NGX Release and Staging base paths, enumerate version subfolders with `GetDirectories`, call `NgxConfigParser` for each, return `DLSSVersionEntry` list. Handle: path not found, access denied, reparse points, no config files found
- [ ] T020 [P] [Phase 3] Implement `IGlobalScanner.cs` interface and `GlobalScanner.cs` in `DLSSVersionToolkit.Core/Services/`: scan AnWave folder, read DLL versions using `FileVersionInfo.GetVersionInfo()`, handle nvngx_dlss.dll, nvngx_dlssg.dll, nvngx_dlssd.dll, nvngx_deepdvc.dll, sl.common.dll. Handle: missing DLLs (skip), reparse points, empty version metadata (Unknown), access denied
- [ ] T021 [P] [Phase 3] Implement `IStreamlineScanner.cs` interface and `StreamlineScanner.cs` in `DLSSVersionToolkit.Core/Services/`: scan Streamline SDK folder at bin\x64, read DLL versions for nvngx_dlss.dll, nvngx_dlssg.dll, nvngx_dlssd.dll, nvngx_deepdvc.dll, sl.common.dll. Handle: path not found, missing bin\x64 subfolder, auto-detect in Downloads folder
- [ ] T022 [P] [Phase 3] Implement `IVersionComparer.cs` interface and `VersionComparer.cs` in `DLSSVersionToolkit.Core/Services/`: compare versions across `List<DLSSVersionEntry>`, find newest per component (DLSS, FrameGen, DLSSD, DeepDVC), generate `Recommendation` list. Use `System.Version` for comparison, normalize non-standard formats. Handle: "Unknown" as lowest, all sources up-to-date
- [ ] T023 [P] [Phase 3] Implement `IScanService.cs` interface and `ScanService.cs` in `DLSSVersionToolkit.Core/Services/`: orchestrate all scanners (Ngx, Global, Streamline), call VersionComparer, return `ScanResult`. Use settings service for custom paths. Handle: optional sources missing (add warning, continue), scan errors (add to Errors list)

---

## Phase 4: Core Library — Upgrade/Sync Services

**Purpose**: Implement backup, upgrade, and sync operations.

- [ ] T024 [P] [Phase 4] Implement `IBackupService.cs` interface and `BackupService.cs` in `DLSSVersionToolkit.Core/Services/`: create timestamped backup to `.dlss-backup-<yyyyMMdd-HHmmss>` in versions parent directory, verify file count matches source, handle long paths via `\\?\` prefix. Return backup path string or null on failure
- [ ] T025 [P] [Phase 4] Implement `IRestoreService.cs` interface and `RestoreService.cs` in `DLSSVersionToolkit.Core/Services/`: restore Release folder from backup path, verify file count matches, handle failures gracefully
- [ ] T026 [P] [Phase 4] Implement `IUpgradeService.cs` interface and `UpgradeService.cs` in `DLSSVersionToolkit.Core/Services/`: upgrade from Staging to NGX Release. Process: find latest Staging version via NgxScanner, create backup via BackupService, copy DLLs (nvngx_dlss.dll, nvngx_dlssg.dll, nvngx_dlssd.dll) and config file from Staging to Release using `File.Copy`, on any failure call RestoreService, return `UpgradeOperation`
- [ ] T027 [P] [Phase 4] Implement sync operation in `UpgradeService`: sync from Streamline SDK or AnWave to NGX Release. Process: validate source has required DLLs, create backup, copy nvngx_*.dll files from source to Release, copy nvngx_package_config.txt, restore on failure. Handle: source DLLs missing, source config missing, permissions denied
- [ ] T028 [Phase 4] Write unit tests for BackupService, RestoreService, UpgradeService using xUnit with test fixtures (mock folder structures in temp directory)

---

## Phase 5: WPF UI — Main Window Layout

**Purpose**: Build the main dashboard window with toolbar, DataGrid, recommendation bar, and status bar.

- [ ] T029 [P] [Phase 5] Define color resources and styles in `App.xaml`: NVIDIA green (#76B900), dark background (#1E1E1E), surface (#2D2D2D), text (#FFFFFF / #AAAAAA), error (#F44336), warning (#FFC107). Button style, DataGrid style, text styles per ui-layout.md
- [ ] T030 [P] [Phase 5] Build `MainWindow.xaml`: grid layout with title bar region (auto), toolbar row (50px), main content area (scrollviewer with DataGrid), recommendation bar (40px), status bar (30px). Set window properties: MinWidth=700, MinHeight=500, Width=900, Height=600, ResizeMode=CanResize
- [ ] T031 [Phase 5] Implement toolbar buttons in `MainWindow.xaml`: [Scan Now] (icon + text), [Upgrade Release] (icon + text), [Sync from...] (dropdown), [Export] (icon + text), [⚙] settings button. Style with dark theme per ui-layout.md
- [ ] T032 [P] [Phase 5] Implement `MainViewModel.cs` in `ViewModels/`: use `[ObservableProperty]` for Versions (ObservableCollection<DLSSVersionEntry>), Recommendations (ObservableCollection<Recommendation>), LastScanTime, NextScanCountdown, ScanStatus, IsScanning. Use `[RelayCommand]` for ScanCommand, UpgradeCommand, SyncCommand, ExportCommand, SettingsCommand, CheckNowCommand, ShowDashboardCommand, ExitCommand
- [ ] T033 [P] [Phase 5] Build version comparison DataGrid in `MainWindow.xaml`: columns Source, BuildID, DLSS, FrameGen, DLSSD, DeepDVC, Streamline. Use custom DataGridCellStyle to highlight cells green when IsNewest* is true. Source column with icon
- [ ] T034 [Phase 5] Build recommendation bar: text block bound to first recommendation description, [Sync to NGX] button if applicable, expandable details section
- [ ] T035 [Phase 5] Build status bar: last scan time (left), next scan countdown (center), scan status indicator with colored dot (right)

---

## Phase 6: WPF UI — Settings Dialog

**Purpose**: Build the settings dialog for custom path configuration.

- [ ] T036 [P] [Phase 6] Implement `SettingsViewModel.cs` in `ViewModels/`: bind to AppSettings, commands for BrowseNgxPath, BrowseAnWavePath, BrowseStreamlinePath, SaveSettings, CancelSettings. Use `[ObservableProperty]` for all setting fields
- [ ] T037 [P] [Phase 6] Build `SettingsDialog.xaml`: modal window, section "Paths" with labeled TextBox + Browse button for each path (NGX Base, AnWave, Streamline SDK). Section "Options" with checkboxes for StartMinimized, AutoScan, MinimizeToTray, NotifyOnNewVersion. [Save] and [Cancel] buttons
- [ ] T038 [Phase 6] Wire Settings dialog: MainViewModel.SettingsCommand opens SettingsDialog as modal, on Save calls SettingsService.SaveAsync, on Cancel disposes without saving

---

## Phase 7: WPF UI — System Tray

**Purpose**: Implement system tray integration, minimize to tray, tray context menu, and notifications.

- [ ] T039 [P] [Phase 7] Implement `TrayIconService.cs` in `DLSSVersionToolkit/Services/`: wrap `Hardcodet.NotifyIcon.Wpf.TaskbarIcon`, set icon from Assets/icon.ico, set tooltip to "DLSS Version Toolkit — [status]", set context menu from XAML (Show Dashboard, Check Now, separator, Exit). Handle left-click (restore window), right-click (context menu)
- [ ] T040 [P] [Phase 7] Implement `NotificationService.cs` in `DLSSVersionToolkit/Services/`: show balloon notifications via `TaskbarIcon.ShowBalloonTip()` with title "DLSS Version Toolkit", message text, icon. Handle notification click to restore window
- [ ] T041 [P] [Phase 7] Implement `ScanSchedulerService.cs` in `DLSSVersionToolkit/Services/`: use `System.Timers.Timer` for periodic scanning (4-hour interval). On startup, schedule first scan after 5-second delay. Scan on timer elapsed, compare to previous scan, if new version detected call NotificationService. Expose `ScanNow()` method for manual trigger
- [ ] T042 [Phase 7] Wire window close (not just minimize): handle MainWindow.Closing event, set `e.Cancel = true`, hide window, show tray icon. App exits only via tray "Exit" menu item
- [ ] T043 [Phase 7] Wire tray icon events: left-click restores window (Show(), Activate()), "Show Dashboard" menu item same, "Check Now" triggers ScanSchedulerService.ScanNow(), "Exit" calls Application.Current.Shutdown()
- [ ] T044 [Phase 7] Wire App.xaml.cs startup: schedule initial scan via ScanSchedulerService, start minimized to tray if AppSettings.StartMinimized is true

---

## Phase 8: WPF UI — Export

**Purpose**: Implement CSV and JSON export functionality.

- [ ] T045 [P] [Phase 8] Implement `ExportService.cs` in `DLSSVersionToolkit.Core/Services/`: write CSV (comma-separated with header row: Source, BuildID, DLSS, FrameGen, DLSSD, DeepDVC, Streamline, Path, ScannedAt) and JSON (full ScanResult serialized with `System.Text.Json`) to user-specified path
- [ ] T046 [Phase 8] Wire Export button: MainViewModel.ExportCommand opens `SaveFileDialog` with filter "CSV Files (*.csv)|*.csv|JSON Files (*.json)|*.json", calls ExportService with selected format, shows success message with file path

---

## Phase 9: Polish & Testing

**Purpose**: Final integration, unit tests, and build verification.

- [ ] T047 [Phase 9] Write unit tests for all Core services using xUnit: NgxConfigParser (valid config, missing components, corrupt file, reparse point), NgxScanner (mock folder structure), GlobalScanner (mock DLLs), VersionComparer (version comparison edge cases), BackupService, UpgradeService
- [ ] T048 [Phase 9] Integration tests: create temp NGX folder structure, run full scan, verify correct entries returned
- [ ] T049 [Phase 9] Test single-instance: launch app, try launching again, verify first instance brought to foreground
- [ ] T050 [Phase 9] Test system tray: minimize window, verify tray icon appears, right-click shows menu, "Exit" closes app
- [ ] T051 [Phase 9] Test settings persistence: set custom paths, restart app, verify paths loaded
- [ ] T052 [Phase 9] Test upgrade flow: create mock staging version newer than release, click upgrade, verify backup created, files copied, operation status
- [ ] T053 [Phase 9] Run `dotnet publish` command to create single-file .exe, verify file exists, verify it runs on a clean machine (or at minimum, verify no missing dependencies)
- [ ] T054 [Phase 9] Update AGENTS.md in root: add .NET 9 and WPF to active technologies

---

## Dependencies & Execution Order

### Phase Dependencies

- Phase 1 (Setup) → No dependencies
- Phase 2 (Models) → Depends on Phase 1
- Phase 3 (Core Scanning) → Depends on Phase 2
- Phase 4 (Core Upgrade) → Depends on Phase 3
- Phase 5 (WPF Main Window) → Depends on Phase 3 + Phase 4
- Phase 6 (WPF Settings) → Depends on Phase 5
- Phase 7 (WPF Tray) → Depends on Phase 5
- Phase 8 (WPF Export) → Depends on Phase 3
- Phase 9 (Polish) → Depends on all previous

### Within-Phase Parallelization

- T008 + T009 + T010 can run in parallel (different files)
- T013 + T014 + T015 + T016 + T017 can run in parallel (different files)
- T018 + T019 + T020 + T021 + T022 can run in parallel (different files)
- T024 + T025 + T026 + T027 can run in parallel (different files)
- T029 + T030 + T032 + T033 can run in parallel (different files)
- T036 + T037 can run in parallel (different files)
- T039 + T040 + T041 can run in parallel (different files)
- T045 can run independently (after Phase 3)

### MVP Scope

- MVP = Phase 1 + Phase 2 + Phase 3 + Phase 5 (User Stories 1 + 7)
- MVP delivers: working WPF dashboard showing all DLSS versions with comparison and recommendations
- MVP does NOT include: upgrade, sync, settings, tray, notifications, export, periodic scans