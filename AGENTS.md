# dlss-version-toolkit Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-06-11

## Active Technologies

- **.NET 9 + WPF** (002-dlss-gui): WPF single-file .exe, framework-dependent deployment, C# core logic, CommunityToolkit.Mvvm, Hardcodet.NotifyIcon.Wpf
- **PowerShell 5.1+** (001-dlss-version-checker): Windows PowerShell module and CLI (legacy, superseded by 002-dlss-gui)

## Project Structure

```text
src/
├── DLSSVersionToolkit.Core/         # Core logic library (no WPF)
│   ├── Models/                        # DLSSVersionEntry, ScanResult, AppUpdateInfo, etc.
│   └── Services/                      # NgxScanner, UpgradeService, AppUpdateService, etc.
├── DLSSVersionToolkit/               # WPF application
│   ├── ViewModels/                   # MainViewModel
│   ├── Views/                        # SettingsDialog
│   ├── Converters/                   # UI converters
│   ├── MainWindow.xaml               # Sidebar-dashboard UI
│   └── App.xaml                      # Theme, styles, startup
└── DLSSVersionToolkit.sln

tests/
└── DLSSVersionToolkit.Tests/         # xUnit tests
```

The single-file `DLSSVersionToolkit.exe` (~3.9 MB) is produced by CI on each `v*` tag and
attached to the GitHub release — it is not committed to the repo.

## Commands

```bash
# Build
dotnet build src/DLSSVersionToolkit.sln

# Publish single-file exe
dotnet publish src/DLSSVersionToolkit/DLSSVersionToolkit.csproj --configuration Release --self-contained false

# Run tests
dotnet test
```

## Code Style

- **C# / .NET 9**: Use CommunityToolkit.Mvvm `[ObservableProperty]` and `[RelayCommand]` source generators
- **WPF**: NVIDIA-leaning sidebar dashboard, true-black canvas (#000000) with layered panel surfaces (#0E0E0E/#171717), #76B900 green strictly as a signal accent, Inter/Segoe UI font, Consolas for paths/versions. New theme tokens live alongside the original brushes in App.xaml (additive — never remove old keys).

## Recent Changes

- **v0.0.31**: App auto-updater (`AppUpdateService` — startup GitHub-release check, in-place exe rename-swap with rollback + `--wait-for-pid` restart handshake, gated by `CheckForAppUpdates` setting); sidebar simplified (TOOLS→ADVANCED with "Update All runs these for you", Settings promoted to CONFIGURE, new HELP group); first-run quick-guide card (`HasSeenQuickGuide`). Removed 4 unused RelayCommands (UpgradeAsync, SyncFromStreamlineAsync, SyncDlssSdkToBothAsync, ExitApp — none were bound in the simplified UI). README/AGENTS docs refreshed.
- **v0.0.30**: Relicensed MIT→Apache-2.0 (NOTICE attributes scubamount); old releases wiped (latest only); `PackageLicenseExpression=Apache-2.0`.
- **v0.0.29**: UI redesign — NVIDIA-leaning sidebar dashboard + scubamount maker attribution in an in-window header.
- **v0.0.28**: Fixed error dialog on every clean exit (ReleaseMutex on an unowned single-instance mutex).
- **v0.0.27**: Progress indicator while applying a preset across all game profiles.
- **v0.0.26**: Per-game profile sweep — apply preset to BaseProfile + every game profile (the real preset fix).
- **v0.0.25**: Set the SR/RR/FG override ENABLE flag ("Custom") on apply — preset selection alone is a no-op without it.
- **v0.0.17**: `TryParseVersion` (4-component), removed Apply to AnWave buttons, Update All rework (always calls DownloadLatestAsync), Setup reads real DLL versions, direct disk probe fallback
- **v0.0.16**: `GetCachedSdkVersion()` substring bug fix (prefix length 13→9), `SetupAnWaveAsync()` early-exit when DLL exists, `AutoApplyToAnWaveAsync()` service fallback
- **v0.0.15**: `ExtractVersionFromUrl()` regex fix, multi-path NGX scanning, `OneClickUpdateAllAsync` AnWave path fallback, "already up to date" messaging
- **v0.0.12**: Documentation audit and fixes — csproj version aligned to 0.0.11 (was 2.0.0.0); fixed `AutoScanEnabled` test assertion (model defaults `false`, test was asserting `true`); implemented `StartMinimized` in App.xaml.cs (setting existed but was never read on startup); updated winget manifest (version, description, .NET 9 dependency); updated scoop manifest (version, bin→DLSSVersionToolkit.exe, removed stale psmodule reference, added shortcut); README: removed "Upgrade Release" from Advanced Operations (command exists in ViewModel but not wired to UI), fixed Windows 10 version 1908→1903
- **v0.0.11**: Improved dialog messages — all dialogs now show version numbers, file lists, and actionable "what to do next" guidance
- **v0.0.10**: Cache management — skip re-download if version already cached; `TrimCache(3)` keeps latest 3 DLSS SDK zips; `TrimGlomCache(2)` keeps latest 2 nvidiaDlssGlom .rars; `GetCacheInfo()` returns count + total bytes; active cached file never deleted during trimming
- **v0.0.9**: Hardened path resolution and error handling — `SyncFromStreamline` falls back to cached SDK when no path configured; `SyncFromAnWave` uses AnWaveAutoService detected path; all sync paths validated with `Directory.Exists()` before use; `ApplyToAnWave` checks path existence; `OneClickUpdateAll` uses all 3 AnWave path sources (installed, settings, detected)
- **v0.0.8**: Security patch — SharpCompress 0.37.2 → 0.48.0 to patch CVE-2026-44788 (GHSA-6c8g-7p36-r338, directory traversal in `WriteToDirectory`); `ArchiveFactory.Open()` → `ArchiveFactory.OpenArchive()`; Dependabot alert auto-resolved to `fixed`
- **v0.0.7**: File lock fix (temp dir extraction), one-click Update All, UI redesign (hero banner, DataGrid, collapsible Advanced, AnWave status panel)
- **002-dlss-gui**: Complete WPF GUI rewrite. Single-file .exe (~918 KB). Features: version scanning, upgrade, sync, system tray, periodic background scan, export (CSV/JSON), settings. Supersedes 001-dlss-version-checker.

<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->