# dlss-version-toolkit Development Guidelines

Hand-maintained. Last updated: 2026-08-24 (v0.0.59).

> Regenerate this file when shipping a release that changes structure, commands, or a standing
> lesson. There is no generator — the previous header claimed to be machine-derived from feature
> plans, and no such script has ever existed in this repo, which made the file read as generated
> while it silently went six releases stale. The counts and cited symbols below are gated by
> `AgentsDocClaimsTests`, so CI reddens instead of letting them drift.

## Active Technologies

**.NET 9 + WPF** (`002-dlss-gui`) — the whole shipping app. Single-file `.exe`,
framework-dependent (needs the .NET 9 Desktop Runtime), C# core logic, CommunityToolkit.Mvvm,
Hardcodet.NotifyIcon.Wpf, SharpCompress.

`src/DLSSVersion/*.psm1` + `check-dlss-versions.ps1` + `install.ps1` are the **dead**
`001-dlss-version-checker` PowerShell module. Not in `DLSSVersionToolkit.sln`, not built by CI,
not referenced by the README. Nothing reads them. Treat as removable, not as a supported path.

## Project Structure

```text
src/
├── DLSSVersionToolkit.Core/            # Core logic library (no WPF) — all 26 services
│   ├── Models/                         # AppSettings, DlssPreset, OverrideManifest,
│   │                                   #   UpdateRunReport, ScanResult, ...
│   └── Services/
│       ├── NgxPathResolver.cs          # READ vs WRITE path split — see standing lessons
│       ├── NgxModelLayout.cs           # packed version folders, .bin naming
│       ├── LocalDllImportService.cs     # import loose nvngx_*.dll (no release asset exists)
│       ├── OverrideManifestService.cs   # records that a local DLL outranks the download channel
│       ├── UpgradeService.cs            # NGX/Streamline sync
│       ├── WhitelistService.cs          # NVIDIA App whitelist + IsOpsSupported unlock
│       ├── PresetOverrideService.cs     # SR/RR/FG preset writes via NvAPI
│       ├── OperationGuard.cs            # PE signature, path containment, post-copy verify
│       └── ...                          # scanners, downloaders, backup, export, app updater
├── DLSSVersionToolkit/                 # WPF application
│   ├── ViewModels/MainViewModel.cs      # ~2.4k lines; Update All orchestration lives here
│   ├── Views/                           # SettingsDialog, BackupsDialog,
│   │                                    #   UpdateAllPreflightDialog
│   ├── Converters/
│   ├── MainWindow.xaml                  # sidebar-dashboard UI
│   └── App.xaml                         # theme, styles, startup
├── DLSSVersion/                         # DEAD PowerShell module (see above)
└── DLSSVersionToolkit.sln               # 3 projects: Core, app, Tests

tests/
└── DLSSVersionToolkit.Tests/            # xUnit, 19 files, 371 tests at v0.0.59
```

The single-file `DLSSVersionToolkit.exe` (3,990,041 bytes at v0.0.55) is produced by CI on each
`v*` tag and attached to the GitHub release — it is not committed.

## Commands

**Windows only.** The app targets `net9.0-windows` with `UseWPF`, so it cannot be built or tested
on macOS or Linux — the `windows-latest` GitHub Actions runners are the only build/test path.
`ci.yml` runs build + tests on every push and PR; `release.yml` publishes the exe on a `v*` tag.

```bash
# Build
dotnet build src/DLSSVersionToolkit.sln

# Publish single-file exe
dotnet publish src/DLSSVersionToolkit/DLSSVersionToolkit.csproj --configuration Release --self-contained false

# Run tests
dotnet test
```

CA1416 warnings ("only supported on: windows") from `DllVersionReader` call sites are expected and
benign — the app is win-x64 only.

Version bumps touch three lines in `src/DLSSVersionToolkit/DLSSVersionToolkit.csproj`:
`AssemblyVersion`, `FileVersion`, `Version`.

**Because CI is the only compiler, a new source-scanning gate can and should be red-armed before
spending a CI round-trip:** port its predicate to Python, run it against
`git archive <previous-tag> src`, and assert the hit count matches the number of known offenders.

## Code Style

- **C# / .NET 9**: CommunityToolkit.Mvvm `[ObservableProperty]` / `[RelayCommand]` source
  generators.
