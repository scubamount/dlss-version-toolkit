# Research: DLSS Version Toolkit — WPF GUI

**Feature Branch**: `002-dlss-gui` | **Date**: 2026-05-08

---

## 1. WPF Single-File .NET 9 Deployment

### Decision

Use WPF with .NET 9, framework-dependent deployment (no self-contained runtime included). Produce a single .exe. No installer, no separate DLLs.

### Rationale

- WPF produces native Windows UI with standard window chrome, system tray support, and native dialogs.
- .NET 9 is the latest LTS-adjacent version with good Windows 10/11 support.
- Framework-dependent keeps the .exe small (~5-10MB). Users with .NET 9 already installed (increasingly common) get a tiny download. Those without install .NET 9 once.
- Single-file publish with `dotnet publish -r win-x64 --self-contained false -p:PublishSingleFile=true` produces one .exe.
- WPF is built into .NET — no extra NuGet packages needed for basic UI.

### Alternatives Considered

- **Self-contained .exe**: Would be ~50-80MB with .NET runtime bundled. Much larger download. Users with .NET 9 installed would download redundant runtime. Rejected for size.
- **Avalonia UI**: Cross-platform capable. Adds NuGet dependency and complexity. This tool will never run on Linux/macOS. Rejected per Constitution III.
- **Electron**: 150MB+ runtime overhead. Excessive for a version checker. Rejected per Constitution III.
- **WinForms**: Works but looks dated. WPF provides better modern UI with DataGrid, styling, and visual polish. Preferred.
- **MAUI**: .NET Multi-platform App UI. More complexity than WPF for Windows-only tool. Rejected.

---

## 2. Project Structure for WPF + Core Library

### Decision

Create a solution with two projects:
1. `DLSSVersionToolkit.Core` — Class library containing all DLSS scanning, config parsing, upgrade/sync logic. No WPF dependency. This makes the core logic unit-testable.
2. `DLSSVersionToolkit` — WPF application referencing the Core library. Contains all UI (XAML, ViewModels, App entry point).

### Rationale

- Separation of concerns: core logic has no UI dependency, making it testable and reusable.
- `DLSSVersionToolkit.Core` can be unit tested without launching WPF.
- The Core library mirrors the intent of the original PS module but in C#.
- Solution layout:
  ```
  DLSSVersionToolkit/
  ├── src/
  │   ├── DLSSVersionToolkit.Core/          # Core logic library
  │   │   ├── DLSSVersionToolkit.Core.csproj
  │   │   ├── Services/
  │   │   │   ├── INgxScanner.cs
  │   │   │   ├── NgxScanner.cs             # NGX Release/Staging scanning
  │   │   │   ├── IGlobalScanner.cs
  │   │   │   ├── GlobalScanner.cs          # AnWave scanning
  │   │   │   ├── IStreamlineScanner.cs
  │   │   │   ├── StreamlineScanner.cs      # Streamline SDK scanning
  │   │   │   ├── IVersionComparer.cs
  │   │   │   ├── VersionComparer.cs        # Version comparison logic
  │   │   │   ├── IUpgradeService.cs
  │   │   │   ├── UpgradeService.cs         # Upgrade/sync operations
  │   │   │   ├── IConfigParser.cs
  │   │   │   ├── NgxConfigParser.cs        # nvngx_package_config.txt parsing
  │   │   │   ├── IBackupService.cs
  │   │   │   ├── BackupService.cs          # Backup/restore logic
  │   │   │   └── ISettingsService.cs
  │   │   │   └── SettingsService.cs        # App settings read/write
  │   │   └── Models/
  │   │       ├── DLSSVersionEntry.cs
  │   │       ├── ScanResult.cs
  │   │       ├── UpgradeOperation.cs
  │   │       ├── AppSettings.cs
  │   │       └── Recommendation.cs
  │   └── DLSSVersionToolkit/               # WPF app
  │       ├── DLSSVersionToolkit.csproj
  │       ├── App.xaml / App.xaml.cs
  │       ├── MainWindow.xaml / MainWindow.xaml.cs
  │       ├── ViewModels/
  │       │   ├── MainViewModel.cs
  │       │   └── SettingsViewModel.cs
  │       ├── Views/
  │       │   ├── DashboardView.xaml
  │       │   ├── SettingsDialog.xaml
  │       │   └── ConfirmDialog.xaml
  │       ├── Services/
  │       │   ├── TrayIconService.cs
  │       │   └── NotificationService.cs
  │       └── Converters/
  │           └── VersionToColorConverter.cs
  └── DLSSVersionToolkit.sln
  ```

