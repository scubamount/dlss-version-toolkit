# DLSS Version Toolkit

> A Windows GUI for checking, upgrading, and syncing NVIDIA DLSS versions across all sources. Built by Scubamount.

![DLSS Version Toolkit](docs/main-window.png)

## Overview

NVIDIA DLSS components live in multiple locations on your system. Two are managed by the NVIDIA App and drivers, and two are **optional** — they only exist if you download them separately:

| Source | Description | Auto-installed? |
|--------|-------------|-----------------|
| **NGX Release** | Active DLSS override used by games | Yes — by NVIDIA App |
| **NGX Staging** | Driver-staged DLSS versions | Yes — by NVIDIA drivers |
| **AnWave / dlssglom** | Global DLL injection override | No — [download from GitHub](https://github.com/cybertron010/dlssglom) |
| **Streamline SDK** | NVIDIA's SDK with the latest DLLs | No — [download from NVIDIA Developer](https://developer.nvidia.com/streamline-sdk) |

This tool scans all available sources, shows you which version is installed where, highlights the newest versions per component, and lets you upgrade or sync to the latest with a single click.

**Supported components:** DLSS, Frame Generation (dlssg), DLSSD, DeepDVC, Streamline SDK

## Features

- **Visual dashboard** — see all DLSS versions at a glance in a clean dark-themed table
- **One-click upgrade** — promote NGX Staging to NGX Release with automatic backup
- **Sync from any source** — pull newer DLLs from Streamline SDK or AnWave into NGX Release
- **Download latest DLSS** — fetch the newest official DLSS SDK directly from NVIDIA's GitHub
- **Export** — save version reports as CSV or JSON
- **System tray** — minimize to tray and receive notifications when new versions are detected
- **Background scanning** — optionally check for updates every 4 hours automatically
- **Hardened operations** — PE header verification, post-copy file validation, automatic rollback on failure, path allowlisting
- **Single instance** — only one copy of the app runs at a time

## Screenshots

### Main Dashboard
![Main Window](docs/main-window.png)

### Settings
![Settings Dialog](docs/settings-dialog.png)

## Requirements

- Windows 10 version 1908+ or Windows 11
- .NET 9 Runtime (framework-dependent build — [download .NET 9](https://dotnet.microsoft.com/download/dotnet/9.0))
- NVIDIA GPU with DLSS support
- NVIDIA App with DLSS override enabled

## Installation

### Option 1: Run the .exe

```powershell
git clone https://github.com/scubamount/dlss-version-toolkit.git
cd dlss-version-toolkit
.\src\DLSSVersionToolkit\bin\Release\net9.0-windows\win-x64\publish\DLSSVersionToolkit.exe
```

### Option 2: Build from Source

```powershell
# Requires .NET 9 SDK
git clone https://github.com/scubamount/dlss-version-toolkit.git
cd dlss-version-toolkit

# Build
dotnet build src/DLSSVersionToolkit.sln --configuration Release

# Run
dotnet run --project src/DLSSVersionToolkit/DLSSVersionToolkit.csproj --configuration Release

# Publish single-file exe
dotnet publish src/DLSSVersionToolkit/DLSSVersionToolkit.csproj --configuration Release --self-contained false
```

## Usage

### First Launch

On first launch, all paths are empty — the app auto-detects NGX, AnWave, and Streamline SDK locations automatically. You only need to set custom paths if your installations are in non-standard locations.

### Scanning

Click **Scan Now** to check all available DLSS sources. The dashboard shows:

- **Source** — which DLSS installation the version came from
- **Build ID** — internal NVIDIA build number
- **DLSS** — DLSS Super Resolution version
- **Frame Gen** — Frame Generation version
- **DLSSD** — DLSS Depth version
- **DeepDVC** — Deep DVC version
- **Streamline** — Streamline SDK version

Green text indicates the newest version for that component across all sources.

### Upgrading

1. Click **Upgrade Release** — this promotes the latest NGX Staging version to NGX Release
2. The app creates a timestamped backup (`.dlss-backup-YYYYMMDD-HHMMSS`) before any changes
3. Confirm the upgrade — the app copies the DLSS DLLs and config from Staging to Release
4. The dashboard refreshes automatically

> **Note:** Administrator privileges are required for upgrade and sync operations. The app will prompt you if you're not running as admin.

### Syncing from Streamline SDK or AnWave

If you've downloaded the Streamline SDK or AnWave and want to sync its newer DLLs into NGX Release:

1. Click **Sync From...** in the toolbar
2. Choose **Streamline SDK** or **AnWave**
3. Confirm — a backup is created first, then the newer DLLs are copied

### Downloading the Latest DLSS

Click **Download DLSS** to fetch the newest official DLSS SDK from NVIDIA's GitHub releases. The download is cached locally, so re-clicking skips the download if it's already present.

### Export

Click **Export** to save a snapshot of your current DLSS setup as:
- **CSV** — spreadsheet-friendly format
- **JSON** — full data with metadata

### Settings

Click **Settings** to configure:
- **NGX Base Path** — where NGX is installed (default: `C:\ProgramData\NVIDIA\NGX`)
- **AnWave Path** — path to AnWave/dlssglom (leave empty for auto-detect)
- **Streamline SDK Path** — path to Streamline SDK (leave empty for auto-detect)
- **Periodic background scans** — enable auto-scan every 4 hours
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
- **PE header verification** — DLLs are checked for valid MZ/PE signatures before being copied
- **Post-copy validation** — file sizes are verified after copy to catch truncated or mismatched binaries
- **Automatic rollback** — if any operation fails, the backup is restored automatically
- **Backup isolation** — backups are stored in the same volume as the target, ensuring restoration is always possible
- **Long path support** — paths exceeding 240 characters are handled via the `\\?\` prefix

## Troubleshooting

### "Administrator access is required"

Run the app as Administrator. Upgrade and sync operations write to `C:\ProgramData\NVIDIA\NGX\`, which requires elevated permissions.

### "No DLSS versions found"

Ensure the NVIDIA App is installed and DLSS override is enabled. The NGX folder structure is created when the DLSS override feature is first used.

### DeepDVC Shows "Unknown"

This is normal — some NVIDIA driver builds don't include DeepDVC in the NGX config file. The toolkit handles this gracefully.

### "Streamline SDK / AnWave not found"

These are not installed by the NVIDIA driver — you must download them separately:
- AnWave/dlssglom: [github.com/cybertron010/dlssglom](https://github.com/cybertron010/dlssglom)
- Streamline SDK: [developer.nvidia.com/streamline-sdk](https://developer.nvidia.com/streamline-sdk)

Place the extracted folders in your Downloads directory for auto-detection, or specify their paths in Settings.

### Download fails

Ensure you have an active internet connection. The app uses the GitHub API to check for releases. If rate-limited, try again later.

## Project Structure

```
dlss-version-toolkit/
├── src/
│   ├── DLSSVersionToolkit.Core/          # Core logic (no WPF dependency)
│   │   ├── Models/                         # Data models
│   │   └── Services/                       # Scanning, upgrade, backup, download
│   ├── DLSSVersionToolkit/                 # WPF application
│   │   ├── ViewModels/                    # MVVM view models
│   │   ├── Views/                         # XAML views
│   │   ├── Converters/                    # UI value converters
│   │   └── App.xaml                       # Theme, styles, startup
│   └── DLSSVersionToolkit.sln
├── tests/
│   └── DLSSVersionToolkit.Tests/          # Unit tests
├── docs/                                   # Screenshots for documentation
├── specs/                                  # Feature specs and plans
└── README.md
```

## Technology

- **.NET 9 + WPF** — framework-dependent single-file deployment
- **CommunityToolkit.Mvvm** — source-generated observable properties and commands
- **Hardcodet.NotifyIcon.Wpf** — system tray integration
- **xUnit** — unit testing

## License

MIT