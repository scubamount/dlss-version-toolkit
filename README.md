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
  <a href="#-tldr">TL;DR</a> ·
  <a href="#-install">Install</a> ·
  <a href="#-what-it-does">What It Does</a> ·
  <a href="#-how-it-works">How It Works</a> ·
  <a href="#-troubleshooting">Troubleshooting</a> ·
  <a href="#-security">Security</a>
</p>

---

## ⚡ TL;DR

**DLSS Version Toolkit** is a Windows app that keeps your NVIDIA DLSS DLLs up to date and
forces the render preset you want — across every game — in one click.

> **Pick a preset → click Update All → restart your game.** The toolkit downloads the latest
> DLSS SDK, syncs it everywhere DLSS lives on your system, whitelists the NVIDIA App so it
> stops reverting your choice, and applies your preset to every game profile. It also updates
> itself.

Single self-contained `.exe`. No installer. Your DLLs, your machine.

---

## 🚀 Install

### Run the .exe (recommended)

Download **`DLSSVersionToolkit.exe`** from the
[latest release](https://github.com/scubamount/dlss-version-toolkit/releases/latest) and run it.
No installer — it's a single self-contained executable (needs the
[.NET 9 Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)).

The app **updates itself**: when a newer release ships, an **⬆ vX.Y.Z available** pill appears
in the header — click it to download, swap, and restart in place. Toggle off in Settings.

### Build from source

```powershell
# Requires the .NET 9 SDK
git clone https://github.com/scubamount/dlss-version-toolkit.git
cd dlss-version-toolkit
dotnet build src/DLSSVersionToolkit.sln --configuration Release
dotnet publish src/DLSSVersionToolkit/DLSSVersionToolkit.csproj --configuration Release --self-contained false
```

**Requirements:** Windows 10 (1903+) or 11 · NVIDIA GPU with DLSS support · NVIDIA App with
DLSS override enabled.

---

## 🎯 What It Does

| Action | What happens |
|---|---|
| 🟢 **Update All** | The one button: whitelist → apply preset to all games → download latest DLSS SDK → sync to NGX Release → set up AnWave |
| 🎚️ **Override Preset** | Pick a DLSS render preset (J / K / L / M) and apply it to the base profile **and every game profile** |
| 🔄 **Auto-scan on launch** | Opens straight to your installed versions, newest-build highlight, and live whitelist / AnWave status — no clicking required |
| ⬆️ **App auto-update** | Checks this repo on launch and offers a one-click in-place update |
| 🧩 **AnWave auto-setup** | Downloads + installs nvidiaDlssGlom, fetches the latest DLSS DLLs, activates the global override |
| 📤 **Export** | Save a snapshot of your DLSS setup as CSV or JSON |
| 🛟 **Advanced (manual)** | Each step Update All runs is also available on its own for recovery: whitelist, downloads, NGX syncs |

**Supported components:** DLSS, Frame Generation (dlssg), DLSSD (Ray Reconstruction), DeepDVC, Streamline SDK.

---

## 🧠 How It Works

NVIDIA DLSS DLLs live in several places. The toolkit scans, compares, and syncs all of them:

