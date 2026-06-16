# DLSS Version Toolkit

> A Windows GUI for checking, upgrading, and syncing NVIDIA DLSS versions across all sources — built and maintained by scubamount.

![DLSS Version Toolkit](docs/main-window.png)

## Overview

NVIDIA DLSS components live in multiple locations on your system. Two are managed by the NVIDIA App and drivers, one is installed automatically by this tool, and one is optional:

| Source | Description | Auto-installed? |
|--------|-------------|-----------------|
| **NGX Release** | Active DLSS override used by games | Yes — by NVIDIA App |
| **NGX Staging** | Driver-staged DLSS versions | Yes — by NVIDIA drivers |
| **AnWave** | Global DLL injection override | Yes — auto-installed by this tool from [SimonMacer/AnWave](https://github.com/SimonMacer/AnWave) |
| **Streamline SDK** | NVIDIA's SDK with the latest DLLs | Yes — auto-downloaded from NVIDIA-RTX/Streamline on GitHub |

This tool also downloads the official **DLSS SDK** (`ngx_dlss_demo_windows.zip`) from [NVIDIA/DLSS](https://github.com/NVIDIA/DLSS) on GitHub automatically — no manual download needed.

**Supported components:** DLSS, Frame Generation (dlssg), DLSSD, DeepDVC, Streamline SDK

## Features

- **Quick guide on first launch** — a short card walks you through the whole flow: pick a preset, click Update All, restart your game
- **App auto-update** — checks this repo for a newer release on launch and offers a one-click in-place update (toggle in Settings)
- **Visual dashboard** — see all DLSS versions at a glance in a clean dark-themed table
- **Update All** — one button to apply your chosen preset to every game profile, download the latest DLSS SDK, sync to NGX Release, and auto-setup AnWave if not installed
- **Override presets** — pick a DLSS render preset (J/K/L/M) and apply it across the base profile and every game profile in one click
- **AnWave auto-setup** — integrated into Update All — downloads and installs nvidiaDlssGlom from GitHub, fetches the latest DLSS DLLs from NVIDIA, and activates the global DLSS override automatically
- **Advanced manual steps** — the individual operations Update All runs (whitelist, downloads, NGX syncs) remain available in the sidebar Advanced group for recovery and debugging
- **Export** — save version reports as CSV or JSON
- **System tray** — minimize to tray with notifications when new versions are detected
- **Background scanning** — optionally check for updates every 4 hours automatically
- **Operation hardening** — pre-flight checks (network, disk space, writable), PE signature verification, post-copy file size validation, backup verification, automatic rollback on failure, path allowlisting
- **Improved dialogs** — every operation result shows version numbers, file lists, and actionable next-step guidance

## Screenshots

### Main Dashboard
![Main Window](docs/main-window.png)

## Requirements

- Windows 10 version 1903+ or Windows 11
- .NET 9 Runtime ([download .NET 9](https://dotnet.microsoft.com/download/dotnet/9.0))
- NVIDIA GPU with DLSS support
- NVIDIA App with DLSS override enabled

## Installation

### Option 1: Run the .exe

Download `DLSSVersionToolkit.exe` from the [latest release](https://github.com/scubamount/dlss-version-toolkit/releases/latest) and run it. No installer — it's a single self-contained executable (requires the .NET 9 Runtime).

The app **checks for its own updates** on launch: when a newer release is published, an **⬆ vX.Y.Z available** pill appears in the header — click it to download and install the update in place, then restart. This can be turned off in Settings.

### Option 2: Build from Source

```powershell
# Requires .NET 9 SDK
git clone https://github.com/scubamount/dlss-version-toolkit.git
cd dlss-version-toolkit

# Build
dotnet build src/DLSSVersionToolkit.sln --configuration Release

# Publish single-file exe
dotnet publish src/DLSSVersionToolkit/DLSSVersionToolkit.csproj --configuration Release --self-contained false
```

## Usage

### First Launch

On first launch, the app scans all known DLSS sources automatically. The dashboard shows your current DLSS versions and whether an update is available.

If AnWave is not installed, the status card in the sidebar shows "not set" — click **Setup AnWave** in the sidebar *Configure* group to install it automatically.

### The Dashboard

The main window shows:
- **Current version** — the DLSS version currently active in NGX Release
- **Available version** — the latest version cached locally (from NVIDIA's GitHub)
- **Update status** — whether a newer version is available
- **AnWave status** — whether AnWave is installed and which version is active

Green text in the table indicates the newest version for that component across all sources.

### Update All (Recommended)

1. Pre-flight checks — verifies network connectivity, disk space (500 MB minimum), and target directory write access
2. Applies the whitelist — removes NVIDIA App's DLSS override restrictions
3. Applies your selected **Override Preset** to the base profile and every game profile
4. Downloads the latest DLSS SDK from NVIDIA/DLSS on GitHub (skipped if already cached)
5. Syncs the SDK DLLs to NGX Release (with verified backup and automatic rollback on failure)
6. Auto-setups AnWave if not installed — downloads nvidiaDlssGlom, fetches DLSS DLLs, activates override
7. Applies the updated DLLs to the AnWave folder (PE signature + file size verified)

Each step shows a dialog with the version applied, files copied, and what to do next. If AnWave setup fails, NGX is still updated — try Setup AnWave separately from the Advanced menu.

### Individual Operations (Advanced)

**Update All** is the recommended path — it runs the whole sequence in the right order. The individual steps it performs are also available on their own from the sidebar **Advanced** group, useful for recovery when one step fails:

- **Apply Whitelist** — remove NVIDIA App's DLSS override restrictions and restart NVIDIA services
- **Download DLSS SDK** — download the newest official DLSS SDK from NVIDIA's GitHub
- **Download Streamline** — download the latest Streamline SDK from NVIDIA-RTX/Streamline
- **Sync NGX from DLSS** — apply the cached DLSS SDK to NGX Release
- **Sync NGX from AnWave** — copy DLLs from AnWave into NGX Release
- **Export Report** — save the current version table as CSV or JSON

### Export

Click **Export** to save a snapshot of your current DLSS setup as:
- **CSV** — spreadsheet-friendly format
- **JSON** — full data with metadata

### Settings

Click **Settings** to configure:
- **NGX Base Path** — where NGX is installed (default: `C:\ProgramData\NVIDIA\NGX`)
- **AnWave Path** — path to AnWave (leave empty for auto-detect)
- **Streamline SDK Path** — path to Streamline SDK (leave empty for auto-detect)
- **Periodic background scans** — enable auto-scan every 4 hours
- **Check for app updates** — check this repo for a newer app release on launch (on by default)
- **Minimize to tray** — keep running in the background when the window is closed
- **Notifications** — show alerts when new DLSS versions are detected

### System Tray

When **Minimize to tray** is enabled:
- Closing the window hides the app to the system tray
- Right-click the tray icon for **Show Dashboard**, **Check Now**, or **Exit**
- Double-click the tray icon to restore the main window

## Security

DLSS Version Toolkit implements defense-in-depth for file operations:

- **Path allowlisting** — only `C:\ProgramData\NVIDIA\NGX` and `%APPDATA%\NVIDIA\NGX` are writable targets
- **Pre-flight checks** — network connectivity, disk space (500 MB), and directory write access verified before any operation starts
- **PE header verification** — DLLs are checked for valid MZ/PE signatures before being copied
- **Post-copy validation** — file sizes are verified after copy to catch truncated or mismatched binaries
- **Backup verification** — backups are validated (non-empty, correct file count) before any file modification proceeds
- **Automatic rollback** — if any operation fails, the verified backup is restored automatically
- **Backup isolation** — backups are stored in the same volume as the target, ensuring restoration is always possible
- **OperationGuard** — centralized verification class used across all services (network, disk, writable, PE signature, file, backup, directory creation)
- **Long path support** — paths exceeding 240 characters are handled via the `\\?\` prefix
- **SharpCompress 0.48.0** — patched against CVE-2026-44788 (directory traversal in `WriteToDirectory`)

## Troubleshooting

### "Administrator access is required"

Run the app as Administrator. Upgrade and sync operations write to `C:\ProgramData\NVIDIA\NGX\`, which requires elevated permissions.

### "No DLSS versions found"

Ensure the NVIDIA App is installed and DLSS override is enabled. The NGX folder structure is created when the DLSS override feature is first used.

### DeepDVC Shows "Unknown"

This is normal — some NVIDIA driver builds don't include DeepDVC in the NGX config file. The toolkit handles this gracefully.

### "AnWave not installed"

AnWave setup is fully integrated into **Update All** — just click Update All and the tool will automatically install AnWave if it's not already present. You can also click **Setup AnWave** in the Advanced section to install it separately. No manual download required.

### "Streamline SDK not found"

The app automatically downloads the Streamline SDK from NVIDIA-RTX/Streamline on GitHub when needed. If you have a manual Streamline SDK installation, place the extracted folder in your Downloads directory for auto-detection, or specify its path in Settings. The scan auto-detects the Streamline SDK in your Downloads folder even when the Settings path is empty.

### "AnWave/dlssglom not found or has no valid DLLs"

This warning appears in the status bar when the AnWave directory exists but contains no DLLs with valid version info. Click **Update All** to re-setup AnWave and apply the latest DLLs. If your Settings has an incorrect AnWave path, clear it and let the app auto-detect.

## Project Structure

```
dlss-version-toolkit/
├── src/
│   ├── DLSSVersionToolkit.Core/          # Core logic library (no WPF dependency)
│   │   ├── Models/                       # DLSSVersionEntry, ScanResult, AppUpdateInfo, etc.
│   │   └── Services/                     # NgxScanner, UpgradeService, DlssDownloadService,
│   │                                      # AnWaveAutoService, AppUpdateService, BackupService, etc.
│   ├── DLSSVersionToolkit/               # WPF application
│   │   ├── ViewModels/                   # MainViewModel (CommunityToolkit.Mvvm)
│   │   ├── Views/                        # SettingsDialog
│   │   ├── Converters/                   # UI value converters
│   │   ├── MainWindow.xaml               # Sidebar-dashboard UI
│   │   └── App.xaml                      # Dark theme, styles, startup
│   └── DLSSVersionToolkit.sln
├── tests/
│   └── DLSSVersionToolkit.Tests/         # xUnit tests (Core logic, version compare, guards)
├── docs/                                  # Screenshots for documentation
├── specs/                                  # Feature specs and plans
└── README.md
```

The release `DLSSVersionToolkit.exe` (~3.9 MB, single-file, framework-dependent) is built by CI on every `v*` tag and attached to the corresponding GitHub release — it is not committed to the repo.

## Technology

- **.NET 9 + WPF** — framework-dependent single-file deployment
- **CommunityToolkit.Mvvm** — source-generated observable properties and commands
- **Hardcodet.NotifyIcon.Wpf** — system tray integration
- **SharpCompress 0.48.0** — .rar extraction for nvidiaDlssGlom packages
- **System.IO.Compression.ZipFile** — built-in .zip extraction for NVIDIA DLSS SDK

## License

[Apache License 2.0](LICENSE). See the [NOTICE](NOTICE) file for attribution requirements.