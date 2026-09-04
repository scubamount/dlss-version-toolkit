# DLSS Version Toolkit

<p align="center">
  <img src="docs/main-window.png" alt="DLSS Version Toolkit — sidebar dashboard" width="900" />
</p>

<p align="center">
  <a href="https://github.com/scubamount/dlss-version-toolkit/releases/latest"><img src="https://img.shields.io/github/v/release/scubamount/dlss-version-toolkit?color=76b900&label=latest" alt="Latest Release"></a>
  <a href="https://github.com/scubamount/dlss-version-toolkit/releases"><img src="https://img.shields.io/github/downloads/scubamount/dlss-version-toolkit/total?color=green" alt="Downloads"></a>
  <a href="https://github.com/scubamount/dlss-version-toolkit/stargazers"><img src="https://img.shields.io/github/stars/scubamount/dlss-version-toolkit?style=social" alt="Stars"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-Apache--2.0-blue" alt="License"></a>
  <img src="https://img.shields.io/badge/platform-Windows%2010%2F11-0078D6" alt="Platform">
  <img src="https://img.shields.io/badge/.NET-9.0-512BD4" alt=".NET 9">
</p>

<p align="center">
  <a href="#tldr">TL;DR</a> ·
  <a href="#install">Install</a> ·
  <a href="#what-it-does">What It Does</a> ·
  <a href="#how-it-works">How It Works</a> ·
  <a href="#troubleshooting">Troubleshooting</a> ·
  <a href="#security">Security</a>
</p>

---

## TL;DR

**DLSS Version Toolkit** is a Windows app that keeps your NVIDIA DLSS DLLs up to date and
forces the render preset you want — across every game — in one click.

> **Pick your presets → click Update All → restart your game.** The toolkit scans every place DLSS
> lives on your system — NGX Release, the driver's OTA/staged versions, Streamline, and AnWave —
> finds the newest build of each DLL (Super Resolution, Frame Generation, Ray Reconstruction,
> DeepDVC, DLSSNR), downloads the latest official Streamline and DLSS SDKs, syncs those DLLs into NGX
> Release (and mirrors them to AnWave), whitelists the NVIDIA App so it stops reverting your
> choice, unlocks games it marks "not supported", and applies your presets to every game profile.
> It also updates itself.

Single-file `.exe`. No installer. Your DLLs, your machine.

---

## Install

### Run the .exe (recommended)