- **WPF**: NVIDIA-leaning sidebar dashboard, true-black canvas (#000000) with layered panel
  surfaces (#0E0E0E/#171717), #76B900 green strictly as a signal accent, Inter/Segoe UI font,
  Consolas for paths/versions. New theme tokens live alongside the original brushes in App.xaml
  (additive — never remove old keys).
- **Dialogs**: `SizeToContent` + `CanResize` with minimums. Never a fixed pixel size — that was the
  v0.0.48 DPI bug.
- **Tests**: separate assembly, no `InternalsVisibleTo`, so helpers under test must be `public`.
  Test files are not one-class-per-file — `search_files "class <Name>"` before adding a test class
  or you get a CS0101 collision.

## Standing lessons

Read these before touching version logic, path logic, or any report.

**DLL bytes are the only version authority.** Six separate bugs came from deriving a version or
status from a stale non-DLL source (a sidecar config, a folder name, a session field, a
source-folder scan). Use `FileVersionInfo`. Corollary: when a fix changes which source wins, audit
every other consumer of those sources in the same pass.

**A resolver serving both readers and writers will hand a writer a path it cannot write** (v0.0.53).
`NgxPathResolver.GetCandidatePaths` is ordered `explicit → registry → defaults`, and the driver's
registry NGX path is the DriverStore — correct for reading (the scanner probes and moves on), fatal
for writing (TrustedInstaller denies Administrators by design). Writes go through
`GetWritableBase`, which filters to the `WriteRoots` allowlist. A user-configured path is honored
only if it is itself a write root: settings is not an escape hatch out of an allowlist.

**Access denied inside a user-owned root is not an elevation problem.** Every NGX write lands in
`%ProgramData%\NVIDIA\NGX` or the AppData equivalent, which the user owns. Advising "run as
Administrator" there sends people to do something that cannot work; the real cause is a game
holding the DLL open. The whitelist/unlock paths write NVIDIA App data and *do* need elevation —
that advice is correct there and only there.

**One rule, one predicate.** Fix the set, not the copy in front of you. `%ProgramData%\NVIDIA\NGX`
had been rebuilt inline seven times before v0.0.53; "did the import land?" was asked four different
ways before v0.0.55. Both are now single definitions with source gates
(`NgxRootLiteral_IsDefinedOnlyInTheResolver`, `ImportLandedPredicate_IsNotRebuiltByCallers`) that
fail if a caller rebuilds them.

**A guard whose predicate is narrower than the event it guards has a hole shaped by the
difference.** The v0.0.55 manifest gate asked `BinFilesWritten > 0` while the guarded event was "a
file was written" — which could also happen in the override tree, producing a written file with no
manifest record and a silent overwrite on the next Update All. Also grep new guards for a
comparison whose two operands are the same expression: `IsPathWithin(ngxBase, ngxBase)` shipped and
was structurally incapable of firing.

**Reconcile every report against itself.** A summary that prints an aggregate *and* a breakdown
needs something asserting `sum(parts) == aggregate`. v0.0.55's import dialog reported 11 files over
a breakdown summing to 8 for two releases: the total was right, the breakdown printed one of two
counters, disk state was fine and CI was green. A green outcome is not evidence the reporting is
sound.

**Non-fatal steps must still be reported.** A `Debug.WriteLine`-only failure is compiled out of
Release and hid a broken Streamline sync for five releases. Partial success is not success: name
what was skipped (`UpdateRunReport` / the run-report drawer is the diagnostic escape hatch, and it
is what makes deleting a manual button safe).

**When you delete a command, grep every flag it SET.** Removing "Apply to all games" in v0.0.54
orphaned `IsApplyingPreset`, whose only writer it was — four dropdowns and a progress bar had been
silently dead, so Update All ran driver writes with every control live. The compiler says nothing.

**Match a known-working reference script's mechanics first, optimize after.** Every deviation from
JPersson77's whitelist script and from nvidiaDlssGlom's observed layout has been a suspect. The NGX
arch prefix (160) and generic app ids (E658703/E658700) are *inferred* from tool output plus
emoose/DLSSTweaks#137, not published by NVIDIA — which is why every write is backed up first.

**A detector and its applier must call one shared function**, or they drift into a false "already
applied."

## Recent Changes

> Per-release detail lives in `git log` and the GitHub releases. Only transferable rationale is
> kept here; superseded implementation notes are deleted rather than annotated.

- **v0.0.59**: Cosmetic cleanup closing the two deferred audit findings — all 70 native
  `MessageBox.Show` call sites routed through a themed in-app dialog (`Views.ThemedMessageBox`,
  same dark palette + button styles; the old boxes rendered bright white OS chrome mid-flow), and
  disabled-state opacity raised off the legibility floor (nav 0.45→0.65 = 2.51→3.96:1 effective;
  primary 0.4→0.55; dark buttons 0.5→0.6). Gates pin both.
- **v0.0.58**: UI accessibility/polish — visible keyboard focus on every button style
  (shared `VisibleFocusStyle`; the custom templates had silently removed the default focus cue
  app-wide), `Text3` raised #757575→#8A8A8A (3.89:1→5.19:1 on Panel2), dialogs size by content
  (the last two fixed-pixel windows from the v0.0.48 class), status dots and the override cell
  got accessible names, backups errors state the next action, dark Expander template,
  already-installed states shown as text instead of opacity dimming.
- **v0.0.56**: AGENTS.md rewritten at real truth (the false machine-derived claim was removed —
  no generator ever existed); its checkable claims now gated by `AgentsDocClaimsTests`. 348 tests.
- **v0.0.57**: Sibling sweep — every standing lesson mechanically re-swept for recurrences.
  Version comparison collapsed onto the shared comparer (a private copy in UpgradeService had
  missed the pad-to-4 fix); same-version resync guard widened from 1 DLL to all four; AnWave's
  override-config version now read back from the copied DLL bytes (URL/tag demoted to fallback);
  post-copy verify failures counted into results instead of Debug-only; Streamline download got
  its twin's integrity gate; scan errors surface in `ScanResult.Errors`; preset skips reported;
  root-literal gate hardened to per-line verdicts.
- **v0.0.54**: Sidebar information architecture — 16 visible nav items → 10 + 4 collapsed. Deleted
  `Sync NGX from DLSS`, `Sync NGX from AnWave`, `Apply to all games` (all pure subsets of Update
  All). Update All gained a pre-flight dialog that confirms, offers local-DLL import with a folder
  picker, and checks the folder for `nvngx_*.dll` *before* the run — a warning after minutes of
  work is not a warning. Import Local DLLs promoted and amber: the one action Update All cannot do
  unattended.
- **v0.0.53**: Never write NGX models into the driver store. Read/write path split in
  `NgxPathResolver`; seven duplicated root literals collapsed to one; tautological guard removed.
- **v0.0.52**: Override manifest primitive — a local DLL import is a *claim* that a file outranks
  the download channel, and nothing recorded it. Plus supersede check, version-gated presets, reset.
  313 tests.
- **v0.0.51**: Decode packed NGX version folders (`20318080` = 310.7.128) and import loose local
  DLLs — the only path that can apply a DLL with no published release asset.
- **v0.0.50**: Dashboard truth pass ("told the truth about nothing").
- **v0.0.36 → v0.0.49**: Comprehensive Update All (Streamline + DLSS SDK + NGX + AnWave in one
  run); NGX versions read from DLL `FileVersionInfo`; `ResolveBinPath` fixed `bin\x64` doubling
  that made Streamline sync copy zero files for five releases; `ProfileIndexStore` fast path skips
  the ~8,000-profile NvAPI scan; DLSS-FG mode + multiplier (2x–6x) with persisted presets;
  whitelist rewritten to match JPersson77's reference script; `IsOpsSupported` unlock; sidebar
  264px and window sizing clamped to the work area by `MainWindow.FitToWorkArea` (v0.0.48).
- **v0.0.35.3**: SHA256 integrity gate on the auto-updater — `release.yml` publishes
  `DLSSVersionToolkit.exe.sha256`, and `DownloadAndApplyAsync` refuses to execute on mismatch.
  Full RCE-from-compromised-release still needs code signing (deferred). `RollForward=LatestMajor`
  so the app runs on .NET 10+.
- **v0.0.31**: App auto-updater — in-place exe rename-swap with rollback and a `--wait-for-pid`
  restart handshake, gated by the `CheckForAppUpdates` setting.
- **v0.0.30**: Relicensed MIT → Apache-2.0; `NOTICE` attributes scubamount.
- **v0.0.26 → v0.0.25**: The real preset fix — apply across BaseProfile *and* every game profile,
  and set the SR/RR/FG override ENABLE flag ("Custom"). Preset selection alone is a no-op without
  the flag.
- **002-dlss-gui**: Complete WPF GUI rewrite; supersedes the `001-dlss-version-checker` PowerShell
  module.

<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->