| Source | What it is | Managed by |
|---|---|---|
| **NGX Release** | The active DLSS override games actually load | NVIDIA App |
| **NGX Staging** | Driver-staged DLSS versions | NVIDIA drivers |
| **AnWave** | Global DLL injection override | Auto-installed by this tool ([SimonMacer/AnWave](https://github.com/SimonMacer/AnWave)) |
| **Streamline SDK** | NVIDIA's SDK with the latest DLLs | Auto-downloaded ([NVIDIA-RTX/Streamline](https://github.com/NVIDIA-RTX/Streamline)) |
| **DLSS SDK** | Official `ngx_dlss_demo_windows.zip` | Auto-downloaded ([NVIDIA/DLSS](https://github.com/NVIDIA/DLSS)) |

### The dashboard

- **Current NGX** → **Latest available** version strip, with an up-to-date / update-available pill.
- **Installed Versions** table — green highlights the newest build of each component across all sources.
- **Sidebar status card** — live dots for scan, AnWave (installed + version), and whitelist (applied / not).

### Update All, step by step

1. **Pre-flight** — network, ≥500 MB disk, and writable target checks
2. **Whitelist** — removes the NVIDIA App override restrictions that otherwise revert your choice
3. **Preset sweep** — applies your Override Preset to the base profile + every game profile on your system
4. **Download** — latest DLSS SDK from NVIDIA/DLSS (skipped if already cached)
5. **Sync** — copies the SDK DLLs to NGX Release with a verified backup + automatic rollback
6. **AnWave** — installs it if missing, then applies the updated DLLs

> After applying, **fully restart your game** (not just to the menu). The on-screen DLSS
> indicator overlay appears in the **bottom-left** corner of supported games.

---

## 🔒 Security

Defense-in-depth for every file operation:

- **Path allowlisting** — only `C:\ProgramData\NVIDIA\NGX` and `%APPDATA%\NVIDIA\NGX` are writable
- **Pre-flight checks** — network, disk space, and write access verified before anything starts
- **PE header verification** — DLLs checked for valid MZ/PE signatures before copy
- **Post-copy validation** — file sizes verified after copy to catch truncated binaries
- **Backup + rollback** — backups validated before any change; restored automatically on failure
- **Long path support** — paths over 240 chars handled via the `\\?\` prefix
- **SharpCompress 0.48.0** — patched against CVE-2026-44788 (directory traversal in `WriteToDirectory`)

The in-app auto-updater is **opt-out**, never silent: it downloads a size-verified exe, swaps it
in place with rollback on failure, and prompts before restarting.

---

## 🧰 Troubleshooting

| Symptom | Fix |
|---|---|
| **"Administrator access is required"** | Run as Administrator — sync writes to `C:\ProgramData\NVIDIA\NGX\` |
| **"No DLSS versions found"** | Install the NVIDIA App and enable DLSS override for at least one game (this creates the NGX folders), then rescan |
| **DeepDVC shows "Unknown"** | Normal — some driver builds omit DeepDVC from the NGX config; handled gracefully |
| **"AnWave not installed"** | Click **Update All** (it auto-installs), or **Setup AnWave** in Advanced |
| **"Streamline SDK not found"** | Auto-downloaded when needed; or drop a manual SDK in Downloads / set its path in Settings |
| **Changes don't show in-game** | Fully restart the game; the DLL version and preset both need a clean game launch |

---

## 🗂️ Project Structure

```
dlss-version-toolkit/
├── src/
│   ├── DLSSVersionToolkit.Core/      # Core logic (no WPF): scanners, services, models
│   │   ├── Models/                   # DLSSVersionEntry, ScanResult, AppUpdateInfo, …
│   │   └── Services/                 # NgxScanner, DlssDownloadService, AnWaveAutoService,
│   │                                 # WhitelistService, PresetOverrideService, AppUpdateService, …
│   ├── DLSSVersionToolkit/           # WPF app
│   │   ├── ViewModels/               # MainViewModel (CommunityToolkit.Mvvm)
│   │   ├── Views/                    # SettingsDialog
│   │   ├── MainWindow.xaml           # Sidebar-dashboard UI
│   │   └── App.xaml                  # Theme, styles, startup
│   └── DLSSVersionToolkit.sln
├── tests/DLSSVersionToolkit.Tests/   # xUnit tests
└── .github/workflows/                # ci.yml (build+test on push/PR) · release.yml (tag → exe)
```

The single-file `DLSSVersionToolkit.exe` (~3.9 MB, framework-dependent) is built by CI on every
`v*` tag and attached to the GitHub release — it is **not** committed to the repo.

**Built with:** .NET 9 + WPF · CommunityToolkit.Mvvm · Hardcodet.NotifyIcon.Wpf ·
NvAPIWrapper.Net (DRS preset overrides) · SharpCompress.

---

## ⭐ Star History

<a href="https://star-history.com/#scubamount/dlss-version-toolkit&Date">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/chart?repos=scubamount/dlss-version-toolkit&type=Date&theme=dark" />
    <source media="(prefers-color-scheme: light)" srcset="https://api.star-history.com/chart?repos=scubamount/dlss-version-toolkit&type=Date" />
    <img alt="Star History Chart" src="https://api.star-history.com/chart?repos=scubamount/dlss-version-toolkit&type=Date" width="100%" />
  </picture>
</a>

---

## 📄 License

[Apache License 2.0](LICENSE) — see the [NOTICE](NOTICE) file for attribution requirements.

<p align="center">
  <strong>Built and maintained by scubamount.</strong>
</p>