Download **`DLSSVersionToolkit.exe`** from the
[latest release](https://github.com/scubamount/dlss-version-toolkit/releases/latest) and run it.
No installer — it's a single-file executable. Requires the
[.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)
(Windows shows a "Download it now" dialog if missing — or install via
`winget install Microsoft.DotNet.DesktopRuntime.9`).

The app updates itself: when a newer release ships, an **update-available** pill appears
in the header — click it to download, swap, and restart in place. Toggle off in Settings.

### Build from source

```powershell
# Requires the .NET 9 SDK
git clone https://github.com/scubamount/dlss-version-toolkit.git
cd dlss-version-toolkit
dotnet build src/DLSSVersionToolkit.sln --configuration Release
dotnet publish src/DLSSVersionToolkit/DLSSVersionToolkit.csproj --configuration Release --self-contained false --runtime win-x64
```

**Requirements:** Windows 10 (1903+) or 11 · NVIDIA GPU with DLSS support · NVIDIA App with
DLSS override enabled.

---

## What It Does

| Action | What happens |
|---|---|
| **Update All** | The one button, with a pre-flight dialog that confirms the run and can import local DLLs before anything starts. Then: whitelist → unlock "not supported" games → apply presets to all games → download Streamline + DLSS SDK → sync to NGX Release → import/re-assert local overrides → set up or update AnWave. A run-report drawer names every step's outcome, including what was skipped and why |
| **Override Presets** | Separate **DLSS-SR**, **DLSS-RR** and **DLSS-FG** preset pickers (Default, A–M, or Latest) applied to the base profile **and every game profile** |
| **Frame Generation control** | DLSS-FG **mode** (Off / Fixed / Auto / Dynamic) and **multiplier** (2x–6x) — your selections persist between launches |
| **Import Local DLLs** | Have a DLL NVIDIA doesn't publish (a leak, a modded build)? Import loose `nvngx_*.dll` files directly. The toolkit records them in an override manifest so Update All preserves them instead of silently overwriting — and re-applies them after every sync unless an official release supersedes them |
| **Unlock Unsupported Games** | For titles the NVIDIA App marks "not supported" (e.g. Star Citizen) — makes the DLSS override options appear. Included in Update All |
| **Auto-scan on launch** | Opens straight to your installed versions, newest-build highlight, and live whitelist / AnWave status — no clicking required |
| **Index Game Profiles** | Caches which driver profiles belong to installed games, so applying presets skips the ~8,000-profile scan. Auto-refreshes when the driver changes |
| **App auto-update** | Checks this repo on launch and offers a one-click in-place update |
| **AnWave auto-setup** | Downloads + installs nvidiaDlssGlom, fetches the latest DLSS DLLs, activates the global override |
| **NGX Backups** | Every sync backs up the current NGX DLLs first; the Backups dialog restores any of them with its own safety backup |
| **Export** | Save a snapshot of your DLSS setup as CSV or JSON |
| **Advanced (manual)** | Every step Update All runs is also available on its own for recovery: whitelist, unlock, downloads, NGX syncs, profile indexing |

### How versions are reported

Every version the app shows — the header, each INSTALLED VERSIONS cell, the completion dialog,
the run report — is read from that specific file's own PE version resource. Nothing is derived
from a folder name, a sidecar config, or the version an update *intended* to install. The
`nvngx_package_config.txt` the driver writes is treated as activation state only; it goes stale
the moment a DLL is swapped, which is what used to put mismatched versions in the grid.

Three status codes, and they mean different things:

| Cell shows | Meaning |
|---|---|
| `310.7.128.0` | The file is present and this is its version |
| `Unknown` | The file **is** there but its version could not be read — corrupt, or held open by a running game |
| `—` | No such file in this tree. Nothing is wrong; that component just isn't installed here |
| `N/A` | Not applicable to this row (e.g. Streamline on an NGX row, which never contains `sl.common.dll`) |

**LATEST AVAILABLE** compares against NVIDIA's feeds, and labels which one it used:

- **GitHub** — NVIDIA's published SDK releases (`NVIDIA/DLSS`, `NVIDIA-RTX/Streamline`). This is
  what a developer builds against.
- **OTA** — NVIDIA's NGX production update channel, the one the driver itself pulls from. It
  usually runs ahead of GitHub (for example DLSS 310.7.128 via OTA while GitHub's newest release
  was 310.7.0), which is why a version here can legitimately be higher than the latest GitHub tag.
- **OTA pre-release** — NVIDIA's staging channel, off by default. It runs ahead of production
  again (310.9.0 / Streamline 2.14.0 while production served 310.7.128 / 2.12.128).

Enable **Settings → Include pre-release (staging) channel** to see the staging builds. They are
real, published NVIDIA builds, but the driver does not hand them to a game on its own, so they are
labelled `OTA pre-release` wherever they appear. A staging version is only ever shown when it is
strictly newer than every other feed — if production catches up to the same number, production
wins and the pre-release label disappears.

If the OTA endpoint is unreachable, the GitHub answer stands and nothing breaks.

### Where downloads come from

By default the toolkit downloads only from NVIDIA's GitHub releases. **Settings → Allow OTA
payload downloads** additionally permits fetching component payloads from NVIDIA's NGX CDN — the
same files the driver's own updater pulls, and the only way to install a build that GitHub has
not published yet.

This is opt-in because it fetches NVIDIA-copyrighted binaries from an endpoint NVIDIA does not
document. Nothing downloaded this way reaches your NGX folder unverified:

1. HTTPS only.
2. The digest published alongside the file must match the bytes received. **No published digest
   means no install** — a missing checksum is treated as a failure, not as a check to skip.
3. The payload must actually be a PE image, which catches a CDN error page returned as HTTP 200.
4. Its Authenticode signature must be valid and the signer must be NVIDIA.
5. Only then is it moved into place, so a failed check can never leave a partial DLL behind.

Any failure falls back to the GitHub path. Note that redistribution terms for NVIDIA's DLSS
binaries are NVIDIA's to set — this feature retrieves what your driver would fetch anyway, but if
you are shipping something built on it, read the SDK license first.

**Supported components:** DLSS, Frame Generation (dlssg), DLSSD (Ray Reconstruction), DeepDVC, DLSSNR, Streamline SDK (incl. global Streamline-plugin override — `sl.common`, `sl.interposer`, `sl.dlss_g`, and friends — same mechanism as the DLSS override).

> **DLSSNR (DLSS 5 neural rendering) — what this tool does with it:** it scans, displays, syncs,
> backs up, and **imports** `nvngx_dlssnr.dll` like any other NGX component. No download channel
> ships it yet (as of 2026-08 it comes out of game builds), so the workflow is: extract
> `nvngx_dlssnr.dll` from the game, drop it in your import folder, run **Import Local DLLs** — it
> lands in the NGX model tree, gets a manifest record, survives Update All (NR imports are never
> marked superseded, since no download channel can supersede them), and AnWave mirrors it when
> present. What the tool does NOT do is write a driver NR-override config section — the driver's
> DLSS-NR override entries (present in 610.xx drivers, and NVIDIA's own profile entries name a
> "DLSS-NR Streamline Override") are not yet functional, so loading NR still depends on what the
> game build itself does or a per-game placement.

---

## How It Works

NVIDIA DLSS DLLs live in several places. The toolkit scans, compares, and syncs all of them:

| Source | What it is | Managed by |
|---|---|---|
| **NGX Release** | The active DLSS override games actually load | NVIDIA App |
| **NGX Staging** | Driver-staged DLSS versions | NVIDIA drivers |
| **AnWave** | Global DLL injection override | Auto-installed by this tool ([SimonMacer/AnWave](https://github.com/SimonMacer/AnWave)) |
| **Streamline SDK** | NVIDIA's SDK with the latest DLLs | Auto-downloaded ([NVIDIA-RTX/Streamline](https://github.com/NVIDIA-RTX/Streamline)) |
| **DLSS SDK** | Official `ngx_dlss_demo_windows.zip` | Auto-downloaded ([NVIDIA/DLSS](https://github.com/NVIDIA/DLSS)) |
| **Local imports** | DLLs you imported yourself, tracked in an override manifest with SHA-256 verification | This tool |

### The dashboard

- **Current NGX** → **Latest available** version strip, with an up-to-date / update-available pill.
- **Installed Versions** table — green highlights the newest build of each component across all sources; the 🔒 Override column marks locally-imported DLLs.
- **Sidebar status card** — live dots for scan, AnWave (installed + version), and whitelist (applied / not).

### Update All, step by step

1. **Pre-flight dialog** — confirms the run, checks network / ≥500 MB disk / writable target, and optionally lets you pick a folder of local DLLs to import as part of the same run
2. **Whitelist** — removes the NVIDIA App override restrictions that otherwise revert your choice
3. **Unlock** — flips `IsOpsSupported` on games the NVIDIA App reports as "not supported" (backs up `ApplicationStorage.json` first)
4. **Preset sweep** — applies your SR / RR / FG presets, FG mode and multiplier to the base profile + every game profile; profiles the driver refuses are counted and reported
5. **Streamline** — downloads the Streamline SDK (size-verified) and syncs it to NGX first: it is the comprehensive source, carrying Frame Generation, Ray Reconstruction and DeepDVC DLLs
6. **DLSS SDK** — downloads the latest official SDK from NVIDIA/DLSS (skipped if cached) and lays its newer Super Resolution DLL on top
7. **Sync** — copies to NGX Release with a verified backup + automatic rollback; if the version is unchanged but any canonical component DLL that the source actually provides is missing, it is recreated
8. **Local overrides** — imports from the pre-flight selection, then re-asserts previously-imported DLLs unless the channel has shipped something newer
9. **AnWave** — installs it if missing, then applies the updated DLLs

Every step is non-fatal: failures and skipped items land in the completion summary and the
run-report drawer rather than aborting the run, so one unavailable source can't block the rest.

> After applying, **fully restart your game** (not just to the menu). The on-screen DLSS
> indicator overlay appears in the **bottom-left** corner of supported games.

---

## Security

Defense-in-depth for every file operation:

- **Path allowlisting** — DLL syncs only write under `C:\ProgramData\NVIDIA\NGX` and `%APPDATA%\NVIDIA\NGX`; a user-configured path is honored only if it is itself inside the allowlist
- **Backups before NVIDIA App edits** — the whitelist and unlock steps write a `.bak` of
  `ApplicationStorage.json` before modifying it, and only touch an explicit list of known keys
- **Pre-flight checks** — network, disk space, and write access verified before anything starts
- **PE header verification** — DLLs checked for valid MZ/PE signatures before copy
- **Post-copy validation** — file sizes verified after copy to catch truncated binaries; a copy that fails verification is deleted and reported, never counted as applied
- **Local import integrity** — imported DLLs recorded with SHA-256 hashes; the manifest re-verifies the bytes on disk, so out-of-app edits are detected
- **Backup + rollback** — backups validated before any change; restored automatically on failure
- **Long path support** — paths over 240 chars handled via the `\\?\` prefix
- **SharpCompress 0.48.0** — patched against CVE-2026-44788 (directory traversal in `WriteToDirectory`)

The in-app auto-updater is opt-out, never silent: it downloads a size-verified exe whose
published `.sha256` checksum must match, swaps it in place with rollback on failure, and prompts
before restarting.

---

## Troubleshooting

| Symptom | Fix |
|---|---|
| **App won't launch** | Install the [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0) — or `winget install Microsoft.DotNet.DesktopRuntime.9` |
| **"Administrator access is required to restart NVIDIA services"** | Run as Administrator — this step edits NVIDIA App data and restarts its services, which needs elevation |
| **Access denied during an NGX sync or local import** | Close any running game — a running game holds its NGX DLLs open. Sync writes go to user-writable folders (`C:\ProgramData\NVIDIA\NGX\`), so elevation normally cannot help here |
| **"No DLSS versions found"** | Install the NVIDIA App and enable DLSS override for at least one game (this creates the NGX folders), then rescan |
| **DeepDVC shows "Unknown"** | Normal — some driver builds omit DeepDVC from the NGX config; handled gracefully |
| **"AnWave not installed"** | Click **Update All** (it auto-installs), or **Setup AnWave** in Advanced |
| **"Streamline SDK not found"** | Auto-downloaded when needed; or drop a manual SDK in Downloads / set its path in Settings |
| **NVIDIA App says a game is "not supported"** | Run **Update All** (or **Unlock Unsupported Games**) as Administrator, then reopen the NVIDIA App. If it still doesn't appear, NVIDIA gates that title server-side and no local change will fix it |
| **Overrides revert after an NVIDIA App update** | The App can rewrite its own config when its game library changes — re-run **Update All** |
| **Changes don't show in-game** | Fully restart the game; the DLL version and preset both need a clean game launch |
| **A scan shows fewer versions than expected** | Check the scan status line — access-denied folders are now named there instead of failing silently |

---

## Project Structure

```
dlss-version-toolkit/
├── src/
│   ├── DLSSVersionToolkit.Core/      # Core logic (no WPF): scanners, services, models
│   │   ├── Models/                   # DLSSVersionEntry, ScanResult, AppUpdateInfo, …
│   │   └── Services/                 # NgxScanner, DlssDownloadService, AnWaveAutoService,
│   │                                 # LocalDllImportService, OverrideManifestService,
│   │                                 # WhitelistService, PresetOverrideService, NgxPathResolver, …
│   ├── DLSSVersionToolkit/           # WPF app
│   │   ├── ViewModels/               # MainViewModel (CommunityToolkit.Mvvm)
│   │   ├── Views/                    # SettingsDialog, BackupsDialog, UpdateAllPreflightDialog
│   │   ├── MainWindow.xaml           # Sidebar-dashboard UI
│   │   └── App.xaml                  # Theme, styles, startup
│   └── DLSSVersionToolkit.sln
├── tests/DLSSVersionToolkit.Tests/   # xUnit tests (368) — including gates that pin doc claims,
│                                     # bug-class regressions, and UI accessibility in CI
└── .github/workflows/                # ci.yml (build+test on push/PR) · release.yml (tag → exe)
```

The single-file `DLSSVersionToolkit.exe` (~3.99 MB, framework-dependent) is built and tested by CI on every
`v*` tag and attached to the GitHub release — it is **not** committed to the repo.

**Built with:** .NET 9 + WPF · CommunityToolkit.Mvvm · Hardcodet.NotifyIcon.Wpf ·
NvAPIWrapper.Net (DRS preset overrides) · SharpCompress.

---

## Star History

<a href="https://star-history.com/#scubamount/dlss-version-toolkit&Date">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/chart?repos=scubamount/dlss-version-toolkit&type=Date&theme=dark" />
    <source media="(prefers-color-scheme: light)" srcset="https://api.star-history.com/chart?repos=scubamount/dlss-version-toolkit&type=Date" />
    <img alt="Star History Chart" src="https://api.star-history.com/chart?repos=scubamount/dlss-version-toolkit&type=Date" width="100%" />
  </picture>
</a>

---

## License

[Apache License 2.0](LICENSE) — see the [NOTICE](NOTICE) file for attribution requirements.

Built and maintained by scubamount.
