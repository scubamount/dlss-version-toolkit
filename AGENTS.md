# dlss-version-toolkit Development Guidelines

Hand-maintained. Last updated: 2026-09-04 (v0.71).

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
├── DLSSVersionToolkit.Core/            # Core logic library (no WPF) — all 29 services
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
│   │                                    #   UpdateAllPreflightDialog, ThemedMessageBox
│   ├── Converters/
│   ├── MainWindow.xaml                  # sidebar-dashboard UI
│   └── App.xaml                         # theme, styles, startup
├── DLSSVersion/                         # DEAD PowerShell module (see above)
└── DLSSVersionToolkit.sln               # 3 projects: Core, app, Tests

tests/
└── DLSSVersionToolkit.Tests/            # xUnit, 25 files, 446 tests at v0.71
```

The single-file `DLSSVersionToolkit.exe` (~4 MB, framework-dependent) is produced by CI on each
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

- **v0.71**: LATEST AVAILABLE consults NVIDIA's OTA channel, not just GitHub. The header compared
  installed versions against GitHub release feeds only — but those publish the *SDK* (what you
  build against) and lag what the driver loads: GitHub's newest DLSS was 310.7.0 while NVIDIA's
  own OTA channel served 310.7.128, Streamline 2.12.0 versus 2.12.128. So "UP TO DATE" was measured
  against a number that was not the newest NVIDIA ships. New `NvidiaOtaService` reads
  `nvngx_server_config.txt` from the NGX OTA CDN and takes the newest of {OTA, GitHub, cached,
  installed}; the header now labels which feed won, because two sources that legitimately disagree
  presented as one anonymous figure reads as a bug.
  - The endpoint is **undocumented**. Every claim about it was verified by fetching (manifest
    parses, implied packed folders resolve to real payloads, published `.sha256` matched an 89 MB
    download, PE FileVersion inside matched the manifest). Therefore: every failure path is
    non-fatal and falls back to GitHub, and the service reads **version metadata only** — it
    downloads no payloads. Pulling executables from an undocumented host into `%ProgramData%` is a
    supply-chain decision that is deliberately not taken here.
  - Production channel, not `dev-models` staging (which already showed 310.9.0 / 2.14.0). A
    staging build is not an update available to the user; prompting toward one creates an update
    that the driver can never satisfy.
  - Lesson: "latest" is not one question. Ask which authority the number is supposed to come from
    before comparing against it, and say so in the UI.
- **v0.70**: DLSS-RR default is Preset F. The app held two contradictory statements about one
  question: `PresetVersionRules` said RR 310.7.128+ "needs Preset F — Preset E does not engage the
  new model" (first-party observation), while `RayReconstructionDefault` — read by fresh installs
  and by the Reset button — said E. Every RR build this toolkit installs is 310.7.128+, so the
  default was a preset that silently does nothing. Gated by asserting the default *equals the rule
  table's answer*, so the two cannot diverge again.
  - Lesson: when a codebase states a rule as data AND as a constant, the constant is the one that
    goes stale — the data was written to be read, the constant was written once.
- **v0.69**: The completion dialog reports disk, and the dashboard is fresh when it closes.
  Update All built its override summary from the pre-run manifest disposition computed at Step 2b
  — before AnWave and Streamline wrote anything — so a run could print
  `Override nvngx_dlss.dll v310.7.128.0 still applied` directly above
  `AnWave: v310.7.0.0 applied (4 files)`: two contradictory answers to "what is installed", neither
  read from the files just written. And the run's only `ScanAsync` sat *after* the modal returned,
  so the header/presets/grid stayed stale behind "All done!" until the user dismissed it and hit
  Rescan. New `AppliedVersionVerifier` reads the newest `dlss_override` version folder after all
  writes; `BuildAppliedOverrideLines` binds every dialog to that read. The refresh moved into
  `EndUpdateAllProgressAsync` — the single funnel every terminal dialog already called — so a new
  dialog cannot forget it.
  - Lesson: a report of what an operation did must be derived from the operation's *effect*, not
    its *input*. Any summary string assigned before the last write is a prediction.
- **v0.68**: Version truth — every grid cell reads its own DLL. `NgxConfigParser` had been
  deriving INSTALLED VERSIONS from `nvngx_package_config.txt` **text**, with a DLL read layered
  over only four of five components (DLSSNR had none). A component whose DLL was absent inherited
  whatever the config still claimed, which is why the grid showed phantom `310.6.0.0` cells and
  rows whose components disagreed. The config is now parsed for **activation state only**
  (`ConfigNamesComponents`); all five components read their own file through
  `DllVersionReader.ReadComponentVersion`. Status codes split into three distinct facts:
  a version (present + readable), `Unknown` (present + unreadable), `—` (absent) — previously the
  last two were one string. `DllVersionReader.IsReportedVersion` is the one predicate for "is this
  a real version rather than a status code"; consumers testing `!= "Unknown"` by literal would
  have treated `N/A` and `—` as versions. Grid column order now Source | Build ID | DLSS |
  Frame Gen | DLSSD | DLSS NR | DeepDVC | Streamline | Override.
  - Two existing tests asserted the defect (`StaleConfigKept_WhenNoDllPresent`,
    `Parse_ValidConfig_ReturnsVersions`) and were inverted. A test named for the buggy behavior is
    how a bug survives six audits: it reads as coverage.
- **v0.67**: Streamline plugin override — the glom mechanism, reverse-engineered and integrated.
  `models\sl_<plugin>_0\versions\<packed>\files\160_E658703.dll` payloads plus per-plugin
  `[sl_<plugin>_0]` / `app_E658703 = <version>` sections in nvngx_config.txt. Ground truth was
  triple-confirmed: three independent runtime observations of the written tree (AnWave #66 log,
  Crimson Desert walkthrough, MSFS crash report) AND the literal config templates + dir->DLL
  table extracted from nvidiaDlssGlom.exe v2.8.24.13's .NET string heap; the 11-plugin set is
  what Streamline SDK v2.12.0 bin/x64 actually ships. `StreamlineOverrideService` syncs every
  mapped plugin DLL present in a sync source (PerformSync, after nvngx verification; DLSS-demo
  sources no-op) and `InstalledPlugins` decodes activation state from packed folder names — DLL
  bytes remain the only version authority; the config file is state, not evidence. sl_dlss_nr_0
  and sl_sdk_0 are known to glom but have no SDK DLL yet — unmapped on purpose; when NVIDIA
  ships sl.dlss_nr.dll the map gains one line.
  - Lesson: RE of a closed tool is a doc claim — cite the observation sources AND pin the shapes
    (extension, appId, dir spelling) with gates, because a silent drift here activates nothing.
- **v0.66**: Backup retention wired + interface dead-code retired. `CleanupOldBackups` existed
  since the backups feature with ZERO callers — every sync and every restore-safety copy added a
  backup folder, nothing ever removed one (unbounded disk growth, worst case ~158MB/run once NR
  ships). Fix: retention lives IN the producer — `CreateBackup` prunes after verifying the new
  copy (newest, never its own victim), covering all callers through the one choke point; returns
  the pruned count; `DefaultKeepCount=10` is now one definition. Gates: retention behavior test
  (13→10, decoy survives) + wiring gate pinning the call and its position after verification.
  The dead-method sweep that found it also retired five zero-caller interface members
  (ApplyToAnWave, GetInstalledGlom/DllVersion, GetCachedVersions, VerifyDllIntegrity).
  - Lesson: an interface member is not a caller. When auditing liveness, the test-only call
    sites keep a member alive (TrimCache's internal call, Remove/GetOverrideVersion tests) but
    a public method whose only references are its own decl/def is dead by the retirement policy.
    Wire cleanup at the producer, not the UI that would forget it.
- **v0.65**: NR override parity. v0.63's DLSSNR migration covered services/scanners/grid/export
  but missed both MainViewModel dictionaries: re-assert's installedByDll never carried the NR row,
  and the grid's override-marker switch dropped it — so an imported NR DLL re-asserted without
  installed-version evidence and never showed the lock marker. Worse, channelByDll gave NR the
  Streamline channel version: Evaluate would mark a user's dropped-in NR import "superseded" and
  stop re-asserting it. NR has no download channel, so it is now EXCLUDED from channelByDll
  (unknown channel never supersedes) and mapped in both dictionaries. Gate C11 pins all three.
  Import Local DLLs already handles NR correctly (it iterates the canonical set; AnWave mirrors
  it; glom OTA copies nvngx_*.dll by wildcard). Driver-side NR override entries (610.xx) are
  present but non-functional — the tool does not write a speculative NR config section.
  - Lesson: a feature migration must grep EVERY consumer dict/switch of the collection it grows,
    including UI-layer ones the compiler cannot catch when keys are string literals.
- **v0.64**: Audit of v0.63 (the direct-pushed DLSSNR commit) found and fixed three real defects.
  (1) The same-version resync guard counted DLLs NO source ships: NgxDllNames grew to include
  nvngx_dlssnr.dll, no NVIDIA/Streamline release carries it, so every up-to-date install read
  "dlssnr missing" forever — Update All re-synced and created a fresh backup every run (and
  `CleanupOldBackups` has NO callers, so retention is dead code — follow-up open). Fix:
  `ShouldResyncForMissingDlls` intersects missing-target with source-present (PerformSync's own
  predicate). (2) winget/scoop manifest hashes had been wrong on EVERY release since at least
  v0.0.59 (all pinned a stale v0.0.13-era hash; v0.63 pinned a LOCAL rebuild's hash) — release.yml
  now pins both manifests itself from the artifact it built; manifests must never be hand-edited
  again. (3) "DLSSNR global override support" overclaimed: no driver config section for NR exists
  as of 2026-08 (researched — the leaked DLL is game-folder-only); README states exactly what the
  tool does with DLSSNR and gained its first gate, `ReadmeComponentList_CoversNgxDllNames`.
  - Lessons: growing a count-bearing canonical set requires auditing every CONSUMING predicate
    (missing-lists, up-to-date checks, "N of N present") — the set is data, the bug is in the
    consumption. Artifact hashes come from the artifact, never from a local rebuild.
- **v0.0.60**: Re-audit of v0.0.59's own new dialog. Keyboard parity restored (primary button
  `IsDefault` — Enter did nothing before, which native MessageBox never allowed); message body
  moved into a `ScrollViewer` (`SizeToContent=Height` + `MaxHeight` clipped long reports — same
  failure mode as the v0.0.48 fixed-size class, opposite mechanism). AGENTS.md: exe-size claim
  relaxed to stable `~3.99 MB` form (exact bytes pinned at v0.0.55 went stale on both later
  releases); structure diagram names ThemedMessageBox, and a new reverse gate (`EveryViewDialog_
  IsNamedInDiagram`) enumerates `Views/*.xaml` against the fenced diagram so a new View without a
  diagram line reddens CI at the commit that adds it.
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