### Alternatives Considered

- **Single project**: All code in one WPF project. Simpler but harder to unit test core logic. Rejected.
- **More granular projects** (Core, Infrastructure, UI): Overkill for this tool's scope. Rejected.

---

## 3. MVVM Architecture for WPF

### Decision

Use MVVM pattern with ViewModels as the binding target. No third-party MVVM framework — use base classes and manual binding. CommunityToolkit.Mvvm for source generators if needed for simpler INotifyPropertyChanged.

### Rationale

- Standard WPF pattern. ViewModels expose data and commands, Views bind to them via XAML.
- CommunityToolkit.Mvvm is a lightweight NuGet package that generates INotifyPropertyChanged and ICommand boilerplate. Reduces boilerplate without adding heavy framework.
- `ObservableCollection` for dynamic lists (version entries, recommendations).
- `RelayCommand` / `AsyncRelayCommand` for button bindings.
- Services injected via constructor (registered in App.xaml.cs DI container or manual).

### Alternatives Considered

- **Prism/Caliburn.Micro**: Heavy frameworks with navigation, regions, etc. Overkill. Rejected.
- **Raw INotifyPropertyChanged without toolkit**: Verbose boilerplate for a 2000-line app. Accepted but tedious.
- **ReactiveUI**: Functional reactive programming. Steeper learning curve. Rejected for simplicity.

---

## 4. System Tray Integration

### Decision

Use `System.Windows.Forms.NotifyIcon` (bundled in Windows Forms interop assembly) or Hardcodet.NotifyIcon.Wpf NuGet package. Hardcodet.NotifyIcon.Wpf is the standard choice for WPF tray icons — it wraps the WinForms NotifyIcon and provides a proper WPF ImageSource binding.

### Rationale

- WPF does not have built-in NotifyIcon support. Need to use the WinForms NotifyIcon via interop.
- Hardcodet.NotifyIcon.Wpf is the de-facto standard WPF tray library. Simple API, handles icon, context menu, double-click, balloon notifications.
- Tray icon persists when window is minimized. Tray context menu provides Show/Check Now/Exit.
- On minimize button click or window close: hide window, show tray icon.
- On tray icon double-click or "Show Dashboard" context menu: show and activate window.

### Alternatives Considered

- **System.Windows.Forms.NotifyIcon directly**: Works but requires adding System.Windows.Forms reference and manual message loop handling. Hardcodet wraps this nicely. Accepted.
- **Avalonia or other UI framework with native tray**: Would require rewriting UI. Rejected.

---

## 5. Background Scanning and Periodic Timer

### Decision

Use `System.Timers.Timer` for periodic background scans (4-hour interval). On app startup, schedule first scan after a short delay (e.g., 5 seconds) to allow UI to render first. Store scan timer in a background task that survives window minimize.

### Rationale

- `System.Timers.Timer` runs on a ThreadPool thread, survives window hide/minimize.
- Scanning is read-only (no admin required), so it can run anytime.
- When a new version is detected, post a system notification via `Hardcodet.NotifyIcon.Wpf`'s balloon tip API.
- On notification click, restore the main window.
- For periodic rescheduling after sleep: the timer continues running. If the system was asleep, the next elapsed event fires as expected. No special sleep handling needed.

### Alternatives Considered

- **Task.Delay in async loop**: Works but Timer is idiomatic for periodic background tasks.
- **Windows Task Scheduler integration**: Overkill. Adds complexity for no benefit.

---

## 6. Single-Instance Enforcement

### Decision

Use a named mutex (`"Global\\DLSSVersionToolkit_SingleInstance"`) to enforce single-instance. On startup, attempt to acquire the mutex. If already owned, bring the existing window to foreground and exit. If not owned, acquire it and continue.

### Rationale

