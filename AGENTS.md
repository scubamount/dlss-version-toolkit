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

- **v0.0.35.5**: Download/swap flow tests (#8 from audit). Refactored `AppUpdateService`: static `HttpClient _http` → instance field with two constructors — parameterless (production, uses shared static client) + `AppUpdateService(HttpClient)` (test-injectable). Backward compatible — `MainViewModel` still uses `new()`. 6 new tests with mock `HttpMessageHandler`: (1) `CheckForUpdateAsync` finds newer version + sha256 asset, (2) API 500 → graceful no-update, (3) no exe asset → no-update, (4) **SHA256 mismatch refuses to execute** (the security gate from v0.0.35.3 now tested), (5) exe 404 → download failure, (6) no-update-available → early fail. 111→117 tests.
- **v0.0.35.4**: Robustness + test coverage. **#6** `DetectCurrentPresetSafe` → `DetectCurrentPresetSafeAsync` (async Task, `await` instead of `.GetAwaiter().GetResult()`). Was on a thread-pool thread via `Task.Run` (not UI thread — no deadlock/freeze), but sync-over-async wasted a thread-pool thread. Now properly async. **#7** audit finding was largely a false positive — download-service catches already log via `Console.Error.WriteLine`; the only truly empty catches were cleanup paths (fine) and 3 AnWaveAutoService detection methods (now have `System.Diagnostics.Debug.WriteLine`). **#4** first UpgradeService tests: 7 tests covering decision logic (disallowed path, no release, no staging, already-up-to-date, backup fail, backup-verify fail) + end-to-end file-copy success (real temp dirs, fake PE DLLs, backup→signature→copy→verify). Mock `INgxScanner`/`IBackupService` classes nested in test. **#8** deferred — AppUpdateService uses static HttpClient (not injectable without refactoring). 104→111 tests.
- **v0.0.35.3**: Auto-updater security + runtime forward-compat. **#1** SHA256 integrity gate: `release.yml` now publishes `DLSSVersionToolkit.exe.sha256` (sha256sum format) alongside the exe. `AppUpdateService.DownloadAndApplyAsync` downloads the checksum after the exe, computes `SHA256.Create().ComputeHashAsync()`, and refuses to execute on mismatch (deletes staged file, returns error with expected/actual hashes). Defense-in-depth against MITM/corruption — full RCE-from-compromised-release still needs code-signing (deferred). `AppUpdateInfo.Sha256Url` field added; `CheckForUpdateAsync` finds `.sha256` asset in the release assets array (no `break` — scans all assets). Backward compatible: older releases without the hash asset are skipped gracefully. **#3** README self-contradiction fixed: 3 spots said "self-contained" (false — app is framework-dependent, needs .NET 9 Desktop Runtime). Now accurately says "single-file" + prominent runtime requirement + winget install command + troubleshooting entry. Added `<RollForward>LatestMajor</RollForward>` to csproj so the app runs on .NET 10+ when .NET 9 is no longer installed. +1 test assertion (`Sha256Url` default).
- **v0.0.35.2**: Systematic audit fixes (6 items). **#5** version display: `ToString(3)` silently truncated the 4th component (0.0.35.1 showed as "0.0.35" in the updater dialog) — replaced with `ToDisplayVersion()` that drops trailing zeros (0.0.35.0→"0.0.35", 0.0.35.1→"0.0.35.1", never below major.minor). **#14** `VersionComparer.IsVersionNewer` IndexOutOfRange on 2-part versions ("310.6" had length-2 array, `parts[2]` threw, caught → "never newer") — now pads to 4 with `Concat(Repeat(0,4)).Take(4)`. **#10** removed stale `System.IO.Compression.ZipFile 4.3.0` (2016 netstandard ref; framework includes it on net9.0). **#12** `ci.yml` got `permissions: contents: read` (was default broad). **#11** added `.github/dependabot.yml` (nuget + github-actions, weekly). **#9** dark ScrollBar + ToolTip templates in App.xaml (last default-light chrome: scrollbars in ScrollViewers/DataGrid/ComboBox popup were light grey; tooltips were default yellow). 4 new tests (ToDisplayVersion 5 cases, 2-part version 4 cases).
- **v0.0.35.1**: UI fix — the SR/RR/FG preset dropdowns' closed-state selection box rendered with a white background (WPF default chrome) showing green text that was hard to read. Added full dark ControlTemplates for ComboBox / ComboBoxItem / the dropdown ToggleButton in App.xaml (Panel2 background, Panel3 hover, dark popup), so no white surfaces remain. Verified the auto-updater handles the 4-part tag: `ParseTagVersion`→`System.Version("0.0.35.1")`, `IsNewer((0,0,35,1),(0,0,35,0))`=true. First decimal-patch release.
- **v0.0.35**: Per-feature DLSS preset knobs. Added independent **DLSS-RR** (Ray Reconstruction, default **E**) and **DLSS-FG** (Frame Generation, default **B**) preset dropdowns alongside the existing DLSS-SR preset (default L). BUG FIX: `ApplyToProfile` previously mirrored the SR letter onto RR (so RR got L); now each feature writes its OWN preset-selection ID — SR `0x10E41DF3`, RR `0x10E41DF7`, FG `0x10E41DF1` (the FG ID was missing entirely; FG never had a preset set before). `DlssPreset` enum expanded to A–M (E now exists); `PresetFromValue` is now a checked enum cast. Window widened 980→1120 (min 900→1080) to fit the three dropdowns. New `DlssPresetShortLabelConverter` for the compact RR/FG labels. 90+ tests pass.
- **v0.0.34**: Perf — minimized per-profile NVAPI calls in the preset sweep (NvAPIWrapper re-fetches the full NVDRS_PROFILE struct on every property access; now read NumberOfApplications once per profile, skip 0-app profiles); 30-min TTL cache on the GitHub release list (auto-scan was hitting the API on every launch). README redesign (centered badges, TL;DR, What-It-Does table, Star History) + stale-content fixes. ci.yml triggers broadened to perf/refactor/docs branches. No behavior change; 84 tests pass.
- **v0.0.33**: State detection + startup scan. New read-only detection so the dashboard reflects reality on launch: `WhitelistService.DetectStateAsync` (Applied/NotApplied/NotApplicable, read-only twin of the apply path), `AnWaveAutoService.DetectInstalled` (disk + DLL-version probe), and auto-scan on launch (App.xaml.cs triggers ScanCommand on the UI thread). Fixed "latest available" showing blank while "up to date" — availability now derives from newest of {GitHub latest, cached SDK, installed} via numeric `VersionComparer.IsNewer` (was lexical string.Compare, mis-ordered 310.6 vs 310.10). UI: indicator on/off colored dot, whitelist status dot state-driven, Advanced Apply-Whitelist/Setup-AnWave dim when detected already-done, sidebar nav scrollable + taller window (HELP no longer cut off). Added `ci.yml` build+test workflow for branches/PRs (compile gate without releasing). 84 tests passing.
- **v0.0.32**: Stale-code + doc audit — removed 4 unbound RelayCommands, wired CheckForAppUpdates into SettingsDialog, README/AGENTS refreshed.
- **v0.0.31**: App auto-updater (`AppUpdateService` — startup GitHub-release check, in-place exe rename-swap with rollback + `--wait-for-pid` restart handshake, gated by `CheckForAppUpdates` setting); sidebar simplified (TOOLS→ADVANCED with "Update All runs these for you", Settings promoted to CONFIGURE, new HELP group); first-run quick-guide card (`HasSeenQuickGuide`).
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