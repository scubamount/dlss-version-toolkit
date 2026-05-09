# dlss-version-toolkit Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-05-08

## Active Technologies

- **.NET 9 + WPF** (002-dlss-gui): WPF single-file .exe, framework-dependent deployment, C# core logic, CommunityToolkit.Mvvm, Hardcodet.NotifyIcon.Wpf
- **PowerShell 5.1+** (001-dlss-version-checker): Windows PowerShell module and CLI (legacy, superseded by 002-dlss-gui)

## Project Structure

```text
src/
├── DLSSVersionToolkit.Core/         # Core logic library (no WPF)
│   ├── Models/                        # DLSSVersionEntry, ScanResult, etc.
│   └── Services/                      # NgxScanner, UpgradeService, etc.
├── DLSSVersionToolkit/               # WPF application
│   ├── ViewModels/                   # MainViewModel
│   ├── Views/                        # SettingsDialog
│   ├── Services/                      # (in-app services)
│   └── Converters/                   # UI converters
└── DLSSVersionToolkit.sln

publish/
└── DLSSVersionToolkit.exe            # Single-file release (~918 KB)
```

## Commands

```bash
# Build
dotnet build src/DLSSVersionToolkit.sln

# Publish single-file exe
dotnet publish src/DLSSVersionToolkit/DLSSVersionToolkit.csproj --configuration Release --self-contained false

# Run tests (Phase 9)
dotnet test
```

## Code Style

- **C# / .NET 9**: Use CommunityToolkit.Mvvm `[ObservableProperty]` and `[RelayCommand]` source generators
- **WPF**: Dark theme (#1E1E1E background, #76B900 green accent), Segoe UI font, Consolas for paths/versions

## Recent Changes

- **002-dlss-gui**: Complete WPF GUI rewrite. Single-file .exe (~918 KB). Features: version scanning, upgrade, sync, system tray, periodic background scan, export (CSV/JSON), settings. Supersedes 001-dlss-version-checker.

<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->