- Named mutex is the standard .NET pattern for single-instance enforcement.
- Use `Mutex` with `initialOwned: false` and call `WaitOne(0)` to check.
- Use `Environment.CurrentDirectory` or `GetForegroundWindow` + `SetForegroundWindow` to bring existing window to front.
- The mutex name should include a GUID to avoid collisions with other apps.

### Alternatives Considered

- **Remoting or WCF**: Overkill. Rejected.
- **Third-party single-instance library**: Unnecessary dependency. Manual mutex is ~10 lines.

---

## 7. Settings Persistence

### Decision

Store settings as JSON in `%APPDATA%\DLSSVersionToolkit\settings.json`. Use `System.Text.Json` for serialization. `AppSettings` class with nullable fields (empty string = use default/auto-detect).

### Rationale

- `%APPDATA%` is the standard Windows location for user-level app data.
- `System.Text.Json` is built into .NET 9 — no external package needed.
- JSON is human-readable and easy to debug/edit manually.
- `File.WriteAllText` / `File.ReadAllText` for read/write. Add locking for concurrent access safety (unlikely but safe).

### Alternatives Considered

- **Registry**: Harder to migrate/backup. Rejected.
- **SQLite**: Overkill for a settings file. Rejected.
- **Encrypted settings**: Not needed — settings are not sensitive. Rejected.

---

## 8. Logging

### Decision

Write structured log entries to `%APPDATA%\DLSSVersionToolkit\logs\dlss-version-toolkit-<date>.log`. Use `Microsoft.Extensions.Logging` abstraction with a simple `FileLogger` implementation. Log levels: Information (scan started/completed, upgrade started/completed), Warning (skipped reparse point, missing optional source), Error (access denied, backup failed, scan error).

### Rationale

- Structured logging enables filtering by level and searching.
- A new log file per day prevents unbounded growth.
- Log rotation: keep last 7 days of logs, delete older ones on startup.
- Log location: `%APPDATA%\DLSSVersionToolkit\logs\` — standard app data path.

### Alternatives Considered

- **Event Log**: Requires admin for some events, harder to read. Rejected.
- **Third-party logging (Serilog, NLog)**: Adds dependency. `Microsoft.Extensions.Logging` is built-in, `FileLogger` is simple to implement.

---

## 9. Backup Strategy (Same as Phase 1)

### Decision

Same as the PowerShell tool: create timestamped backup folder in the same parent directory as Release versions. Format: `.dlss-backup-<yyyyMMdd-HHmmss>`. Copy entire version folder recursively. Verify file count matches after copy. On failure, restore from backup.

### Rationale

- The Phase 1 backup strategy is proven. No reason to change it.
- Backup location is within the same ProgramData path, avoiding permission issues.
- Timestamped prefix prevents collisions on repeated upgrades.

---

## 10. C# Port of Config Parsing

### Decision

Port the PowerShell regex parsing to C#. Use `Regex.Matches` with pattern `@"dlss,\s+([\d.]+)"` etc. Read file as string with `File.ReadAllText`. Handle encoding explicitly with `Encoding.UTF8`.

### Rationale

- Direct port of the logic from `DLSSVersion.psm1`.
- `System.Text.RegularExpressions.Regex` is built into .NET.
- Handle same edge cases: reparse points, large files, corrupt configs, missing components.
- C# FileVersionInfo for DLL metadata reads: `[System.Diagnostics.FileVersionInfo]::GetVersionInfo()` becomes `FileVersionInfo.GetVersionInfo()`.

---

## 11. UI Styling

### Decision

Use a dark theme matching the NVIDIA-style dark UI. Primary color #76B900 (NVIDIA green). Background #1E1E1E, surface #2D2D2D. Use WPF `Style` resources in App.xaml for button, DataGrid, and control templates. No external theme libraries.

### Rationale

- Dark theme is standard for gaming/performance tools. Matches NVIDIA's aesthetic.
- WPF `Style` resources in `App.xaml` apply consistent styling globally.
- Custom DataGrid cell style to highlight newest versions in green.
- Standard Windows font (Segoe UI) ensures readability.
- No third-party UI library needed.

### Alternatives Considered

- **Material Design (MaterialDesignInXAML)**: Heavy library. Overkill. Rejected.
- **Fluent Design (Fluent.Ribbon)**: Adds complexity. Standard dark styling is sufficient.